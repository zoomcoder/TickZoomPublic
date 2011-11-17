#region Copyright
/*
 * Software: TickZoom Trading Platform
 * Copyright 2009 M. Wayne Walter
 * 
 * This library is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation; either
 * version 2.1 of the License, or (at your option) any later version.
 * 
 * Business use restricted to 30 days except as otherwise stated in
 * in your Service Level Agreement (SLA).
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, see <http://www.tickzoom.org/wiki/Licenses>
 * or write to Free Software Foundation, Inc., 51 Franklin Street,
 * Fifth Floor, Boston, MA  02110-1301, USA.
 * 
 */
#endregion

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;

using TickZoom.Api;

namespace TickZoom.TickUtil
{
	public class FastFillQueueImpl : FastQueueImpl<LogicalFillBinary>, FastFillQueue {
		public FastFillQueueImpl(string name, int maxSize) : base(name, maxSize) {
			
		}
	}
	
	public class FastEventQueueImpl : FastQueueImpl<QueueItem>, FastEventQueue {
		public FastEventQueueImpl(string name, int maxSize) : base(name, maxSize) {
			
		}
	}
	
	public struct FastQueueEntry<T> {
		public T Entry;
		public long utcTime;
		public FastQueueEntry(T entry, long utcTime) {
			this.Entry = entry;
			this.utcTime = utcTime;
		}
	}
	
	public class FastQueueImpl<T> : FastQueue<T> // where T : struct
	{
		private static readonly Log log = Factory.SysLog.GetLogger("TickZoom.TickUtil.FastQueueImpl.<" + typeof(FastQueueImpl<T>).GetGenericArguments()[0].Name + ">");
		private readonly bool debug = log.IsDebugEnabled;
		private readonly bool trace = log.IsTraceEnabled;
		private readonly Log instanceLog;
		private bool disableBackupLogging = false;
		string name;
		long lockSpins = 0;
		long lockCount = 0;
		long enqueueSpins = 0;
		long dequeueSpins = 0;
		int enqueueSleepCounter = 0;
		int dequeueSleepCounter = 0;
		long enqueueConflicts = 0;
		long dequeueConflicts = 0;
        SimpleLock spinLock = new SimpleLock();
	    readonly int spinCycles = 1000;
	    int timeout = 30000; // milliseconds
        private SimpleLock nodePoolLocker = new SimpleLock();
        private NodePool<FastQueueEntry<T>> nodePool;
        private static SimpleLock queuePoolLocker = new SimpleLock();
        private static Pool<Queue<FastQueueEntry<T>>> queuePool;
        private ActiveList<FastQueueEntry<T>> queue;
	    volatile bool terminate = false;
	    int processorCount = Environment.ProcessorCount;
		bool isStarted = false;
		bool isPaused = false;
		StartEnqueue startEnqueue;
		PauseEnqueue pauseEnqueue;
		ResumeEnqueue resumeEnqueue;
	    int maxSize;
	    int lowWaterMark;
	    int highWaterMark;
		Exception exception;
        int backupIncrease = 20;
        int backupLevel = 20;
        int backupInitial = 20;
        long earliestUtcTime = long.MaxValue;
		private Task inboundTask;
        private Task outboundTask;
        private int count;
		
		public long EarliestUtcTime {
			get { return earliestUtcTime; }
		}

        public FastQueueImpl(object name)
            : this(name, 1000)
        {

        }

	    public FastQueueImpl(object name, int maxSize) {
        	if( "TickWriter".Equals(name) || "DataReceiverDefault".Equals(name)) {
        		disableBackupLogging = true;
        	}
        	var nameString = name as string;
            //if( !string.IsNullOrEmpty(nameString)) {
            //    if( nameString.Contains("-Receive")) {
	        		backupLevel = backupInitial = 900;
            //    }
            //}
			instanceLog = Factory.SysLog.GetLogger("TickZoom.TickUtil.FastQueue."+name);
			if( trace) log.Trace("Created with capacity " + maxSize);
            if( name is string)
            {
                this.name = (string) name;
            } else if( name is Type)
            {
                this.name = ((Type) name).Name;
            }
			this.maxSize = maxSize;
			this.lowWaterMark = maxSize / 2;
			this.highWaterMark = maxSize / 2;
            queue = new ActiveList<FastQueueEntry<T>>();
			queue.Clear();
			TickUtilFactoryImpl.AddQueue(this);
	    }
        
		public override string ToString()
		{
			return name;
		}
	    
		private bool SpinLockNB() {
        	for( int i=0; i<100000; i++) {
        		if( spinLock.TryLock()) {
        			return true;
        		}
        	}
        	return false;
	    }
	    
	    private void SpinUnLock() {
        	spinLock.Unlock();
	    }
	    
        public void Enqueue(T tick, long utcTime) {
            while (!TryEnqueue(tick, utcTime))
            {
                if( IsFull)
                {
                    throw new ApplicationException("Enqueue failed for " + name + " with " + queue.Count + " items.");
                }
            }
        }

	    private int inboundId;
	    public void ConnectInbound(Task task)
	    {
            if( task == null)
            {
                throw new ArgumentNullException("task cannot be null");
            }
			if( inboundTask == null) {
				inboundTask = task;
				task.ConnectInbound( this, out inboundId);
			}
            else if( inboundTask != task)
            {
                throw new ApplicationException("Attempt to connect inbound task " + task + " to the queue but task " + inboundTask + " was already connected.");
            }
	    }

	    private int outboundId;
        public void ConnectOutbound(Task task)
        {
            if (task == null)
            {
                throw new ArgumentNullException("task cannot be null");
            }
            if (outboundTask == null)
            {
                outboundTask = task;
                task.ConnectInbound(this, out outboundId);
                for( int i=0; i<Capacity; i++)
                {
                    task.IncreaseOutbound(outboundId);
                }
            }
            else if( outboundTask != task)
            {
                throw new ApplicationException("Attempt to connect more than one outbound task " + task + " to the queue but task " + outboundTask + " was already connected.");
            }

        }

	    private bool isBackingUp = false;
		private int maxLastBackup = 0;
	    public bool TryEnqueue(T tick, long utcTime)
	    {
            // If the queue is full, wait for an item to be removed
            if( queue == null ) return false;
            if( !disableBackupLogging) {
	            if( queue.Count >= backupLevel) {
	            	isBackingUp = true;
	            	if( debug) log.Debug( name + " queue is backing up. Now " + queue.Count);
            	    backupLevel += backupIncrease;
	            }
    		    if( queue.Count > maxLastBackup) {
    			    maxLastBackup = queue.Count;
    		    }
            }
            if (queue.Count >= maxSize)
            {
                return false;
            }
            if (!SpinLockNB()) return false;
            try { 
	            if( isDisposed) {
		    		if( exception != null) {
		    			throw new ApplicationException("Enqueue failed.",exception);
		    		} else {
	            		throw new QueueException(EventType.Terminate);
		    		}
	            }
	            if( queue == null ) return false;
	            var temp = queue.Count;
	            if( temp>=maxSize) {
	            	return false;
	            }
                var node = NodePool.Create(new FastQueueEntry<T>(tick, utcTime));
                queue.AddFirst(node);
                if (temp == 0)
                {
	            	this.earliestUtcTime = utcTime;
	            	if( inboundTask != null) {
	            		inboundTask.UpdateUtcTime(inboundId,utcTime);
	            	}
	            }
	           	Interlocked.Increment(ref count);
	           	if( inboundTask != null) inboundTask.IncreaseInbound(inboundId);
            } finally {
	            SpinUnLock();
            }
	        return true;
	    }
	    
	    public void ReleaseCount() {
            while( !SpinLockNB());
	    	var tempCount = Interlocked.Decrement(ref count);
	    	var tempQueueCount = queue.Count;
            if( tempCount == 0) earliestUtcTime = long.MaxValue;
	    	if( tempCount < tempQueueCount) {
	    		throw new ApplicationException("Attempt to reduce FastQueue count less than internal queue: count " + tempCount + ", queue.Count " + tempQueueCount);
	    	}
	    	if( inboundTask != null)
	    	{
                if (tempCount == 0)
                {
                    inboundTask.UpdateUtcTime(inboundId, earliestUtcTime);
                }
                inboundTask.DecreaseInbound(inboundId);
	    	}
            SpinUnLock();
        }
	    
	    public bool Dequeue(out T tick) {
	    	return TryDequeue(out tick);
	    }
	    
	    public bool Peek(out T tick) {
	    	return TryPeekStruct(out tick);
	    }
	    
	    public bool TryPeekStruct(out T tick) {
	    	FastQueueEntry<T> entry;
	    	if( TryPeekStruct(out entry)) {
	    		tick = entry.Entry;
	    		return true;
	    	} else
	    	{
	    	    tick = default(T);
	    		return false;
	    	}
	    }
	    
	    private bool TryPeekStruct(out FastQueueEntry<T> entry)
	    {
            if( terminate) {
	    		if( exception != null) {
	    			throw new ApplicationException("Dequeue failed.",exception);
	    		} else {
	            	throw new QueueException(EventType.Terminate);
	    		}
            }
	    	entry = default(FastQueueEntry<T>);
	    	if( !isStarted) { 
	    		if( !StartDequeue()) return false;
	    	}
	        if( queue == null || queue.Count==0) return false;
	    	if( !SpinLockNB()) return false;
	    	try {
	            if( isDisposed) {
		    		if( exception != null) {
		    			throw new ApplicationException("Dequeue failed.",exception);
		    		} else {
		            	throw new QueueException(EventType.Terminate);
		    		}
	            }
		        if( queue == null || queue.Count==0) return false;
		        entry = queue.Last.Value;
	    	} finally {
	            SpinUnLock();
	    	}
            return true;
	    }
	    
	    public bool TryDequeue(out T tick)
	    {
            if( terminate) {
	    		if( exception != null) {
	    			throw new ApplicationException("Dequeue failed.",exception);
	    		} else {
	            	throw new QueueException(EventType.Terminate);
	    		}
            }
	    	tick = default(T);
	    	if( !isStarted) { 
	    		if( !StartDequeue()) return false;
	    	}
	        if( queue == null || queue.Count==0) return false;
	        var temp = 0;
	    	if( !SpinLockNB()) return false;
	    	try {
	            if( isDisposed) {
		    		if( exception != null) {
		    			throw new ApplicationException("Dequeue failed.",exception);
		    		} else {
		            	throw new QueueException(EventType.Terminate);
		    		}
	            }
		        if( queue == null || queue.Count==0) return false;
	            if( count != queue.Count) {
		        	throw new ApplicationException("Attempt to dequeue another item before calling ReleaseCount() for previously dequeued item. count " + count + ", queue.Count " + queue.Count);
	            }
		        var last = queue.Last;
		        tick = last.Value.Entry;
		        queue.Remove(last);
		        NodePool.Free(last);
	            temp = queue.Count;
                earliestUtcTime = queue.Count == 0 ? long.MaxValue : queue.Last.Value.utcTime;
	    	} finally {
	            SpinUnLock();
	    	}
 			if( temp == 0) {
            	if( isBackingUp) {
            		isBackingUp = false;
            		if( debug) log.Debug( name + " queue now cleared after backup to " + maxLastBackup + " items.");
            	    backupLevel = backupInitial;
            		maxLastBackup = 0;
            	}
	    	}
	    	return true;
	    }
	    
	    public void Clear() {
	    	if( trace) log.Trace("Clear called");
    		while( !SpinLockNB()) ;
	    	if( !isDisposed) {
		        queue.Clear();
	    	    Interlocked.Exchange(ref count, 0);
	    	}
	        SpinUnLock();
	    }
	    
	    public void Flush() {
	    	if( debug) log.Debug("Flush called");
	    	while(!isDisposed && queue.Count>0) {
	    		Factory.Parallel.Yield();
	    	}
	    }
	    
	    public void SetException(Exception ex) {
	    	exception = ex;
	    }
	    
	 	private volatile bool isDisposed = false;
	 	private object disposeLocker = new object();

	    public void Dispose() 
	    {
	        Dispose(true);
	        GC.SuppressFinalize(this);      
	    }
	
	    protected virtual void Dispose(bool disposing)
	    {
	       	if( !isDisposed) {
	    		lock( disposeLocker) {
		            isDisposed = true;   
		            if (disposing) {
				        if( queue!=null) {
				    		while( !SpinLockNB()) ;
				    		try {
						    	inboundTask = null;
						    	var next = queue.First;
						    	for( var node = next; node != null; node = next) {
						    		next = node.Next;
						    		queue.Remove(node);
						    		NodePool.Free(node);
						    	}
				    		} finally {
						        SpinUnLock();
				    		}
				        }
		            }
	    		}
	    	}
	    }
	    
	    public int Count {
	    	get { if(!isDisposed) {
	    			return count;
	    		} else {
	    			return 0;
	    		}
	    	}
	    }
	    
		public long EnqueueConflicts {
			get { return enqueueConflicts; }
		}
	    
		public long DequeueConflicts {
			get { return dequeueConflicts; }
		}
		
		public StartEnqueue StartEnqueue {
			get { return startEnqueue; }
			set { startEnqueue = value;	}
		}
	
		private bool StartDequeue()
		{
			if( trace) log.Trace("StartDequeue called");
			if( !SpinLockNB()) return false;
			isStarted = true;
			if( StartEnqueue != null) {
		    	if( trace) log.Trace("Calling StartEnqueue");
				StartEnqueue();
			}
	        SpinUnLock();			
	        return true;
		}
		
		public int Timeout {
			get { return timeout; }
			set { timeout = value; }
		}
		
		public bool IsStarted {
			get { return isStarted; }
		}
		
		public ResumeEnqueue ResumeEnqueue {
			get { return resumeEnqueue; }
			set { resumeEnqueue = value; }
		}
		
		public PauseEnqueue PauseEnqueue {
			get { return pauseEnqueue; }
			set { pauseEnqueue = value; }
		}
		
		public bool IsPaused {
			get { return isPaused; }
		}
		
		public string GetStats() {
			var average = lockCount == 0 ? 0 : ((lockSpins*spinCycles)/lockCount);
			var sb = new StringBuilder();
			sb.Append("Queue Name=");
			sb.Append(name);
			sb.Append(" items=");
			sb.Append(Count);
			if( earliestUtcTime != long.MaxValue) {
				sb.Append(" age=");
				var age = TimeStamp.UtcNow.Internal - earliestUtcTime;
				sb.Append(age);
			}
		    sb.Append(" locks( count=");
			sb.Append(lockCount);
		    sb.Append(" spins=");
		    sb.Append(lockSpins*spinCycles);
		    sb.Append(" average=");
			sb.Append(average);
			sb.Append(") enqueue( conflicts=");
			sb.Append(enqueueConflicts);
			sb.Append(" spins=");
			sb.Append(enqueueSpins);
			sb.Append(" sleeps=");
			sb.Append(enqueueSleepCounter);
			sb.Append(") dequeue( conflicts=");
			sb.Append(dequeueConflicts);
			sb.Append(" spins=");
			sb.Append(dequeueSpins);
			sb.Append(" sleeps=");
			sb.Append(dequeueSleepCounter);
			return sb.ToString();
		}
	    
		public NodePool<FastQueueEntry<T>> NodePool {
	    	get {
                if( nodePool == null) {
					using(nodePoolLocker.Using()) {
	    				if( nodePool == null) {
	    					nodePool = new NodePool<FastQueueEntry<T>>();
	    				}
	    			}
                }
                return nodePool;
	    	}
		}

		public static Pool<Queue<FastQueueEntry<T>>> QueuePool {
	    	get {
                if( queuePool == null) {
                    using (queuePoolLocker.Using())
                    {
                        if (queuePool == null)
                        {
	    					queuePool = Factory.TickUtil.Pool<Queue<FastQueueEntry<T>>>();
	    				}
	    			}
				}
	    		return queuePool;
	    	}
		}
		
		public int Capacity {
			get { return maxSize; }
		}
		
		public bool IsFull {
			get { return queue.Count >= maxSize; }
		}
		
		public bool IsEmpty {
			get { return queue.Count == 0; }
		}
		
		public string Name {
			get { return name; }
		}
		
	}
}



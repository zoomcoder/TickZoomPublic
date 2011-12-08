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
        public override string ToString()
        {
            return Entry + " UTC " + new TimeStamp(utcTime);
        }
	}
	
	public class FastQueueImpl<T> : FastQueue<T> // where T : struct
	{
	    private readonly Log log;
	    private bool debug;
	    private bool trace;
		private Log instanceLog;
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
        private class TaskConnection
        {
            internal Task task;
            internal int id;
            internal int outboundCount;
            internal void IncreaseOutbound()
            {
                var value = Interlocked.Increment(ref outboundCount);
                task.IncreaseOutbound(id,value);
            }

            public void DecreaseOutbound()
            {
                if( outboundCount > 0)
                {
                    var value = Interlocked.Decrement(ref outboundCount);
                    task.DecreaseOutbound(id,value);
                }
            }
        }
        private List<TaskConnection> outboundTasks = new List<TaskConnection>();
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
		    log = Factory.SysLog.GetLogger("TickZoom.TickUtil.FastQueue."+name.ToString().StripInvalidPathChars());
		    debug = log.IsDebugEnabled;
		    trace = log.IsTraceEnabled;
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

	    private string lockLocation;
		private bool SpinLock(string location)
		{
            while( true)
            {
                int spinLimit = 100000;
                for (int i = 0; i < spinLimit; i++)
                {
                    if (spinLock.TryLock())
                    {
                        lockLocation = location;
                        return true;
                    }
                }
                if( debug) log.Debug("Lock spinned more than " + spinLimit + " times on " + name + " at " + location + ". Locked from " + lockLocation);
            }
	    }
	    
	    private void SpinUnLock() {
        	spinLock.Unlock();
	    }
	    
        public void Enqueue(T tick, long utcTime) {
            while (!TryEnqueue(tick, utcTime))
            {
                if( IsFull)
                {
                    throw new ApplicationException("Enqueue failed for " + name + " with " + count + " items.");
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

        public void ConnectOutbound(Task task)
        {
            if (task == null)
            {
                throw new ArgumentNullException("task cannot be null");
            }
            var found = outboundTasks.Find(x => x.task == task);
            if( found == null || found.task == null)
            {
                var connection = new TaskConnection();
                connection.task = task;
                outboundTasks.Add(connection);
                task.ConnectOutbound(this, out connection.id);
                if( IsFull)
                {
                    connection.IncreaseOutbound();
                }
            }
        }

	    private bool isBackingUp = false;
		private int maxLastBackup = 0;
	    public bool TryEnqueue(T item, long utcTime)
	    {
            // If the queue is full, wait for an item to be removed
            if( !disableBackupLogging) {
	            if( count >= backupLevel) {
	            	isBackingUp = true;
	            	if( debug) log.Debug( name + " queue is backing up. Now " + count);
            	    backupLevel += backupIncrease;
	            }
                if (count > maxLastBackup)
                {
    			    maxLastBackup = count;
    		    }
            }
            if (count >= maxSize)
            {
                return false;
            }
	        SpinLock("TryEnqueue");
            try { 
	            if( isDisposed) {
		    		if( exception != null) {
		    			throw new ApplicationException("Enqueue failed.",exception);
		    		} else {
	            		throw new QueueException(EventType.Terminate);
		    		}
	            }
                if (count >= maxSize)
                {
	            	return false;
	            }
                var priorCount = count;
                if (trace) log.Trace("Enqueue " + item);
                var node = NodePool.Create(new FastQueueEntry<T>(item, utcTime));
                queue.AddFirst(node);
                Interlocked.Increment(ref count);
                if (inboundTask != null)
                {
                    if( trace) log.Trace("IncreaseInbound with count " + count + ", queue count " + queue.Count + ", previous count " + priorCount);
                    inboundTask.IncreaseInbound(inboundId);
                    if (priorCount == 0)
                    {
                        earliestUtcTime = utcTime;
                        inboundTask.UpdateUtcTime(inboundId, utcTime);
                    }
                }
                if (count >= maxSize)
                {
                    for (var i = 0; i < outboundTasks.Count; i++)
                    {
                        outboundTasks[i].IncreaseOutbound();
                    }
                }
            }
            finally
            {
	            SpinUnLock();
            }
	        return true;
	    }
	    
	    public void ReleaseCount()
	    {
	        SpinLock("ReleaseCount");
	        var priorCount = count;
	    	var newCount = Interlocked.Decrement(ref count);
	    	var tempQueueCount = queue.Count;
            if( newCount == 0) earliestUtcTime = long.MaxValue;
	    	if( newCount < tempQueueCount) {
	    		throw new ApplicationException("Attempt to reduce FastQueue count less than internal queue: count " + newCount + ", queue.Count " + tempQueueCount);
	    	}
	    	if( inboundTask != null)
	    	{
                if( trace) log.Trace("DecreaseInbound with count = " + newCount);
                inboundTask.DecreaseInbound(inboundId);
                if (newCount == 0)
                {
                    inboundTask.UpdateUtcTime(inboundId, earliestUtcTime);
                }
	    	}
            if (priorCount >= maxSize && newCount < maxSize)
            {
                if (trace) log.Trace("DecreaseOutbound with count " + newCount + ", previous count " + priorCount);
                for (var i = 0; i < outboundTasks.Count; i++)
                {
                    outboundTasks[i].DecreaseOutbound();
                }
            }
            SpinUnLock();
        }
	    
	    public void Dequeue(out T tick) {
            
            if( !TryDequeue(out tick))
            {
                throw new ApplicationException("Queue is empty");    
            }
	    }
	    
	    public void Peek(out T tick) {
            if (count == 0) throw new ApplicationException("Queue is empty");
            while (!TryPeekStruct(out tick)) ;
	    }

        public bool TryPeek( out T tick)
        {
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
	    	if( !isStarted)
	    	{
	    	    StartDequeue();
	    	}
	        if( queue.Count==0) return false;
	        SpinLock("TryPeek");
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
	    
	    public bool TryDequeue(out T item)
	    {
            if( terminate) {
	    		if( exception != null) {
	    			throw new ApplicationException("Dequeue failed.",exception);
	    		} else {
	            	throw new QueueException(EventType.Terminate);
	    		}
            }
	    	if( !isStarted)
	    	{
	    	    StartDequeue();
	    	}
	        if( queue.Count==0)
	        {
                item = default(T);
                return false;
	        }
	        int priorCount;
	        int newCount = 0;
	        SpinLock("TryDequeue");
	    	try {
	            if( isDisposed) {
		    		if( exception != null) {
		    			throw new ApplicationException("Dequeue failed.",exception);
		    		} else {
		            	throw new QueueException(EventType.Terminate);
		    		}
	            }
		        if( queue == null || queue.Count==0)
		        {
                    item = default(T);
                    return false;
		        }
	            if( count > queue.Count) {
		        	throw new ApplicationException("Attempt to dequeue another item before calling ReleaseCount() for previously dequeued item. count " + count + ", queue.Count " + queue.Count);
	            }
                if (count < queue.Count)
                {
                    throw new ApplicationException("Called ReleaseCount() too many times before dequeuing next item. count " + count + ", queue.Count " + queue.Count);
                }
                priorCount = queue.Count;
		        var last = queue.Last;
		        item = last.Value.Entry;
                if (trace) log.Trace("Dequeue " + item);
                queue.Remove(last);
		        NodePool.Free(last);
	            newCount = queue.Count;
                earliestUtcTime = queue.Count == 0 ? long.MaxValue : queue.Last.Value.utcTime;
	    	} finally {
	            SpinUnLock();
	    	}
 			if( newCount == 0) {
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
	        SpinLock("Clear");
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
				        if( queue!=null)
				        {
				            SpinLock("Dispose");
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
	
		private void StartDequeue()
		{
			if( trace) log.Trace("StartDequeue called");
		    SpinLock("StartDequeue");
			isStarted = true;
			if( StartEnqueue != null) {
		    	if( trace) log.Trace("Calling StartEnqueue");
				StartEnqueue();
			}
	        SpinUnLock();			
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
		    var node = queue.Last;
            for( var i=0; node != null && i<5; node=node.Previous, i++)
            {
                sb.Append(", ");
                sb.Append(node.Value.ToString());
            }
            if (false)
            {

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
            }
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
			get { return count >= maxSize; }
		}
		
		public bool IsEmpty {
			get { return queue.Count == 0; }
		}
		
		public string Name {
			get { return name; }
		}
		
	}
}



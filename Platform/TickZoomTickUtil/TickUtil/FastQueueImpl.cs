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
        private bool verbose;
        private Log instanceLog;
		private bool disableBackupLogging = false;
		string name;
		long enqueueConflicts = 0;
		long dequeueConflicts = 0;
	    readonly int spinCycles = 1000;
	    int timeout = 30000; // milliseconds
        private ActiveQueue<FastQueueEntry<T>> queue;
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
                var value = ++outboundCount;
                task.IncreaseOutbound(id);
            }

            public void DecreaseOutbound()
            {
                if( outboundCount > 0)
                {
                    var value = --outboundCount;
                    task.DecreaseOutbound(id);
                }
            }
        }
        private List<TaskConnection> outboundTasks = new List<TaskConnection>();
		
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
            verbose = log.IsVerboseEnabled;
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
            queue = new ActiveQueue<FastQueueEntry<T>>();
			queue.Clear();
			TickUtilFactoryImpl.AddQueue(this);
	    }

	    public override string ToString()
		{
			return name;
		}

        public void Enqueue(T tick, long utcTime) {
            if(!TryEnqueue(tick, utcTime))
            {
                throw new ApplicationException("Enqueue failed for " + name + " with " + queue.Count + " items.");
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
            if( isDisposed) {
	    		if( exception != null) {
	    			throw new ApplicationException("Enqueue failed.",exception);
	    		} else {
            		throw new QueueException(EventType.Terminate);
	    		}
            }
            if (verbose) log.Verbose("Enqueue " + item);
            if( !queue.TryEnqueue(new FastQueueEntry<T>(item, utcTime)))
	        {
	            return false;
	        }
	        var count = queue.Count;
            if (inboundTask != null)
            {
                if( verbose) log.Verbose("IncreaseInbound with count " + queue.Count);
                if (count == 1)
                {
                    earliestUtcTime = utcTime;
                }
                inboundTask.IncreaseInbound(inboundId,earliestUtcTime);
            }
            if (count >= maxSize)
            {
                for (var i = 0; i < outboundTasks.Count; i++)
                {
                    outboundTasks[i].IncreaseOutbound();
                }
            }
	        return true;
	    }
	    
	    
	    public void Dequeue(out T tick) {
            
            if( !TryDequeue(out tick))
            {
                throw new ApplicationException("Queue is empty");    
            }
	    }
	    
	    public void Peek(out T tick) {
            if (queue.Count == 0) throw new ApplicationException("Queue is empty");
            if( !TryPeekStruct(out tick))
            {
                throw new ApplicationException("Queue is empty.");
            }
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
	    	entry = default(FastQueueEntry<T>);
	    	if( !isStarted)
	    	{
	    	    StartDequeue();
	    	}
	        if( queue.Count==0) return false;
            if( isDisposed) {
	    		if( exception != null) {
	    			throw new ApplicationException("Dequeue failed.",exception);
	    		} else {
	            	throw new QueueException(EventType.Terminate);
	    		}
            }
	        if( queue.Count==0) return false;
	        entry = queue.Peek();
            return true;
	    }
	    
	    public bool TryDequeue(out T item)
	    {
            if (isDisposed)
            {
                if (exception != null)
                {
                    throw new ApplicationException("Enqueue failed.", exception);
                }
                else
                {
                    throw new QueueException(EventType.Terminate);
                }
            }
            if (!isStarted)
	    	{
	    	    StartDequeue();
	    	}
	        FastQueueEntry<T> entry;
            var priorCount = queue.Count;
            if (!queue.TryDequeue(out entry))
	        {
	            item = default(T);
	            return false;
	        }
	        item = entry.Entry;
            if (verbose) log.Verbose("Dequeue " + item);
            var count = queue.Count;
            earliestUtcTime = count == 0 ? long.MaxValue : queue.Peek().utcTime;
            if (count == 0) earliestUtcTime = long.MaxValue;
            if (inboundTask != null)
            {
                if (verbose) log.Verbose("DecreaseInbound with count = " + count);
                inboundTask.DecreaseInbound(inboundId, earliestUtcTime);
            }
            if (count + 1 == maxSize)
            {
                if (verbose) log.Verbose("DecreaseOutbound with count " + count + ", previous count " + priorCount);
                for (var i = 0; i < outboundTasks.Count; i++)
                {
                    outboundTasks[i].DecreaseOutbound();
                }
            }
 			if( count == 0) {
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
	    	if( !isDisposed) {
		        queue.Clear();
	    	}
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
					    	inboundTask = null;
				        }
		            }
	    		}
	    	}
	    }
	    
	    public int Count {
	    	get { if(!isDisposed) {
	    			return queue.Count;
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
			isStarted = true;
			if( StartEnqueue != null) {
		    	if( trace) log.Trace("Calling StartEnqueue");
				StartEnqueue();
			}
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
            //var node = queue.Last;
            //for( var i=0; node != null && i<5; node=node.Previous, i++)
            //{
            //    sb.Append(", ");
            //    sb.Append(node.Value.ToString());
            //}
		    return sb.ToString();
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



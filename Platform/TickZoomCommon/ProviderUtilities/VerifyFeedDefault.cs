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
using System.Threading;

using TickZoom.Api;

namespace TickZoom.Common
{
	public class VerifyFeedDefault : VerifyFeed
	{
        private static readonly Log log = Factory.SysLog.GetLogger(typeof(VerifyFeedDefault));
		private readonly bool debug = log.IsDebugEnabled;
        private readonly bool trace = log.IsTraceEnabled;
        private readonly bool verbose = log.IsVerboseEnabled;
		private TickSync tickSync;
		private volatile SymbolState symbolState = SymbolState.None;
		private volatile BrokerState brokerState = BrokerState.Disconnected;
		private Task task;
		private static object taskLocker = new object();
	    private Pool<TickBinaryBox> tickPool;
		private bool keepReceived = false;
		private List<TickBinary> received = new List<TickBinary>();
		private int pauseSeconds = 3;
	    private SymbolInfo symbol;
        private TickIO lastTick = Factory.TickUtil.TickIO();
	    private QueueFilter filter;
	    private Agent agent;
		
		public List<TickBinary> GetReceived() {
			return received;
		}

		public SymbolState OnGetReceiverState(SymbolInfo symbol)
		{
			return symbolState;
		}

		public VerifyFeedDefault(SymbolInfo symbol)
		{
		    this.symbol = symbol;
            tickSync = SyncTicks.GetTickSync(symbol.BinaryIdentifier);
            tickPool =  Factory.Parallel.TickPool(symbol);
        }

        public void Initialize( Task task)
        {
            this.task = task;
            task.Scheduler = Scheduler.EarliestTime;
            filter = task.GetFilter();
            task.Start();
            task.Pause();
        }

		public void Start()
		{
		}
		
		public long VerifyEvent(Action<SymbolInfo, int, object> assertTick, int timeout)
		{
			return VerifyEvent(1, assertTick, symbol, timeout);
		}
		
		public long Verify(Action<TickIO, TickIO, long> assertTick, int timeout)
		{
			return Verify(2, assertTick, timeout);
		}
		
		int countLog = 0;
		TickBinary tickBinary = new TickBinary();
		TickIO tickIO = Factory.TickUtil.TickIO();
		public long Verify(int expectedCount, Action<TickIO, TickIO, long> assertTick, int timeout) {
			return Verify( expectedCount, assertTick, timeout, null);
		}

        public bool TryDequeueTick(ref TickBinary tickBinary)
        {
            EventItem eventItem;
            var result = false;
            if( filter.Receive(out eventItem))
            {
                var eventType = (EventType)eventItem.EventType;
                switch( eventType)
                {
                    case EventType.Tick:
                        TickBinaryBox box = (TickBinaryBox)eventItem.EventDetail;
                        tickBinary = box.TickBinary;
                        if( symbolState == SymbolState.RealTime)
                        {
                            int x = 0; // log.Info(tickBinary.ToString());
                        }
                        box.Free();
                        result = true;
                        filter.Pop();
                        break;
                    default:
                        filter.Pop();
                        throw new QueueException(eventType);
                }
            }
            return result;
        }

		private bool actionAlreadyRun = false;
		public long Verify(int expectedCount, Action<TickIO, TickIO, long> assertTick, int timeout, Action action)
		{
			if (debug) log.Debug("Verify");
            long endTime = Factory.Parallel.TickCount + timeout * 1000;
			count = 0;
		    do
		    {
		        if (propagateException != null)
		        {
		            throw propagateException;
		        }
		        try
		        {
		            if (TryDequeueTick(ref tickBinary))
		            {
		                tickIO.Inject(tickBinary);
		                if (debug && countLog < 5)
		                {
		                    log.Debug("Received a tick " + tickIO + " UTC " + tickIO.UtcTime);
		                    countLog++;
		                }
		                else if (trace)
		                {
		                    log.Trace("Received a tick " + tickIO + " UTC " + tickIO.UtcTime);
		                }
		                startTime = Factory.TickCount;
		                count++;
		                if (count > 0)
		                {
		                    if (assertTick != null)
		                    {
		                        assertTick(tickIO, lastTick, symbol.BinaryIdentifier);
		                    }
		                    if (count%10000 == 0)
		                    {
		                        log.Info("Read " + count + " ticks");
		                    }
		                }
		                lastTick.Copy(tickIO);
		                if (!actionAlreadyRun && action != null)
		                {
		                    actionAlreadyRun = true;
		                    action();
		                }
		                if (SyncTicks.Enabled && symbolState == SymbolState.RealTime)
		                {
		                    tickSync.RemoveTick(ref tickBinary);
		                }
		                if (count >= expectedCount)
		                {
		                    break;
		                }
		            }
		            else
		            {
		                Thread.Sleep(100);
		                //if (queue.Count == 0 && SyncTicks.Enabled && SymbolState == SymbolState.RealTime)
		                //{
		                //    tickSync.RemoveTick();
		                //}
		            }
		        }
		        catch (QueueException ex)
		        {
		            if (HandleQueueException(ex))
		            {
		                break;
		            }
		        }
		    } while (Factory.Parallel.TickCount < endTime);
			return count;
		}
		
		public long Wait(int expectedTicks, int timeout)
		{
			if (debug) log.Debug("Wait");
			long startTime = Factory.Parallel.TickCount;
		    lastTick = Factory.TickUtil.TickIO();
			count = 0;
			while (Factory.Parallel.TickCount - startTime < timeout * 1000) {
				if( propagateException != null) {
					throw propagateException;
				}
				try { 
					if( TryDequeueTick(ref tickBinary)) {
						tickIO.Inject(tickBinary);
                        if (debug && count < 5)
                        {
                            log.Debug("Received a tick " + tickIO + " UTC " + tickIO.UtcTime);
                            countLog++;
                        }
                        else if (trace)
                        {
                            log.Trace("Received a tick " + tickIO + " UTC " + tickIO.UtcTime);
                        }
                        count++;
						lastTick.Copy(tickIO);
                        if (SyncTicks.Enabled && symbolState == SymbolState.RealTime)
                        {
                            tickSync.RemoveTick(ref tickBinary);
                        }
                        if (count >= expectedTicks)
                        {
							break;
						}
					} else {
						Thread.Sleep(100);
					}
				} catch (QueueException ex) {
					if( HandleQueueException(ex)) {
						break;
					}
				}
			}
			return count;
		}

        public bool VerifyState(SymbolState expectedSymbolState, int timeout)
        {
            if (debug) log.Debug("VerifyState symbol " + expectedSymbolState + ", timeout " + timeout);
            long startTime = Factory.TickCount;
            count = 0;
            while (Factory.TickCount - startTime < timeout * 1000)
            {
                if (propagateException != null)
                {
                    throw propagateException;
                }
                try
                {
                    if (TryDequeueTick(ref tickBinary))
                    {
                        tickIO.Inject(tickBinary);
                        if (debug) log.Debug("Received tick " + tickIO);
                        if (SyncTicks.Enabled && symbolState == SymbolState.RealTime)
                        {
                            tickSync.RemoveTick(ref tickBinary);
                        }
                    }
                    else
                    {
                        Thread.Sleep(100);
                    }
                }
                catch (QueueException ex)
                {
                    if (HandleQueueException(ex))
                    {
                        break;
                    }
                }
                if (symbolState == expectedSymbolState)
                {
                    return true;
                }
            }
            return false;
        }

        public bool VerifyState(BrokerState expectedBrokerState, SymbolState expectedSymbolState, int timeout)
        {
			if (debug) log.Debug("VerifyState broker " + expectedBrokerState + ", symbol " + expectedSymbolState + ", timeout " + timeout);
			long startTime = Factory.TickCount;
			count = 0;
			TickBinary binary = new TickBinary();
			while (Factory.TickCount - startTime < timeout * 1000) {
				if( propagateException != null) {
					throw propagateException;
				}
				try { 
					if( !TryDequeueTick(ref binary)) {
                        if (SyncTicks.Enabled && symbolState == SymbolState.RealTime)
                        {
                            tickSync.RemoveTick(ref binary);
                        }
					} else {
                        Thread.Sleep(100);
                    }
				} catch (QueueException ex) {
					if( HandleQueueException(ex)) {
						break;
					}
				}
				if( brokerState == expectedBrokerState && symbolState == expectedSymbolState) {
					return true;
				}
			}
			return false;
		}
		
		public long VerifyEvent(int expectedCount, Action<SymbolInfo,int,object> assertEvent, SymbolInfo symbol, int timeout)
		{
			if (debug) log.Debug("VerifyEvent");
			long startTime = Factory.TickCount;
			count = 0;
			while (Factory.TickCount - startTime < timeout * 1000) {
				if( propagateException != null) {
					throw propagateException;
				}
				try {
					// Remove ticks just so as to get to the event we want to see.
					if( TryDequeueTick(ref tickBinary)) {
						if (customEventType> 0) {
							assertEvent(customEventSymbol,customEventType,customEventDetail);
							count++;
						} else {
							Thread.Sleep(10);
						}
                        if (SyncTicks.Enabled && symbolState == SymbolState.RealTime)
                        {
                            tickSync.RemoveTick(ref tickBinary);
                        }
                        if (count >= expectedCount)
                        {
							break;
						}
					} else {
						Thread.Sleep(100);
					}
				} catch (QueueException ex) {
					if( HandleQueueException(ex)) {
						break;
					}
				}
			}
			return count;
		}
		
		public int VerifyPosition(int expectedPosition, int timeout) {
			return VerifyPosition( expectedPosition, timeout, null);
		}
		
		public int VerifyPosition(int expectedPosition, int timeout, Action action)
		{
			if (debug)
				log.Debug("VerifyFeed");
			log.Info("Sleeping " + pauseSeconds + " seconds to allow checking for over filling of orders.");
			Thread.Sleep( pauseSeconds * 1000);
			long startTime = Factory.TickCount;
			count = 0;
			int position;
			bool actionAlreadyRun = false;
			TickBinary binary = new TickBinary();
			while (Factory.TickCount - startTime < timeout * 1000) {
				if( propagateException != null) {
					throw propagateException;
				}
				try { 
					if( !TryDequeueTick(ref binary)) {
						Thread.Sleep(10);
					} else {
						if( !actionAlreadyRun && action != null) {
							actionAlreadyRun = true;
							action();
						}
                        if (SyncTicks.Enabled && symbolState == SymbolState.RealTime)
                        {
                            tickSync.RemoveTick(ref binary);
                        }
                    }
				} catch (QueueException ex) {
					if( HandleQueueException(ex)) {
						break;
					}
				}
				if( actualPositions.TryGetValue(symbol.BinaryIdentifier,out position)) {
					if( position == expectedPosition) {
						return expectedPosition;
					}
				}
			}
			if( actualPositions.TryGetValue(symbol.BinaryIdentifier,out position)) {
				return position;
			} else {
				return 0;
			}
		}

		private bool HandleQueueException( QueueException ex) {
			log.Notice("QueueException: " + ex.EntryType + " with " + count + " ticks so far.");
			switch (ex.EntryType) {
				case EventType.StartHistorical:
					symbolState = SymbolState.Historical;
					break;
				case EventType.EndHistorical:
					symbolState = SymbolState.None;
					break;
				case EventType.StartRealTime:
					symbolState = SymbolState.RealTime;
					break;
				case EventType.EndRealTime:
					symbolState = SymbolState.None;
					break;
				case EventType.StartBroker:
					brokerState = BrokerState.Connected;
                    if (SyncTicks.Enabled)
                    {
                        while (tickSync.SentSwtichBrokerState)
                        {
                            tickSync.ClearSwitchBrokerState("endbroker");
                        }
                    }
                    break;
				case EventType.EndBroker:
					brokerState = BrokerState.Disconnected;
                    if (SyncTicks.Enabled)
                    {
                        while (tickSync.SentSwtichBrokerState)
                        {
                            tickSync.ClearSwitchBrokerState("endbroker");
                        }
                    }
                    break;
				case EventType.Terminate:
					symbolState = SymbolState.None;
					return true;
                case EventType.RequestPosition:
                    symbolState = SymbolState.None;
                    return false;
                default:
					throw new ApplicationException("Unexpected QueueException: " + ex.EntryType);
			}
			return false;
		}
		
		volatile int count = 0;
		long startTime;
		public void StartTimeTheFeed()
		{
            startTime = Factory.TickCount;
			count = 0;
			countLog = 0;
            task.Resume();
		}
		
		private Exception propagateException = null;
		
		private void OnException( Exception ex) {
			propagateException = ex;	
		}

		public int EndTimeTheFeed(int expectedTickCount, int timeoutSeconds)
		{
			while( count < expectedTickCount && Factory.TickCount < startTime + timeoutSeconds * 1000) {
				if( propagateException != null) {
					throw new ApplicationException("EndTimeTheFeed found exception thrown in back end: " + propagateException.Message, propagateException);
				}
				Thread.Sleep(100);
			}
    		log.Notice("Expected " + expectedTickCount + " and received " + count + " ticks.");
			log.Notice("Last tick received at : " + tickIO.ToPosition());
            if (count < expectedTickCount)
            {
                var queueStats = Factory.Parallel.GetQueueStats();
                log.Info(queueStats);
            }
			if( propagateException != null) {
				throw propagateException;
			}
			return count;
		}

        public void Shutdown()
        {
            Dispose();
        }

		public Yield Invoke()
		{
			lock(taskLocker) {
				try {
					if (!TryDequeueTick(ref tickBinary)) {
						return Yield.NoWork.Repeat;
					}
					if( keepReceived) {
						received.Add(tickBinary);
					}
					startTime = Factory.TickCount;
					tickIO.Inject(tickBinary);
                    count++;
                    if (debug && count <= 5)
                    {
                        log.Debug("Received tick #" + count + " " + tickIO + " UTC " + tickIO.UtcTime);
						countLog++;
					} else if( trace)
					{
                        log.Trace("Received tick #" + count + " " + tickIO + " UTC " + tickIO.UtcTime);
                    }
					if( count == 0) {
						log.Notice("First tick received: " + tickIO.ToPosition());
					}
					if (count % 10000 == 0) {
						log.Info("Read " + count + " ticks");
					}
					if( SyncTicks.Enabled && symbolState == SymbolState.RealTime)
					{
					    tickSync.RemoveTick(ref tickBinary);
					}
					return Yield.DidWork.Repeat;
				} catch (QueueException ex) {
					HandleQueueException(ex);
				}
				return Yield.NoWork.Repeat;
			}
		}

		Dictionary<long, int> actualPositions = new Dictionary<long, int>();

		public double GetPosition(SymbolInfo symbol)
		{
			return actualPositions[symbol.BinaryIdentifier];
		}

		public bool OnLogicalFill(SymbolInfo symbol, LogicalFillBinary fill)
		{
			log.Info("Got Logical Fill of " + symbol + " at " + fill.Price + " for " + fill.Position);
			actualPositions[symbol.BinaryIdentifier] = fill.Position;
			if( SyncTicks.Enabled) tickSync.RemovePhysicalFill(fill);
			return true;
		}

		public bool OnStop()
		{
			Dispose();
			return true;
		}

		public void ReportError(ErrorDetail error)
		{
			OnException( new Exception(error.ErrorMessage));
		}
		
 		private volatile bool isDisposed = false;
	    public void Dispose() 
	    {
	        Dispose(true);
	        GC.SuppressFinalize(this);      
	    }
	
	    protected virtual void Dispose(bool disposing)
	    {
       		if( !isDisposed) {
	    		lock( taskLocker) {
		            isDisposed = true;   
		            if (disposing) {
		            	if( task != null) {
			            	task.Stop();
		            	}
		            }
		            task = null;
		            // Leave tickQueue set so any extraneous
		            // events will see the queue is already terminated.
//		            tickQueue = null;
	    		}
    		}
	    }
	    
		public bool IsRealTime {
			get { return symbolState == SymbolState.RealTime; }
		}
		
		volatile SymbolInfo customEventSymbol;
		volatile int customEventType;
		volatile object customEventDetail;
		public bool OnCustomEvent(SymbolInfo symbol, int eventType, object eventDetail) {
			customEventSymbol = symbol;
			customEventType = eventType;
			customEventDetail = eventDetail;
			return true;
		}

		
		public TickIO LastTick {
			get { return lastTick; }
		}
		
		public bool KeepReceived {
			get { return keepReceived; }
			set { keepReceived = value; }
		}
		
		public int PauseSeconds {
			get { return pauseSeconds; }
			set { pauseSeconds = value; }
		}

	    public Task Task
	    {
	        get { return task; }
	    }

	    public Agent Agent
	    {
	        get { return agent; }
	        set { agent = value; }
	    }

	    public void Clear()
	    {
			while( true) 
            {
                try
                {
                    if (TryDequeueTick(ref tickBinary))
                    {
                        tickIO.Inject(tickBinary);
                        if( debug) log.Debug("Clearing out tick #" + count + " " + tickIO + " UTC " + tickIO.UtcTime);
                        if (SyncTicks.Enabled && symbolState == SymbolState.RealTime)
                        {
                            tickSync.RemoveTick(ref tickBinary);
                        }
                    }
                    else
                    {
                        break;
                    }
                }
                catch (QueueException ex)
                {
                    HandleQueueException(ex);
                }
            }
            symbolState = SymbolState.None;
        }

        public bool IsFinalized()
        {
            throw new NotImplementedException();
        }
    }
}

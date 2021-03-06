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
using System.IO;
using TickZoom.Api;
using TickZoom.TickUtil;

namespace TickZoom.FIX
{
    public class SimulateSymbolSyncTicks : SimulateSymbol, LogAware
    {
		private static Log log = Factory.SysLog.GetLogger(typeof(SimulateSymbolSyncTicks));
        private volatile bool debug;
        private volatile bool trace;
        private volatile bool verbose;
        public void RefreshLogLevel()
        {
            debug = log.IsDebugEnabled;
            trace = log.IsTraceEnabled;
            verbose = log.IsVerboseEnabled;
        }
        private FillSimulator fillSimulator;
		private TickFile reader;
        private Action<long> onEndTick;
        private Action<long, SymbolInfo, Tick> onTick;
		private Task queueTask;
		private TickSync tickSync;
		private SymbolInfo symbol;
		private TickIO nextTick = Factory.TickUtil.TickIO();
		private bool isFirstTick = true;
		private FIXSimulatorSupport fixSimulatorSupport;
        private QuoteSimulatorSupport quoteSimulatorSupport;
        private LatencyMetric latency;
		private long tickCounter = 0;
	    private int diagnoseMetric;
        private TickIO currentTick = Factory.TickUtil.TickIO();
        private TickIO temporaryTick = Factory.TickUtil.TickIO();
        private string symbolString;
        private Agent agent;
        private PartialFillSimulation PartialFillSimulation;
        private long id;
        private bool endOfTickData;
        public Agent Agent
        {
            get { return agent; }
            set { agent = value; }
        }
	
		public SimulateSymbolSyncTicks( FIXSimulatorSupport fixSimulatorSupport, 
            QuoteSimulatorSupport quoteSimulatorSupport,
		    string symbolString,
            PartialFillSimulation partialFillSimulation,
            TimeStamp endTime,
            long id)
		{
            log.Register(this);
            this.id = id;
            this.fixSimulatorSupport = fixSimulatorSupport;
		    this.quoteSimulatorSupport = quoteSimulatorSupport;
            this.onTick = quoteSimulatorSupport.OnTick;
            this.onEndTick = quoteSimulatorSupport.OnEndTick;
            this.PartialFillSimulation = partialFillSimulation;
		    this.symbolString = symbolString;
			this.symbol = Factory.Symbol.LookupSymbol(symbolString);
            fillSimulator = Factory.Utility.FillSimulator("FIX", Symbol, false, true, null);
		    fillSimulator.EnableSyncTicks = SyncTicks.Enabled;
            FillSimulator.OnPhysicalFill = fixSimulatorSupport.OnPhysicalFill;
            FillSimulator.OnRejectOrder = fixSimulatorSupport.OnRejectOrder;
            fillSimulator.PartialFillSimulation = partialFillSimulation;
            tickSync = SyncTicks.GetTickSync(Symbol.BinaryIdentifier);
            latency = new LatencyMetric("SimulateSymbolSyncTicks-" + symbolString.StripInvalidPathChars());
            diagnoseMetric = Diagnose.RegisterMetric("Simulator");
            if (debug) log.Debug("Opening tick file for reading.");
            reader = Factory.TickUtil.TickFile();
            try
            {
                reader.Initialize("Test\\MockProviderData", symbolString, TickFileMode.Read);
                reader.EndTime = endTime;
            }
            catch( FileNotFoundException ex)
            {
                log.Info("File for symbol " + symbolString + " not found: " + ex.Message);
            }
		}

        public void Shutdown()
        {
            Dispose();
        }

        public void Initialize(Task task)
        {
            queueTask = task;
            queueTask.Name = "SimulateSymbolSyncTicks-" + symbolString;
            queueTask.Scheduler = Scheduler.RoundRobin;
            quoteSimulatorSupport.QuotePacketQueue.ConnectOutbound(queueTask);
            queueTask.Start();
            tickSync.ChangeCallBack = TickSyncChangedEvent;
        }


        public Yield Invoke()
        {
            LatencyManager.IncrementSymbolHandler();
            if (!tickSync.TryLock())
            {
                TryCompleteTick();
                return Yield.NoWork.Repeat;
            }
            else
            {
                if (trace) log.Trace("Locked tickSync for " + Symbol);
            }
            if( !endOfTickData)
            {
                DequeueTick();
                return Yield.NoWork.Repeat;
            }
            return Yield.DidWork.Repeat;
        }

        private void TickSyncChangedEvent()
        {
            if (tickSync.Completed || tickSync.SentProcessPhysicalOrders || tickSync.SentReprocessPhysicalOrders)
            {
                if (verbose) log.Verbose("TickSyncChangedEvent(" + symbol + ") resuming task.");
                queueTask.Resume();
            }
            else
            {
                if (verbose) log.Verbose("TickSyncChangedEvent(" + symbol + ") not ready to resume task.");
            }
        }

        private void TryCompleteTick()
        {
            if (endOfTickData)
            {
                if (tickSync.SentSwtichBrokerState)
                {
                    tickSync.ClearSwitchBrokerState("Finished.");
                }
                if( tickSync.SentOrderChange)
                {
                    tickSync.RemoveOrderChange();
                }
            }
            if (!endOfTickData && tickSync.Completed)
            {
		    	if( verbose) log.Verbose("TryCompleteTick() Next Tick");
		    	tickSync.Clear();
            }
            else if (tickSync.OnlyProcessPhysicalOrders)
            {
                if (trace) log.Trace("Process physical orders - " + tickSync);
                FillSimulator.StartTick(nextTick);
                if (FillSimulator.IsChanged)
                {
                    FillSimulator.ProcessOrders();
                }
                tickSync.RemoveProcessPhysicalOrders();
            }
            else if (tickSync.OnlyReprocessPhysicalOrders || FillSimulator.IsChanged)
            {
                if (trace) log.Trace("Reprocess physical orders - " + tickSync);
                if( FillSimulator.IsChanged)
                {
                    FillSimulator.ProcessOrders();
                }
                if( tickSync.SentReprocessPhysicalOrders)
                {
                    tickSync.ClearReprocessPhysicalOrders();
                }
            }
        }

		private void DequeueTick() {
            LatencyManager.IncrementSymbolHandler();

            if (!reader.TryReadTick(temporaryTick))
            {
                if( onEndTick == null)
                {
                    throw new ApplicationException("OnEndTick was null");
                }
                onEndTick(id);
                endOfTickData = true;
                queueTask.Resume();
                if (debug) log.Debug("End Of Tick Data.");
                return;
            }
			tickCounter++;
            if (isFirstTick)
            {
                currentTick.Inject(temporaryTick.Extract());
            }
            else
            {
                currentTick.Inject(nextTick.Extract());
            }
            isFirstTick = false;
            FillSimulator.StartTick(currentTick);
            nextTick.Inject(temporaryTick.Extract());
            tickSync.AddTick(nextTick);
		    if( trace) log.Trace("Dequeue tick " + nextTick.UtcTime + "." + nextTick.UtcTime.Microsecond);
		    ProcessOnTickCallBack();
		}
		
		private Message quoteMessage;
		private void ProcessOnTickCallBack() {
            LatencyManager.IncrementSymbolHandler();
            if (quoteMessage == null)
            {
                quoteMessage = quoteSimulatorSupport.QuoteSocket.MessageFactory.Create();
            }
			onTick( id, Symbol, nextTick);
            queueTask.Pause();
            TickSyncChangedEvent();
        }

        public void TryProcessAdjustments()
        {
            FillSimulator.ProcessAdjustments();
        }

        public void ChangeOrder(CreateOrChangeOrder order)
        {
            FillSimulator.OnChangeBrokerOrder(order);
        }

        public void CreateOrder(CreateOrChangeOrder order)
        {
            FillSimulator.OnCreateBrokerOrder(order);
        }

        public void CancelOrder(CreateOrChangeOrder order)
        {
            FillSimulator.OnCancelBrokerOrder(order);
        }

        public CreateOrChangeOrder GetOrderById(long clientOrderId)
        {
            return FillSimulator.GetOrderById(clientOrderId);
        }

        protected volatile bool isDisposed = false;
	    public void Dispose() 
	    {
	        Dispose(true);
	        GC.SuppressFinalize(this);      
	    }
	
	    protected virtual void Dispose(bool disposing)
	    {
       		if( !isDisposed) {
	            isDisposed = true;   
	            if (disposing) {
                    if (debug) log.Debug("Dispose()");
                    if (queueTask != null)
                    {
                        queueTask.Stop();
                        queueTask.Join();
                    }
	            	if( reader != null) {
	            		reader.Dispose();
	            	}
                    if( fillSimulator != null)
                    {
                        if (debug) log.Debug("Setting fillSimulator.IsOnline false");
                        fillSimulator.IsOnline = false;
                    }
                    else
                    {
                        if (debug) log.Debug("fillSimulator is null.");
                    }
                    tickSync.ForceClear("DisposeSimulator");
                }
    		}
            else
       		{
                if (debug) log.Debug("isDisposed " + isDisposed);
            }
	    }    
	        
	    public FillSimulator FillSimulator
	    {
	        get { return fillSimulator; }
	    }

        public bool IsOnline
        {
            get { return FillSimulator.IsOnline; }
            set { fillSimulator.IsOnline = value; }
        }

        public SymbolInfo Symbol
	    {
	        get { return symbol; }
	    }

        public int ActualPosition
        {
            get { return (int) FillSimulator.ActualPosition; }
        }

    }
}
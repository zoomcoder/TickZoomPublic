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
    public class SimulateSymbolRealTime : SimulateSymbol, LogAware
    {
		private static Log log = Factory.SysLog.GetLogger(typeof(SimulateSymbolRealTime));
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
		private Action<Message,SymbolInfo,Tick> onTick;
		private Task queueTask;
		private SymbolInfo symbol;
		private TickIO nextTick = Factory.TickUtil.TickIO();
		private bool isFirstTick = true;
		private FIXSimulatorSupport fixSimulatorSupport;
		private LatencyMetric latency;
		private long tickCounter = 0;
	    private int diagnoseMetric;
        private TickIO currentTick = Factory.TickUtil.TickIO();
        private TickIO temporaryTick = Factory.TickUtil.TickIO();
        private string symbolString;
        private Agent agent;
        private PartialFillSimulation PartialFillSimulation;
        public Agent Agent
        {
            get { return agent; }
            set { agent = value; }
        }

        public SimulateSymbolRealTime(FIXSimulatorSupport fixSimulatorSupport, 
		    string symbolString,
            PartialFillSimulation partialFillSimulation,
		    Action<Message,SymbolInfo,Tick> onTick,
		    Action<PhysicalFill> onPhysicalFill,
		    Action<CreateOrChangeOrder,bool,string> onRejectOrder) {
            log.Register(this);
			this.fixSimulatorSupport = fixSimulatorSupport;
			this.onTick = onTick;
		    this.PartialFillSimulation = partialFillSimulation;
		    this.symbolString = symbolString;
			this.symbol = Factory.Symbol.LookupSymbol(symbolString);
            fillSimulator = Factory.Utility.FillSimulator("FIX", Symbol, false, true, null);
            FillSimulator.OnPhysicalFill = onPhysicalFill;
            FillSimulator.OnRejectOrder = onRejectOrder;
            fillSimulator.PartialFillSimulation = partialFillSimulation;
            latency = new LatencyMetric("SimulateSymbolRealTime-" + symbolString.StripInvalidPathChars());
            diagnoseMetric = Diagnose.RegisterMetric("Simulator");
            if (debug) log.Debug("Openning tick file for reading.");
            reader = new TickFile();
            try
            {
                reader.Initialize("Test\\MockProviderData", symbolString, TickFileMode.Read);
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
            fixSimulatorSupport.QuotePacketQueue.ConnectOutbound(queueTask);
            queueTask.Start();
        }


        public Yield Invoke()
        {
            LatencyManager.IncrementSymbolHandler();
            if( DequeueTick())
            {
                return Yield.DidWork.Repeat;
            }
            return Yield.NoWork.Repeat;
        }

		private bool DequeueTick() {
            LatencyManager.IncrementSymbolHandler();
            var result = false;

            if (!reader.TryReadTick(temporaryTick))
            {
                return result;
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
            if (FillSimulator.IsChanged)
            {
                FillSimulator.ProcessOrders();
            }
		    if( trace) log.Trace("Dequeue tick " + nextTick.UtcTime + "." + nextTick.UtcTime.Microsecond);
		    ProcessOnTickCallBack();
		    TryEnqueuePacket();
		    return true;
		}
		
		private Message quoteMessage;
		private void ProcessOnTickCallBack() {
            LatencyManager.IncrementSymbolHandler();
            if (quoteMessage == null)
            {
                quoteMessage = fixSimulatorSupport.QuoteSocket.MessageFactory.Create();
            }
			onTick( quoteMessage, Symbol, nextTick);
			if( trace) log.Trace("Added tick to packet: " + nextTick.UtcTime);
            quoteMessage.SendUtcTime = nextTick.UtcTime.Internal;
		}

		private void TryEnqueuePacket() {
            LatencyManager.IncrementSymbolHandler();
            if (quoteMessage.Data.GetBuffer().Length == 0)
            {
				return;
			}
		    fixSimulatorSupport.QuotePacketQueue.Enqueue(quoteMessage, quoteMessage.SendUtcTime);
			if( trace) log.Trace("Enqueued tick packet: " + new TimeStamp(quoteMessage.SendUtcTime));
            quoteMessage = fixSimulatorSupport.QuoteSocket.MessageFactory.Create();
            queueTask.Pause();
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

        public CreateOrChangeOrder GetOrderById(string clientOrderId)
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
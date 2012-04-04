using System;
using System.Collections.Generic;
using TickZoom.Api;

namespace TickZoom.FIX
{
    public class ProviderSimulatorSupport : AgentPerformer
    {
        private static Log log = Factory.SysLog.GetLogger(typeof(ProviderSimulatorSupport));
        private volatile bool debug;
        private volatile bool trace;
        private volatile bool verbose;
        public virtual void RefreshLogLevel()
        {
            debug = log.IsDebugEnabled;
            trace = log.IsTraceEnabled;
            verbose = log.IsVerboseEnabled;
        }

        private Agent agent;
        private Task task;
        private string mode;
        private SimpleLock symbolHandlersLocker = new SimpleLock();
        private Dictionary<long, SimulateSymbol> symbolHandlers = new Dictionary<long, SimulateSymbol>();
        private long nextSimulateSymbolId;
        private PartialFillSimulation partialFillSimulation;
        private TimeStamp endTime;
        private bool isOrderServerOnline = false;
        private FIXSimulatorSupport fixSimulator;
        private QuoteSimulatorSupport quotesSimulator;
        private QueueFilter filter;

        public ProviderSimulatorSupport(string mode, ProjectProperties projectProperties, Type fixSimulatorType, Type quoteSimulatorType)
        {
            this.mode = mode;
            partialFillSimulation = projectProperties.Simulator.PartialFillSimulation;
            this.endTime = projectProperties.Starter.EndTime;
            fixSimulator = (FIXSimulatorSupport) Factory.Parallel.SpawnPerformer(fixSimulatorType, mode, projectProperties, this);
            quotesSimulator = (QuoteSimulatorSupport) Factory.Parallel.SpawnPerformer(quoteSimulatorType, mode, projectProperties, this);
        }

        public void FlushFillQueues()
        {
            var handlers = new List<SimulateSymbol>();
            using (symbolHandlersLocker.Using())
            {
                if (debug) log.Debug("Flushing all fill queues.");
                foreach (var kvp in symbolHandlers)
                {
                    handlers.Add(kvp.Value);
                }
            }
            foreach (var handler in handlers)
            {
                handler.FillSimulator.FlushFillQueue();
            }
            if (debug) log.Debug("Current FIX Simulator orders.");
            foreach (var handler in handlers)
            {
                handler.FillSimulator.LogActiveOrders();
            }
        }

        public void SwitchBrokerState(string description, bool isOnline)
        {
            foreach (var kvp in symbolHandlers)
            {
                var symbolBinary = kvp.Key;
                var handler = kvp.Value;
                var tickSync = SyncTicks.GetTickSync(symbolBinary);
                tickSync.SetSwitchBrokerState(description);
                if (handler.IsOnline != isOnline)
                {
                    handler.IsOnline = isOnline;
                    if (!isOnline)
                    {
                        while (tickSync.SentPhyscialOrders)
                        {
                            tickSync.RemovePhysicalOrder("Rollback");
                        }
                        while (tickSync.SentOrderChange)
                        {
                            tickSync.RemoveOrderChange();
                        }
                        while (tickSync.SentPhysicalFillsCreated)
                        {
                            tickSync.RemovePhysicalFill("Rollback");
                        }
                        while (tickSync.SentPositionChange)
                        {
                            tickSync.RemovePositionChange("Rollback");
                        }
                        while (tickSync.SentWaitingMatch)
                        {
                            tickSync.RemoveWaitingMatch("Rollback");
                        }
                    }
                }
            }
        }

        public void AddSymbol(string symbol)
        {
            var symbolInfo = Factory.Symbol.LookupSymbol(symbol);
            using (symbolHandlersLocker.Using())
            {
                if (!symbolHandlers.ContainsKey(symbolInfo.BinaryIdentifier))
                {
                    if (SyncTicks.Enabled)
                    {
                        var symbolHandler = (SimulateSymbol)Factory.Parallel.SpawnPerformer(typeof(SimulateSymbolSyncTicks),
                                                                                            fixSimulator, quotesSimulator, symbol, partialFillSimulation, endTime, nextSimulateSymbolId++);
                        symbolHandlers.Add(symbolInfo.BinaryIdentifier, symbolHandler);
                    }
                    else
                    {
                        var symbolHandler = (SimulateSymbol)Factory.Parallel.SpawnPerformer(typeof(SimulateSymbolRealTime),
                                                                                            fixSimulator, quotesSimulator, symbol, partialFillSimulation, nextSimulateSymbolId++);
                        symbolHandlers.Add(symbolInfo.BinaryIdentifier, symbolHandler);
                    }
                }
            }
            if (IsOrderServerOnline)
            {
                SetOrderServerOnline();
            }
        }

        public void SetOrderServerOnline()
        {
            using (symbolHandlersLocker.Using())
            {
                foreach (var kvp in symbolHandlers)
                {
                    var handler = kvp.Value;
                    handler.IsOnline = true;
                }
            }
            if (!IsOrderServerOnline)
            {
                IsOrderServerOnline = true;
                log.Info("Order server back online.");
            }
        }

        public void SetOrderServerOffline()
        {
            using (symbolHandlersLocker.Using())
            {
                foreach (var kvp in symbolHandlers)
                {
                    var handler = kvp.Value;
                    handler.IsOnline = false;
                }
            }
            IsOrderServerOnline = false;
        }

        public int GetPosition(SymbolInfo symbol)
        {
            // Don't lock. This call always wrapped in a locked using clause.
            SimulateSymbol symbolSyncTicks;
            if (symbolHandlers.TryGetValue(symbol.BinaryIdentifier, out symbolSyncTicks))
            {
                return symbolSyncTicks.ActualPosition;
            }
            return 0;
        }

        public void CreateOrder(CreateOrChangeOrder order)
        {
            SimulateSymbol symbolSyncTicks;
            using (symbolHandlersLocker.Using())
            {
                symbolHandlers.TryGetValue(order.Symbol.BinaryIdentifier, out symbolSyncTicks);
            }
            if (symbolSyncTicks != null)
            {
                symbolSyncTicks.CreateOrder(order);
            }
        }

        public void TryProcessAdustments(CreateOrChangeOrder order)
        {
            SimulateSymbol symbolSyncTicks;
            using (symbolHandlersLocker.Using())
            {
                symbolHandlers.TryGetValue(order.Symbol.BinaryIdentifier, out symbolSyncTicks);
            }
            if (symbolSyncTicks != null)
            {
                symbolSyncTicks.TryProcessAdjustments();
            }
        }

        public void ChangeOrder(CreateOrChangeOrder order)
        {
            SimulateSymbol symbolSyncTicks;
            using (symbolHandlersLocker.Using())
            {
                symbolHandlers.TryGetValue(order.Symbol.BinaryIdentifier, out symbolSyncTicks);
            }
            if (symbolSyncTicks != null)
            {
                symbolSyncTicks.ChangeOrder(order);
            }
        }

        public void CancelOrder(CreateOrChangeOrder order)
        {
            SimulateSymbol symbolSyncTicks;
            using (symbolHandlersLocker.Using())
            {
                symbolHandlers.TryGetValue(order.Symbol.BinaryIdentifier, out symbolSyncTicks);
            }
            if (symbolSyncTicks != null)
            {
                symbolSyncTicks.CancelOrder(order);
            }
        }

        public CreateOrChangeOrder GetOrderById(SymbolInfo symbol, long clientOrderId)
        {
            SimulateSymbol symbolSyncTicks;
            using (symbolHandlersLocker.Using())
            {
                symbolHandlers.TryGetValue(symbol.BinaryIdentifier, out symbolSyncTicks);
            }
            if (symbolSyncTicks != null)
            {
                return symbolSyncTicks.GetOrderById(clientOrderId);
            }
            else
            {
                throw new ApplicationException("StartSymbol was never called for " + symbol + " so now symbol handler was found.");
            }
        }

        public void Shutdown()
        {
            Dispose();
        }

        public int Count
        {
            get { return symbolHandlers.Count; }
        }

        public bool IsOrderServerOnline
        {
            get { return isOrderServerOnline; }
            set { isOrderServerOnline = value; }
        }

        public long NextSimulateSymbolId
        {
            get { return nextSimulateSymbolId; }
        }

        public Agent Agent
        {
            get { return agent; }
            set { agent = value; }
        }

        public void Initialize(Task task)
        {
            this.task = task;
            filter = task.GetFilter();
            task.Scheduler = Scheduler.EarliestTime;
            task.Start();
            if (debug) log.Debug("Starting Provider Simulator Support.");
        }

        public Yield Invoke()
        {
            return Yield.NoWork.Repeat;
        }

        protected volatile bool isDisposed = false;
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!isDisposed)
            {
                isDisposed = true;
                if (disposing)
                {
                    if (debug) log.Debug("Dispose()");
                    if( fixSimulator != null)
                    {
                        fixSimulator.Dispose();
                    }
                    if( quotesSimulator != null)
                    {
                        quotesSimulator.Dispose();
                    }
                    if (debug) log.Debug("ShutdownHandlers()");
                    if (symbolHandlers != null)
                    {
                        using (symbolHandlersLocker.Using())
                        {
                            if (debug) log.Debug("There are " + symbolHandlers.Count + " symbolHandlers.");
                            foreach (var kvp in symbolHandlers)
                            {
                                var handler = kvp.Value;
                                if (debug) log.Debug("Disposing symbol handler " + handler);
                                handler.Agent.SendEvent(new EventItem(EventType.Shutdown));
                            }
                            symbolHandlers.Clear();
                        }
                    }
                    else
                    {
                        if (debug) log.Debug("symbolHandlers is null.");
                    }
                    if (task != null)
                    {
                        task.Stop();
                    }
                }
            }
        }
    }
}
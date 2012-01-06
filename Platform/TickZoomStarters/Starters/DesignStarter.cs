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
using System.ComponentModel;
using System.Threading;
using TickZoom.Api;

namespace TickZoom.Starters
{
    /// <summary>
    /// This starter is use by the GUI to initialize model for
    /// browsing of properties.
    /// </summary>
    public class DesignStarter : StarterCommon
    {
        Log log = Factory.SysLog.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        TickEngine engine;

        public DesignStarter()
        {
        }

        bool CancelPending
        {
            get
            {
                if (BackgroundWorker != null)
                {
                    return BackgroundWorker.CancellationPending;
                }
                else
                {
                    return false;
                }
            }
        }

        public override void Run(ModelInterface model)
        {
            Factory.Parallel.SetMode(parallelMode);
            Factory.SysLog.ResetConfiguration();
            engine = Factory.Engine.TickEngine("Design");
            engine.MaxBarsBack = 2;
            engine.MaxTicksBack = 2;
            SymbolInfo symbolInfo = Factory.Symbol.LookupSymbol("Design");
            engine.SymbolInfo = new SymbolInfo[] { symbolInfo };
            engine.Providers = SetupProviders(false, false);
            engine.IntervalDefault = ProjectProperties.Starter.IntervalDefault;
            engine.RunMode = RunMode.Historical;
            engine.EnableTickFilter = ProjectProperties.Engine.EnableTickFilter;

            if (CancelPending) return;

            engine.Model = model;

            engine.QueueTask();
            engine.WaitTask();
            var parallel = Factory.Parallel;
            parallel.Dispose();
            while (!parallel.IsFinalized)
            {
                Thread.Sleep(100);
            }
            parallel.Release();

        }

        public override void Wait()
        {
            engine.WaitTask();
            var parallel = Factory.Parallel;
            parallel.Dispose();
            while (!parallel.IsFinalized)
            {
                Thread.Sleep(100);
            }
            parallel.Release();
        }

        public override Agent[] SetupProviders(bool quietMode, bool singleLoad)
        {
            return new Agent[] { Factory.Parallel.SpawnProvider(typeof(DesignAgent)) };
        }


        public class DesignAgent : Agent
        {
            private volatile bool isDisposed = false;

            private DesignAgent()
            {

            }

            public Receiver GetReceiver()
            {
                throw new NotImplementedException();
            }

            public void StartSymbol(Receiver receiver, SymbolInfo symbol, object eventDetail)
            {
                var tickPool = Factory.Parallel.TickPool(symbol);
                TickIO tickIO = Factory.TickUtil.TickIO();
                tickIO.Initialize();
                tickIO.SetSymbol(symbol.BinaryIdentifier);
                tickIO.SetTime(new TimeStamp(2000, 1, 1));
                tickIO.SetQuote(100D, 100D);
                var item = new EventItem(symbol, (int)EventType.StartHistorical);
                receiver.SendEvent(item);
                var binaryBox = tickPool.Create();
                var tickId = binaryBox.TickBinary.Id;
                binaryBox.TickBinary = tickIO.Extract();
                binaryBox.TickBinary.Id = tickId;
                item = new EventItem(symbol, (int)EventType.Tick, binaryBox);
                receiver.SendEvent(item);
                tickIO.Initialize();
                tickIO.SetSymbol(symbol.BinaryIdentifier);
                tickIO.SetTime(new TimeStamp(2000, 1, 2));
                tickIO.SetQuote(101D, 101D);
                binaryBox = tickPool.Create();
                tickId = binaryBox.TickBinary.Id;
                binaryBox.TickBinary = tickIO.Extract();
                binaryBox.TickBinary.Id = tickId;
                item = new EventItem(symbol, (int)EventType.Tick, binaryBox);
                receiver.SendEvent(item);
                item = new EventItem(symbol, (int)EventType.EndHistorical);
                receiver.SendEvent(item);
            }

            public void StopSymbol(Receiver receiver, SymbolInfo symbol)
            {
            }

            public void PositionChange(Receiver receiver, SymbolInfo symbol, double signal, Iterable<LogicalOrder> orders)
            {
                throw new NotImplementedException();
            }

            public void Signal(Receiver receiver, string symbol, double signal)
            {
            }

            public void Start(Receiver receiver)
            {
            }

            public void Stop(Receiver receiver)
            {
            }

            private volatile bool isFinalized;
            public bool IsFinalized
            {
                get { return isFinalized; }
            }

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
                    isFinalized = true;
                }
            }

            public bool SendEvent(EventItem eventItem)
            {
                var result = true;
                var receiver = eventItem.Receiver;
                var symbol = eventItem.Symbol;
                var eventType = eventItem.EventType;
                var eventDetail = eventItem.EventDetail;
                switch ((EventType)eventType)
                {
                    case EventType.Initialize:
                        // Nothing to do.
                        break;
                    case EventType.Connect:
                        Start(receiver);
                        break;
                    case EventType.Disconnect:
                        Stop(receiver);
                        break;
                    case EventType.StartSymbol:
                        StartSymbol(receiver, symbol, eventDetail);
                        break;
                    case EventType.StopSymbol:
                        StopSymbol(receiver, (SymbolInfo)eventDetail);
                        break;
                    case EventType.PositionChange:
                        PositionChangeDetail positionChange = (PositionChangeDetail)eventDetail;
                        PositionChange(receiver, symbol, positionChange.Position, positionChange.Orders);
                        break;
                    case EventType.Terminate:
                    case EventType.RemoteShutdown:
                        Dispose();
                        break;
                        break;
                    default:
                        throw new ApplicationException("Unexpected event type: " + (EventType)eventType);
                }
                return result;
            }
        }
    }
}
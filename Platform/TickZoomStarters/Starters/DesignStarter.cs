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


        public class DesignAgent : AgentPerformer
        {
            private volatile bool isDisposed = false;
            private Task task;

            private DesignAgent()
            {

            }

            public void Initialize( Task task)
            {
                this.task = task;
                task.Scheduler = Scheduler.EarliestTime;
                task.Start();
            }

            public void Shutdown()
            {
                Dispose();
            }

            public void StartSymbol(EventItem eventItem)
            {
                var tickPool = Factory.Parallel.TickPool(eventItem.Symbol);
                TickIO tickIO = Factory.TickUtil.TickIO();
                tickIO.Initialize();
                tickIO.SetSymbol(eventItem.Symbol.BinaryIdentifier);
                tickIO.SetTime(new TimeStamp(2000, 1, 1));
                tickIO.SetQuote(100D, 100D);
                var item = new EventItem(eventItem.Symbol, EventType.StartHistorical);
                eventItem.Agent.SendEvent(item);
                var binaryBox = tickPool.Create();
                var tickId = binaryBox.TickBinary.Id;
                binaryBox.TickBinary = tickIO.Extract();
                binaryBox.TickBinary.Id = tickId;
                item = new EventItem(eventItem.Symbol, EventType.Tick, binaryBox);
                eventItem.Agent.SendEvent(item);
                tickIO.Initialize();
                tickIO.SetSymbol(eventItem.Symbol.BinaryIdentifier);
                tickIO.SetTime(new TimeStamp(2000, 1, 2));
                tickIO.SetQuote(101D, 101D);
                binaryBox = tickPool.Create();
                tickId = binaryBox.TickBinary.Id;
                binaryBox.TickBinary = tickIO.Extract();
                binaryBox.TickBinary.Id = tickId;
                item = new EventItem(eventItem.Symbol, EventType.Tick, binaryBox);
                eventItem.Agent.SendEvent(item);
                item = new EventItem(eventItem.Symbol, EventType.EndHistorical);
                eventItem.Agent.SendEvent(item);
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
                    if( task != null)
                    {
                        task.Stop();
                    }
                    isFinalized = true;
                }
            }

            public Yield Invoke()
            {
                EventItem eventItem;
                if( task.Filter.Receive(out eventItem))
                {
                    switch( eventItem.EventType)
                    {
                        case EventType.Connect:
                            break;
                        case EventType.Disconnect:
                            break;
                        case EventType.StartSymbol:
                            StartSymbol(eventItem);
                            break;
                        case EventType.StopSymbol:
                            break;
                        case EventType.PositionChange:
                            break;
                        case EventType.Terminate:
                            break;
                        default:
                            throw new ApplicationException("Unexpected event type: " + eventItem.EventType);
                    }
                    task.Filter.Pop();
                }
                return Yield.NoWork.Repeat;
            }
        }
    }
}
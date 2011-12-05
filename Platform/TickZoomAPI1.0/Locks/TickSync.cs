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
using System.Runtime.InteropServices;
using System.Threading;

namespace TickZoom.Api
{
    [StructLayout(LayoutKind.Sequential)]
    unsafe public struct TickSyncState
    {
        public int isLocked;
        public long symbolBinaryId;
        public int ticks;
        public int positionChange;
        public int processPhysical;
        public int reprocessPhysical;
        public int physicalFillsCreated;
        public int physicalFillsWaiting;
        public int physicalOrders;
        public int physicalFillSimulators;
        public int switchBrokerState;
        public bool Compare(TickSyncState other)
        {
            return ticks == other.ticks && positionChange == other.positionChange && switchBrokerState == other.switchBrokerState &&
                   processPhysical == other.processPhysical && physicalFillsCreated == other.physicalFillsCreated &&
                   physicalFillsWaiting == other.physicalFillsWaiting &&
                   reprocessPhysical == other.reprocessPhysical && physicalOrders == other.physicalOrders;

        }
    }

    public unsafe class TickSync
    {
        private static readonly Log staticLog = Factory.SysLog.GetLogger(typeof(TickSync));
        private readonly bool debug = staticLog.IsDebugEnabled;
        private readonly bool trace = staticLog.IsTraceEnabled;
        private Log log;
        private TickSyncState* state;
        private TickSyncState* rollback;
        private SymbolInfo symbolInfo;
        private string symbol;
        private bool rollbackNeeded = false;

        internal TickSync(long symbolId, TickSyncState* tickSyncPtr)
        {
            this.state = tickSyncPtr;
            this.rollback = state + 1;
            (*state).symbolBinaryId = symbolId;
            (*rollback).symbolBinaryId = symbolId;
            this.symbolInfo = Factory.Symbol.LookupSymbol(symbolId);
            this.symbol = symbolInfo.Symbol.StripInvalidPathChars();
            this.log = Factory.SysLog.GetLogger(typeof(TickSync).FullName + "." + symbol);
            if (trace) log.Trace("created with binary symbol id = " + symbolId);
        }

        public bool Completed
        {
            get
            {
                var value = CheckCompletedInternal();
                return value;
            }
        }
        private bool CheckCompletedInternal()
        {
            return (*state).ticks == 0 && (*state).positionChange == 0 && (*state).switchBrokerState == 0 && 
                   (*state).physicalOrders == 0 && (*state).physicalFillsCreated == 0 &&
                   (*state).processPhysical == 0 && (*state).reprocessPhysical == 0;
        }
        private bool CheckOnlyProcessingOrders()
        {
            return (*state).physicalOrders == 0 &&
                   (*state).physicalFillsCreated == 0 && (*state).processPhysical > 0;
        }

        private bool CheckOnlyReprocessOrders()
        {
            return (*state).physicalOrders == 0 && (*state).reprocessPhysical > 0;
            // && (*state).physicalFillSimulators == 1;
        }

        private bool CheckProcessingOrders()
        {
            return (*state).positionChange > 0 || (*state).physicalOrders > 0 || (*state).switchBrokerState > 0 ||
                   (*state).physicalFillsCreated > 0 || (*state).processPhysical > 0 ||
                   (*state).reprocessPhysical > 0;
        }

        private bool CheckOnlyOneFillLeft()
        {
            return (*state).positionChange > 0 || (*state).physicalOrders > 0 ||
                   (*state).physicalFillsCreated == 1 || (*state).processPhysical > 0 ||
                   (*state).reprocessPhysical > 0;
        }

        public void CaptureState()
        {
            *rollback = *state;
            if (trace) log.Trace("Captured state for rollback: " + ToString(*rollback));
        }

        public void Clear()
        {
            if (!CheckCompletedInternal())
            {
                log.Error("All counters must complete to 0 before clearing the tick sync. Currently: " + this);
                //System.Diagnostics.Debugger.Break();
                //throw new ApplicationException("Tick, position changes, physical orders, and physical fills, must all complete before clearing the tick sync. Current numbers are: " + this);
            }
            ForceClear();
        }

        public bool TryLock()
        {
            return Interlocked.CompareExchange(ref (*state).isLocked, 1, 0) == 0;
        }

        public bool IsLocked
        {
            get { return (*state).isLocked == 1; }
        }

        public void Unlock()
        {
            Interlocked.Exchange(ref (*state).isLocked, 0);
        }

        public void ForceClear()
        {
            Interlocked.Exchange(ref (*state).ticks, 0);
            Interlocked.Exchange(ref (*state).physicalOrders, 0);
            Interlocked.Exchange(ref (*state).processPhysical, 0);
            Interlocked.Exchange(ref (*state).reprocessPhysical, 0);
            Interlocked.Exchange(ref (*state).positionChange, 0);
            Interlocked.Exchange(ref (*state).switchBrokerState, 0);
            Interlocked.Exchange(ref (*state).physicalFillsCreated, 0);
            Interlocked.Exchange(ref (*state).physicalFillsWaiting, 0);
            Unlock();
            if (trace) log.Trace("ForceClear() " + this);
        }

        public void ForceClearOrders()
        {
            Interlocked.Exchange(ref (*state).physicalOrders, 0);
            Interlocked.Exchange(ref (*state).positionChange, 0);
            Interlocked.Exchange(ref (*state).processPhysical, 0);
            Interlocked.Exchange(ref (*state).reprocessPhysical, 0);
            Interlocked.Exchange(ref (*state).physicalFillsCreated, 0);
            Interlocked.Exchange(ref (*state).physicalFillsWaiting, 0);
            if (trace) log.Trace("ForceClearOrders() " + this);
        }

        public override string ToString()
        {
            return ToString(*state);
        }

        private string ToString(TickSyncState temp)
        {
            return "TickSync Ticks " + temp.ticks + ", Sent Orders " + temp.physicalOrders + ", Changes " + temp.positionChange + ", Switch Broker " + temp.switchBrokerState + ", Process Orders " + temp.processPhysical + ", Reprocess " + temp.reprocessPhysical + ", Fills Created " + temp.physicalFillsCreated + ", Fills Waiting " + temp.physicalFillsWaiting + ", Simulators " + temp.physicalFillSimulators;
        }

        public void AddTick(Tick tick)
        {
            var value = Interlocked.Increment(ref (*state).ticks);
            if (trace) log.Trace("AddTick(" + tick + ") " + this);
            if( value > 1)
            {
                throw new ApplicationException("Tick counter was allowed to go over 1.");
            }
        }
        public void RemoveTick(ref TickBinary tick)
        {
            var value = Interlocked.Decrement(ref (*state).ticks);
            if (trace) log.Trace("RemoveTick(" + value + "," + tick + ") " + this);
            if (value < 0)
            {
                var temp = Interlocked.Increment(ref (*state).ticks);
                if (debug) log.Debug("Tick counter was " + value + ". Incremented to " + temp);
            }
        }

        public void AddPhysicalFill(object fill)
        {
            RollbackPhysicalFills();
            var valueCreated = Interlocked.Increment(ref (*state).physicalFillsCreated);
            var valueWaiting = Interlocked.Increment(ref (*state).physicalFillsWaiting);
            if (trace) log.Trace("AddPhysicalFill( Created " + valueCreated + ", Waiting " + valueWaiting + ", Fill " + fill + ") " + this);
        }

        public void RollbackPhysicalFills()
        {
            var resultCreated = false;
            var resultWaiting = false;
            while ((*rollback).physicalFillsCreated > 0 && (*state).physicalFillsCreated > 0)
            {
                Interlocked.Decrement(ref (*state).physicalFillsCreated);
                Interlocked.Decrement(ref (*rollback).physicalFillsCreated);
                resultCreated = true;
            }
            while ((*rollback).physicalFillsWaiting > 0 && (*state).physicalFillsWaiting > 0)
            {
                Interlocked.Decrement(ref (*state).physicalFillsWaiting);
                Interlocked.Decrement(ref (*rollback).physicalFillsWaiting);
                resultWaiting = true;
            }
            if (trace && (resultCreated || resultWaiting)) log.Trace("RollbackPhysicalFills( Created " + (*state).physicalFillsCreated + ", Waiting " + (*state).physicalFillsCreated);
        }

        public void RemovePhysicalFill(object fill)
        {
            var valueCreated = Interlocked.Decrement(ref (*state).physicalFillsCreated);
            var valueWaiting = Interlocked.Decrement(ref (*state).physicalFillsWaiting);
            if (trace) log.Trace("RemovePhysicalFill( Created " + valueCreated + ", Waiting " + valueWaiting + ", " + fill + ") " + this);
            if (valueCreated < 0)
            {
                var temp = Interlocked.Increment(ref (*state).physicalFillsCreated);
                if (debug) log.Debug("physicalFillsCreated counter was " + valueCreated + ". Incremented to " + temp);
            }
            if (valueWaiting < 0)
            {
                var temp = Interlocked.Increment(ref (*state).physicalFillsWaiting);
                if (debug) log.Debug("physicalFillsWaiting counter was " + valueWaiting + ". Incremented to " + temp);
            }
        }

        public void RemovePhysicalFillWaiting(object fill)
        {
            var valueWaiting = Interlocked.Decrement(ref (*state).physicalFillsWaiting);
            if (trace) log.Trace("RemovePhysicalFillWaiting( Waiting " + valueWaiting + ", " + fill + ") " + this);
            if (valueWaiting < 0)
            {
                var temp = Interlocked.Increment(ref (*state).physicalFillsWaiting);
                if (debug) log.Debug("physicalFillsWaiting counter was " + valueWaiting + ". Incremented to " + temp);
            }
        }

        public void AddPhysicalFillSimulator(string name)
        {
            var value = Interlocked.Increment(ref (*state).physicalFillSimulators);
            if (trace) log.Trace("AddPhysicalFillSimulator( " + name + ") " + this);
        }

        public void RemovePhysicalFillSimulator(string name)
        {
            var value = Interlocked.Decrement(ref (*state).physicalFillSimulators);
            if (trace) log.Trace("RemovePhysicalFillSimulator( " + name + " ) " + this);
            if (value < 0)
            {
                var temp = Interlocked.Increment(ref (*state).physicalFillSimulators);
                if (debug) log.Debug("PhysicalFillSimulators counter was " + value + ". Incremented to " + temp);
            }
        }

        public void AddPhysicalOrder(object order)
        {
            var value = Interlocked.Increment(ref (*state).physicalOrders);
            RollbackPhysicalOrders();
            if (trace) log.Trace("AddPhysicalOrder(" + value + "," + order + ") " + this);
        }

        public void RollbackPhysicalOrders()
        {
            while ((*rollback).physicalOrders > 0 && (*state).physicalOrders > 0)
            {
                Interlocked.Decrement(ref (*state).physicalOrders);
                Interlocked.Decrement(ref (*rollback).physicalOrders);
            }
        }
        public void RemovePhysicalOrder(object order)
        {
            var value = Interlocked.Decrement(ref (*state).physicalOrders);
            if (trace) log.Trace("RemovePhysicalOrder(" + value + "," + order + ") " + this);
            if (value < 0)
            {
                var temp = Interlocked.Increment(ref (*state).physicalOrders);
                if (debug) log.Debug("PhysicalOrders counter was " + value + ". Incremented to " + temp);
            }
        }
        public void RemovePhysicalOrder()
        {
            var value = Interlocked.Decrement(ref (*state).physicalOrders);
            if (trace) log.Trace("RemovePhysicalOrder(" + value + ") " + this);
            if (value < 0)
            {
                var temp = Interlocked.Increment(ref (*state).physicalOrders);
                if (debug) log.Debug("PhysicalOrders counter was " + value + ". Incremented to " + temp);
            }
        }

        public void AddSwitchBrokerState(string description)
        {
            var value = Interlocked.Increment(ref (*state).switchBrokerState);
            if (trace) log.Trace("AddSwitchBrokerState(" + description + ", " + value + ") " + this);
        }

        public void RemoveSwitchBrokerState(string description)
        {
            var value = Interlocked.Decrement(ref (*state).switchBrokerState);
            if (trace) log.Trace("RemoveSwitchBrokerState(" + description + "," + value + ") " + this);
            if (value < 0)
            {
                var temp = Interlocked.Increment(ref (*state).switchBrokerState);
                if (debug) log.Debug("SwitchBrokerState counter was " + value + ". Incremented to " + temp);
            }
        }

        public void AddPositionChange(string description)
        {
            var value = Interlocked.Increment(ref (*state).positionChange);
            RollbackPositionChange();
            if (trace) log.Trace("AddPositionChange(" + description + ", " + value + ") " + this);
        }

        public void RollbackPositionChange()
        {
            while ((*rollback).positionChange > 0 && (*state).positionChange > 0)
            {
                Interlocked.Decrement(ref (*state).positionChange);
                Interlocked.Decrement(ref (*rollback).positionChange);
            }
        }

        public void RemovePositionChange(string description)
        {
            var value = Interlocked.Decrement(ref (*state).positionChange);
            if (trace) log.Trace("RemovePositionChange(" + description + "," + value + ") " + this);
            if (value < 0)
            {
                var temp = Interlocked.Increment(ref (*state).positionChange);
                if (debug) log.Debug("PositionChange counter was " + value + ". Incremented to " + temp);
            }
        }

        public void AddProcessPhysicalOrders()
        {
            var value = Interlocked.Increment(ref (*state).processPhysical);
            RollbackProcessPhysicalOrders();
            if (trace) log.Trace("AddProcessPhysicalOrders(" + value + ") " + this);
        }

        public void RollbackProcessPhysicalOrders()
        {
            while ((*rollback).processPhysical > 0)
            {
                if( (*state).processPhysical > 0)
                {
                    var temp = Interlocked.Decrement(ref (*state).processPhysical);
                    if (trace) log.Trace("PositionChange actual state rolled back to " + temp + " " + this);
                }
                {
                    var temp = Interlocked.Decrement(ref (*rollback).processPhysical);
                    if (trace) log.Trace("PositionChange rollback state rolled back to " + temp + " " + this);
                }
            }
        }

        public void RemoveProcessPhysicalOrders()
        {
            var value = Interlocked.Decrement(ref (*state).processPhysical);
            if (trace) log.Trace("RemoveProcessPhysicalOrders(" + value + ") " + this);
            if (value < 0)
            {
                var temp = Interlocked.Increment(ref (*state).processPhysical);
                if( debug) log.Debug("ProcessPhysical counter was " + value + ". Incremented to " + temp);
            }
        }

        public void SetReprocessPhysicalOrders()
        {
            if ((*state).reprocessPhysical == 0)
            {
                var value = Interlocked.Increment(ref (*state).reprocessPhysical);
                if (trace) log.Trace("SetReprocessPhysicalOrders(" + value + ") " + this);
            }
        }

        public void AddReprocessPhysicalOrders()
        {
            var value = Interlocked.Increment(ref (*state).reprocessPhysical);
            RollbackReprocessPhysicalOrders();
            if (trace) log.Trace("AddReprocessPhysicalOrders(" + value + ") " + this);
        }

        public void RollbackReprocessPhysicalOrders()
        {
            while ((*rollback).reprocessPhysical > 0)
            {
                if( (*state).reprocessPhysical > 0)
                {
                    Interlocked.Decrement(ref (*state).reprocessPhysical);
                }
                Interlocked.Decrement(ref (*rollback).reprocessPhysical);
            }
        }

        public void RemoveReprocessPhysicalOrders()
        {
            var value = Interlocked.Decrement(ref (*state).reprocessPhysical);
            if (trace) log.Trace("RemoveReprocessPhysicalOrders(" + value + ") " + this);
            if (value < 0)
            {
                var temp = Interlocked.Increment(ref (*state).reprocessPhysical);
                if (debug) log.Debug("ReprocessPhysical counter was " + value + ". Incremented to " + temp);
            }
        }

        public bool SentPhysicalFillsWaiting
        {
            get { return (*state).physicalFillsWaiting > 0; }
        }

        public bool SentPhysicalFillsCreated
        {
            get { return (*state).physicalFillsCreated > 0; }
        }

        public bool SentPositionChange
        {
            get { return (*state).positionChange > 0; }
        }

        public bool SentSwtichBrokerState
        {
            get { return (*state).switchBrokerState > 0; }
        }

        public bool OnlyReprocessPhysicalOrders
        {
            get { return CheckOnlyReprocessOrders(); }
        }

        public bool IsSinglePhysicalFillSimulator
        {
            get { return (*state).physicalFillSimulators == 1;  }
        }

        public bool OnlyProcessPhysicalOrders
        {
            get { return CheckOnlyProcessingOrders(); }
        }

        public bool IsProcessingOrders
        {
            get { return CheckProcessingOrders(); }
        }

        public bool IsOnlyOneFillLeft
        {
            get { return CheckOnlyOneFillLeft(); }
        }

        public bool SentProcessPhysicalOrders
        {
            get { return (*state).processPhysical > 0; }
        }

        public bool SentReprocessPhysicalOrders
        {
            get { return (*state).reprocessPhysical > 0; }
        }

        public bool SentPhyscialOrders
        {
            get { return (*state).physicalOrders > 0; }
        }
    }
}

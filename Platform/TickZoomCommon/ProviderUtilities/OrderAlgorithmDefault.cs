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
using System.Configuration;
using System.IO;
using System.Text;
using System.Threading;

using TickZoom.Api;

namespace TickZoom.Common
{
    public class OrderAlgorithmDefault : OrderAlgorithm, LogAware {
		private static readonly Log staticLog = Factory.SysLog.GetLogger(typeof(OrderAlgorithmDefault));
        private volatile bool trace = staticLog.IsTraceEnabled;
        private volatile bool debug = staticLog.IsDebugEnabled;
        public void RefreshLogLevel()
        {
            if (log != null)
            {
                debug = log.IsDebugEnabled;
                trace = log.IsTraceEnabled;
            }
        }
        private bool DoSyncTicks
        {
            get { return enableSyncTicks && !handleSimulatedExits; }
        }
        private Log log;
		private SymbolInfo symbol;
		private PhysicalOrderHandler physicalOrderHandler;
        private volatile bool bufferedLogicalsChanged = false;
        private List<CreateOrChangeOrder> originalPhysicals;
        private List<CreateOrChangeOrder> physicalOrders;
        private List<LogicalOrder> bufferedLogicals;
        private List<LogicalOrder> originalLogicals;
        private List<LogicalOrder> logicalOrders;
        private List<LogicalOrder> extraLogicals;
		private int desiredPosition;
		private Action<SymbolInfo,LogicalFillBinary> onProcessFill;
		private bool handleSimulatedExits = false;
		private TickSync tickSync;
	    private LogicalOrderCache logicalOrderCache;
        private bool isPositionSynced = false;
        private long minimumTick;
        private List<MissingLevel> missingLevels = new List<MissingLevel>();
        private PhysicalOrderCache physicalOrderCache;
        private long recency;
        private string name;
        private bool enableSyncTicks;
        private int rejectRepeatCounter;
        private int confirmedOrderCount;
        private bool isBrokerOnline;
        private bool receivedDesiredPosition;

        public class OrderArray<T>
        {
            private int capacity = 16;
            private T[] orders;
            public OrderArray()
            {
                orders = new T[capacity];
            }
        }

        public struct MissingLevel
        {
            public int Size;
            public long Price;
        }
		
		public OrderAlgorithmDefault(string name, SymbolInfo symbol, PhysicalOrderHandler brokerOrders, LogicalOrderCache logicalOrderCache, PhysicalOrderCache physicalOrderCache) {
			log = Factory.SysLog.GetLogger(typeof(OrderAlgorithmDefault).FullName + "." + symbol.Symbol.StripInvalidPathChars() + "." + name );
            log.Register(this);
			this.symbol = symbol;
		    this.logicalOrderCache = logicalOrderCache;
		    this.physicalOrderCache = physicalOrderCache;
		    this.name = name;
			tickSync = SyncTicks.GetTickSync(symbol.BinaryIdentifier);
			this.physicalOrderHandler = brokerOrders;
            this.originalLogicals = new List<LogicalOrder>();
            this.bufferedLogicals = new List<LogicalOrder>();
            this.logicalOrders = new List<LogicalOrder>();
            this.originalPhysicals = new List<CreateOrChangeOrder>();
            this.physicalOrders = new List<CreateOrChangeOrder>();
            this.extraLogicals = new List<LogicalOrder>();
            this.minimumTick = symbol.MinimumTick.ToLong();
            if( debug) log.Debug("Starting recency " + recency);
		}

        public bool PositionChange( PositionChangeDetail positionChange, bool isRecovered)
        {
            if( positionChange.Recency < recency)
            {
                if( debug) log.Debug("PositionChange recency " + positionChange.Recency + " less than " + recency + " so ignoring.");
                if( DoSyncTicks)
                {
                    if (!tickSync.SentWaitingMatch)
                    {
                        tickSync.AddWaitingMatch("StalePositionChange");
                    }
                    tickSync.RemovePositionChange(name);
                }
                return false;
            }
            if (debug) log.Debug("PositionChange(" + positionChange + ")");
            recency = positionChange.Recency;
            SetDesiredPosition(positionChange.Position);
            SetStrategyPositions(positionChange.StrategyPositions);
            SetLogicalOrders(positionChange.Orders);
            if (isRecovered)
            {
                TrySyncPosition(positionChange.StrategyPositions);
                PerformCompareProtected();
            }
            else
            {
                if (debug) log.Debug("PositionChange event received while FIX was offline or recovering. Skipping SyncPosition and ProcessOrders.");
                if (DoSyncTicks && isBrokerOnline)
                {
                    if (!tickSync.SentWaitingMatch)
                    {
                        tickSync.AddWaitingMatch("PositionChange");
                    }
                }
            }
            if (DoSyncTicks)
            {
                tickSync.RemovePositionChange(name);
            }
            return true;
        }
		
		private List<CreateOrChangeOrder> TryMatchId( IEnumerable<CreateOrChangeOrder> list, LogicalOrder logical)
		{
            var physicalOrderMatches = new List<CreateOrChangeOrder>();
            foreach (var physical in list)
		    {
				if( logical.SerialNumber == physical.LogicalSerialNumber ) {
                    switch( physical.OrderState)
                    {
                        case OrderState.Suspended:
                            if (debug) log.Debug("Cannot match a suspended order: " + physical);
                            break;
                        case OrderState.Filled:
                            if (debug) log.Debug("Cannot match a filled order: " + physical);
                            break;
                        default:
                            if( physical.ReplacedBy == null)
                            {
                                physicalOrderMatches.Add(physical);
                            }
                            break;
                    }
				}
			}
			return physicalOrderMatches;
		}

        private bool TryCancelBrokerOrder(CreateOrChangeOrder physical)
        {
            return TryCancelBrokerOrder(physical, false);
        }

        private bool TryCancelBrokerOrder(CreateOrChangeOrder physical, bool forStaleOrder)
        {
			bool result = false;
            if (!physical.IsPending)
            {
                result = Cancel(physical,forStaleOrder);
            }
            return result;
        }

        private bool CancelStale( CreateOrChangeOrder physical)
        {
            return Cancel(physical, true);
        }
		
        private bool Cancel(CreateOrChangeOrder physical, bool forStaleOrder)
        {
			var result = false;
            var cancelOrder = new CreateOrChangeOrderDefault(OrderState.Pending, symbol, physical);
            physical.ReplacedBy = cancelOrder;
            if (physicalOrderCache.HasCancelOrder(cancelOrder))
            {
                if (debug) log.Debug("Ignoring cancel broker order " + physical.BrokerOrder + " as physical order cache has a cancel or replace already.");
                return result;
            }
            if (rejectRepeatCounter > 1)
            {
                if (debug) log.Debug("Ignoring broker order while waiting on reject recovery.");
                return result;
            }
            if (physical.CancelCount > 15)
            {
                log.Error("Already tried canceling this order " + physical.CancelCount + " times: " + physical);
                while (true)
                {
                    Thread.Sleep(1000);
                }
                //throw new InvalidOperationException("Already tried canceling this order 3 times: " + physical);
            }
            if (debug) log.Debug("Cancel Broker Order: " + cancelOrder);
            physicalOrderCache.SetOrder(cancelOrder);
            if( !forStaleOrder)
            {
                TryAddPhysicalOrder(cancelOrder);
            }
            if (physicalOrderHandler.OnCancelBrokerOrder(cancelOrder))
            {
                physical.CancelCount++;
                result = true;
            }
            else
            {
                if( !forStaleOrder)
                {
                    TryRemovePhysicalOrder(cancelOrder);
                }
                physicalOrderCache.RemoveOrder(cancelOrder.BrokerOrder);
            }
		    return result;
		}
		
		private void TryChangeBrokerOrder(CreateOrChangeOrder createOrChange, CreateOrChangeOrder origOrder) {
            if (origOrder.OrderState == OrderState.Active)
            {
                createOrChange.OriginalOrder = origOrder;
                origOrder.ReplacedBy = createOrChange;
                if (physicalOrderCache.HasCancelOrder(createOrChange))
                {
                    if (debug) log.Debug("Ignoring broker order " + origOrder.BrokerOrder + " as physical order cache has a cancel or replace already.");
                    return;
                }
	            if (rejectRepeatCounter > 1)
                {
                    if (debug) log.Debug("Ignoring broker order while waiting on reject recovery.");
                    return;
                }
                if (debug) log.Debug("Change Broker Order: " + createOrChange);
                TryAddPhysicalOrder(createOrChange);
                physicalOrderCache.SetOrder(createOrChange);
                if (!physicalOrderHandler.OnChangeBrokerOrder(createOrChange))
                {
                    physicalOrderCache.RemoveOrder(createOrChange.BrokerOrder);
                    TryRemovePhysicalOrder(createOrChange);
                }
            }
		}
		
		private void TryAddPhysicalOrder(CreateOrChangeOrder createOrChange) {
            if (DoSyncTicks) tickSync.AddPhysicalOrder(createOrChange);
		}

        private void TryRemovePhysicalOrder(CreateOrChangeOrder createOrChange)
        {
            if (DoSyncTicks) tickSync.RemovePhysicalOrder(createOrChange);
        }

        private bool TryCreateBrokerOrder(CreateOrChangeOrder physical)
        {
			if( debug) log.Debug("Create Broker Order " + physical);
            if (physical.Size <= 0)
            {
                throw new ApplicationException("Sorry, order size must be greater than or equal to zero.");
            }
            if (physicalOrderCache.HasCreateOrder(physical))
            {
                if( debug) log.Debug("Ignoring broker order as physical order cache has a create order already.");
                return false;
            }
            if (rejectRepeatCounter > 1)
            {
                if (debug) log.Debug("Ignoring broker order while waiting on reject recovery.");
                return false;
            }
            TryAddPhysicalOrder(physical);
            physicalOrderCache.SetOrder(physical);
            if (!physicalOrderHandler.OnCreateBrokerOrder(physical))
            {
                physicalOrderCache.RemoveOrder(physical.BrokerOrder);
                TryRemovePhysicalOrder(physical);
            }
            return true;
        }

        private string ToString(List<CreateOrChangeOrder> matches)
        {
            var sb = new StringBuilder();
            foreach( var physical in matches)
            {
                sb.AppendLine(physical.ToString());
            }
            return sb.ToString();
        }

        public virtual bool ProcessMatchPhysicalEntry(LogicalOrder logical, List<CreateOrChangeOrder> matches)
		{
            if (matches.Count != 1)
            {
                log.Warn("Expected 1 match but found " + matches.Count + " matches for logical order: " + logical + "\n" + ToString(matches));
            }
            physicalOrders.Remove(matches[0]);
            return ProcessMatchPhysicalEntry(logical, matches[0], logical.Position, logical.Price);
		}

        protected bool ProcessMatchPhysicalEntry(LogicalOrder logical, CreateOrChangeOrder physical, int position, double price)
        {
            var result = true;
			log.Trace("ProcessMatchPhysicalEntry()");
			var strategyPosition = GetStrategyPosition(logical);
            var difference = position - Math.Abs(strategyPosition);
			log.Trace("position difference = " + difference);
			if( difference == 0)
			{
			    result = false;
				TryCancelBrokerOrder(physical);
			} else if( difference != physical.Size) {
                result = false;
                if (strategyPosition == 0)
                {
					physicalOrders.Remove(physical);
					var side = GetOrderSide(logical.Type);
					var changeOrder = new CreateOrChangeOrderDefault(OrderAction.Change,symbol,logical,side,difference,price);
                    TryChangeBrokerOrder(changeOrder, physical);
				} else {
					if( strategyPosition > 0) {
						if( logical.Type == OrderType.BuyStop || logical.Type == OrderType.BuyLimit) {
							physicalOrders.Remove(physical);
							var side = GetOrderSide(logical.Type);
                            var changeOrder = new CreateOrChangeOrderDefault(OrderAction.Change, symbol, logical, side, difference, price);
                            TryChangeBrokerOrder(changeOrder, physical);
						} else {
                            if (debug) log.Debug("Strategy position is long " + strategyPosition + " so canceling " + logical.Type + " order..");
                            TryCancelBrokerOrder(physical);
						}
					}
					if( strategyPosition < 0) {
						if( logical.Type == OrderType.SellStop || logical.Type == OrderType.SellLimit) {
							physicalOrders.Remove(physical);
							var side = GetOrderSide(logical.Type);
                            var changeOrder = new CreateOrChangeOrderDefault(OrderAction.Change, symbol, logical, side, difference, price);
                            TryChangeBrokerOrder(changeOrder, physical);
                        }
                        else
                        {
                            if (debug) log.Debug("Strategy position is short " + strategyPosition + " so canceling " + logical.Type + " order..");
							TryCancelBrokerOrder(physical);
						}
					}
				}
			} else if( price.ToLong() != physical.Price.ToLong()) {
                result = false;
                physicalOrders.Remove(physical);
				var side = GetOrderSide(logical.Type);
                var changeOrder = new CreateOrChangeOrderDefault(OrderAction.Change, symbol, logical, side, difference, price);
                TryChangeBrokerOrder(changeOrder, physical);
            }
            else
            {
				result = VerifySide( logical, physical, price);
			}
            return result;
        }

        private bool ProcessMatchPhysicalReverse(LogicalOrder logical, List<CreateOrChangeOrder> matches)
        {
            if (matches.Count != 1)
            {
                log.Warn("Expected 1 match but found " + matches.Count + " matches for logical order: " + logical + "\n" + ToString(matches));
            }
            physicalOrders.Remove(matches[0]);
            var physical = matches[0];
            return ProcessMatchPhysicalReverse(logical, physical, logical.Position, logical.Price);
        }

        private bool ProcessMatchPhysicalReverse(LogicalOrder logical, CreateOrChangeOrder createOrChange, int position, double price)
        {
            var result = true;
            var strategyPosition = GetStrategyPosition(logical);
            var logicalPosition =
				logical.Type == OrderType.BuyLimit ||
				logical.Type == OrderType.BuyMarket ||
				logical.Type == OrderType.BuyStop ? 
				position : - position;
			var physicalPosition = 
				createOrChange.Side == OrderSide.Buy ?
				createOrChange.Size : - createOrChange.Size;
			var delta = logicalPosition - strategyPosition;
			var difference = delta - physicalPosition;
			if( delta == 0 || (logicalPosition > 0 && strategyPosition > logicalPosition) ||
			  (logicalPosition < 0 && strategyPosition < logicalPosition))
			{
			    result = false;
				TryCancelBrokerOrder(createOrChange);
			} else if( difference != 0) {
                result = false;
				if( delta > 0) {
                    var changeOrder = new CreateOrChangeOrderDefault(OrderAction.Change, symbol, logical, OrderSide.Buy, Math.Abs(delta), price);
                    TryChangeBrokerOrder(changeOrder, createOrChange);
                }
                else
                {
					OrderSide side;
					if( strategyPosition > 0 && logicalPosition < 0) {
						side = OrderSide.Sell;
						delta = strategyPosition;
						if( delta == createOrChange.Size) {
							result = ProcessMatchPhysicalChangePriceAndSide( logical, createOrChange, delta, price);
							return result;
						}
					} else {
						side = OrderSide.SellShort;
					}
					side = (long) strategyPosition >= (long) Math.Abs(delta) ? OrderSide.Sell : OrderSide.SellShort;
                    var changeOrder = new CreateOrChangeOrderDefault(OrderAction.Change, symbol, logical, side, Math.Abs(delta), price);
                    TryChangeBrokerOrder(changeOrder, createOrChange);
                }
			} else {
				result = ProcessMatchPhysicalChangePriceAndSide( logical, createOrChange, delta, price);
			}
            return result;
        }
		
        private bool MatchLogicalToPhysicals(LogicalOrder logical, List<CreateOrChangeOrder> matches, Func<LogicalOrder, CreateOrChangeOrder, int, double, bool> onMatchCallback){
            var result = true;
            var price = logical.Price.ToLong();
            var sign = 1;
            var levels = 1;
            switch (logical.Type)
            {
                case OrderType.BuyMarket:
                case OrderType.SellMarket:
                    break;
                case OrderType.BuyLimit:
                case OrderType.SellStop:
                    sign = -1;
                    levels = logical.Levels;
                    break;
                case OrderType.SellLimit:
                case OrderType.BuyStop:
                    levels = logical.Levels;
                    break;
                default:
                    throw new InvalidOperationException("Unknown logical order type: " + logical.Type);

            }
            missingLevels.Clear();
            var levelSize = logical.Levels == 1 ? logical.Position : logical.LevelSize;
            var logicalPosition = logical.Position;
            var level = logical.Levels - 1;
            for (var i = 0; i < logical.Levels; i++, level --, logicalPosition -= levelSize)
            {
                var size = Math.Min(logicalPosition,levelSize) ;
                if( size == 0) break;
                var levelPrice = price + sign*minimumTick*logical.LevelIncrement*level;
                // Find a match.
                var matched = false;
                for (var j = 0; j < matches.Count; j++)
                {
                    var physical = matches[j];
                    physicalOrders.Remove(physical);
                    if (physical.Price.ToLong() != levelPrice) continue;
                    if( !onMatchCallback(logical, physical, size, levelPrice.ToDouble()))
                    {
                        result = false;
                    }
                    matches.RemoveAt(j);
                    matched = true;
                    break;
                }
                if (!matched)
                {
                    missingLevels.Add(new MissingLevel { Price = levelPrice, Size = size });
                }
            }
            for (var i = 0; i < matches.Count; i++)
            {
                var physical = matches[i];
                if( missingLevels.Count > 0)
                {
                    var missingLevel = missingLevels[0];
                    if( !onMatchCallback(logical, physical, missingLevel.Size, missingLevel.Price.ToDouble()))
                    {
                        result = false;
                    }
                    missingLevels.RemoveAt(0);
                }
                else
                {
                    TryCancelBrokerOrder(physical);
                }

            }
            for (var i = 0; i < missingLevels.Count; i++ ) 
            {
                result = false;
                var missingLevel = missingLevels[i];
                ProcessMissingPhysical(logical, missingLevel.Size, missingLevel.Price.ToDouble());
            }
            return result;
        }

        private bool ProcessMatchPhysicalChange(LogicalOrder logical, List<CreateOrChangeOrder> matches)
        {
            return MatchLogicalToPhysicals(logical, matches, ProcessMatchPhysicalChange);
        }

        private bool ProcessMatchPhysicalChange(LogicalOrder logical, CreateOrChangeOrder createOrChange, int position, double price)
        {
            var result = true;
            var strategyPosition = GetStrategyPosition(logical);
            var logicalPosition = 
				logical.Type == OrderType.BuyLimit ||
				logical.Type == OrderType.BuyMarket ||
				logical.Type == OrderType.BuyStop ? 
				position : - position;
			logicalPosition += strategyPosition;
			var physicalPosition = 
				createOrChange.Side == OrderSide.Buy ?
				createOrChange.Size : - createOrChange.Size;
			var delta = logicalPosition - strategyPosition;
			var difference = delta - physicalPosition;
			if( debug) log.Debug("PhysicalChange("+logical.SerialNumber+") delta="+delta+", strategyPosition="+strategyPosition+", difference="+difference);
//			if( delta == 0 || strategyPosition == 0) {
			if( delta == 0) {
				if( debug) log.Debug("(Delta=0) Canceling: " + createOrChange);
			    result = false;
				TryCancelBrokerOrder(createOrChange);
			} else if( difference != 0) {
                result = false;
                var origBrokerOrder = createOrChange.BrokerOrder;
				if( delta > 0) {
                    var changeOrder = new CreateOrChangeOrderDefault(OrderAction.Change, symbol, logical, OrderSide.Buy, Math.Abs(delta), price);
					if( debug) log.Debug("(Delta) Changing " + origBrokerOrder + " to " + changeOrder);
                    TryChangeBrokerOrder(changeOrder, createOrChange);
                }
                else
                {
					OrderSide side;
					if( strategyPosition > 0 && logicalPosition < 0) {
						side = OrderSide.Sell;
						delta = strategyPosition;
						if( delta == createOrChange.Size) {
							if( debug) log.Debug("Delta same as size: Check Price and Side.");
							ProcessMatchPhysicalChangePriceAndSide(logical,createOrChange,delta,price);
							return result;
						}
					} else {
						side = OrderSide.SellShort;
					}
					side = (long) strategyPosition >= (long) Math.Abs(delta) ? OrderSide.Sell : OrderSide.SellShort;
					if( side == createOrChange.Side) {
                        var changeOrder = new CreateOrChangeOrderDefault(OrderAction.Change, symbol, logical, side, Math.Abs(delta), price);
						if( debug) log.Debug("(Size) Changing " + origBrokerOrder + " to " + changeOrder);
                        TryChangeBrokerOrder(changeOrder, createOrChange);
                    }
                    else
                    {
						if( debug) log.Debug("(Side) Canceling " + createOrChange);
						TryCancelBrokerOrder(createOrChange);
					}
				}
		    } else {
				result = ProcessMatchPhysicalChangePriceAndSide(logical,createOrChange,delta,price);
			}
            return result;
        }
		
		private bool ProcessMatchPhysicalChangePriceAndSide(LogicalOrder logical, CreateOrChangeOrder createOrChange, int delta, double price)
		{
		    var result = true;
			if( price.ToLong() != createOrChange.Price.ToLong())
			{
			    result = false;
				var origBrokerOrder = createOrChange.BrokerOrder;
				physicalOrders.Remove(createOrChange);
				var side = GetOrderSide(logical.Type);
				if( side == createOrChange.Side) {
                    var changeOrder = new CreateOrChangeOrderDefault(OrderAction.Change, symbol, logical, side, Math.Abs(delta), price);
					if( debug) log.Debug("(Price) Changing " + origBrokerOrder + " to " + changeOrder);
                    TryChangeBrokerOrder(changeOrder, createOrChange);
                }
                else
                {
					if( debug) log.Debug("(Price) Canceling wrong side" + createOrChange);
					TryCancelBrokerOrder(createOrChange);
				}
			} else {
				result = VerifySide( logical, createOrChange, price);
			}
		    return result;
		}

        private bool ProcessMatchPhysicalExit(LogicalOrder logical, List<CreateOrChangeOrder> matches)
        {
            if (matches.Count != 1)
            {
                log.Warn("Expected 1 match but found " + matches.Count + " matches for logical order: " + logical + "\n" + ToString(matches));
            }
            var physical = matches[0];
            physicalOrders.Remove(matches[0]);
            return ProcessMatchPhysicalExit(logical, physical, logical.Position, logical.Price);
        }

        private bool ProcessMatchPhysicalExit(LogicalOrder logical, CreateOrChangeOrder createOrChange, int position, double price)
        {
            var result = true;
            var strategyPosition = GetStrategyPosition(logical);
            if (strategyPosition == 0)
			{
			    result = false;
				TryCancelBrokerOrder(createOrChange);
			} else if( Math.Abs(strategyPosition) != createOrChange.Size || price.ToLong() != createOrChange.Price.ToLong()) {
                result = false;
				physicalOrders.Remove(createOrChange);
				var side = GetOrderSide(logical.Type);
                var changeOrder = new CreateOrChangeOrderDefault(OrderAction.Change, symbol, logical, side, Math.Abs(strategyPosition), price);
                TryChangeBrokerOrder(changeOrder, createOrChange);
            }
            else
            {
				result = VerifySide( logical, createOrChange, price);
			}
            return result;
        }

        private bool ProcessMatchPhysicalExitStrategy(LogicalOrder logical, List<CreateOrChangeOrder> matches)
        {
            if( logical.Levels == 1) {
                if (matches.Count != 1)
                {
                    log.Warn("Expected 1 match but found " + matches.Count + " matches for logical order: " + logical + "\n" + ToString(matches));
                }
                physicalOrders.Remove(matches[0]);
                var physical = matches[0];
                return ProcessMatchPhysicalExitStrategy(logical,physical,logical.Position,logical.Price);
            }
            return MatchLogicalToPhysicals(logical, matches, ProcessMatchPhysicalExitStrategy);
        }

        private bool ProcessMatchPhysicalExitStrategy(LogicalOrder logical, CreateOrChangeOrder createOrChange, int position, double price)
        {
            var result = true;
            var strategyPosition = GetStrategyPosition(logical);
            if (strategyPosition == 0)
			{
			    result = false;
				TryCancelBrokerOrder(createOrChange);
			} else if( Math.Abs(strategyPosition) != createOrChange.Size || price.ToLong() != createOrChange.Price.ToLong()) {
                result = false;
                var origBrokerOrder = createOrChange.BrokerOrder;
				physicalOrders.Remove(createOrChange);
				var side = GetOrderSide(logical.Type);
                var changeOrder = new CreateOrChangeOrderDefault(OrderAction.Change, symbol, logical, side, Math.Abs(strategyPosition), price);
                TryChangeBrokerOrder(changeOrder, createOrChange);
            }
            else
            {
				result = VerifySide( logical, createOrChange, price);
			}
            return result;
        }

        private SimpleLock matchLocker = new SimpleLock();
		private bool ProcessMatch(LogicalOrder logical, List<CreateOrChangeOrder> matches)
		{
		    var result = false;
		    if( !matchLocker.TryLock()) return false;
            try
		    {
			    if( trace) log.Trace("Process Match()");
			    switch( logical.TradeDirection) {
				    case TradeDirection.Entry:
					    result = ProcessMatchPhysicalEntry( logical, matches);
					    break;
				    case TradeDirection.Exit:
                        result = ProcessMatchPhysicalExit(logical, matches);
					    break;
				    case TradeDirection.ExitStrategy:
                        result = ProcessMatchPhysicalExitStrategy(logical, matches);
					    break;
				    case TradeDirection.Reverse:
                        result = ProcessMatchPhysicalReverse(logical, matches);
					    break;
				    case TradeDirection.Change:
                        result = ProcessMatchPhysicalChange(logical, matches);
					    break;
				    default:
					    throw new ApplicationException("Unknown TradeDirection: " + logical.TradeDirection);
			    }
		    } finally
            {
                matchLocker.Unlock();
            }
		    return result;
		}

		private bool VerifySide( LogicalOrder logical, CreateOrChangeOrder createOrChange, double price)
		{
		    var result = true;
#if VERIFYSIDE
			var side = GetOrderSide(logical.Type);
			if( createOrChange.Side != side && ( createOrChange.Type != OrderType.BuyMarket && createOrChange.Type != OrderType.SellMarket)) {
                if (debug) log.Debug("Cancel because " + createOrChange.Side + " != " + side + ": " + createOrChange);
				if( TryCancelBrokerOrder(createOrChange))
				{
                    createOrChange = new CreateOrChangeOrderDefault(OrderState.Pending, symbol, logical, side, createOrChange.Size, price);
                    TryCreateBrokerOrder(createOrChange);
                    result = false;
                }
			}
#endif
		    return result;
		}

        private int GetStrategyPosition(LogicalOrder logical)
        {
            var strategyPosition = (int)physicalOrderCache.GetStrategyPosition(logical.StrategyId);
            if (handleSimulatedExits)
            {
                strategyPosition = logical.StrategyPosition;
            }
            return strategyPosition;
        }
		
		private bool ProcessExtraLogical(LogicalOrder logical)
		{
		    var result = true;
            // When flat, allow entry orders.
			switch(logical.TradeDirection) {
				case TradeDirection.Entry:
    				result = ProcessMissingPhysical(logical);
					break;
				case TradeDirection.Exit:
				case TradeDirection.ExitStrategy:
                    if (GetStrategyPosition(logical)!= 0)
                    {
                        result = ProcessMissingPhysical(logical);
					}
					break;
				case TradeDirection.Reverse:
                    result = ProcessMissingPhysical(logical);
					break;
				case TradeDirection.Change:
                    result = ProcessMissingPhysical(logical);
					break;
				default:
					throw new ApplicationException("Unknown trade direction: " + logical.TradeDirection);
			}
		    return result;
		}

        private bool ProcessMissingPhysical(LogicalOrder logical)
        {
            var result = true;
            if( logical.Levels == 1)
            {
                result = ProcessMissingPhysical(logical, logical.Position, logical.Price);
                return result;
            }
            var price = logical.Price.ToLong();
            var sign = 1;
            switch( logical.Type)
            {
                case OrderType.BuyMarket:
                case OrderType.SellMarket:
                    result = ProcessMissingPhysical(logical, logical.Position, logical.Price);
                    return result;
                case OrderType.BuyLimit:
                case OrderType.SellStop:
                    sign = -1;
                    break;
                case OrderType.SellLimit:
                case OrderType.BuyStop:
                    break;
                default:
                    throw new InvalidOperationException("Unknown logical order type: " + logical.Type);

            }
            var logicalPosition = logical.Position;
            var level = sign > 0 ? logical.Levels-1 : 0;
            for( var i=0; i< logical.Levels; i++, level-=sign)
            {
                var size = Math.Min(logical.LevelSize, logicalPosition);
                var levelPrice = price + sign * minimumTick * logical.LevelIncrement * level;
                if( !ProcessMissingPhysical(logical, size, levelPrice.ToDouble()))
                {
                    result = false;
                }
                logicalPosition -= logical.LevelSize;
            }
            return result;
        }

        private bool ProcessMissingPhysical(LogicalOrder logical, int position, double price)
        {
            var result = true;
            var logicalPosition =
                logical.Type == OrderType.BuyLimit ||
                logical.Type == OrderType.BuyMarket ||
                logical.Type == OrderType.BuyStop ?
                position : -position;
            var strategyPosition = GetStrategyPosition(logical);
            var size = Math.Abs(logicalPosition - strategyPosition);
            switch (logical.TradeDirection)
            {
				case TradeDirection.Entry:
					if(debug) log.Debug("ProcessMissingPhysicalEntry("+logical+")");
                    var side = GetOrderSide(logical.Type);
                    if (logicalPosition < 0 && strategyPosition <= 0 && strategyPosition > logicalPosition)
                    {
                        result = false;
                        var physical = new CreateOrChangeOrderDefault(OrderState.Pending, symbol, logical, side, size, price);
                        TryCreateBrokerOrder(physical);
                    }
                    if (logicalPosition > 0 && strategyPosition >= 0 && strategyPosition < logicalPosition)
                    {
                        result = false;
                        var physical = new CreateOrChangeOrderDefault(OrderState.Pending, symbol, logical, side, size, price);
                        TryCreateBrokerOrder(physical);
                    }
					break;
				case TradeDirection.Exit:
				case TradeDirection.ExitStrategy:
                    size = Math.Abs(strategyPosition);
					result = ProcessMissingExit( logical, size, price);
					break;
				case TradeDirection.Reverse:
                    result = ProcessMissingReverse(logical, size, price, logicalPosition);
                    break;
				case TradeDirection.Change:
					logicalPosition += strategyPosition;
					size = Math.Abs(logicalPosition - strategyPosition);
					if( size != 0) {
						if(debug) log.Debug("ProcessMissingChange("+logical+")");
					    result = false;
						side = GetOrderSide(logical.Type);
                        var physical = new CreateOrChangeOrderDefault(OrderState.Pending, symbol, logical, side, size, price);
						TryCreateBrokerOrder(physical);
					}
					break;
				default:
					throw new ApplicationException("Unknown trade direction: " + logical.TradeDirection);
			}
            return result;
        }

        private bool ProcessMissingReverse(LogicalOrder logical, int size, double price, int logicalPosition)
        {
            if (size == 0) return true;
            if (debug) log.Debug("ProcessMissingReverse(" + logical + ")");
            var side = GetOrderSide(logical.Type);
            var strategyPosition = GetStrategyPosition(logical);
            if( logicalPosition < 0)
            {
                if( strategyPosition > 0)
                {
                    var physical = new CreateOrChangeOrderDefault(OrderState.Pending, symbol, logical, side, strategyPosition, price);
                    TryCreateBrokerOrder(physical);
                }
                else if( strategyPosition > logicalPosition)
                {
                    var physical = new CreateOrChangeOrderDefault(OrderState.Pending, symbol, logical, side, size, price);
                    TryCreateBrokerOrder(physical);
                }
            }
            if (logicalPosition > 0)
            {
                if (strategyPosition < 0)
                {
                    var physical = new CreateOrChangeOrderDefault(OrderState.Pending, symbol, logical, side, -strategyPosition, price);
                    TryCreateBrokerOrder(physical);
                }
                else if( strategyPosition < logicalPosition)
                {
                    var physical = new CreateOrChangeOrderDefault(OrderState.Pending, symbol, logical, side, size, price);
                    TryCreateBrokerOrder(physical);
                }
            }
            return false;
        }

        private bool ProcessMissingExit(LogicalOrder logical, int size, double price)
        {
            var result = true;
            var strategyPosition = GetStrategyPosition(logical);
            if (strategyPosition > 0)
            {
                if (logical.Type == OrderType.SellLimit ||
                  logical.Type == OrderType.SellStop ||
                  logical.Type == OrderType.SellMarket)
                {
                    if (debug) log.Debug("ProcessMissingExit( strategy position " + strategyPosition + ", " + logical + ")");
                    result = false;
                    var side = GetOrderSide(logical.Type);
                    var physical = new CreateOrChangeOrderDefault(OrderState.Pending, symbol, logical, side, size, price);
                    TryCreateBrokerOrder(physical);
                }
			}
			if( strategyPosition < 0) {
                if (logical.Type == OrderType.BuyLimit ||
                  logical.Type == OrderType.BuyStop ||
                  logical.Type == OrderType.BuyMarket)
                {
                    result = false;
                    if (debug) log.Debug("ProcessMissingExit( strategy position " + strategyPosition + ", " + logical + ")");
                    var side = GetOrderSide(logical.Type);
                    var physical = new CreateOrChangeOrderDefault(OrderState.Pending, symbol, logical, side, size, price);
                    TryCreateBrokerOrder(physical);
                }
			}
            return result;
        }

        private bool CheckFilledOrder(LogicalOrder logical, int position)
        {
            var strategyPosition = GetStrategyPosition(logical);
            switch (logical.Type)
            {
                case OrderType.BuyLimit:
                case OrderType.BuyMarket:
                case OrderType.BuyStop:
                    if (logical.TradeDirection == TradeDirection.Change)
                    {
                        return position >= logical.Position + strategyPosition;
                    }
                    else
                    {
                        return position >= logical.Position;
                    }
                case OrderType.SellLimit:
                case OrderType.SellMarket:
                case OrderType.SellStop:
                    if (logical.TradeDirection == TradeDirection.Change)
                    {
                        return position <= -logical.Position + strategyPosition;
                    }
                    else
                    {
                        return position <= -logical.Position;
                    }
                default:
                    throw new ApplicationException("Unknown OrderType: " + logical.Type);
            }
        }
		
		private OrderSide GetOrderSide(OrderType type) {
			switch( type) {
				case OrderType.BuyLimit:
				case OrderType.BuyMarket:
				case OrderType.BuyStop:
					return OrderSide.Buy;
				case OrderType.SellLimit:
				case OrderType.SellMarket:
				case OrderType.SellStop:
					if( physicalOrderCache.GetActualPosition(symbol) > 0) {
						return OrderSide.Sell;
					} else {
						return OrderSide.SellShort;
					}
				default:
					throw new ApplicationException("Unknown OrderType: " + type);
			}
		}
		
		private int FindPendingAdjustments() {
            var positionDelta = desiredPosition - physicalOrderCache.GetActualPosition(symbol);
			var pendingAdjustments = 0;

            originalPhysicals.Clear();
            originalPhysicals.AddRange(physicalOrderCache.GetActiveOrders(symbol));

            for (var i = 0; i < originalPhysicals.Count; i++ )
            {
                CreateOrChangeOrder order = originalPhysicals[i];
                if (order.Type != OrderType.BuyMarket &&
                   order.Type != OrderType.SellMarket)
                {
                    continue;
                }
                switch (order.OrderState)
                {
                    case OrderState.Filled:
                        continue;
                    case OrderState.Active:
                    case OrderState.Pending:
                    case OrderState.PendingNew:
                    case OrderState.Expired:
                    case OrderState.Suspended:
                        break;
                    default:
                        throw new ApplicationException("Unknown order state: " + order.OrderState);
                }
                if (order.LogicalOrderId == 0)
                {
                    if (order.Type == OrderType.BuyMarket)
                    {
                        pendingAdjustments += order.Size;
                    }
                    if (order.Type == OrderType.SellMarket)
                    {
                        pendingAdjustments -= order.Size;
                    }
                    if (positionDelta > 0)
                    {
                        if (pendingAdjustments > positionDelta)
                        {
                            TryCancelBrokerOrder(order);
                            pendingAdjustments -= order.Size;
                        }
                        else if (pendingAdjustments < 0)
                        {
                            TryCancelBrokerOrder(order);
                            pendingAdjustments += order.Size;
                        }
                    }
                    if (positionDelta < 0)
                    {
                        if (pendingAdjustments < positionDelta)
                        {
                            TryCancelBrokerOrder(order);
                            pendingAdjustments += order.Size;
                        }
                        else if (pendingAdjustments > 0)
                        {
                            TryCancelBrokerOrder(order);
                            pendingAdjustments -= order.Size;
                        }
                    }
                    if (positionDelta == 0)
                    {
                        TryCancelBrokerOrder(order);
                        pendingAdjustments += order.Type == OrderType.SellMarket ? order.Size : -order.Size;
                    }
                    physicalOrders.Remove(order);
                }
            }
			return pendingAdjustments;
		}

        public void TrySyncPosition(Iterable<StrategyPosition> strategyPositions)
        {
            physicalOrderCache.SyncPositions(strategyPositions);
            SyncPosition();
        }

	    private void SyncPosition()
        {
            if( !ReceivedDesiredPosition) return;
            // Find any pending adjustments.
            var pendingAdjustments = FindPendingAdjustments();
            var positionDelta = desiredPosition - physicalOrderCache.GetActualPosition(symbol);
			var delta = positionDelta - pendingAdjustments;
			CreateOrChangeOrder createOrChange;
            if( delta != 0)
            {
                isPositionSynced = false;
                log.Info("SyncPosition() Issuing adjustment order because expected position is " + desiredPosition + " but actual is " + physicalOrderCache.GetActualPosition(symbol) + " plus pending adjustments " + pendingAdjustments);
                if (debug) log.Debug("TrySyncPosition - " + tickSync);
            }
            else if( positionDelta == 0)
            {
                if( debug) log.Debug("SyncPosition() found position currently synced. With expected " + desiredPosition + " and actual " + physicalOrderCache.GetActualPosition(symbol) + " plus pending adjustments " + pendingAdjustments);
                isPositionSynced = true;
            }
			if( delta > 0)
			{
                createOrChange = new CreateOrChangeOrderDefault(OrderAction.Create, OrderState.Pending, symbol, OrderSide.Buy, OrderType.BuyMarket, OrderFlags.None, 0, (int) delta, 0, 0, 0, null, default(TimeStamp));
                log.Info("Sending adjustment order to position: " + createOrChange);
			    TryCreateBrokerOrder(createOrChange);
            }
            else if (delta < 0)
            {
                OrderSide side;
                var pendingDelta = physicalOrderCache.GetActualPosition(symbol) + pendingAdjustments;
                var sendAdjustment = false;
				if( pendingDelta > 0) {
					side = OrderSide.Sell;
				    delta = Math.Min(pendingDelta, -delta);
				    sendAdjustment = true;
				}
                else if (pendingAdjustments == 0)
                {
                    side = OrderSide.SellShort;
                    sendAdjustment = true;
                }
                if( sendAdjustment)
                {
                    side = physicalOrderCache.GetActualPosition(symbol) >= Math.Abs(delta) ? OrderSide.Sell : OrderSide.SellShort;
                    createOrChange = new CreateOrChangeOrderDefault(OrderAction.Create, OrderState.Pending, symbol, side, OrderType.SellMarket, OrderFlags.None, 0, (int) Math.Abs(delta), 0, 0, 0, null, default(TimeStamp));
                    log.Info("Sending adjustment order to correct position: " + createOrChange);
                    TryCreateBrokerOrder(createOrChange);
                }
            }
        }

        public void SetStrategyPositions(Iterable<StrategyPosition> strategyPositions)
        {
            physicalOrderCache.SyncPositions(strategyPositions);
		}

        public void SetLogicalOrders(Iterable<LogicalOrder> inputLogicals)
        {
            if (trace)
            {
                int count = originalLogicals == null ? 0 : originalLogicals.Count;
                log.Trace("SetLogicalOrders() order count = " + count);
            }
            logicalOrderCache.SetActiveOrders(inputLogicals);
            bufferedLogicals.Clear();
            bufferedLogicals.AddRange(logicalOrderCache.GetActiveOrders());
            bufferedLogicalsChanged = true;
            if (debug) log.Debug("SetLogicalOrders( logicals " + bufferedLogicals.Count + ")");
        }
		
		public void SetDesiredPosition(	int position)
		{
		    receivedDesiredPosition = true;
			this.desiredPosition = position;
		}

		private bool CheckForPendingInternal() {
			var result = false;
		    for(var i=0; i< originalPhysicals.Count; i++)
		    {
		        var order = originalPhysicals[i];
				if( order.IsPending)
				{
                    //order.PendingCount++;
                    //if( order.PendingCount > 100)
                    //{
                    //    log.Error("This order was pending a long time: " + order.PendingCount);
                    //    while(true)
                    //    {
                    //        Thread.Sleep(1000);
                    //    }
                    //}
					if( debug) log.Debug("Pending order: " + order);
					result = true;	
				}
			}
			return result;
		}

        public void ProcessHeartBeat()
        {
        }

        public bool CheckForPending()
        {
            var expiryLimit = Factory.Parallel.UtcNow;
            expiryLimit.AddSeconds(-5);
            if (trace) log.Trace("Checking for orders pending since: " + expiryLimit);
            var foundAny = false;
            var cancelList = physicalOrderCache.GetOrdersList((x) => x.Symbol == symbol && (x.IsPending) && x.Action == OrderAction.Cancel);
            if( HandlePending(cancelList,expiryLimit))
            {
                foundAny = true;
            }
            var orderList = physicalOrderCache.GetOrdersList((x) => x.Symbol == symbol && (x.IsPending) && x.Action != OrderAction.Cancel);
            if( HandlePending(orderList,expiryLimit))
            {
                foundAny = true;
            }
            return foundAny;
        }

        private bool HandlePending( List<CreateOrChangeOrder> list, TimeStamp expiryLimit) {
            var cancelOrders = new List<CreateOrChangeOrder>();
            var foundAny = false;
            foreach( var order in list)
            {
                foundAny = true;
                if( debug) log.Debug("Pending order: " + order);
                var lastChange = order.LastModifyTime;
                if( order.ReplacedBy != null)
                {
                    lastChange = order.ReplacedBy.LastModifyTime;
                }
                if( lastChange < expiryLimit)
                {
                    if( order.Action == OrderAction.Cancel)
                    {
                        order.OrderState = OrderState.Expired;
                        if (debug) log.Debug("Removing pending and stale Cancel order: " + order);
                        var origOrder = order.OriginalOrder;
                        if (origOrder != null)
                        {
                            origOrder.ReplacedBy = null;
                        }
                        cancelOrders.Add(order);
                    }
                    else
                    {
                        order.OrderState = OrderState.Expired;
                        var diff = Factory.Parallel.UtcNow - lastChange;
                        var message = "Attempting to cancel pending order " + order.BrokerOrder + " because it is stale over " + diff.TotalSeconds + " seconds.";
                        if (DoSyncTicks)
                        {
                            log.Info(message);
                        }
                        else
                        {
                            log.Warn(message);
                        }
                        if (!CancelStale(order))
                        {
                            if( debug) log.Debug("Cancel failed to send for order: " + order);
                        }
                    }
                }
            }
            if( cancelOrders.Count > 0)
            {
                PerformCompareProtected();
                foreach( var order in cancelOrders)
                {
                    physicalOrderCache.RemoveOrder(order.BrokerOrder);
                    if( order.OriginalOrder.OrderState != OrderState.Expired)
                    {
                        tickSync.RemovePhysicalOrder(order);
                    }
                }
            }
            return foundAny;
        }

        private LogicalOrder FindActiveLogicalOrder(long serialNumber)
        {
            for(var i=0; i<originalLogicals.Count; i++)
            {
                var order = originalLogicals[i];
                if (order.SerialNumber == serialNumber)
                {
                    return order;
                }
            }
            return null;
        }

		public void ProcessFill( PhysicalFill physical) {
            if (debug) log.Debug("ProcessFill() physical: " + physical);
		    CreateOrChangeOrder order;
            if( !physicalOrderCache.TryGetOrderById(physical.BrokerOrder, out order))
		    {
		        throw new ApplicationException("Cannot find physical order for id " + physical.BrokerOrder + " in fill: " + physical);
		    }
		    var adjustment = order.LogicalOrderId == 0;
            var beforePosition = physicalOrderCache.GetActualPosition(symbol);
		    physicalOrderCache.IncreaseActualPosition(symbol, physical.Size);
            if (debug) log.Debug("Updating actual position from " + beforePosition + " to " + physicalOrderCache.GetActualPosition(symbol) + " from fill size " + physical.Size);
			var isCompletePhysicalFill = physical.RemainingSize == 0;
            TryFlushBufferedLogicals();

		    if( isCompletePhysicalFill) {
                if (debug) log.Debug("Physical order completely filled: " + order);
                order.OrderState = OrderState.Filled;
                originalPhysicals.Remove(order);
                physicalOrders.Remove(order);
                if (order.ReplacedBy != null)
                {
                    if (debug) log.Debug("Found this order in the replace property. Removing it also: " + order.ReplacedBy);
                    originalPhysicals.Remove(order.ReplacedBy);
                    physicalOrders.Remove(order.ReplacedBy);
                    physicalOrderCache.RemoveOrder(order.ReplacedBy.BrokerOrder);
                    if (DoSyncTicks)
                    {
                        tickSync.RemovePhysicalOrder(order.ReplacedBy);
                    }
                }
                physicalOrderCache.RemoveOrder(order.BrokerOrder);
			}
            else
            {
                if (debug) log.Debug("Physical order partially filled: " + order);
                order.Size = physical.RemainingSize;

            }

            if( adjustment) {
                if (debug) log.Debug("Leaving symbol position at desired " + desiredPosition + ", since this appears to be an adjustment market order: " + order);
                if (debug) log.Debug("Skipping logical fill for an adjustment market order.");
                if (debug) log.Debug("Performing extra compare.");
                PerformCompareProtected();
                TryRemovePhysicalFill(physical);
                return;
            }

            var isFilledAfterCancel = false;

            var logical = FindActiveLogicalOrder(order.LogicalSerialNumber);
            if( logical == null)
            {
                if (debug) log.Debug("Logical order not found. So logical was already canceled: " + physical );
                isFilledAfterCancel = true;
            }
            else
            {
                if (logical.Price.ToLong() != order.Price.ToLong())
                {
                    if (debug) log.Debug("Already canceled because physical order price " + order.Price + " dffers from logical order price " + logical);
                    isFilledAfterCancel = true;
                }
            }

            if (debug) log.Debug("isFilledAfterCancel " + isFilledAfterCancel + ", OffsetTooLateToCancel " + order.OffsetTooLateToCancel);
            if (isFilledAfterCancel)
            {
                TryRemovePhysicalFill(physical);
                if( ReceivedDesiredPosition)
                {
                    if (debug) log.Debug("Will sync positions because fill from order already canceled: " + order.ReplacedBy);
                    SyncPosition();
                }
                return;
            } 

            if( logical == null)
            {
                throw new InvalidOperationException("Logical cannot be null");
            }

		    LogicalFillBinary fill;
            desiredPosition += physical.Size;
            var strategyPosition = GetStrategyPosition(logical);
            if (debug) log.Debug("Adjusting symbol position to desired " + desiredPosition + ", physical fill was " + physical.Size);
            var position = strategyPosition + physical.Size;
            if (debug) log.Debug("Creating logical fill with position " + position + " from strategy position " + strategyPosition);
            if (position != strategyPosition)
            {
                if (debug) log.Debug("strategy position " + position + " differs from logical order position " + strategyPosition + " for " + logical);
            }
            ++recency;
            fill = new LogicalFillBinary(position, recency, physical.Price, physical.Time, physical.UtcTime, order.LogicalOrderId, order.LogicalSerialNumber, logical.Position, physical.IsSimulated, physical.IsActual);
            if (debug) log.Debug("Fill price: " + fill);
            ProcessFill(fill, logical, isCompletePhysicalFill, physical.IsRealTime);
		}

        private TaskLock performCompareLocker = new TaskLock();
		private void PerformCompareProtected()
		{
		    var count = ++recursiveCounter;
		    var compareSuccess = false;
		    if( count == 1)
		    {
				while( recursiveCounter > 0)
				{
                    for (var i = 0; i < recursiveCounter-1; i++ )
                    {
                        --recursiveCounter;
                    }
					try
					{
                        if (!isPositionSynced)
                        {
                            SyncPosition();
                        }
                        // Is it still not synced?
                        if (isPositionSynced)
                        {
                            compareSuccess = PerformCompareInternal();
                            if( debug)
                            {
                                log.Debug("PerformCompareInternal() returned: " + compareSuccess);
                            }
                            if (trace) log.Trace("PerformCompare finished - " + tickSync);
                        }
                        else
                        {
                            var extra = DoSyncTicks ? tickSync.ToString() : "";
                            if (debug) log.Debug("PerformCompare ignored. Position not yet synced. " + extra);
                        }

					}
                    finally
					{
					    --recursiveCounter;
					}
				}
            }
            else
			{
			    if( debug) log.Debug( "Skipping ProcesOrders. RecursiveCounter " + count + "\n" + tickSync);
			}
            if (compareSuccess)
            {
                if (DoSyncTicks && !handleSimulatedExits)
                {
                    if (tickSync.SentWaitingMatch)
                    {
                        tickSync.RemoveWaitingMatch("PerformCompare");
                    }
                }
                if( rejectRepeatCounter > 0 && confirmedOrderCount > 0)
                {
                    if( debug) log.Debug("ConfirmedOrderCount " + confirmedOrderCount + " greater than zero so resetting reject counter.");
                    rejectRepeatCounter = 0;
                }
            }
            if (DoSyncTicks && !compareSuccess && isBrokerOnline)
            {
                if (!tickSync.SentWaitingMatch)
                {
                    tickSync.AddWaitingMatch("PositionChange");
                }
            }
		}
		
		private void TryRemovePhysicalFill(PhysicalFill fill) {
            if (DoSyncTicks) tickSync.RemovePhysicalFill(fill);
		}
		
		private void ProcessFill( LogicalFillBinary fill, LogicalOrder filledOrder, bool isCompletePhysicalFill, bool isRealTime) {
			if( debug) log.Debug( "ProcessFill() logical: " + fill + (!isRealTime ? " NOTE: This is NOT a real time fill." : ""));
			int orderId = fill.OrderId;
			if( orderId == 0) {
				// This is an adjust-to-position market order.
				// Position gets set via SetPosition instead.
				return;
			}

			if( debug) log.Debug( "Matched fill with order: " + filledOrder);

		    var strategyPosition = GetStrategyPosition(filledOrder);
            var orderPosition =
                filledOrder.Type == OrderType.BuyLimit ||
                filledOrder.Type == OrderType.BuyMarket ||
                filledOrder.Type == OrderType.BuyStop ?
                filledOrder.Position : -filledOrder.Position;
            if (filledOrder.TradeDirection == TradeDirection.Change)
            {
				if( debug) log.Debug("Change order fill = " + orderPosition + ", strategy = " + strategyPosition + ", fill = " + fill.Position);
				fill.IsComplete = orderPosition + strategyPosition == fill.Position;
                var change = fill.Position - strategyPosition;
                filledOrder.Position = Math.Abs(orderPosition - change);
                if (debug) log.Debug("Changing order to position: " + filledOrder.Position);
            }
            else
            {
                fill.IsComplete = CheckFilledOrder(filledOrder, fill.Position);
            }
			if( fill.IsComplete)
			{
                if (debug) log.Debug("LogicalOrder is completely filled.");
			    MarkAsFilled(filledOrder);
            }
            CleanupAfterFill(filledOrder, fill);
            UpdateOrderCache(filledOrder, fill);
            if (isCompletePhysicalFill && !fill.IsComplete)
            {
                if (filledOrder.TradeDirection == TradeDirection.Entry && fill.Position == 0)
                {
                    if (debug) log.Debug("Found a entry order which flattened the position. Likely due to bracketed entries that both get filled: " + filledOrder);
                    MarkAsFilled(filledOrder);
                    CleanupAfterFill(filledOrder, fill);
                }
                else if( isRealTime)
                {
                    if (debug) log.Debug("Found complete physical fill but incomplete logical fill. Physical orders...");
                    var matches = TryMatchId(physicalOrderCache.GetActiveOrders(symbol), filledOrder);
                    if( matches.Count > 0)
                    {
                        ProcessMatch(filledOrder, matches);
                    }
                    else
                    {
                        ProcessMissingPhysical(filledOrder);
                    }
                }
			}
            if (onProcessFill != null)
            {
                if (debug) log.Debug("Sending logical fill for " + symbol + ": " + fill);
                onProcessFill(symbol, fill);
            }
			if( debug) log.Debug("Performing extra compare.");
			PerformCompareProtected();
        }

        private void MarkAsFilled(LogicalOrder filledOrder)
        {
            try
            {
                if (debug) log.Debug("Marking order id " + filledOrder.Id + " as completely filled.");
                originalLogicals.Remove(filledOrder);
            }
            catch (ApplicationException ex)
            {
                log.Warn("Ignoring execption and continuing: " + ex.Message, ex);
            }
            catch (ArgumentException ex)
            {
                log.Error(ex.Message + " Was the order already marked as filled? : " + filledOrder);
            }
        }

        private void CancelLogical(LogicalOrder order)
        {
            originalLogicals.Remove(order);
        }

		private void CleanupAfterFill(LogicalOrder filledOrder, LogicalFillBinary fill) {
			bool clean = false;
			bool cancelAllEntries = false;
			bool cancelAllExits = false;
			bool cancelAllExitStrategies = false;
			bool cancelAllReverse = false;
			bool cancelAllChanges = false;
		    bool cancelDueToPartialFill = false;
            if( fill.IsComplete)
            {
                var strategyPosition = GetStrategyPosition(filledOrder);
                if (strategyPosition == 0)
                {
                    cancelAllChanges = true;
                    clean = true;
                }
                switch (filledOrder.TradeDirection)
                {
                    case TradeDirection.Change:
                        break;
                    case TradeDirection.Entry:
                        cancelAllEntries = true;
                        clean = true;
                        break;
                    case TradeDirection.Exit:
                    case TradeDirection.ExitStrategy:
                        cancelAllExits = true;
                        cancelAllExitStrategies = true;
                        cancelAllEntries = true;
                        cancelAllChanges = true;
                        clean = true;
                        break;
                    case TradeDirection.Reverse:
                        cancelAllReverse = true;
                        cancelAllEntries = true;
                        clean = true;
                        break;
                    default:
                        throw new ApplicationException("Unknown trade direction: " + filledOrder.TradeDirection);
                }
            }
            else
            {
                switch (filledOrder.TradeDirection)
                {
                    case TradeDirection.Change:
                    case TradeDirection.Entry:
                        break;
                    case TradeDirection.Exit:
                    case TradeDirection.ExitStrategy:
                    case TradeDirection.Reverse:
                        cancelAllEntries = true;
                        cancelAllChanges = true;
                        cancelDueToPartialFill = true;
                        clean = true;
                        break;
                    default:
                        throw new ApplicationException("Unknown trade direction: " + filledOrder.TradeDirection);
                }
            }
			if( clean) {
			    for(var i = 0; i<originalLogicals.Count; i++)
			    {
			        var order = originalLogicals[i];
					if( order.StrategyId == filledOrder.StrategyId) {
						switch( order.TradeDirection) {
							case TradeDirection.Entry:
								if( cancelAllEntries)
								{
                                    if( cancelDueToPartialFill)
                                    {
                                        if( debug) log.Debug("Canceling Entry order due to partial fill: " + order);
                                    }
								    CancelLogical(order);
								}
								break;
							case TradeDirection.Change:
                                if (cancelAllChanges)
                                {
                                    if (cancelDueToPartialFill)
                                    {
                                        if (debug) log.Debug("Canceling Entry order due to partial fill: " + order);
                                    }
                                    CancelLogical(order);
                                }
								break;
							case TradeDirection.Exit:
                                if (cancelAllExits)
                                {
                                    CancelLogical(order);
                                }
								break;
							case TradeDirection.ExitStrategy:
                                if (cancelAllExitStrategies)
                                {
                                    CancelLogical(order);
                                }
								break;
							case TradeDirection.Reverse:
                                if (cancelAllReverse)
                                {
                                    CancelLogical(order);
                                }
								break;
							default:
								throw new ApplicationException("Unknown trade direction: " + filledOrder.TradeDirection);
						}
					}
				}
			}
		}
	
		private void UpdateOrderCache(LogicalOrder order, LogicalFill fill)
		{
            var strategyPosition = GetStrategyPosition(order);
            if (debug) log.Debug("Adjusting strategy position from " + strategyPosition + " to " + fill.Position + ". Recency " + fill.Recency + " for strategy id " + order.StrategyId);
            if( handleSimulatedExits)
            {
                order.StrategyPosition = fill.Position;
            }
            else
            {
                physicalOrderCache.SetStrategyPosition(symbol, order.StrategyId, fill.Position);
            }
		}
		
		public int ProcessOrders() {
            if (debug) log.Debug("ProcessOrders()");
			PerformCompareProtected();
            return 0;
		}

		private int recursiveCounter;
		private bool PerformCompareInternal()
		{
		    var result = true;

            if (debug)
			{
                var mismatch = physicalOrderCache.GetActualPosition(symbol) == desiredPosition ? "match" : "MISMATCH";
			    log.Debug("PerformCompare for " + symbol + " with " +
                          physicalOrderCache.GetActualPosition(symbol) + " actual " +
			              desiredPosition + " desired. Positions " + mismatch + ".");
			}
				
            originalPhysicals.Clear();
            originalPhysicals.AddRange(physicalOrderCache.GetActiveOrders(symbol));

		    CheckForPending();
		    var hasPendingOrders = CheckForPendingInternal();
            if (hasPendingOrders)
            {
                if (debug) log.Debug("Found pending physical orders. So ending order comparison.");
                return false;
            }

		    TryFlushBufferedLogicals();

            if (debug)
            {
                log.Debug(originalLogicals.Count + " logicals, " + originalPhysicals.Count + " physicals.");
            }

            if (debug)
            {
                LogOrders(originalLogicals, "Original Logical");
                LogOrders(originalPhysicals, "Original Physical");
            }

            logicalOrders.Clear();
			logicalOrders.AddRange(originalLogicals);
			
			physicalOrders.Clear();
			if(originalPhysicals != null) {
				physicalOrders.AddRange(originalPhysicals);
			}

			CreateOrChangeOrder createOrChange;
			extraLogicals.Clear();
			while( logicalOrders.Count > 0)
			{
				var logical = logicalOrders[0];
			    var matches = TryMatchId(physicalOrders, logical);
                if( matches.Count > 0)
                {
                    if( !ProcessMatch( logical, matches))
                    {
                        if (debug) log.Debug("logical order didn't match: " + logical);
                        result = false;
                    }
                }
                else
                {
                    extraLogicals.Add(logical);
				}
				logicalOrders.Remove(logical);
			}

			if( trace) log.Trace("Found " + physicalOrders.Count + " extra physicals.");
			int cancelCount = 0;
            if( physicalOrders.Count > 0)
            {
                if (debug) log.Debug("Extra physical orders: " + physicalOrders.Count);
                result = false;
            }
			while( physicalOrders.Count > 0)
			{
			    createOrChange = physicalOrders[0];
				if( TryCancelBrokerOrder(createOrChange)) {
					cancelCount++;
				}
				physicalOrders.RemoveAt(0);
			}
			
			if( cancelCount > 0) {
				// Wait for cancels to complete before creating any orders.
				return result;
			}

            if (trace) log.Trace("Found " + extraLogicals.Count + " extra logicals.");
            while (extraLogicals.Count > 0)
            {
                var logical = extraLogicals[0];
                if( !ProcessExtraLogical(logical))
                {
                    if (debug) log.Debug("Extra logical order: " + logical);
                    result = false;
                }
                extraLogicals.Remove(logical);
            }
            return result;
        }

        private void TryFlushBufferedLogicals()
        {
            if (bufferedLogicalsChanged)
            {
                if (debug) log.Debug("Buffered logicals were updated so refreshing original logicals list ...");
                originalLogicals.Clear();
                if (bufferedLogicals != null)
                {
                    originalLogicals.AddRange(bufferedLogicals);
                }
                bufferedLogicalsChanged = false;
            }
        }

        private void LogOrders( IEnumerable<LogicalOrder> orders, string name)
        {
            foreach(var order in orders)
            {
                log.Debug("Logical Order: " + order);
            }
        }

        private void LogOrders( IEnumerable<CreateOrChangeOrder> orders, string name)
        {
            if( debug)
            {
                var first = true;
                foreach (var order in orders)
                {
                    if( first)
                    {
                        log.Debug("Listing " + name + " orders:");
                        first = false;
                    }
                    log.Debug(name + ": " + order);
                }
                if( first)
                {
                    log.Debug("Empty list of " + name + " orders.");
                }
            }
        }
	
		public long ActualPosition {
            get { return physicalOrderCache.GetActualPosition(symbol); }
		}

		public void SetActualPosition( long position)
		{
		    physicalOrderCache.SetActualPosition(symbol, position);
		}

        public void IncreaseActualPosition( int position)
        {
            var result = physicalOrderCache.IncreaseActualPosition(symbol, position);
            if( debug) log.Debug("Changed actual postion to " + result);
        }

		public PhysicalOrderHandler PhysicalOrderHandler {
			get { return physicalOrderHandler; }
		}
		
		public Action<SymbolInfo,LogicalFillBinary> OnProcessFill {
			get { return onProcessFill; }
			set { onProcessFill = value; }
		}
		
		public bool HandleSimulatedExits {
			get { return handleSimulatedExits; }
			set { handleSimulatedExits = value; }
		}

	    public LogicalOrderCache LogicalOrderCache
	    {
	        get { return logicalOrderCache; }
	    }

        public bool IsSynchronized
        {
            get { return isPositionSynced; }
        }

	    public bool IsPositionSynced
	    {
	        get { return isPositionSynced; }
	        set { isPositionSynced = value; }
	    }

        public bool EnableSyncTicks
        {
            get { return enableSyncTicks; }
            set { enableSyncTicks = value; }
        }

        public int RejectRepeatCounter
        {
            get { return rejectRepeatCounter; }
            set { rejectRepeatCounter = value; }
        }

        public bool IsBrokerOnline
        {
            get { return isBrokerOnline; }
            set { isBrokerOnline = value; }
                    }

        public bool ReceivedDesiredPosition
        {
            get { return receivedDesiredPosition; }
        }

        // This is a callback to confirm order was properly placed.
        public void ConfirmChange(long brokerOrder, bool isRealTime)
        {
            CreateOrChangeOrder order;
            if (!physicalOrderCache.TryGetOrderById(brokerOrder, out order))
            {
                log.Warn("ConfirmChange: Cannot find physical order for id " + brokerOrder);
                return;
            }
            ++confirmedOrderCount;
            order.OrderState = OrderState.Active;
            if (debug) log.Debug("ConfirmChange(" + (isRealTime ? "RealTime" : "Recovery") + ") " + order);
            physicalOrderCache.PurgeOriginalOrder(order);
            if (isRealTime)
            {
                PerformCompareProtected();
            }
            if (DoSyncTicks)
            {
                tickSync.RemovePhysicalOrder(order);
            }
        }

        public bool HasBrokerOrder( CreateOrChangeOrder order)
        {
            return false;
        }

        public void ConfirmActive(long brokerOrder, bool isRealTime)
        {
            CreateOrChangeOrder order;
            if (!physicalOrderCache.TryGetOrderById(brokerOrder, out order))
            {
                log.Warn("ConfirmActive: Cannot find physical order for id " + brokerOrder);
                return;
            }
            if (debug) log.Debug("ConfirmActive(" + (isRealTime ? "RealTime" : "Recovery") + ") " + order);
            order.OrderState = OrderState.Active;
            if (isRealTime)
            {
                PerformCompareProtected();
            }
            if (DoSyncTicks)
            {
                tickSync.RemovePhysicalOrder(order);
            }
        }

        public void ConfirmCreate(long brokerOrder, bool isRealTime)
        {
            CreateOrChangeOrder order;
            if (!physicalOrderCache.TryGetOrderById(brokerOrder, out order))
            {
                log.Warn("ConfirmCreate: Cannot find physical order for id " + brokerOrder);
                return;
            }
            ++confirmedOrderCount;
            order.OrderState = OrderState.Active;
            if (debug) log.Debug("ConfirmCreate(" + (isRealTime ? "RealTime" : "Recovery") + ") " + order);
            if (isRealTime)
            {
                PerformCompareProtected();
            }
            if (DoSyncTicks)
            {
                tickSync.RemovePhysicalOrder(order);
            }
        }

        public void RejectOrder(long brokerOrder, bool isRealTime, bool retryImmediately)
        {
            CreateOrChangeOrder order;
            if (!physicalOrderCache.TryGetOrderById(brokerOrder, out order))
            {
                if( debug) log.Debug("RejectOrder: Cannot find physical order for id " + brokerOrder + ". Probably already filled or canceled.");
                return;
            }
            ++rejectRepeatCounter;
            confirmedOrderCount = 0;
            if (debug) log.Debug("RejectOrder(" + RejectRepeatCounter + ", " + (isRealTime ? "RealTime" : "Recovery") + ") " + order);
            physicalOrderCache.RemoveOrder(order.BrokerOrder);
            var origOrder = order.OriginalOrder;
            if (origOrder != null)
            {
                if( origOrder.OrderState == OrderState.Expired)
                {
                    if (debug) log.Debug("Removing expired order: " + order.OriginalOrder);
                    physicalOrderCache.PurgeOriginalOrder(order);
                }
            }
            if (isRealTime && retryImmediately)
            {
                if (!CheckForPending())
                {
                    PerformCompareProtected();
                }
            }
            if (DoSyncTicks)
            {
                tickSync.RemovePhysicalOrder(order);
            }
        }

        private int rejectCounter;

        public void ConfirmCancel(long brokerOrderId, bool isRealTime)
        {
            CreateOrChangeOrder brokerOrder;
            if (!physicalOrderCache.TryGetOrderById(brokerOrderId, out brokerOrder))
            {
                log.Warn("ConfirmCancel: Cannot find physical order for id " + brokerOrder);
                return;
            }
            if (brokerOrder.Action != OrderAction.Cancel)
            {
                var tempOrder = brokerOrder.ReplacedBy;
                if( tempOrder != null && tempOrder.Action == OrderAction.Cancel)
                {
                    brokerOrder = tempOrder;
                }
            }
            var origOrder = brokerOrder.OriginalOrder;
            ++confirmedOrderCount;
            if (debug) log.Debug("ConfirmCancel(" + (isRealTime ? "RealTime" : "Recovery") + ") " + brokerOrder);
            physicalOrderCache.RemoveOrder(brokerOrder.BrokerOrder);
            if (origOrder != null)
            {
                physicalOrderCache.RemoveOrder(origOrder.BrokerOrder);
            }
            if (isRealTime)
            {
			    PerformCompareProtected();
            }
            if (DoSyncTicks)
            {
                if( origOrder == null)
                {
                    log.Error("Original order is null: " + brokerOrder);
                }
                else
                {
                    if (origOrder.ReplacedBy != null)
                    {
                        tickSync.RemovePhysicalOrder(origOrder.ReplacedBy);
                    }
                    else
                    {
                        tickSync.RemovePhysicalOrder(origOrder);
                    }
                }
            }
        }
		
		public Iterable<CreateOrChangeOrder> GetActiveOrders(SymbolInfo symbol)
		{
			throw new NotImplementedException();
		}

    }
}

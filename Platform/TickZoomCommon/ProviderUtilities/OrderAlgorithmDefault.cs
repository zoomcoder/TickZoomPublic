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
        private Log log;
		private SymbolInfo symbol;
		private PhysicalOrderHandler physicalOrderHandler;
        private ActiveList<CreateOrChangeOrder> originalPhysicals;
        private SimpleLock bufferedLogicalsLocker = new SimpleLock();
        private volatile bool bufferedLogicalsChanged = false;
		private ActiveList<LogicalOrder> bufferedLogicals;
        private ActiveList<LogicalOrder> canceledLogicals;
        private ActiveList<LogicalOrder> originalLogicals;
		private ActiveList<LogicalOrder> logicalOrders;
        private ActiveList<CreateOrChangeOrder> physicalOrders;
		private List<LogicalOrder> extraLogicals = new List<LogicalOrder>();
		private int desiredPosition;
		private Action<SymbolInfo,LogicalFillBinary> onProcessFill;
		private bool handleSimulatedExits = false;
		private int actualPosition = 0;
		private int sentPhysicalOrders = 0;
		private TickSync tickSync;
		private Dictionary<long,long> filledOrders = new Dictionary<long,long>();
	    private LogicalOrderCache logicalOrderCache;
        private bool isPositionSynced = false;
        private long minimumTick;
        private List<MissingLevel> missingLevels = new List<MissingLevel>();
        private PhysicalOrderCache physicalOrderCache;

        public struct MissingLevel
        {
            public int Size;
            public long Price;
        }
		
		public OrderAlgorithmDefault(string name, SymbolInfo symbol, PhysicalOrderHandler brokerOrders, LogicalOrderCache logicalOrderCache, PhysicalOrderCache physicalOrderCache) {
			this.log = Factory.SysLog.GetLogger(typeof(OrderAlgorithmDefault).FullName + "." + symbol.Symbol.StripInvalidPathChars() + "." + name );
            log.Register(this);
			this.symbol = symbol;
		    this.logicalOrderCache = logicalOrderCache;
		    this.physicalOrderCache = physicalOrderCache;
			this.tickSync = SyncTicks.GetTickSync(symbol.BinaryIdentifier);
			this.physicalOrderHandler = brokerOrders;
            this.canceledLogicals = new ActiveList<LogicalOrder>();
            this.originalLogicals = new ActiveList<LogicalOrder>();
			this.bufferedLogicals = new ActiveList<LogicalOrder>();
            this.originalPhysicals = new ActiveList<CreateOrChangeOrder>();
			this.logicalOrders = new ActiveList<LogicalOrder>();
            this.physicalOrders = new ActiveList<CreateOrChangeOrder>();
		    this.minimumTick = symbol.MinimumTick.ToLong();
		}
		
		private List<CreateOrChangeOrder> TryMatchId( Iterable<CreateOrChangeOrder> list, LogicalOrder logical)
		{
            var physicalOrderMatches = new List<CreateOrChangeOrder>();
            for (var current = list.First; current != null; current = current.Next)
		    {
		        var physical = current.Value;
				if( logical.Id == physical.LogicalOrderId) {
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
			bool result = false;
            if (physical.OrderState != OrderState.Pending &&
                // Market orders can't be canceled.
                physical.Type != OrderType.BuyMarket &&
                physical.Type != OrderType.SellMarket)
            {
                result = Cancel(physical);
            }
            return result;
        }
		
        public bool Cancel(CreateOrChangeOrder physical)
        {
			var result = false;
            var cancelOrder = new CreateOrChangeOrderDefault(OrderState.Pending, symbol, physical);
            using (physicalOrderCache.Lock())
            {
                physical.ReplacedBy = cancelOrder;
            }
            if (physicalOrderCache.HasCancelOrder(cancelOrder))
            {
                if (debug) log.Debug("Ignoring cancel broker order " + physical.BrokerOrder + " as physical order cache has a cancel or replace already.");
            }
            else
            {
                if (debug) log.Debug("Cancel Broker Order: " + cancelOrder);
                physicalOrderCache.SetOrder(cancelOrder);
                TryAddPhysicalOrder(cancelOrder);
                if (physicalOrderHandler.OnCancelBrokerOrder(cancelOrder))
                {
                    sentPhysicalOrders++;
                    result = true;
                }
                else
                {
                    TryRemovePhysicalOrder(cancelOrder);
                    physicalOrderCache.RemoveOrder(cancelOrder.BrokerOrder);
                }
            }
		    return result;
		}
		
		private void TryChangeBrokerOrder(CreateOrChangeOrder createOrChange, CreateOrChangeOrder origOrder) {
            if (createOrChange.OrderState == OrderState.Active)
            {
                createOrChange.OriginalOrder = origOrder;
                origOrder.ReplacedBy = createOrChange;
                if (physicalOrderCache.HasCancelOrder(createOrChange))
                {
                    if (debug) log.Debug("Ignoring broker order " + origOrder.BrokerOrder + " as physical order cache has a cancel or replace already.");
                    return;
                }
                if (debug) log.Debug("Change Broker Order: " + createOrChange);
                TryAddPhysicalOrder(createOrChange);
                if (physicalOrderHandler.OnChangeBrokerOrder(createOrChange))
                {
                    sentPhysicalOrders++;
                    physicalOrderCache.SetOrder(createOrChange);
                }
                else
                {
                    TryRemovePhysicalOrder(createOrChange);
                }
			}
		}
		
		private void TryAddPhysicalOrder(CreateOrChangeOrder createOrChange) {
			if( SyncTicks.Enabled) tickSync.AddPhysicalOrder(createOrChange);
		}

        private void TryRemovePhysicalOrder(CreateOrChangeOrder createOrChange)
        {
            if (SyncTicks.Enabled) tickSync.RemovePhysicalOrder(createOrChange);
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
            TryAddPhysicalOrder(physical);
            if (physicalOrderHandler.OnCreateBrokerOrder(physical))
            {
                physicalOrderCache.SetOrder(physical);
                sentPhysicalOrders++;
            }
            else
            {
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

        public virtual void ProcessMatchPhysicalEntry(LogicalOrder logical, List<CreateOrChangeOrder> matches)
		{
            if (matches.Count != 1)
            {
                log.Warn("Expected 1 match but found " + matches.Count + " matches for logical order: " + logical + "\n" + ToString(matches));
            }
            physicalOrders.Remove(matches[0]);
            ProcessMatchPhysicalEntry(logical, matches[0], logical.Position, logical.Price);
            return;
		}

        protected void ProcessMatchPhysicalEntry(LogicalOrder logical, CreateOrChangeOrder physical, int position, double price)
        {
			log.Trace("ProcessMatchPhysicalEntry()");
			var strategyPosition = logical.StrategyPosition;
			var difference = position - Math.Abs(strategyPosition);
			log.Trace("position difference = " + difference);
			if( difference == 0) {
				TryCancelBrokerOrder(physical);
			} else if( difference != physical.Size) {
				if( strategyPosition == 0) {
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
				physicalOrders.Remove(physical);
				var side = GetOrderSide(logical.Type);
                var changeOrder = new CreateOrChangeOrderDefault(OrderAction.Change, symbol, logical, side, difference, price);
                TryChangeBrokerOrder(changeOrder, physical);
            }
            else
            {
				VerifySide( logical, physical, price);
			}
		}

        private void ProcessMatchPhysicalReverse(LogicalOrder logical, List<CreateOrChangeOrder> matches)
        {
            if (matches.Count != 1)
            {
                log.Warn("Expected 1 match but found " + matches.Count + " matches for logical order: " + logical + "\n" + ToString(matches));
            }
            physicalOrders.Remove(matches[0]);
            var physical = matches[0];
            ProcessMatchPhysicalReverse(logical, physical, logical.Position, logical.Price);
            return;
        }

        private void ProcessMatchPhysicalReverse(LogicalOrder logical, CreateOrChangeOrder createOrChange, int position, double price)
        {
			var strategyPosition = logical.StrategyPosition;
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
			if( delta == 0 || strategyPosition > 0 && logicalPosition > 0 ||
			  strategyPosition < 0 && logicalPosition < 0) {
				TryCancelBrokerOrder(createOrChange);
			} else if( difference != 0) {
				var origBrokerOrder = createOrChange.BrokerOrder;
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
							ProcessMatchPhysicalChangePriceAndSide( logical, createOrChange, delta, price);
							return;
						}
					} else {
						side = OrderSide.SellShort;
					}
					side = (long) strategyPosition >= (long) Math.Abs(delta) ? OrderSide.Sell : OrderSide.SellShort;
                    var changeOrder = new CreateOrChangeOrderDefault(OrderAction.Change, symbol, logical, side, Math.Abs(delta), price);
                    TryChangeBrokerOrder(changeOrder, createOrChange);
                }
			} else {
				ProcessMatchPhysicalChangePriceAndSide( logical, createOrChange, delta, price);
			}
		}
		
        private void MatchLogicalToPhysicals(LogicalOrder logical, List<CreateOrChangeOrder> matches, Action<LogicalOrder, CreateOrChangeOrder, int, double> onMatchCallback)
        {
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
                    onMatchCallback(logical, physical, size, levelPrice.ToDouble());
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
                    onMatchCallback(logical, physical, missingLevel.Size, missingLevel.Price.ToDouble());
                    missingLevels.RemoveAt(0);
                }
                else
                {
                    ProcessExtraPhysical(physical);
                }

            }
            for (var i = 0; i < missingLevels.Count; i++ ) 
            {
                var missingLevel = missingLevels[i];
                ProcessMissingPhysical(logical, missingLevel.Size, missingLevel.Price.ToDouble());
            }
        }

        private void ProcessMatchPhysicalChange(LogicalOrder logical, List<CreateOrChangeOrder> matches)
        {
            MatchLogicalToPhysicals(logical, matches, ProcessMatchPhysicalChange);
        }

        private void ProcessMatchPhysicalChange(LogicalOrder logical, CreateOrChangeOrder createOrChange, int position, double price)
        {
			var strategyPosition = logical.StrategyPosition;
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
				TryCancelBrokerOrder(createOrChange);
			} else if( difference != 0) {
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
							return;
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
				ProcessMatchPhysicalChangePriceAndSide(logical,createOrChange,delta,price);
			}
		}
		
		private void ProcessMatchPhysicalChangePriceAndSide(LogicalOrder logical, CreateOrChangeOrder createOrChange, int delta, double price) {
			if( price.ToLong() != createOrChange.Price.ToLong()) {
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
				VerifySide( logical, createOrChange, price);
			}
		}

        private void ProcessMatchPhysicalExit(LogicalOrder logical, List<CreateOrChangeOrder> matches)
        {
            if (matches.Count != 1)
            {
                log.Warn("Expected 1 match but found " + matches.Count + " matches for logical order: " + logical + "\n" + ToString(matches));
            }
            var physical = matches[0];
            physicalOrders.Remove(matches[0]);
            ProcessMatchPhysicalExit(logical, physical, logical.Position, logical.Price);
            return;
        }

        private void ProcessMatchPhysicalExit(LogicalOrder logical, CreateOrChangeOrder createOrChange, int position, double price)
        {
			var strategyPosition = logical.StrategyPosition;
			if( strategyPosition == 0) {
				TryCancelBrokerOrder(createOrChange);
			} else if( Math.Abs(strategyPosition) != createOrChange.Size || price.ToLong() != createOrChange.Price.ToLong()) {
				var origBrokerOrder = createOrChange.BrokerOrder;
				physicalOrders.Remove(createOrChange);
				var side = GetOrderSide(logical.Type);
                var changeOrder = new CreateOrChangeOrderDefault(OrderAction.Change, symbol, logical, side, Math.Abs(strategyPosition), price);
                TryChangeBrokerOrder(changeOrder, createOrChange);
            }
            else
            {
				VerifySide( logical, createOrChange, price);
			}
		}

        private void ProcessMatchPhysicalExitStrategy(LogicalOrder logical, List<CreateOrChangeOrder> matches)
        {
            if( logical.Levels == 1) {
                if (matches.Count != 1)
                {
                    log.Warn("Expected 1 match but found " + matches.Count + " matches for logical order: " + logical + "\n" + ToString(matches));
                }
                physicalOrders.Remove(matches[0]);
                var physical = matches[0];
                ProcessMatchPhysicalExitStrategy(logical,physical,logical.Position,logical.Price);
                return;
            }
            MatchLogicalToPhysicals(logical, matches, ProcessMatchPhysicalExitStrategy);
        }

        private void ProcessMatchPhysicalExitStrategy(LogicalOrder logical, CreateOrChangeOrder createOrChange, int position, double price)
        {
			var strategyPosition = logical.StrategyPosition;
			if( strategyPosition == 0) {
				TryCancelBrokerOrder(createOrChange);
			} else if( Math.Abs(strategyPosition) != createOrChange.Size || price.ToLong() != createOrChange.Price.ToLong()) {
				var origBrokerOrder = createOrChange.BrokerOrder;
				physicalOrders.Remove(createOrChange);
				var side = GetOrderSide(logical.Type);
                var changeOrder = new CreateOrChangeOrderDefault(OrderAction.Change, symbol, logical, side, Math.Abs(strategyPosition), price);
                TryChangeBrokerOrder(changeOrder, createOrChange);
            }
            else
            {
				VerifySide( logical, createOrChange, price);
			}
		}

        private SimpleLock matchLocker = new SimpleLock();
		private void ProcessMatch(LogicalOrder logical, List<CreateOrChangeOrder> matches)
		{
		    if( !matchLocker.TryLock()) return;
            try
		    {
			    if( trace) log.Trace("Process Match()");
			    switch( logical.TradeDirection) {
				    case TradeDirection.Entry:
					    ProcessMatchPhysicalEntry( logical, matches);
					    break;
				    case TradeDirection.Exit:
					    ProcessMatchPhysicalExit( logical, matches);
					    break;
				    case TradeDirection.ExitStrategy:
					    ProcessMatchPhysicalExitStrategy( logical, matches);
					    break;
				    case TradeDirection.Reverse:
					    ProcessMatchPhysicalReverse( logical, matches);
					    break;
				    case TradeDirection.Change:
					    ProcessMatchPhysicalChange( logical, matches);
					    break;
				    default:
					    throw new ApplicationException("Unknown TradeDirection: " + logical.TradeDirection);
			    }
		    } finally
            {
                matchLocker.Unlock();
            }
		}

		private void VerifySide( LogicalOrder logical, CreateOrChangeOrder createOrChange, double price) {
			var side = GetOrderSide(logical.Type);
			if( createOrChange.Side != side) {
                if (debug) log.Debug("Canceling because " + createOrChange.Side + " != " + side + ": " + createOrChange);
				TryCancelBrokerOrder(createOrChange);
                createOrChange = new CreateOrChangeOrderDefault(OrderState.Active, symbol, logical, side, createOrChange.Size, price);
				TryCreateBrokerOrder(createOrChange);
			}
		}
		
		private void ProcessExtraLogical(LogicalOrder logical) {
			// When flat, allow entry orders.
			switch(logical.TradeDirection) {
				case TradeDirection.Entry:
    				ProcessMissingPhysical(logical);
					break;
				case TradeDirection.Exit:
				case TradeDirection.ExitStrategy:
					if( logical.StrategyPosition != 0) {
						ProcessMissingPhysical(logical);
					}
					break;
				case TradeDirection.Reverse:
					ProcessMissingPhysical(logical);
					break;
				case TradeDirection.Change:
					ProcessMissingPhysical(logical);
					break;
				default:
					throw new ApplicationException("Unknown trade direction: " + logical.TradeDirection);
			}
		}

        private void ProcessMissingPhysical(LogicalOrder logical)
        {
            if( logical.Levels == 1)
            {
                ProcessMissingPhysical(logical, logical.Position, logical.Price);
                return;
            }
            var price = logical.Price.ToLong();
            var sign = 1;
            switch( logical.Type)
            {
                case OrderType.BuyMarket:
                case OrderType.SellMarket:
                    ProcessMissingPhysical(logical, logical.Position, logical.Price);
                    return;
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
                ProcessMissingPhysical(logical, size, levelPrice.ToDouble());
                logicalPosition -= logical.LevelSize;
            }
        }

        private void ProcessMissingPhysical(LogicalOrder logical, int position, double price) {
            var logicalPosition =
                logical.Type == OrderType.BuyLimit ||
                logical.Type == OrderType.BuyMarket ||
                logical.Type == OrderType.BuyStop ?
                position : -position;
            var size = Math.Abs(logicalPosition - logical.StrategyPosition);
            switch (logical.TradeDirection)
            {
				case TradeDirection.Entry:
					if(debug) log.Debug("ProcessMissingPhysicalEntry("+logical+")");
                    var side = GetOrderSide(logical.Type);
                    if (logicalPosition < 0 && logical.StrategyPosition <= 0 && logical.StrategyPosition > logicalPosition)
                    {
                        var physical = new CreateOrChangeOrderDefault(OrderState.Active, symbol, logical, side, size, price);
                        TryCreateBrokerOrder(physical);
                    }
                    if (logicalPosition > 0 && logical.StrategyPosition >= 0 && logical.StrategyPosition < logicalPosition)
                    {
                        var physical = new CreateOrChangeOrderDefault(OrderState.Active, symbol, logical, side, size, price);
                        TryCreateBrokerOrder(physical);
                    }
					break;
				case TradeDirection.Exit:
				case TradeDirection.ExitStrategy:
					size = Math.Abs(logical.StrategyPosition);
					ProcessMissingExit( logical, size, price);
					break;
				case TradeDirection.Reverse:
                    if (logicalPosition < 0 && logical.StrategyPosition > logicalPosition)
                    {
                        ProcessMissingReverse(logical, size, price);
                    }
                    if (logicalPosition > 0 && logical.StrategyPosition < logicalPosition)
                    {
                        ProcessMissingReverse(logical, size, price);
                    }
                    break;
				case TradeDirection.Change:
					logicalPosition += logical.StrategyPosition;
					size = Math.Abs(logicalPosition - logical.StrategyPosition);
					if( size != 0) {
						if(debug) log.Debug("ProcessMissingPhysical("+logical+")");
						side = GetOrderSide(logical.Type);
                        var physical = new CreateOrChangeOrderDefault(OrderState.Active, symbol, logical, side, size, price);
						TryCreateBrokerOrder(physical);
					}
					break;
				default:
					throw new ApplicationException("Unknown trade direction: " + logical.TradeDirection);
			}
		}

        private void ProcessMissingReverse(LogicalOrder logical, int size, double price)
        {
            if (debug) log.Debug("ProcessMissingPhysical(" + logical + ")");
            var side = GetOrderSide(logical.Type);
            var physical = new CreateOrChangeOrderDefault(OrderState.Active, symbol, logical, side, size, price);
            TryCreateBrokerOrder(physical);
        }

        private void ProcessMissingExit(LogicalOrder logical, int size, double price)
        {
			if( logical.StrategyPosition > 0) {
				if( logical.Type == OrderType.SellLimit ||
				  logical.Type == OrderType.SellStop ||
				  logical.Type == OrderType.SellMarket) {
					if(debug) log.Debug("ProcessMissingPhysical("+logical+")");
					var side = GetOrderSide(logical.Type);
                    var physical = new CreateOrChangeOrderDefault(OrderState.Active, symbol, logical, side, size, price);
					TryCreateBrokerOrder(physical);
				}
			}
			if( logical.StrategyPosition < 0) {
				if( logical.Type == OrderType.BuyLimit ||
				  logical.Type == OrderType.BuyStop ||
				  logical.Type == OrderType.BuyMarket) {
					if(debug) log.Debug("ProcessMissingPhysical("+logical+")");
					var side = GetOrderSide(logical.Type);
                    var physical = new CreateOrChangeOrderDefault(OrderState.Active, symbol, logical, side, size, price);
					TryCreateBrokerOrder(physical);
				}
			}
		}

        private bool CheckFilledOrder(LogicalOrder logical, int position)
        {
            switch (logical.Type)
            {
                case OrderType.BuyLimit:
                case OrderType.BuyMarket:
                case OrderType.BuyStop:
                    if (logical.TradeDirection == TradeDirection.Change)
                    {
                        return position >= logical.Position + logical.StrategyPosition;
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
                        return position <= -logical.Position + logical.StrategyPosition;
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
					if( actualPosition > 0) {
						return OrderSide.Sell;
					} else {
						return OrderSide.SellShort;
					}
				default:
					throw new ApplicationException("Unknown OrderType: " + type);
			}
		}
		
		private bool ProcessExtraPhysical(CreateOrChangeOrder createOrChange) {
			return TryCancelBrokerOrder( createOrChange);
		}
		
		private int FindPendingAdjustments() {
			var positionDelta = desiredPosition - actualPosition;
			var pendingAdjustments = 0;

            originalPhysicals.Clear();
            originalPhysicals.AddLast(physicalOrderCache.GetActiveOrders(symbol));

			var next = originalPhysicals.First;
			for( var node = next; node != null; node = next) {
				next = node.Next;
				CreateOrChangeOrder order = node.Value;
				if(order.Type != OrderType.BuyMarket &&
				   order.Type != OrderType.SellMarket) {
					continue;
				}
			    switch (order.OrderState) 
			    {
                    case OrderState.Filled:
                    case OrderState.Lost:
			            continue;
                    case OrderState.Active:
                    case OrderState.Pending:
                    case OrderState.PendingNew:
                    case OrderState.Suspended:
			            break;
                    default:
                        throw new ApplicationException("Unknown order state: " + order.OrderState);
			    }
				if( order.LogicalOrderId == 0) {
					if( order.Type == OrderType.BuyMarket) {
						pendingAdjustments += order.Size;
					}
					if( order.Type == OrderType.SellMarket) {
						pendingAdjustments -= order.Size;
					}
					if( positionDelta > 0) {
						if( pendingAdjustments > positionDelta) {
							TryCancelBrokerOrder(order);
							pendingAdjustments -= order.Size;
						} else if( pendingAdjustments < 0) {
							TryCancelBrokerOrder(order);
							pendingAdjustments += order.Size;
						}
					}
					if( positionDelta < 0) {
						if( pendingAdjustments < positionDelta) {
							TryCancelBrokerOrder(order);
							pendingAdjustments += order.Size;
						} else if( pendingAdjustments > 0) {
							TryCancelBrokerOrder(order);
							pendingAdjustments -= order.Size;
						}
					}
					if( positionDelta == 0) {
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
            if (isPositionSynced)
            {
                if (debug) log.Debug("TrySyncPosition() ignore. Position already synced.");
                return;
            }
            logicalOrderCache.SyncPositions(strategyPositions);
            SyncPosition();
        }

	    private void SyncPosition()
        {
            // Find any pending adjustments.
            var pendingAdjustments = FindPendingAdjustments();
            var positionDelta = desiredPosition - actualPosition;
			var delta = positionDelta - pendingAdjustments;
			CreateOrChangeOrder createOrChange;
            if( delta != 0)
            {
                IsPositionSynced = false;
                log.Notice("SyncPositionInternal() Issuing adjustment order because expected position is " + desiredPosition + " but actual is " + actualPosition + " plus pending adjustments " + pendingAdjustments);
                if (debug) log.Debug("TrySyncPosition - " + tickSync);
            }
            else if( positionDelta == 0)
            {
                IsPositionSynced = true;
                log.Notice("SyncPositionInternal() found position currently synced. With expected " + desiredPosition + " and actual " + actualPosition + " plus pending adjustments " + pendingAdjustments);
            }
			if( delta > 0)
			{
                createOrChange = new CreateOrChangeOrderDefault(OrderAction.Create, OrderState.Active, symbol, OrderSide.Buy, OrderType.BuyMarket, OrderFlags.None, 0, delta, 0, 0, null, null, default(TimeStamp));
                log.Info("Sending adjustment order to position: " + createOrChange);
                if( TryCreateBrokerOrder(createOrChange))
                {
                    if (SyncTicks.Enabled)
                    {
                        tickSync.RemoveProcessPhysicalOrders();
                    }
                }
            }
            else if (delta < 0)
            {
                OrderSide side;
                var pendingDelta = actualPosition + pendingAdjustments;
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
                    side = actualPosition >= Math.Abs(delta) ? OrderSide.Sell : OrderSide.SellShort;
                    createOrChange = new CreateOrChangeOrderDefault(OrderAction.Create, OrderState.Active, symbol, side, OrderType.SellMarket, OrderFlags.None, 0, Math.Abs(delta), 0, 0, null, null, default(TimeStamp));
                    log.Info("Sending adjustment order to correct position: " + createOrChange);
                    if (TryCreateBrokerOrder(createOrChange))
                    {
                        if (SyncTicks.Enabled)
                        {
                            tickSync.RemoveProcessPhysicalOrders();
                        }
                    }
                }
            }
        }

        public void SetLogicalOrders(Iterable<LogicalOrder> inputLogicals, Iterable<StrategyPosition> strategyPositions)
        {
			if( trace) {
				int count = originalLogicals == null ? 0 : originalLogicals.Count;
				log.Trace("SetLogicalOrders() order count = " + count);
			}
            if (CheckForFilledOrders(inputLogicals))
            {
                if (debug) log.Debug("Found already filled orders in position change event. Ignoring until recent fills get posted.");
                return;
            }
            logicalOrderCache.SetActiveOrders(inputLogicals);
			bufferedLogicals.Clear();
			bufferedLogicals.AddLast(logicalOrderCache.ActiveOrders);
		    canceledLogicals.AddLast(logicalOrderCache.ActiveOrders);
		    bufferedLogicalsChanged = true;
            if( debug) log.Debug("SetLogicalOrders( logicals " + bufferedLogicals.Count + ", strategy positions " + strategyPositions.Count);
		}
		
		public void SetDesiredPosition(	int position) {
			this.desiredPosition = position;
		}
		
		private bool CheckForPendingInternal() {
			var result = false;
		    var next = originalPhysicals.First;
		    for (var current = next; current != null; current = next)
		    {
		        next = current.Next;
		        var order = current.Value;
				if( order.OrderState == OrderState.Pending ||
                    order.OrderState == OrderState.PendingNew || 
                    order.Type == OrderType.BuyMarket ||
				    order.Type == OrderType.SellMarket) {
					if( debug) log.Debug("Pending order: " + order);
					result = true;	
				}
			}
			return result;
		}

        public void CheckForPending()
        {
            var expiryLimit = TimeStamp.UtcNow;
            if( SyncTicks.Enabled)
            {
                expiryLimit.AddSeconds(-1);
            }
            else
            {
                expiryLimit.AddSeconds(-5);
            }
            if (debug) log.Debug("Checking for orders pending since: " + expiryLimit);
            var list = physicalOrderCache.GetOrders((x) => x.Symbol == symbol && (x.OrderState == OrderState.Pending || x.OrderState == OrderState.PendingNew));
            var cancelOrders = new List<CreateOrChangeOrder>();
            foreach( var order in list)
            {
                if( debug) log.Debug("Pending order: " + order);
                var lastChange = order.LastStateChange;
                if( lastChange < expiryLimit)
                {
                    if( order.Action == OrderAction.Cancel)
                    {
                        if (debug) log.Debug("Removing pending and stale Cancel order: " + order);
                        physicalOrderCache.RemoveOrder(order.BrokerOrder);
                        var origOrder = order.OriginalOrder;
                        if (origOrder != null)
                        {
                            origOrder.ReplacedBy = null;
                        }
                        cancelOrders.Add(order);
                    }
                    else if( Cancel(order))
                    {
                        var diff = TimeStamp.UtcNow - lastChange;
                        if( !SyncTicks.Enabled)
                        {
                            log.Warn( "Sent cancel for pending order " + order.BrokerOrder + " that is stale over " + diff.TotalSeconds + " seconds.");
                        }
                        order.ResetLastChange();
                    }
                }
            }
            if( cancelOrders.Count > 0)
            {
                PerformCompareInternal();
                foreach( var order in cancelOrders)
                {
                    if (SyncTicks.Enabled)
                    {
                        tickSync.RemovePhysicalOrder(order);
                    }
                }
            }
        }

        private LogicalOrder FindActiveLogicalOrder(long serialNumber)
        {
            for (var current = originalLogicals.First; current != null; current = current.Next)
            {
                var order = current.Value;
                if (order.SerialNumber == serialNumber)
                {
                    return order;
                }
            }
            return null;
        }

        private LogicalOrder FindHistoricalLogicalOrder(long serialNumber) {
            for (var current = canceledLogicals.Last; current != null; current = current.Previous)
            {
                var order = current.Value;
				if( order.SerialNumber == serialNumber) {
                    return order;
                }
            }
            while( canceledLogicals.Count > 20)
            {
                canceledLogicals.RemoveFirst();
            }
		    return null;
		}

        private void TryCleanCanceledLogicals()
        {
            //if( canceledLogicals.Count > 100)
            //{
            //    canceledLogicals.RemoveFirst();
            //}
        }
		
		public void ProcessFill( PhysicalFill physical) {
            if (debug) log.Debug("ProcessFill() physical: " + physical);
		    var beforePosition = actualPosition;
            actualPosition += physical.Size;
            if( debug) log.Debug("Updating actual position from " + beforePosition + " to " + actualPosition + " from fill size " + physical.Size);
			var isCompletePhysicalFill = physical.RemainingSize == 0;
            var isFilledAfterCancel = false;
            TryFlushBufferedLogicals();
            var logical = FindActiveLogicalOrder(physical.Order.LogicalSerialNumber);
            if( logical == null)
            {
                logical = FindHistoricalLogicalOrder(physical.Order.LogicalSerialNumber);
                if( logical != null)
                {
                    isFilledAfterCancel = true;
                }
            }
            else
            {
                if( logical.Price != physical.Order.Price)
                {
                    isFilledAfterCancel = true;
                }
            }

		    if( isCompletePhysicalFill) {
				if( debug) log.Debug("Physical order completely filled: " + physical.Order);
                physical.Order.OrderState = OrderState.Filled;
                originalPhysicals.Remove(physical.Order);
                physicalOrders.Remove(physical.Order);
                if (physical.Order.ReplacedBy != null)
                {
                    if (debug) log.Debug("Found this order in the replace property. Removing it also: " + physical.Order.ReplacedBy);
                    originalPhysicals.Remove(physical.Order.ReplacedBy);
                    physicalOrders.Remove(physical.Order.ReplacedBy);
                    physicalOrderCache.RemoveOrder(physical.Order.ReplacedBy.BrokerOrder);
                }
                physicalOrderCache.RemoveOrder(physical.Order.BrokerOrder);
			}
            else
            {
				if( debug) log.Debug("Physical order partially filled: " + physical.Order);
			}

            if (debug) log.Debug("isFilledAfterCancel " + isFilledAfterCancel + ", OffsetTooLateToCancel " + physical.Order.OffsetTooLateToCancel);
            if (isFilledAfterCancel && physical.Order.OffsetTooLateToCancel)
            {
                if (debug) log.Debug("Will sync positions because fill from order already canceled: " + physical.Order.ReplacedBy);
                SyncPosition();
                TryRemovePhysicalFill(physical);
            }
            else if (!isPositionSynced && physical.Order.LogicalSerialNumber > 0L)
            {
                if (debug) log.Debug("ProcessFill() for logical ignored. Position not yet synced.");
                return;
            } else {
    		    LogicalFillBinary fill;
                if( logical != null) {
                    desiredPosition += physical.Size;
                    if (debug) log.Debug("Adjusting symbol position to desired " + desiredPosition + ", physical fill was " + physical.Size);
                    var position = logical.StrategyPosition + physical.Size;
                    if (debug) log.Debug("Creating logical fill with position " + position + " from strategy position " + logical.StrategyPosition);
                    if( position != logical.Position)
                    {
                        if( debug) log.Debug("strategy position " + position + " differs from logical order position " + logical.Position + " for " + logical);
                    }
                    var strategyPosition = (StrategyPositionDefault)logical.Strategy;
                    fill = new LogicalFillBinary(
                        position, strategyPosition.Recency + 1, physical.Price, physical.Time, physical.UtcTime, physical.Order.LogicalOrderId, physical.Order.LogicalSerialNumber, logical.Position, physical.IsSimulated);
                }
                else
                {
                    log.Info("Leaving symbol position at desired " + desiredPosition + ", since this appears to be an adjustment market order: " + physical.Order);
                    if (debug) log.Debug("Skipping logical fill for an adjustment market order.");
                    if (debug) log.Debug("Performing extra compare.");
                    PerformCompareProtected();
                    TryRemovePhysicalFill(physical);
                    return;
                }
                if (debug) log.Debug("Fill price: " + fill);
                ProcessFill(fill, logical, isCompletePhysicalFill, physical.IsRealTime);
            }
		}		

        private TaskLock performCompareLocker = new TaskLock();
		private void PerformCompareProtected() {
			var count = Interlocked.Increment(ref recursiveCounter);
		    if( count == 1)
		    {
		        var isCompareSuccess = false;
                //var whileCounter = 0;
				while( recursiveCounter > 0)
				{
                    for (var i = 0; i < recursiveCounter-1; i++ )
                    {
                        Interlocked.Decrement(ref recursiveCounter);
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
                            isCompareSuccess = PerformCompareInternal();
                            physicalOrderHandler.ProcessOrders();
                            if (trace) log.Trace("PerformCompare finished - " + tickSync);
                        }
                        else
                        {
                            var extra = SyncTicks.Enabled ? tickSync.ToString() : "";
                            if (debug) log.Debug("PerformCompare ignored. Position not yet synced. " + extra);
                        }

                        if (SyncTicks.Enabled)
                        {
                            tickSync.RollbackPhysicalOrders();
                            tickSync.RollbackPositionChange();
                            tickSync.RollbackProcessPhysicalOrders();
                            tickSync.RollbackReprocessPhysicalOrders();
                            tickSync.RollbackPhysicalFills();
                        }
					}
                    finally {
						Interlocked.Decrement( ref recursiveCounter);
                    }
				}
                if (SyncTicks.Enabled)
                {
                    if( isCompareSuccess && tickSync.SentPositionChange && tickSync.IsSinglePhysicalFillSimulator)
                    {
                        tickSync.RemovePositionChange();
                    }
                }
            }
            else
			{
			    if( debug) log.Debug( "Skipping ProcesOrders. RecursiveCounter " + count + "\n" + tickSync);
			}
		}
		private long nextOrderId = 1000000000;
		private bool useTimeStampId = true;
		private long GetUniqueOrderId() {
			if( useTimeStampId) {
				return TimeStamp.UtcNow.Internal;
			} else {
				return Interlocked.Increment(ref nextOrderId);
			}
		}
		
		private void TryRemovePhysicalFill(PhysicalFill fill) {
			if( SyncTicks.Enabled) tickSync.RemovePhysicalFill(fill);
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

            var strategyPosition = filledOrder.StrategyPosition;
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
			UpdateOrderCache(filledOrder, fill);
            if (isCompletePhysicalFill && !fill.IsComplete)
            {
                if (filledOrder.TradeDirection == TradeDirection.Entry && fill.Position == 0)
                {
                    if (debug) log.Debug("Found a entry order which flattened the position. Likely due to bracketed entries that both get filled: " + filledOrder);
                    MarkAsFilled(filledOrder);
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
                    if (SyncTicks.Enabled)
                    {
                        tickSync.SetReprocessPhysicalOrders();
                    }
                }
			}
            if (onProcessFill != null)
            {
                if (debug) log.Debug("Sending logical fill for " + symbol + ": " + fill);
                onProcessFill(symbol, fill);
            }
            if (isRealTime && (fill.IsComplete || isCompletePhysicalFill))
            {
				if( debug) log.Debug("Performing extra compare.");
				PerformCompareProtected();
			}
        }

        private void MarkAsFilled(LogicalOrder filledOrder)
        {
            try
            {
                if (debug) log.Debug("Marking order id " + filledOrder.Id + " as completely filled.");
                filledOrders.Add(filledOrder.SerialNumber, TimeStamp.UtcNow.Internal);
                originalLogicals.Remove(filledOrder);
                CleanupAfterFill(filledOrder);
            }
            catch (ApplicationException)
            {

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

		private void CleanupAfterFill(LogicalOrder filledOrder) {
			bool clean = false;
			bool cancelAllEntries = false;
			bool cancelAllExits = false;
			bool cancelAllExitStrategies = false;
			bool cancelAllReverse = false;
			bool cancelAllChanges = false;
			if( filledOrder.StrategyPosition == 0) {
				cancelAllChanges = true;
				clean = true;
			}
			switch( filledOrder.TradeDirection) {
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
					cancelAllChanges = true;
					clean = true;
					break;
				case TradeDirection.Reverse:
					cancelAllReverse = true;
					clean = true;
					break;
				default:
					throw new ApplicationException("Unknown trade direction: " + filledOrder.TradeDirection);
			}
			if( clean) {
                TryCleanCanceledLogicals();
			    for (var current = originalLogicals.First; current != null; current = current.Next)
			    {
			        var order = current.Value;
					if( order.StrategyId == filledOrder.StrategyId) {
						switch( order.TradeDirection) {
							case TradeDirection.Entry:
								if( cancelAllEntries) CancelLogical(order);
								break;
							case TradeDirection.Change:
                                if (cancelAllChanges) CancelLogical(order);
								break;
							case TradeDirection.Exit:
                                if (cancelAllExits) CancelLogical(order);
								break;
							case TradeDirection.ExitStrategy:
                                if (cancelAllExitStrategies) CancelLogical(order);
								break;
							case TradeDirection.Reverse:
                                if (cancelAllReverse) CancelLogical(order);
								break;
							default:
								throw new ApplicationException("Unknown trade direction: " + filledOrder.TradeDirection);
						}
					}
				}
			}
		}
	
		private void UpdateOrderCache(LogicalOrder order, LogicalFill fill) {
			var strategyPosition = (StrategyPositionDefault) order.Strategy;
            if( debug) log.Debug("Adjusting strategy position to " + fill.Position + " from " + strategyPosition.ActualPosition + ". Recency " + fill.Recency + " for strategy id " + strategyPosition.Id);
            strategyPosition.TrySetPosition(fill.Position, fill.Recency);
//			orderCache.RemoveInactive(order);
		}
		
		public int ProcessOrders() {
            if (debug) log.Debug("ProcessOrders()");
            sentPhysicalOrders = 0;
			PerformCompareProtected();
            return sentPhysicalOrders;
		}

		private bool CheckForFilledOrders(Iterable<LogicalOrder> orders) {
            if( orders == null) return false;
		    var next = orders.First;
		    for (var current = next; current != null; current = next)
		    {
		        next = current.Next;
		        var logical = current.Value;
				var binaryTime = 0L;
				if( filledOrders.TryGetValue( logical.SerialNumber, out binaryTime)) {
					if( debug) log.Debug("Found already filled order: " + logical);
				   	return true;
				}
			}
			return false;
		}
		
		private int recursiveCounter;
		private bool PerformCompareInternal()
		{
			if( debug)
			{
			    log.Debug("PerformCompare for " + symbol + " with " +
			              actualPosition + " actual " +
			              desiredPosition + " desired.");
			}
				
            originalPhysicals.Clear();
            originalPhysicals.AddLast(physicalOrderCache.GetActiveOrders(symbol));

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
			logicalOrders.AddLast(originalLogicals);
			
			physicalOrders.Clear();
			if(originalPhysicals != null) {
				physicalOrders.AddLast(originalPhysicals);
			}
			
			CreateOrChangeOrder createOrChange;
			extraLogicals.Clear();
			while( logicalOrders.Count > 0) {
				var logical = logicalOrders.First.Value;
			    var matches = TryMatchId(physicalOrders, logical);
                if( matches.Count > 0)
                {
                    ProcessMatch( logical, matches);
                }
                else
                {
                    extraLogicals.Add(logical);
				}
				logicalOrders.Remove(logical);
			}

			
			if( trace) log.Trace("Found " + physicalOrders.Count + " extra physicals.");
			int cancelCount = 0;
			while( physicalOrders.Count > 0) {
				createOrChange = physicalOrders.First.Value;
				if( ProcessExtraPhysical(createOrChange)) {
					cancelCount++;
				}
				physicalOrders.Remove(createOrChange);
			}
			
			if( cancelCount > 0) {
				// Wait for cancels to complete before creating any orders.
				return false;
			}

            if (trace) log.Trace("Found " + extraLogicals.Count + " extra logicals.");
            while (extraLogicals.Count > 0)
            {
                var logical = extraLogicals[0];
                ProcessExtraLogical(logical);
                extraLogicals.Remove(logical);
            }
		    return true;
		}

        private void TryFlushBufferedLogicals()
        {
            if (bufferedLogicalsChanged)
            {
                if (CheckForFilledOrders(bufferedLogicals))
                {
                    if (debug) log.Debug("Found already filled orders in position change event. Ignoring until recent fills get posted.");
                    bufferedLogicalsChanged = false;
                }
                else
                {
                    if (debug) log.Debug("Buffered logicals were updated so refreshing original logicals list ...");
                    originalLogicals.Clear();
                    if (bufferedLogicals != null)
                    {
                        originalLogicals.AddLast(bufferedLogicals);
                    }
                    bufferedLogicalsChanged = false;
                }
            }
        }

        private void LogOrders( Iterable<LogicalOrder> orders, string name)
        {
            for (var node = orders.First; node != null; node = node.Next)
            {
                var order = node.Value;
                log.Debug("Logical Order: " + order);
            }
        }

        private void LogOrders( Iterable<CreateOrChangeOrder> orders, string name)
        {
            if( debug)
            {
                if( orders.Count > 0)
                {
                    log.Debug("Listing " + name + " orders:");
                }
                else
                {
                    log.Debug("Empty list of " + name + " orders.");
                    return;
                }
                for (var current = orders.First; current != null; current = current.Next)
                {
                    var order = current.Value;
                    log.Debug(name + ": " + order);
                }
            }
        }
	
		public int ActualPosition {
			get { return actualPosition; }
		}

		public void SetActualPosition( int position)
		{
		    var value = Interlocked.Exchange(ref actualPosition, position);
            if (debug) log.Debug("SetActualPosition(" + actualPosition + ")");
        }

        public void IncreaseActualPosition( int position)
        {
            var count = Math.Abs(position);
            var result = actualPosition;
            for( var i=0; i<count; i++)
            {
                if (position > 0)
                {
                    result = Interlocked.Increment(ref actualPosition);
                } else
                {
                    result = Interlocked.Decrement(ref actualPosition);
                }
            }
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

	    public bool IsPositionSynced
	    {
	        get { return isPositionSynced; }
	        set { isPositionSynced = value; }
	    }

	    // This is a callback to confirm order was properly placed.
        public void ConfirmChange(CreateOrChangeOrder order, bool isRealTime)
        {
            if (debug) log.Debug("ConfirmChange(" + (isRealTime ? "RealTime" : "Recovery") + ") " + order);
            physicalOrderCache.SetOrder(order);
            if (order.OriginalOrder != null)
            {
                physicalOrderCache.RemoveOrder(order.OriginalOrder.BrokerOrder);
                using (physicalOrderCache.Lock())
                {
                    order.OriginalOrder = null;
                }
            }
            if (isRealTime)
            {
                PerformCompareProtected();
            }
            if (SyncTicks.Enabled)
            {
                if (!tickSync.SentProcessPhysicalOrders)
                {
                    tickSync.SetReprocessPhysicalOrders();
                }
                tickSync.RemovePhysicalOrder(order);
            }
        }

        public bool HasBrokerOrder( CreateOrChangeOrder order)
        {
            return false;
        }

        public void ConfirmActive(CreateOrChangeOrder order, bool isRealTime)
        {
            if (debug) log.Debug("ConfirmActive(" + (isRealTime ? "RealTime" : "Recovery") + ") " + order);
            physicalOrderCache.SetOrder(order);
            if (isRealTime)
            {
                PerformCompareProtected();
            }
        }

        public void ConfirmCreate(CreateOrChangeOrder order, bool isRealTime)
        {
            if( debug) log.Debug("ConfirmCreate(" + (isRealTime ? "RealTime" : "Recovery") + ") " + order);
            physicalOrderCache.SetOrder(order);
            if (isRealTime)
            {
                PerformCompareProtected();
            }
            if (SyncTicks.Enabled)
            {
                if (!tickSync.SentProcessPhysicalOrders)
                {
                    tickSync.SetReprocessPhysicalOrders();
                }
                tickSync.RemovePhysicalOrder(order);
            }
        }

        public void RejectOrder(CreateOrChangeOrder order, bool removeOriginal, bool isRealTime)
        {
            if (debug) log.Debug("RejectOrder(" + (isRealTime ? "RealTime" : "Recovery") + ") " + order);
            physicalOrderCache.RemoveOrder(order.BrokerOrder);
            var origOrder = order.OriginalOrder;
            if (origOrder != null)
            {
                origOrder.ReplacedBy = null;
                if (removeOriginal)
                {
                    if( origOrder.OriginalOrder != null)
                    {
                        origOrder.OriginalOrder.ReplacedBy = null;
                    }
                    physicalOrderCache.RemoveOrder(origOrder.BrokerOrder);
                }
                else if (origOrder.OrderState == OrderState.Pending)
                {
                    origOrder.OrderState = OrderState.Active;
                }
            }
            if (isRealTime)
            {
                PerformCompareProtected();
            }
            if (SyncTicks.Enabled)
            {
                if( order.Action == OrderAction.Cancel)
                {
                    tickSync.RemovePhysicalOrder(order);
                }
                if (removeOriginal && origOrder != null && (origOrder.OrderState == OrderState.Pending || origOrder.OrderState == OrderState.PendingNew))
                {
                    tickSync.RemovePhysicalOrder(origOrder);
                }
            }
        }

        public void RemovePending(CreateOrChangeOrder order, bool isRealTime)
        {
            if (debug) log.Debug("RemovePending(" + (isRealTime ? "RealTime" : "Recovery") + ") " + order);
            order.OrderState = OrderState.Lost;
            if (isRealTime)
            {
                PerformCompareProtected();
            }
            if (SyncTicks.Enabled)
            {
                tickSync.RemovePhysicalOrder(order);
            }
        }

        public void ConfirmCancel(CreateOrChangeOrder order, bool isRealTime)
		{
            if (debug) log.Debug("ConfirmCancel(" + (isRealTime ? "RealTime" : "Recovery") + ") " + order);
            physicalOrderCache.RemoveOrder(order.BrokerOrder);
            var origOrder = order.OriginalOrder;
            if( origOrder != null)
            {
                origOrder.ReplacedBy = null;
            }
            if (order.ReplacedBy == null)
            {
                if (debug) log.Debug("CancelOrder w/o any replaced order specified happens normally: " + order + " ");
            }
            else
            {
                if( debug) log.Debug("Removing 'replaced by' order: " + order.ReplacedBy);
                physicalOrderCache.RemoveOrder(order.ReplacedBy.BrokerOrder);
            }
            if (isRealTime)
            {
			    PerformCompareProtected();
            }
            if (SyncTicks.Enabled)
            {
                if (order.ReplacedBy != null)
                {
                    tickSync.RemovePhysicalOrder(order.ReplacedBy);
                }
                else
                {
                    tickSync.RemovePhysicalOrder(order);
                }
            }
        }
		
		public Iterable<CreateOrChangeOrder> GetActiveOrders(SymbolInfo symbol)
		{
			throw new NotImplementedException();
		}

    }
}

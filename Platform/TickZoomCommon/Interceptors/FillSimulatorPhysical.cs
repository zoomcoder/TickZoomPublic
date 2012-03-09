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
using TickZoom.Api;
using TickZoom.Common;

namespace TickZoom.Interceptors
{
    public class FillSimulatorPhysical : FillSimulator, LogAware
    {
        private static readonly Log staticLog = Factory.SysLog.GetLogger(typeof(FillSimulatorPhysical));
        private Log log;
        private volatile bool trace = staticLog.IsTraceEnabled;
        private volatile bool verbose = staticLog.IsVerboseEnabled;
        private volatile bool debug = staticLog.IsDebugEnabled;
        private FillSimulatorLogic fillLogic;
        private bool isChanged;
        private bool enableSyncTicks;
        public void RefreshLogLevel()
        {
            if (log != null)
            {
                debug = log.IsDebugEnabled;
                trace = log.IsTraceEnabled;
            }
        }
        private struct FillWrapper
        {
            public bool IsCounterSet;
            public PhysicalFill Fill;
            public CreateOrChangeOrder Order;
        }
        private Queue<FillWrapper> fillQueue = new Queue<FillWrapper>();
        private struct RejectWrapper
        {
            public CreateOrChangeOrder Order;
            public bool RemoveOriginal;
            public string Message;
        }
        private Queue<RejectWrapper> rejectQueue = new Queue<RejectWrapper>();

        private PartialFillSimulation partialFillSimulation;

        private Dictionary<long, CreateOrChangeOrder> orderMap = new Dictionary<long, CreateOrChangeOrder>();
        private ActiveList<CreateOrChangeOrder> increaseOrders = new ActiveList<CreateOrChangeOrder>();
        private ActiveList<CreateOrChangeOrder> decreaseOrders = new ActiveList<CreateOrChangeOrder>();
        private ActiveList<CreateOrChangeOrder> marketOrders = new ActiveList<CreateOrChangeOrder>();
        private NodePool<CreateOrChangeOrder> nodePool = new NodePool<CreateOrChangeOrder>();
        private object orderMapLocker = new object();
        private bool isOpenTick = false;
        private TimeStamp openTime;

        private Action<PhysicalFill,CreateOrChangeOrder> onPhysicalFill;
        private Action<CreateOrChangeOrder, bool, string> onRejectOrder;
        private Action<long> onPositionChange;
        private bool useSyntheticMarkets = true;
        private bool useSyntheticStops = true;
        private bool useSyntheticLimits = true;
        private SymbolInfo symbol;
        private int actualPosition = 0;
        private TickSync tickSync;
        private TickIO currentTick = Factory.TickUtil.TickIO();
        private PhysicalOrderConfirm confirmOrders;
        private bool isBarData = false;
        private bool createSimulatedFills = false;
        // Randomly rotate the partial fills but using a fixed
        // seed so that test results are reproducable.
        private Random random = new Random(1234);
        private long minimumTick;
        private static int maxPartialFillsPerOrder = 1;
        private volatile bool isOnline = false;
        private string name;
        private bool createActualFills;
        private TriggerController triggers;
        private Dictionary<long, long> serialTriggerMap = new Dictionary<long, long>();

        public FillSimulatorPhysical(string name, SymbolInfo symbol, bool createSimulatedFills, bool createActualFills, TriggerController triggers)
        {
            this.symbol = symbol;
            this.name = name;
            this.triggers = triggers;
            this.minimumTick = symbol.MinimumTick.ToLong();
            this.tickSync = SyncTicks.GetTickSync(symbol.BinaryIdentifier);
            this.createSimulatedFills = createSimulatedFills;
            this.log = Factory.SysLog.GetLogger(typeof(FillSimulatorPhysical).FullName + "." + symbol.Symbol.StripInvalidPathChars() + "." + name);
            this.log.Register(this);
            this.createActualFills = createActualFills;
            fillLogic = new FillSimulatorLogic(name, symbol, FillCallback);
            IsChanged = true;
            PartialFillSimulation = symbol.PartialFillSimulation;
        }

        private bool hasCurrentTick = false;
        public void OnOpen(Tick tick)
        {
            if (trace) log.Trace("OnOpen(" + tick + ")");
            isOpenTick = true;
            openTime = tick.Time;
            if (!tick.IsQuote && !tick.IsTrade)
            {
                throw new ApplicationException("tick w/o either trade or quote data? " + tick);
            }
            currentTick.Inject(tick.Extract());
            hasCurrentTick = true;
            IsChanged = true;
        }

        public Iterable<CreateOrChangeOrder> GetActiveOrders(SymbolInfo symbol)
        {
            ActiveList<CreateOrChangeOrder> activeOrders = new ActiveList<CreateOrChangeOrder>();
            activeOrders.AddLast(increaseOrders);
            activeOrders.AddLast(decreaseOrders);
            activeOrders.AddLast(marketOrders);
            return activeOrders;
        }

        public bool OnChangeBrokerOrder(CreateOrChangeOrder other)
        {
            var order = other.Clone();
            if (debug) log.Debug("OnChangeBrokerOrder( " + order + ")");
            var origOrder = CancelBrokerOrder(order.OriginalOrder.BrokerOrder);
            if (origOrder == null)
            {
                if (debug) log.Debug("PhysicalOrder too late to change. Already filled or canceled, ignoring.");
                var message = "No such order";
                if (onRejectOrder != null)
                {
                    SendReject(order, true, message);
                }
                else
                {
                    throw new ApplicationException(message + " while handling order: " + order);
                }
                return true;
            }
            if( CreateBrokerOrder(order))
            {
                if (confirmOrders != null) confirmOrders.ConfirmChange(order.BrokerOrder, true);
                UpdateCounts();
            }
            return true;
        }

        public bool TryGetOrderById(long orderId, out CreateOrChangeOrder createOrChangeOrder)
        {
            LogOpenOrders();
            lock (orderMapLocker)
            {
                return orderMap.TryGetValue(orderId, out createOrChangeOrder);
            }
        }


        public CreateOrChangeOrder GetOrderById(long orderId)
        {
            CreateOrChangeOrder order;
            lock (orderMapLocker)
            {
                if (!TryGetOrderById(orderId, out order))
                {
                    throw new ApplicationException(symbol + ": Cannot find physical order by id: " + orderId);
                }
            }
            return order;
        }


        private bool CreateBrokerOrder(CreateOrChangeOrder order)
        {
#if VERIFYSIDE
            if (!VerifySide(order))
            {
                return false;
            }
#endif
            lock (orderMapLocker)
            {
                try
                {
                    orderMap.Add(order.BrokerOrder, order);
                    if (trace) log.Trace("Added order " + order.BrokerOrder);
                }
                catch (ArgumentException)
                {
                    throw new ApplicationException("An broker order id of " + order.BrokerOrder + " was already added.");
                }
            }
            TriggerOperation operation = default(TriggerOperation);
            switch (order.Type)
            {
                case OrderType.BuyMarket:
                case OrderType.SellMarket:
                    break;
                case OrderType.BuyStop:
                case OrderType.SellLimit:
                    operation = TriggerOperation.GreaterOrEqual;
                    break;
                case OrderType.BuyLimit:
                case OrderType.SellStop:
                    operation = TriggerOperation.LessOrEqual;
                    break;
                case OrderType.StopLoss:
                default:
                    throw new ArgumentOutOfRangeException();
            }
            if (triggers != null)
            {
                var triggerId = triggers.AddTrigger(order.LogicalSerialNumber, TriggerData.Price, operation, order.Price, TriggerCallback);
                serialTriggerMap[order.LogicalSerialNumber] = triggerId;
            }

            SortAdjust(order);
            IsChanged = true;
            OrderChanged();
            return true;
        }

        private void TriggerCallback(long logicalSerialNumber)
        {
            IsChanged = false;
            ClearOrderChanged();
            if (hasCurrentTick)
            {
                ProcessOrdersInternal(currentTick);
            }
            else
            {
                if (debug) log.Debug("Skipping TriggerCallback because HasCurrentTick is " + hasCurrentTick);
            }
        }

        private CreateOrChangeOrder CancelBrokerOrder(long oldOrderId)
        {
            CreateOrChangeOrder createOrChangeOrder;
            if (TryGetOrderById(oldOrderId, out createOrChangeOrder))
            {
                var node = (ActiveListNode<CreateOrChangeOrder>)createOrChangeOrder.Reference;
                if (node.List != null)
                {
                    node.List.Remove(node);
                }
                nodePool.Free(node);
                lock (orderMapLocker)
                {
                    orderMap.Remove(oldOrderId);
                }
                if (triggers != null)
                {
                    var triggerId = serialTriggerMap[createOrChangeOrder.LogicalSerialNumber];
                    serialTriggerMap.Remove(createOrChangeOrder.LogicalSerialNumber);
                    triggers.RemoveTrigger(triggerId);
                }
                LogOpenOrders();
            }
            return createOrChangeOrder;
        }

        public bool HasBrokerOrder(CreateOrChangeOrder order)
        {
            var list = increaseOrders;
            switch (order.Type)
            {
                case OrderType.BuyLimit:
                case OrderType.SellStop:
                    list = decreaseOrders;
                    break;
                case OrderType.SellLimit:
                case OrderType.BuyStop:
                    list = increaseOrders;
                    break;
                case OrderType.BuyMarket:
                case OrderType.SellMarket:
                    list = marketOrders;
                    break;
                default:
                    throw new ApplicationException("Unexpected order type: " + order.Type);
            }
            for (var current = list.First; current != null; current = current.Next)
            {
                var queueOrder = current.Value;
                if (order.LogicalSerialNumber == queueOrder.LogicalSerialNumber)
                {
                    if (debug) log.Debug("Create ignored because order was already on active order queue.");
                    return true;
                }
            }
            return false;
        }

        public bool OnCreateBrokerOrder(CreateOrChangeOrder other)
        {
            var order = other.Clone();
            if (debug) log.Debug("OnCreateBrokerOrder( " + order + ")");
            if (order.Size <= 0)
            {
                throw new ApplicationException("Sorry, Size of order must be greater than zero: " + order);
            }
            if( CreateBrokerOrder(order))
            {
                if (confirmOrders != null) confirmOrders.ConfirmCreate(order.BrokerOrder, true);
                UpdateCounts();
            }
            return true;
        }

        public bool OnCancelBrokerOrder(CreateOrChangeOrder order)
        {
            if (debug) log.Debug("OnCancelBrokerOrder( " + order.OriginalOrder.BrokerOrder + ")");
            var origOrder = CancelBrokerOrder(order.OriginalOrder.BrokerOrder);
            if (origOrder == null)
            {
                if (debug) log.Debug("PhysicalOrder too late to change. Already filled or canceled, ignoring.");
                var message = "No such order";
                if (onRejectOrder != null)
                {
                    SendReject(order, true, message);
                }
                else
                {
                    throw new ApplicationException(message + " while handling order: " + order);
                }
                return true;
            }
            origOrder.ReplacedBy = order;
            if (confirmOrders != null) confirmOrders.ConfirmCancel(order.BrokerOrder, true);
            UpdateCounts();
            return true;
        }

        public int ProcessOrders()
        {
            IsChanged = false;
            ClearOrderChanged();
            if (hasCurrentTick)
            {
                ProcessOrdersInternal(currentTick);
            }
            else
            {
                if (debug) log.Debug("Skipping ProcessOrders because HasCurrentTick is " + hasCurrentTick);
            }
            return 1;
        }

        public int ProcessAdjustments()
        {
            if (hasCurrentTick)
            {
                ProcessAdjustmentsInternal(currentTick);
            }
            return 1;
        }

        public void StartTick(Tick lastTick)
        {
            if (trace) log.Trace("StartTick(" + lastTick + ")");
            if (!lastTick.IsQuote && !lastTick.IsTrade)
            {
                throw new ApplicationException("tick w/o either trade or quote data? " + lastTick);
            }
            currentTick.Inject(lastTick.Extract());
            hasCurrentTick = true;
            IsChanged = true;
        }

        public void LogActiveOrders()
        {
            var orders = GetActiveOrders(symbol);
            for (var current = orders.First; current != null; current = current.Next)
            {
                var order = current.Value;
                if (debug) log.Debug(order.ToString());
            }
        }
        private void ProcessAdjustmentsInternal(Tick tick)
        {
            if (verbose) log.Verbose("ProcessAdjustments( " + symbol + ", " + tick + " )");
            if (symbol == null)
            {
                throw new ApplicationException("Please set the Symbol property for the " + GetType().Name + ".");
            }
            for (var node = marketOrders.First; node != null; node = node.Next)
            {
                var order = node.Value;
                if (order.LogicalOrderId == 0)
                {
                    OnProcessOrder(order, tick);
                }
            }
            if (onPhysicalFill == null)
            {
                throw new ApplicationException("Please set the OnPhysicalFill property.");
            }
            else
            {
                FlushFillQueue();
            }
        }

        private void OnProcessOrder(CreateOrChangeOrder order, Tick tick)
        {
            if (tick.UtcTime < order.UtcCreateTime)
            {
                //if (trace) log.Trace
                log.Info("Skipping check of " + order.Type + " on tick UTC time " + tick.UtcTime + "." + order.UtcCreateTime.Microsecond + " because earlier than order create UTC time " + order.UtcCreateTime + "." + order.UtcCreateTime.Microsecond);
                return;
            }
            if( tick.UtcTime > order.LastReadTime )
            {
                order.LastReadTime = tick.UtcTime;
                fillLogic.TryFillOrder(order, tick);
            }
        }

        private void ProcessOrdersInternal(Tick tick)
        {
            if (isOpenTick && tick.Time > openTime)
            {
                if (trace)
                {
                    log.Trace("ProcessOrders( " + symbol + ", " + tick + " ) [OpenTick]");
                }
                isOpenTick = false;
            }
            else if (trace)
            {
                log.Trace("ProcessOrders( " + symbol + ", " + tick + " )");
            }
            if (symbol == null)
            {
                throw new ApplicationException("Please set the Symbol property for the " + GetType().Name + ".");
            }
            if (trace) log.Trace("Orders: Market " + marketOrders.Count + ", Increase " + increaseOrders.Count + ", Decrease " + decreaseOrders.Count);
            if (marketOrderCount > 0)
            {
                for (var node = marketOrders.First; node != null; node = node.Next)
                {
                    var order = node.Value;
                    OnProcessOrder(order, tick);
                }
            }
            if (increaseOrderCount > 0)
            {
                for (var node = increaseOrders.First; node != null; node = node.Next)
                {
                    var order = node.Value;
                    OnProcessOrder(order, tick);
                }
            }
            if (decreaseOrderCount > 0)
            {
                for (var node = decreaseOrders.First; node != null; node = node.Next)
                {
                    var order = node.Value;
                    OnProcessOrder(order, tick);
                }
            }
            if (onPhysicalFill == null)
            {
                throw new ApplicationException("Please set the OnPhysicalFill property.");
            }
            else
            {
                FlushFillQueue();
            }
        }

        public void FlushFillQueue()
        {
            if (!isOnline)
            {
                if (verbose) log.Verbose("Unable to flush fill queue yet because isOnline is " + isOnline);
                return;
            }
            while (fillQueue.Count > 0)
            {
                var wrapper = fillQueue.Dequeue();
                if (debug) log.Debug("Dequeuing fill ( isOnline " + isOnline + "): " + wrapper.Fill);
                if (enableSyncTicks && !wrapper.IsCounterSet) tickSync.AddPhysicalFill(wrapper.Fill);
                onPhysicalFill(wrapper.Fill, wrapper.Order);
            }
            while (rejectQueue.Count > 0)
            {
                var wrapper = rejectQueue.Dequeue();
                if (debug) log.Debug("Dequeuing reject " + wrapper.Order);
                onRejectOrder(wrapper.Order, wrapper.RemoveOriginal, wrapper.Message);
            }
        }

        private void LogOpenOrders()
        {
            if (trace)
            {
                log.Trace("Found " + orderMap.Count + " open orders for " + symbol + ":");
                lock (orderMapLocker)
                {
                    foreach (var kvp in orderMap)
                    {
                        var order = kvp.Value;
                        log.Trace(order.ToString());
                    }
                }
            }
        }

        private int decreaseOrderCount;
        private int increaseOrderCount;
        private int marketOrderCount;

        private void UpdateCounts()
        {
            decreaseOrderCount = decreaseOrders.Count;
            increaseOrderCount = increaseOrders.Count;
            marketOrderCount = marketOrders.Count;
        }

        public int OrderCount
        {
            get { return decreaseOrderCount + increaseOrderCount + marketOrderCount; }
        }

        private void SortAdjust(CreateOrChangeOrder order)
        {
            switch (order.Type)
            {
                case OrderType.BuyLimit:
                case OrderType.SellStop:
                    SortAdjust(decreaseOrders, order, (x, y) => y.Price - x.Price);
                    break;
                case OrderType.SellLimit:
                case OrderType.BuyStop:
                    SortAdjust(increaseOrders, order, (x, y) => x.Price - y.Price);
                    break;
                case OrderType.BuyMarket:
                case OrderType.SellMarket:
                    Adjust(marketOrders, order);
                    break;
                default:
                    throw new ApplicationException("Unexpected order type: " + order.Type);
            }
        }

        private void AssureNode(CreateOrChangeOrder order)
        {
            if (order.Reference == null)
            {
                order.Reference = nodePool.Create(order);
            }
        }

        private void Adjust(ActiveList<CreateOrChangeOrder> list, CreateOrChangeOrder order)
        {
            AssureNode(order);
            var addedOne = false;
            var node = (ActiveListNode<CreateOrChangeOrder>)order.Reference;
            if (node.List == null)
            {
                list.AddLast(node);
            }
            else if (!node.List.Equals(list))
            {
                node.List.Remove(node);
                list.AddLast(node);
            }
        }

        private void SortAdjust(ActiveList<CreateOrChangeOrder> list, CreateOrChangeOrder order, Func<CreateOrChangeOrder, CreateOrChangeOrder, double> compare)
        {
            AssureNode(order);
            var orderNode = (ActiveListNode<CreateOrChangeOrder>)order.Reference;
            if (orderNode.List == null || !orderNode.List.Equals(list))
            {
                if (orderNode.List != null)
                {
                    orderNode.List.Remove(orderNode);
                }
                bool found = false;
                var next = list.First;
                for (var node = next; node != null; node = next)
                {
                    next = node.Next;
                    var other = node.Value;
                    if (object.ReferenceEquals(order, other))
                    {
                        found = true;
                        break;
                    }
                    else
                    {
                        var result = compare(order, other);
                        if (result < 0)
                        {
                            list.AddBefore(node, orderNode);
                            found = true;
                            break;
                        }
                    }
                }
                if (!found)
                {
                    list.AddLast(orderNode);
                }
            }
        }

        private bool VerifySide(CreateOrChangeOrder order)
        {
            switch (order.Type)
            {
                case OrderType.SellMarket:
                case OrderType.SellStop:
                case OrderType.SellLimit:
                    return VerifySellSide(order);
                    break;
                case OrderType.BuyMarket:
                case OrderType.BuyStop:
                case OrderType.BuyLimit:
                    return VerifyBuySide(order);
                    break;
                default:
                    throw new ApplicationException("Unknown order type: " + order.Type);
            }
        }

        private void FillCallback(Order order, double price, Tick tick)
        {
            var physicalOrder = (CreateOrChangeOrder)order;
            int size = 0;
            switch (order.Type)
            {
                case OrderType.BuyMarket:
                case OrderType.BuyStop:
                case OrderType.BuyLimit:
                    size = physicalOrder.Size;
                    break;
                case OrderType.SellMarket:
                case OrderType.SellStop:
                case OrderType.SellLimit:
                    size = -physicalOrder.Size;
                    break;
                default:
                    throw new ApplicationException("Unknown order type: " + order.Type);
            }
            CreatePhysicalFillHelper(size, price, tick.Time, tick.UtcTime, physicalOrder);
        }

        private void OrderSideWrongReject(CreateOrChangeOrder order)
        {
            var message = "Sorry, improper setting of a " + order.Side + " order when position is " + actualPosition;
            lock (orderMapLocker)
            {
                orderMap.Remove(order.BrokerOrder);
            }
            if (onRejectOrder != null)
            {
                if (debug) log.Debug("Rejecting order because position is " + actualPosition + " but order side was " + order.Side + ": " + order);
                SendReject(order, true, message);
            }
            else
            {
                throw new ApplicationException(message + " while handling order: " + order);
            }
        }

        private void SendReject(CreateOrChangeOrder order, bool removeOriginal, string  message)
        {
            var wrapper = new RejectWrapper
                              {
                                  Order = order,
                                  RemoveOriginal = removeOriginal,
                                  Message = message
                              };
            rejectQueue.Enqueue(wrapper);
        }

        private bool VerifySellSide(CreateOrChangeOrder order)
        {
            var result = true;
            if (actualPosition > 0)
            {
                if (order.Side != OrderSide.Sell)
                {
                    OrderSideWrongReject(order);
                    result = false;
                }
            }
            else
            {
                if (order.Side != OrderSide.SellShort)
                {
                    OrderSideWrongReject(order);
                    result = false;
                }
            }
            return result;
        }

        private bool VerifyBuySide(CreateOrChangeOrder order)
        {
            var result = true;
            if (order.Side != OrderSide.Buy)
            {
                OrderSideWrongReject(order);
                result = false;
            }
            return result;
        }

        private void CreatePhysicalFillHelper(int totalSize, double price, TimeStamp time, TimeStamp utcTime, CreateOrChangeOrder order)
        {
            if (debug) log.Debug("Filling order: " + order);
            var remainingSize = order.Size;
            var split = 1;
            var numberFills = split;
            switch (PartialFillSimulation)
            {
                case PartialFillSimulation.None:
                    break;
                case PartialFillSimulation.PartialFillsTillComplete:
                    numberFills = split = random.Next(maxPartialFillsPerOrder) + 1;
                    break;
                case PartialFillSimulation.PartialFillsIncomplete:
                    if (order.Type == OrderType.BuyLimit || order.Type == OrderType.SellLimit)
                    {
                        split = 5;
                        numberFills = 3;
                        if (debug) log.Debug("True Partial of only " + numberFills + " fills out of " + split + " for " + order);
                    }
                    else
                    {
                        numberFills = split = random.Next(maxPartialFillsPerOrder) + 1;
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException("Unrecognized partial fill simulation: " + PartialFillSimulation);
            }
            var lastSize = totalSize / split;
            var cumulativeQuantity = 0;
            if (lastSize == 0) lastSize = totalSize;
            var count = 0;
            while (remainingSize > 0 && count < numberFills)
            {
                count++;
                remainingSize -= Math.Abs(lastSize);
                if (count >= split)
                {
                    lastSize += Math.Sign(lastSize) * remainingSize;
                    remainingSize = 0;
                }
                cumulativeQuantity += lastSize;
                if (remainingSize == 0)
                {
                    CancelBrokerOrder(order.BrokerOrder);
                }
                order.Size = remainingSize;
                CreateSingleFill(lastSize, totalSize, cumulativeQuantity, remainingSize, price, time, utcTime, order);
            }
        }

        private void CreateSingleFill(int size, int totalSize, int cumulativeSize, int remainingSize, double price, TimeStamp time, TimeStamp utcTime, CreateOrChangeOrder order)
        {
            if (debug) log.Debug("Changing actual position from " + this.actualPosition + " to " + (actualPosition + size) + ". Fill size is " + size);
            this.actualPosition += size;
            //if( onPositionChange != null) {
            //    onPositionChange( actualPosition);
            //}
            var fill = new PhysicalFillDefault(size, price, time, utcTime, order.BrokerOrder, createSimulatedFills, totalSize, cumulativeSize, remainingSize, false, createActualFills);
            if (debug) log.Debug("Enqueuing fill (online: " + isOnline + "): " + fill);
            var wrapper = new FillWrapper
                              {
                                  IsCounterSet = isOnline,
                                  Fill = fill,
                                  Order = order,
                              };
            if (enableSyncTicks && wrapper.IsCounterSet) tickSync.AddPhysicalFill(fill);
            fillQueue.Enqueue(wrapper);
        }

        public bool UseSyntheticLimits
        {
            get { return useSyntheticLimits; }
            set { useSyntheticLimits = value; }
        }

        public bool UseSyntheticStops
        {
            get { return useSyntheticStops; }
            set { useSyntheticStops = value; }
        }

        public bool UseSyntheticMarkets
        {
            get { return useSyntheticMarkets; }
            set { useSyntheticMarkets = value; }
        }

        public Action<PhysicalFill,CreateOrChangeOrder> OnPhysicalFill
        {
            get { return onPhysicalFill; }
            set { onPhysicalFill = value; }
        }

        public int GetActualPosition(SymbolInfo symbol)
        {
            return actualPosition;
        }

        public int ActualPosition
        {
            get { return actualPosition; }
            set
            {
                if (actualPosition != value)
                {
                    if (debug) log.Debug("Setter: ActualPosition changed from " + actualPosition + " to " + value);
                    actualPosition = value;
                    if (onPositionChange != null)
                    {
                        onPositionChange(actualPosition);
                    }
                }
            }
        }

        public Action<long> OnPositionChange
        {
            get { return onPositionChange; }
            set { onPositionChange = value; }
        }

        public PhysicalOrderConfirm ConfirmOrders
        {
            get { return confirmOrders; }
            set
            {
                confirmOrders = value;
                if (confirmOrders == this)
                {
                    throw new ApplicationException("Please set ConfirmOrders to an object other than itself to avoid circular loops.");
                }
            }
        }

        public bool IsBarData
        {
            get { return isBarData; }
            set { isBarData = value; }
        }

        public Action<CreateOrChangeOrder, bool, string> OnRejectOrder
        {
            get { return onRejectOrder; }
            set { onRejectOrder = value; }
        }

        public TimeStamp CurrentTick
        {
            get { return currentTick.UtcTime; }
        }

        public static int MaxPartialFillsPerOrder
        {
            get { return maxPartialFillsPerOrder; }
            set { maxPartialFillsPerOrder = value; }
        }

        public bool IsOnline
        {
            get { return isOnline; }
            set
            {
                if (isOnline != value)
                {
                    isOnline = value;
                    if (debug) log.Debug("IsOnline changed to " + isOnline);
                    if (!createSimulatedFills)
                    {
                        if (debug) log.Debug("Switching PhysicalFillSimulator tick sync counter.");
                        if (isOnline)
                        {
                            tickSync.AddPhysicalFillSimulator(name);
                        }
                        else
                        {
                            tickSync.RemovePhysicalFillSimulator(name);
                        }
                    }
                    else if (debug)
                    {
                        log.Debug("createSimulatedFills " + createSimulatedFills);
                    }
                }
            }
        }

        public PartialFillSimulation PartialFillSimulation
        {
            get { return partialFillSimulation; }
            set { partialFillSimulation = value; }
        }

        public bool EnableSyncTicks
        {
            get { return enableSyncTicks; }
            set { enableSyncTicks = value; }
        }

        private void OrderChanged()
        {
            if (enableSyncTicks && !tickSync.SentOrderChange)
            {
                tickSync.AddOrderChange();
            }
        }

        private void ClearOrderChanged()
        {
            if (enableSyncTicks && tickSync.SentOrderChange)
            {
                tickSync.RemoveOrderChange();
            }
        }

        public bool IsChanged
        {
            get { return isChanged; }
            set
            {
                if (isChanged != value)
                {
                    //if (debug) log.Debug("IsChanged from " + isChanged + " to " + value);
                    isChanged = value;
                }
            }
        }
    }
}

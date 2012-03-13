using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using TickZoom.Api;

namespace TickZoom.Common
{
    public class PhysicalOrderCacheDefault : PhysicalOrderCache, LogAware
    {
        private Log log;
        private volatile bool info;
        private volatile bool trace;
        private volatile bool debug;
        public virtual void RefreshLogLevel()
        {
            if (log != null)
            {
                info = log.IsInfoEnabled;
                debug = log.IsDebugEnabled;
                trace = log.IsTraceEnabled;
            }
        }

        protected Dictionary<int, CreateOrChangeOrder> ordersBySequence = new Dictionary<int, CreateOrChangeOrder>();
        protected Dictionary<long, CreateOrChangeOrder> ordersByBrokerId = new Dictionary<long, CreateOrChangeOrder>();
        protected Dictionary<long, CreateOrChangeOrder> ordersBySerial = new Dictionary<long, CreateOrChangeOrder>();
        protected Dictionary<long, SymbolPosition> positions = new Dictionary<long, SymbolPosition>();
        protected Dictionary<int, StrategyPosition> strategyPositions = new Dictionary<int, StrategyPosition>();

        protected class SymbolPosition
        {
            public long Position;
            public override string ToString()
            {
                return Position.ToString();
            }
        }

        public PhysicalOrderCacheDefault()
        {
            log = Factory.SysLog.GetLogger(typeof(PhysicalOrderCacheDefault));
            log.Register(this);
        }

        public virtual void AssertAtomic() { }

        public IEnumerable<CreateOrChangeOrder> GetActiveOrders(SymbolInfo symbol)
        {
            AssertAtomic();
            var list = GetOrders((o) => o.Symbol == symbol);
            foreach (var order in list)
            {
                if (order.OrderState != OrderState.Filled)
                {
                    yield return order;
                }
            }
        }

        public void SyncPositions(Iterable<StrategyPosition> strategyPositions)
        {
            if( strategyPositions == null) return;
            if (trace)
            {
                for (var node = strategyPositions.First; node != null; node = node.Next)
                {
                    var sp = node.Value;
                    log.Trace("Received strategy position. " + sp);
                }
            }
            for (var current = strategyPositions.First; current != null; current = current.Next)
            {
                var position = current.Value;
                StrategyPosition strategyPosition;
                if (!this.strategyPositions.TryGetValue(position.Id, out strategyPosition))
                {
                    strategyPosition = new StrategyPositionDefault(position.Id, position.Symbol);
                    this.strategyPositions.Add(position.Id, strategyPosition);
                }
                strategyPosition.TrySetPosition(position.ExpectedPosition);
            }
//            if( debug) log.Debug("SyncPositions() strategy positions:\n" + StrategyPositionsToString());
        }

        public void SetActualPosition(SymbolInfo symbol, long position)
        {
            using (positionsLocker.Using())
            {
                if (debug) log.Debug("SetActualPosition( " + symbol + " = " + position + ")");
                SymbolPosition symbolPosition;
                if (!positions.TryGetValue(symbol.BinaryIdentifier, out symbolPosition))
                {
                    symbolPosition = new SymbolPosition { Position = position };
                    positions.Add(symbol.BinaryIdentifier, symbolPosition);
                }
                else
                {
                    positions[symbol.BinaryIdentifier].Position = position;
                }
            }
        }

        public long GetActualPosition(SymbolInfo symbol)
        {
            using (positionsLocker.Using())
            {
                SymbolPosition symbolPosition;
                if (positions.TryGetValue(symbol.BinaryIdentifier, out symbolPosition))
                {
                    return symbolPosition.Position;
                }
                else
                {
                    return 0L;
                }
            }
        }

        public void SetStrategyPosition(SymbolInfo symbol, int strategyId, long position)
        {
            using (positionsLocker.Using())
            {
                StrategyPosition strategyPosition;
                if (!this.strategyPositions.TryGetValue(strategyId, out strategyPosition))
                {
                    strategyPosition = new StrategyPositionDefault(strategyId, symbol);
                    this.strategyPositions.Add(strategyId, strategyPosition);
                }
                strategyPosition.SetExpectedPosition(position);
            }
//            if (debug) log.Debug("SetStrategyPosition() strategy positions:\n" + StrategyPositionsToString());
        }

        public long GetStrategyPosition(int strategyId)
        {
            using (positionsLocker.Using())
            {
                StrategyPosition strategyPosition;
                if (strategyPositions.TryGetValue(strategyId, out strategyPosition))
                {
                    return strategyPosition.ExpectedPosition;
                }
                else
                {
                    return 0L;
                }
            }
        }

        public long IncreaseActualPosition(SymbolInfo symbol, long increase)
        {
            using (positionsLocker.Using())
            {
                SymbolPosition symbolPosition;
                if (!positions.TryGetValue(symbol.BinaryIdentifier, out symbolPosition))
                {
                    symbolPosition = new SymbolPosition { Position = increase };
                    positions.Add(symbol.BinaryIdentifier, symbolPosition);
                }
                else
                {
                    symbolPosition.Position += increase;
                }
                return symbolPosition.Position;
            }
        }

        public bool TryGetOrderById(long brokerOrder, out CreateOrChangeOrder order)
        {
            AssertAtomic();
            if (brokerOrder == null)
            {
                order = null;
                return false;
            }
            return ordersByBrokerId.TryGetValue(brokerOrder, out order);
        }

        public bool TryGetOrderBySequence(int sequence, out CreateOrChangeOrder order)
        {
            AssertAtomic();
            if (sequence == 0)
            {
                order = null;
                return false;
            }
            return ordersBySequence.TryGetValue(sequence, out order);
        }

        public CreateOrChangeOrder GetOrderById(long brokerOrder)
        {
            AssertAtomic();
            CreateOrChangeOrder order;
            if (!ordersByBrokerId.TryGetValue(brokerOrder, out order))
            {
                throw new ApplicationException("Unable to find order for id: " + brokerOrder);
            }
            return order;
        }

        public void PurgeOriginalOrder(CreateOrChangeOrder order)
        {
            if( order.OriginalOrder == null) return;
            var clientOrderId = order.OriginalOrder.BrokerOrder;
            if (trace) log.Trace("PurgeOriginalOrder( " + clientOrderId + ")");
            AssertAtomic();
            RemoveOrderInternal(order.OriginalOrder.BrokerOrder);
            order.OriginalOrder = null;
        }

        public CreateOrChangeOrder RemoveOrder(long clientOrderId)
        {
            if (trace) log.Trace("RemoveOrder( " + clientOrderId + ")");
            AssertAtomic();
            var topOrder = RemoveOrderInternal(clientOrderId);
            if( topOrder != null && topOrder.OriginalOrder != null)
            {
                topOrder.OriginalOrder.ReplacedBy = null;
            }
            return topOrder;
        }

        private CreateOrChangeOrder RemoveOrderInternal(long clientOrderId)
        {
            if (clientOrderId == 0)
            {
                return null;
            }
            CreateOrChangeOrder order = null;
            if (ordersByBrokerId.TryGetValue(clientOrderId, out order))
            {
                var result = ordersByBrokerId.Remove(clientOrderId);
                if (result && trace) log.Trace("Removed order by broker id " + clientOrderId + ": " + order);
                CreateOrChangeOrder orderBySerial;
                if (ordersBySerial.TryGetValue(order.LogicalSerialNumber, out orderBySerial))
                {
                    if (orderBySerial.BrokerOrder.Equals(clientOrderId))
                    {
                        var result2 = ordersBySerial.Remove(order.LogicalSerialNumber);
                        if (result2 && trace) log.Trace("Removed order by logical serial " + order.LogicalSerialNumber + ": " + orderBySerial);
                    }
                }
                ordersBySequence.Remove(order.Sequence);
                return order;
            }
            return null;
        }

        public bool TryGetOrderBySerial(long logicalSerialNumber, out CreateOrChangeOrder order)
        {
            AssertAtomic();
            return ordersBySerial.TryGetValue(logicalSerialNumber, out order);
        }

        public CreateOrChangeOrder GetOrderBySerial(long logicalSerialNumber)
        {
            AssertAtomic();
            CreateOrChangeOrder order;
            if (!ordersBySerial.TryGetValue(logicalSerialNumber, out order))
            {
                throw new ApplicationException("Unable to find order by serial for id: " + logicalSerialNumber);
            }
            return order;
        }

        public bool HasCreateOrder(CreateOrChangeOrder order)
        {
            foreach (var queueOrder in GetActiveOrders(order.Symbol))
            {
                if (queueOrder.Action == OrderAction.Create && order.LogicalSerialNumber == queueOrder.LogicalSerialNumber)
                {
                    if (debug) log.Debug("Create ignored because order was already on create order queue: " + queueOrder);
                    return true;
                }
            }
            return false;
        }

        public bool HasCancelOrder(PhysicalOrder order)
        {
            foreach (var clientId in GetActiveOrders(order.Symbol))
            {
                if (clientId.OriginalOrder != null && order.OriginalOrder.BrokerOrder == clientId.OriginalOrder.BrokerOrder)
                {
                    if (debug) log.Debug("Cancel or Changed ignored because previous order order working for: " + order);
                    return true;
                }
            }
            return false;
        }

        public void SetOrder(CreateOrChangeOrder order)
        {
            AssertAtomic();
            if (trace) log.Trace("Assigning order " + order.BrokerOrder + " with " + order.LogicalSerialNumber);
            ordersByBrokerId[order.BrokerOrder] = order;
            if (order.Sequence != 0)
            {
                ordersBySequence[order.Sequence] = order;
            }
            if (order.LogicalSerialNumber != 0)
            {
                ordersBySerial[order.LogicalSerialNumber] = order;
                if (order.Action == OrderAction.Cancel && order.OriginalOrder == null)
                {
                    throw new ApplicationException("CancelOrder w/o any original order setting: " + order);
                }
            }
        }

        public List<CreateOrChangeOrder> GetOrdersList(Func<CreateOrChangeOrder, bool> select)
        {
            var list = new List<CreateOrChangeOrder>();
            foreach (var order in GetOrders(select))
            {
                list.Add(order);
            }
            return list;
        }

        public IEnumerable<CreateOrChangeOrder> GetOrders(Func<CreateOrChangeOrder, bool> select)
        {
            AssertAtomic();
            var list = new List<CreateOrChangeOrder>();
            foreach (var kvp in ordersByBrokerId)
            {
                var order = kvp.Value;
                if (select(order))
                {
                    yield return order;
                }
            }
        }

        public void ResetLastChange()
        {
            AssertAtomic();
            if (debug) log.Debug("Resetting last change time for all physical orders.");
            foreach (var kvp in ordersByBrokerId)
            {
                var order = kvp.Value;
                order.ResetLastChange();
            }
        }

        public string OrdersToString()
        {
            var sb = new StringBuilder();
            foreach (var kvp in ordersByBrokerId)
            {
                sb.AppendLine(kvp.Value.ToString());
            }
            return sb.ToString();
        }

        private volatile bool isDisposed = false;
        protected SimpleLock positionsLocker = new SimpleLock();

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!isDisposed)
            {
                log.Info("Dispose()");
                if (disposing)
                {
                    isDisposed = true;
                }
            }
        }

        public int Count()
        {
            return ordersByBrokerId.Count;
        }

        public string SymbolPositionsToString()
        {
            using (positionsLocker.Using())
            {
                return SymbolPositionsToStringInternal();
            }
        }

        public string StrategyPositionsToString()
        {
            using (positionsLocker.Using())
            {
                return StrategyPositionsToStringInternal();
            }
        }

        protected string SymbolPositionsToStringInternal()
        {
            var sb = new StringBuilder();
            foreach (var kvp in positions)
            {
                var symbol = Factory.Symbol.LookupSymbol(kvp.Key);
                var position = kvp.Value;
                sb.AppendLine(symbol + " " + position);
            }
            return sb.ToString();
        }

        protected string StrategyPositionsToStringInternal()
        {
            var sb = new StringBuilder();
            foreach (var kvp in strategyPositions)
            {
                var strategyPosition = kvp.Value;
                sb.AppendLine(strategyPosition.ToString());
            }
            return sb.ToString();
        }
    }
}
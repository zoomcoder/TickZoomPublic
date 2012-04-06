#region Copyright
/*
 * Software: TickZoom Trading Platform
 * Copyright 2012 M. Wayne Walter
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
using System.Linq;
using System.Text;
using System.Threading;

using TickZoom.Api;
using TickZoom.LimeFIX;
using TickZoom.Presentation;
using TickZoom.Common;

using LimeProviderUnitTests.MockTickZoom;
namespace LimeProviderUnitTests
{

    public class LimeProviderTests : AgentPerformer
    {
        Log log = Factory.SysLog.GetLogger(typeof(LimeProviderTests));
        string testName;
        LimeFIXProvider market;
        LimeFIXProvider client;
        Task task;
        SymbolInfo MSFT;
        StrategyInterface Strategy;
        ActiveList<LogicalOrder> Orders = new ActiveList<LogicalOrder>();
        OrderAlgorithm clientOrderAlgorithm;

        public LimeProviderTests(string name)
        {
            testName = name;
            Strategy = Factory.Utility.Strategy();
            Strategy.Context = new MockContext();
        }


        public void StartTests()
        {
            clientOrderAlgorithm = client.GetOrderAlgorithm(MSFT);
            client.SetOrderAlgorithm(MSFT, new MockOrderAlgorithm());
        }
        
        public void Login()
        {
            Factory.Provider.StartSockets();

            client.Agent.SendEvent(new EventItem(this, EventType.Connect));

            var startSymbol = new StartSymbolDetail(TimeStamp.MinValue);
            MSFT = Factory.Symbol.LookupSymbol("MSFT");

            client.Agent.SendEvent(new EventItem(this, MSFT, EventType.StartSymbol, startSymbol));
            //market.Agent.SendEvent(new EventItem(this, EventType.Connect));
        }

        public bool IsLoggedIn()
        {
            return client.IsRecovered;
        }

        internal void Logout()
        {
            if (client.IsRecovered)
                client.OnLogout();
            if (market.IsRecovered)
                market.OnLogout();
            DateTime timeout = DateTime.Now.AddSeconds(30);
            while (DateTime.Now < timeout && client.IsRecovered && market.IsRecovered)
                System.Threading.Thread.Sleep(100);

            client.Dispose();
            market.Dispose();
        }

        public void TestChange()
        {
            var order = CreateEntry(Strategy, MSFT, OrderType.BuyLimit, 35, 100, 100);
            SendOrders(client.Agent, MSFT, 100, 1);
        }

        public void SendOrders(Agent provider, SymbolInfo symbol, int desiredPosition, int secondsDelay)
        {
            var strategyPositions = new ActiveList<StrategyPosition>();
            provider.SendEvent(new EventItem(this, symbol, EventType.PositionChange,
                new PositionChangeDetail(symbol, desiredPosition, Orders, strategyPositions, 
                    TimeStamp.UtcNow.Internal, 1L)));
        }

        public void OrderPlaceAndCancel()
        {
            log.Info("Starting Test OrderPlaceAndCancel");
            int startCount = client.OrderStore.Count();

            var logicalOrder = Factory.Engine.LogicalOrder(MSFT);
            logicalOrder.TradeDirection = TradeDirection.Entry;
            logicalOrder.Type = OrderType.BuyLimit;
            logicalOrder.Price = 35;
            logicalOrder.Position = 100;

            var priceOrder = new CreateOrChangeOrderDefault(OrderAction.Create, MSFT, logicalOrder, OrderSide.Buy, logicalOrder.Position, logicalOrder.Price);
     
            bool ok = client.OnCreateBrokerOrder( priceOrder );

            if (!ok)
            {
                log.ErrorFormat("Test Aborted: Failed to place order {0}", priceOrder);

                return;
            }

            if (!WaitOn(() => client.OrderStore.Count() == startCount, 30, "Order not placed"))
            {
                return;
            }
            

            log.InfoFormat("Start orders {0}, current Orders {1}", startCount,
                client.OrderStore.Count() );


            var orderList = client.OrderStore.GetOrdersList(c => c.Symbol == MSFT);
            foreach (var order in orderList)
            {
                log.InfoFormat("Cancel Order {0}", order);
                if (order.OriginalOrder == null)
                    order.OriginalOrder = order;
                client.OnCancelBrokerOrder(order);
            }

            WaitOn(() => client.OrderStore.Count() > 0, 30, "Orders not canceled");

            if (client.OrderStore.Count() > 0)
            {
                log.Error("Failed to cancel all orders");
                foreach (var order in client.OrderStore.GetOrders(c => c.Symbol == MSFT))
                {
                    log.ErrorFormat("Cancel failed for Order {0}", order);
                }
            }
            else
            {
                log.Info("All orders canceled");
            }
            log.Info("Completed Test OrderPlaceAndCancel");
        }


        public void OrderPlaceChangeAndCancel( int changes )
        {
            log.Info("Starting Test OrderPlaceChangeAndCancel");
            int startCount = client.OrderStore.Count();

            LogicalOrder logicalOrder;
            CreateOrChangeOrderDefault priceOrder;
            bool ok = PlaceOrder( client, MSFT, OrderType.BuyLimit, 100, 35, out logicalOrder, out priceOrder );

            if (!ok)
            {
                log.ErrorFormat("Test Aborted: Failed to place order {0}", priceOrder);

                return;
            }

            if (!WaitOn(() => client.OrderStore.Count() == startCount, 30, "Order not placed"))
            {
                int couont = client.OrderStore.Count();
                return;
            }

            for (int i = 0; i < changes; i++)
            {
                Thread.Sleep(1000);

                log.InfoFormat("Start orders {0}, current Orders {1}", startCount,
                    client.OrderStore.Count());

                var changeOrder = new CreateOrChangeOrderDefault(OrderAction.Change, MSFT, logicalOrder,
                    OrderSide.Buy, 100, logicalOrder.Price + 1);
                changeOrder.OriginalOrder = priceOrder;
                ok = client.OnChangeBrokerOrder(changeOrder);
                if (!ok)
                {
                    log.ErrorFormat("Test Aborted: Failed to change order {0}", priceOrder);

                    return;
                }
            }
            Thread.Sleep(1000);

            var orderList = client.OrderStore.GetOrdersList(c => c.Symbol == MSFT);
            foreach (var order in orderList)
            {
                log.InfoFormat("Cancel Order {0}", order);
                order.OriginalOrder = order;
                client.OnCancelBrokerOrder(order);
            }

            WaitOn(() => client.OrderStore.Count() > 0, 30, "Orders not canceled");

            if (client.OrderStore.Count() > 0)
            {
                log.Error("Failed to cancel all orders");
                foreach (var order in client.OrderStore.GetOrders(c => c.Symbol == MSFT))
                {
                    log.ErrorFormat("Cancel failed for Order {0}", order);
                }
            }
            else
            {
                log.Info("All orders canceled");
            } 
            
            log.Info("Completed Test OrderPlaceChangeAndCancel");
        }

   
        public void OrderBuyAndFill()
        {

            log.Info("Starting Test OrderBuyAndFill");
            int startCount = client.OrderStore.Count();
           
            LogicalOrder clientLogicalOrder;
            CreateOrChangeOrderDefault clientOrder;
            bool ok = PlaceOrder(client, MSFT, OrderType.BuyLimit, 100, 35, out clientLogicalOrder, out clientOrder);

            if (!ok)
            {
                log.ErrorFormat("Test Aborted: Failed to place order {0}", clientOrder);

                return;
            }

            var state = clientOrder.OrderState;
            if (!WaitOn(() => clientOrder.OrderState == state, 5, "Order not placed"))
            {
                int couont = client.OrderStore.Count();
                return;
            }

            LogicalOrder marketLogicalOrder;
            CreateOrChangeOrderDefault marketOrder;
            ok = PlaceOrder(market, MSFT, OrderType.SellMarket, 100, 35, out marketLogicalOrder, out marketOrder);

            if (!ok)
            {
                log.ErrorFormat("Test Aborted: Failed to place order {0}", marketOrder);

                return;
            }

            if (!WaitOn(() => market.OrderStore.Count() == startCount, 30, "Order not placed"))
            {
                int couont = market.OrderStore.Count();
                return;
            }

            log.Info("Completed Test OrderBuyAndFill");
        }

        private bool PlaceOrder(LimeFIXProvider target, SymbolInfo symbol, OrderType type, 
            int size, double price, out LogicalOrder logicalOrder, out CreateOrChangeOrderDefault priceOrder)
        {
            logicalOrder = Factory.Engine.LogicalOrder(symbol);
            logicalOrder.TradeDirection = TradeDirection.Entry;
            logicalOrder.Type = type;
            logicalOrder.Price = price;
            logicalOrder.Position = size;

            priceOrder = new CreateOrChangeOrderDefault(OrderAction.Create, symbol, logicalOrder, OrderSide.Buy, logicalOrder.Position, logicalOrder.Price);

            return target.OnCreateBrokerOrder(priceOrder);
        }

        public LogicalOrder CreateChange(StrategyInterface strategy, SymbolInfo symbol, OrderType orderType, double price, int position, int strategyPosition)
        {
            return CreateOrder(strategy, symbol, TradeDirection.Change, orderType, price, position, strategyPosition);
        }

        public LogicalOrder CreateEntry(StrategyInterface strategy, SymbolInfo symbol, OrderType orderType, double price, int position, int strategyPosition)
        {
            return CreateOrder(strategy, symbol, TradeDirection.Entry, orderType, price, position, strategyPosition);
        }
        public LogicalOrder CreateExit(StrategyInterface strategy, SymbolInfo symbol, OrderType orderType, double price, int strategyPosition)
        {
            return CreateOrder(strategy, symbol, TradeDirection.Exit, orderType, price, 0, strategyPosition);
        }

        public LogicalOrder CreateOrder(StrategyInterface strategy,SymbolInfo symbol, TradeDirection tradeDirection, 
            OrderType orderType, double price, int position, int strategyPosition)
        {
            LogicalOrder debugOrder = Factory.Engine.LogicalOrder(symbol);
            LogicalOrder order = Factory.Engine.LogicalOrder(symbol, strategy);
            order.StrategyId = 1;
            order.StrategyPosition = strategyPosition;
            order.TradeDirection = tradeDirection;
            order.Type = orderType;
            order.Price = price;
            order.Position = position;
            order.Status = OrderStatus.Active;
            strategy.AddOrder(order);
            Orders.AddLast(order);
            strategy.Position.Change(strategyPosition, price, TimeStamp.UtcNow);
            return order;
        }

        bool WaitOn(Func<bool> test, int seconds, string error)
        {
            bool ok = false;
            var timeout = DateTime.Now.AddSeconds(30);
            while (test() && DateTime.Now <= timeout)
                System.Threading.Thread.Sleep(10);
            if (test())
            {
                log.Error("WaitOn timed out: " + error);
            }
            else
                ok = true;
            return ok;
        }

        private void OnException(Exception ex)
        {
            log.Error(ex.Message, ex);
        }

        public void BuyMarket()
        {
            log.Info("Buy Market Test");
        }

        #region AgentPerformer Members

        public Agent Agent
        {
            get;
            set;
        }

        public void Initialize(Task t)
        {
            Console.WriteLine("Initialize");
            task = t;
            var agentPerformaner = Factory.Parallel.SpawnPerformer(typeof(LimeFIXProvider), "ClientTest");
            client = agentPerformaner as LimeFIXProvider;

            agentPerformaner = Factory.Parallel.SpawnPerformer(typeof(LimeFIXProvider), "MarketTest");
            market = agentPerformaner as LimeFIXProvider;

            task.Start();
            Console.WriteLine("Task started");
        }

        public Yield Invoke()
        {
             EventItem eventItem;
            if (task.Filter.Receive(out eventItem))
            {
                switch (eventItem.EventType)
                {
                    case EventType.StartBroker:
                        Console.WriteLine("Start broker");
                        task.Filter.Pop();
                        return Yield.DidWork.Return;
                    case EventType.EndBroker:
                        Console.WriteLine("End broker");
                        task.Filter.Pop();
                        return Yield.DidWork.Return;
                    default:
                        Console.WriteLine("LimeProviderTests: Unknown event {0}", eventItem);
                        return Yield.DidWork.Repeat;
                }
            }
            return Yield.DidWork.Repeat;
        }

        public void Shutdown()
        {
            Console.WriteLine("Shutting down");
            log.Info("Shutdown");
            client.Agent.SendEvent(new EventItem(this, EventType.RemoteShutdown));
            client.Agent.SendEvent(new EventItem(this, EventType.Terminate));
            market.Agent.SendEvent(new EventItem(this, EventType.RemoteShutdown));
            market.Agent.SendEvent(new EventItem(this, EventType.Terminate));
            log.Info("Shutdown: messages sent");
            System.Threading.Thread.Sleep(1000);
        }

        #endregion

        public class MockOrderAlgorithm : OrderAlgorithm
        {
            #region OrderAlgorithm Members

            public bool PositionChange(PositionChangeDetail change, bool isRecovered)
            {
                return false;
            }

            public void SetDesiredPosition(int position)
            {
            }

            public void SetStrategyPositions(Iterable<StrategyPosition> strategyPositions)
            {
                throw new NotImplementedException();
            }

            public void SetLogicalOrders(Iterable<LogicalOrder> logicalOrders)
            {
            }

            public void ProcessFill(PhysicalFill fill)
            {
            }

            public void SetActualPosition(long position)
            {
            }

            public void IncreaseActualPosition(int position)
            {
            }

            public void TrySyncPosition(Iterable<StrategyPosition> strategyPositions)
            {
            }

            public bool HandleSimulatedExits
            {
                get
                {
                    throw new NotImplementedException();
                }
                set
                {
                    throw new NotImplementedException();
                }
            }

            public PhysicalOrderHandler PhysicalOrderHandler
            {
                get { throw new NotImplementedException(); }
            }

            public Action<SymbolInfo, LogicalFillBinary> OnProcessFill
            {
                get
                {
                    throw new NotImplementedException();
                }
                set
                {
                    throw new NotImplementedException();
                }
            }

            public long ActualPosition
            {
                get { return 0 ; }
            }

            public bool IsPositionSynced
            {
                get
                {
                    return false;
                }
                set
                {
                    throw new NotImplementedException();
                }
            }

            public int ProcessOrders()
            {
                throw new NotImplementedException();
            }

            public void RemovePending(CreateOrChangeOrder order, bool isRealTime)
            {
            }

            public bool CheckForPending()
            {
                return false;
            }

            public void ProcessHeartBeat()
            {
            }

            #endregion

            #region PhysicalOrderConfirm Members

            public void ConfirmActive(CreateOrChangeOrder order, bool isRecovered)
            {
            }

            public void ConfirmCreate(CreateOrChangeOrder order, bool isRecovered)
            {
            }

            public void ConfirmCancel(CreateOrChangeOrder order, bool isRecovered)
            {
            }

            public void ConfirmChange(CreateOrChangeOrder order, bool isRecovered)
            {
            }

            public void RejectOrder(CreateOrChangeOrder order, bool removeOriginal, bool isRealTime)
            {
            }

            #endregion
        }

        public class MockContext : Context
        {
            int modelId = 0;
            int logicalOrderId = 0;
            static readonly long startingLogicalSerialNumber = 1000000000;
            long logicalOrderSerialNumber = startingLogicalSerialNumber;
            public BinaryStore TradeData
            {
                get { throw new NotImplementedException(); }
            }
            public void AddOrder(LogicalOrder order)
            { throw new NotImplementedException(); }
            public int IncrementOrderId()
            {
                return Interlocked.Increment(ref logicalOrderId);
            }
            public long IncrementOrderSerialNumber(long symbolBinary)
            {
                return Interlocked.Increment(ref logicalOrderSerialNumber) + startingLogicalSerialNumber * symbolBinary;
            }
            public int IncrementModelId()
            {
                return Interlocked.Increment(ref modelId);
            }
        }
    }
}

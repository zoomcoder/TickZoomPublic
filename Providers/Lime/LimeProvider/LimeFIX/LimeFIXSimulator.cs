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
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

using TickZoom.Api;
using TickZoom.FIX;
using TickZoom.LimeQuotes;

namespace TickZoom.LimeFIX 
{
    public class LimeFIXSimulator : FIXSimulatorSupport, LogAware {
        public LimeFIXSimulator(string mode, ProjectProperties projectProperties, ProviderSimulatorSupport providerSimulator)
            : base(mode, projectProperties, providerSimulator, 6489, new MessageFactoryFix44())
        {
            
        }

#if REDO
        private static Log log = Factory.SysLog.GetLogger(typeof(LimeFIXSimulator));
        private volatile bool debug;
        private volatile bool trace;

        private Random random = new Random(1234);

        private string target;
        private string sender;

        private ServerState quoteState = ServerState.Startup;

        private Dictionary<string, int> _SymbolID = new Dictionary<string, int>();
        private SimpleLock _SymbolIDLock = new SimpleLock();

        private SimpleLock lastTicksLocker = new SimpleLock();
        private TickIO[] lastTicks = new TickIO[0];
        private CurrentTick[] currentTicks = new CurrentTick[0];
        private bool onlineNextTime = false;

        public LimeFIXSimulator(string mode, ProjectProperties properties)
            : base(mode, properties, 6489, LimeQuoteProviderSupport.QuotesSimulatorPort, new MessageFactoryFix42(), new MessageFactoryLimeQuotes())
        {
		    log.Register(this);
		}

        protected override void OnConnectFIX(Socket socket)
        {
            quoteState = ServerState.Startup;
            base.OnConnectFIX(socket);
        }

        #region Lime FIX
        //UNDONE: Lime FIX
        public override void ParseFIXMessage(Message message)
        {
            var packetFIX = (MessageFIX4_2)message;
            switch (packetFIX.MessageType)
            {
                case "AF": // Request Orders
                    FIXOrderList(packetFIX);
                    break;
                case "AN": // Request Positions
                    FIXPositionList(packetFIX);
                    break;
                case "G":
                    FIXChangeOrder(packetFIX);
                    break;
                case "D":
                    FIXCreateOrder(packetFIX);
                    break;
                case "F":
                    FIXCancelOrder(packetFIX);
                    break;
                case "0":
                    if (debug) log.Debug("Received heartbeat response.");
                    break;
                case "g":
                    FIXRequestSessionStatus(packetFIX);
                    break;
                case "5":
                    log.Info("Received logout message.");
                    SendLogout();
                    //Dispose();
                    break;
                default:
                    throw new ApplicationException("Unknown FIX message type '" + packetFIX.MessageType + "'\n" + packetFIX);
            }
        }

        //UNDONE: Lime FIX
        private void FIXOrderList(MessageFIX4_2 packet)
		{
			var mbtMsg = (FIXMessage4_2) FixFactory.Create();
			mbtMsg.SetText("END");
			mbtMsg.AddHeader("8");
            if (debug) log.Debug("Sending end of order list: " + mbtMsg);
            SendMessage(mbtMsg);
        }

        //UNDONE: Lime FIX
        private void FIXPositionList(MessageFIX4_2 packet)
		{
            var mbtMsg = (FIXMessage4_2)FixFactory.Create();
			mbtMsg.SetText("DONE");
			mbtMsg.AddHeader("AO");
            if (debug) log.Debug("Sending end of position list: " + mbtMsg);
            SendMessage(mbtMsg);
		}

        //UNDONE: Lime FIX
        private void FIXChangeOrder(MessageFIX4_2 packet)
        {
            var symbol = Factory.Symbol.LookupSymbol(packet.Symbol);
            var order = ConstructOrder(packet, packet.ClientOrderId);
            if (!IsOrderServerOnline)
            {
                log.Info(symbol + ": Rejected " + packet.ClientOrderId + ". Order server offline.");
                OnRejectOrder(order, true, symbol + ": Order Server Offline.");
                return;
            }
            CreateOrChangeOrder origOrder = null;
			if( debug) log.Debug( "FIXChangeOrder() for " + packet.Symbol + ". Client id: " + packet.ClientOrderId + ". Original client id: " + packet.OriginalClientOrderId);
			try {
				origOrder = GetOrderById( symbol, packet.OriginalClientOrderId);
			} catch( ApplicationException) {
				if( debug) log.Debug( symbol + ": Rejected " + packet.ClientOrderId + ". Cannot change order: " + packet.OriginalClientOrderId + ". Already filled or canceled.");
                OnRejectOrder(order, true, symbol + ": Cannot change order. Probably already filled or canceled.");
				return;
			}
		    order.OriginalOrder = origOrder;
			if( order.Side != origOrder.Side) {
				var message = symbol + ": Cannot change " + origOrder.Side + " to " + order.Side;
				log.Error( message);
                OnRejectOrder(order, false, message);
				return;     
			}
			if( order.Type != origOrder.Type) {
				var message = symbol + ": Cannot change " + origOrder.Type + " to " + order.Type;
				log.Error( message);
                OnRejectOrder(order, false, message);
				return;     
			}
			ChangeOrder(order);
            ProcessChangeOrder(order);
		}

        //UNDONE: Lime FIX
        private void ProcessChangeOrder(CreateOrChangeOrder order)
        {
            SendExecutionReport(order, "E", 0.0, 0, 0, 0, (int)order.Size, TimeStamp.UtcNow);
            SendPositionUpdate(order.Symbol, GetPosition(order.Symbol));
            SendExecutionReport(order, "5", 0.0, 0, 0, 0, (int)order.Size, TimeStamp.UtcNow);
            SendPositionUpdate(order.Symbol, GetPosition(order.Symbol));
        }

        //UNDONE: Lime FIX
        private void FIXRequestSessionStatus(MessageFIX4_2 packet)
        {
            if (packet.TradingSessionId != "TSSTATE")
            {
                throw new ApplicationException("Expected TSSTATE for trading session id but was: " + packet.TradingSessionId);
            }
            if (!packet.TradingSessionRequestId.Contains(sender) || !packet.TradingSessionRequestId.Contains(packet.Sequence.ToString()))
            {
                throw new ApplicationException("Expected unique trading session request id but was:" + packet.TradingSessionRequestId);
            }

            requestSessionStatus = true;
            if (onlineNextTime)
            {
                SetOrderServerOnline();
                onlineNextTime = false;
            }
            if (IsOrderServerOnline)
            {
                SendSessionStatusOnline();
            }
            else
            {
                SendSessionStatus("3");
            }
            onlineNextTime = true;
        }

        //UNDONE: Lime FIX
        private void FIXCancelOrder(MessageFIX4_2 packet)
        {
            var symbol = Factory.Symbol.LookupSymbol(packet.Symbol);
            if (!IsOrderServerOnline)
            {
                if (debug) log.Debug(symbol + ": Cannot cancel order by client id: " + packet.OriginalClientOrderId + ". Order Server Offline.");
                OnRejectCancel(packet.Symbol, packet.ClientOrderId, packet.OriginalClientOrderId, symbol + ": Order Server Offline");
                return;
            }
            if (debug)
                log.Debug("FIXCancelOrder() for " + packet.Symbol + ". Original client id: " +
                          packet.OriginalClientOrderId);
            CreateOrChangeOrder origOrder = null;
            try
            {
                origOrder = GetOrderById(symbol, packet.OriginalClientOrderId);
            }
            catch (ApplicationException)
            {
                if (debug)
                    log.Debug(symbol + ": Cannot cancel order by client id: " + packet.OriginalClientOrderId +
                              ". Probably already filled or canceled.");
                OnRejectCancel(packet.Symbol, packet.ClientOrderId, packet.OriginalClientOrderId, "No such order");
                return;
            }
            var cancelOrder = ConstructCancelOrder(packet, packet.ClientOrderId);
            cancelOrder.OriginalOrder = origOrder;
            CancelOrder(cancelOrder);
            ProcessCancelOrder(cancelOrder);
            TryProcessAdustments(cancelOrder);
            return;
        }

        //UNDONE: Lime FIX
        private void ProcessCancelOrder(CreateOrChangeOrder cancelOrder)
        {
            var origOrder = cancelOrder.OriginalOrder;
            var randomOrder = random.Next(0, 10) < 5 ? cancelOrder : origOrder;
            SendExecutionReport(randomOrder, "6", 0.0, 0, 0, 0, (int)origOrder.Size, TimeStamp.UtcNow);
            SendPositionUpdate(cancelOrder.Symbol, GetPosition(cancelOrder.Symbol));
            SendExecutionReport(randomOrder, "4", 0.0, 0, 0, 0, (int)origOrder.Size, TimeStamp.UtcNow);
            SendPositionUpdate(cancelOrder.Symbol, GetPosition(cancelOrder.Symbol));
        }

        //UNDONE: Lime FIX
        private void FIXCreateOrder(MessageFIX4_2 packet)
        {
            if (debug) log.Debug("FIXCreateOrder() for " + packet.Symbol + ". Client id: " + packet.ClientOrderId);
            var symbol = Factory.Symbol.LookupSymbol(packet.Symbol);
            var order = ConstructOrder(packet, packet.ClientOrderId);
            //++rejectOrderCount;
            //if( rejectOrderCount > 20)
            //{
            //    OnRejectOrder(order,true,"Insufficient buying power.");
            //    return;
            //}
            if (!IsOrderServerOnline)
            {
                if (debug) log.Debug(symbol + ": Rejected " + packet.ClientOrderId + ". Order server offline.");
                OnRejectOrder(order, true, symbol + ": Order Server Offline.");
                return;
            }
            if (packet.Symbol == "TestPending")
            {
                log.Info("Ignoring FIX order since symbol is " + packet.Symbol);
            }
            else
            {
                if (string.IsNullOrEmpty(packet.ClientOrderId))
                {
                    System.Diagnostics.Debugger.Break();
                }
                CreateOrder(order);
                ProcessCreateOrder(order);
                TryProcessAdustments(order);
            }
            return;
        }

        //UNDONE: Lime FIX
        private void ProcessCreateOrder(CreateOrChangeOrder order) {
            SendExecutionReport(order, "A", 0.0, 0, 0, 0, (int)order.Size, TimeStamp.UtcNow);
            SendPositionUpdate(order.Symbol, GetPosition(order.Symbol));
            if (order.Symbol.FixSimulationType == FIXSimulationType.BrokerHeldStopOrder &&
                (order.Type == OrderType.BuyStop || order.Type == OrderType.StopLoss))
            {
                SendExecutionReport(order, "A", "D", 0.0, 0, 0, 0, (int)order.Size, TimeStamp.UtcNow);
                SendPositionUpdate(order.Symbol, GetPosition(order.Symbol));
            }
            else
            {
                SendExecutionReport(order, "0", 0.0, 0, 0, 0, (int)order.Size, TimeStamp.UtcNow);
                SendPositionUpdate(order.Symbol, GetPosition(order.Symbol));
            }
        }

        //UNDONE: Lime FIX
        private CreateOrChangeOrder ConstructOrder(MessageFIX4_2 packet, string clientOrderId) {
            if (string.IsNullOrEmpty(clientOrderId)) {
                var message = "Client order id was null or empty. FIX Message is: " + packet;
                log.Error(message);
                throw new ApplicationException(message);
            }
            var symbol = Factory.Symbol.LookupSymbol(packet.Symbol);
            var side = OrderSide.Buy;
			switch( packet.Side) {
                case "1":
                    side = OrderSide.Buy;
                    break;
                case "2":
                    side = OrderSide.Sell;
                    break;
                case "5":
                    side = OrderSide.SellShort;
                    break;
            }
            var type = OrderType.BuyLimit;
			switch( packet.OrderType) {
                case "1":
					if( side == OrderSide.Buy) {
                        type = OrderType.BuyMarket;
					} else {
                        type = OrderType.SellMarket;
                    }
                    break;
                case "2":
					if( side == OrderSide.Buy) {
                        type = OrderType.BuyLimit;
					} else {
                        type = OrderType.SellLimit;
                    }
                    break;
                case "3":
					if( side == OrderSide.Buy) {
                        type = OrderType.BuyStop;
					} else {
                        type = OrderType.SellStop;
                    }
                    break;
            }
            var clientId = clientOrderId.Split(new char[] { '.' });
            var logicalId = int.Parse(clientId[0]);
            var utcCreateTime = new TimeStamp(packet.TransactionTime);
            var physicalOrder = Factory.Utility.PhysicalOrder(
                OrderAction.Create, OrderState.Active, symbol, side, type, OrderFlags.None,
                packet.Price, packet.OrderQuantity, logicalId, 0, clientOrderId, null, utcCreateTime);
            if (debug) log.Debug("Received physical Order: " + physicalOrder);
            return physicalOrder;
        }

        //UNDONE: Lime FIX
        private CreateOrChangeOrder ConstructCancelOrder(MessageFIX4_2 packet, string clientOrderId)
        {
            if (string.IsNullOrEmpty(clientOrderId))
            {
                var message = "Client order id was null or empty. FIX Message is: " + packet;
                log.Error(message);
                throw new ApplicationException(message);
            }
            var symbol = Factory.Symbol.LookupSymbol(packet.Symbol);
            var side = OrderSide.Buy;
            var type = OrderType.None;
            var clientId = clientOrderId.Split(new char[] { '.' });
            var logicalId = int.Parse(clientId[0]);
            var utcCreateTime = new TimeStamp(packet.TransactionTime);
            var physicalOrder = Factory.Utility.PhysicalOrder(
                OrderAction.Cancel, OrderState.Active, symbol, side, type, OrderFlags.None,
                0D, 0, logicalId, 0, clientOrderId, null, utcCreateTime);
            if (debug) log.Debug("Received physical Order: " + physicalOrder);
            return physicalOrder;
        }
        	

        protected override FIXTFactory1_1 CreateFIXFactory(int sequence, string target, string sender)
        {
            this.target = target;
            this.sender = sender;
            return new FIXFactory4_2(sequence, target, sender);
        }

        private void OnPhysicalFill( PhysicalFill fill) {
            if (fill.Order.Symbol.FixSimulationType == FIXSimulationType.BrokerHeldStopOrder &&
                (fill.Order.Type == OrderType.BuyStop || fill.Order.Type == OrderType.SellStop))
            {
                var orderType = fill.Order.Type == OrderType.BuyStop ? OrderType.BuyMarket : OrderType.SellMarket;
                var marketOrder = Factory.Utility.PhysicalOrder(fill.Order.Action, fill.Order.OrderState,
                                                                fill.Order.Symbol, fill.Order.Side, orderType, OrderFlags.None, 0,
                                                                fill.Order.Size, fill.Order.LogicalOrderId,
                                                                fill.Order.LogicalSerialNumber,
                                                                fill.Order.BrokerOrder, null, TimeStamp.UtcNow);
                SendExecutionReport(marketOrder, "0", 0.0, 0, 0, 0, (int)marketOrder.Size, TimeStamp.UtcNow);
            }
            if (debug) log.Debug("Converting physical fill to FIX: " + fill);
            SendPositionUpdate(fill.Order.Symbol, GetPosition(fill.Order.Symbol));
            var orderStatus = fill.CumulativeSize == fill.TotalSize ? "2" : "1";
            SendExecutionReport(fill.Order, orderStatus, "F", fill.Price, fill.TotalSize, fill.CumulativeSize, fill.Size, fill.RemainingSize, fill.UtcTime);
        }

        private void OnRejectOrder(CreateOrChangeOrder order, bool removeOriginal, string error)
        {
			var mbtMsg = (FIXMessage4_2) FixFactory.Create();
			mbtMsg.SetAccount( "33006566");
			mbtMsg.SetClientOrderId( order.BrokerOrder);
			mbtMsg.SetOrderStatus("8");
			mbtMsg.SetText(error);
            mbtMsg.SetSymbol(order.Symbol.Symbol);
            mbtMsg.SetTransactTime(TimeStamp.UtcNow);
            mbtMsg.AddHeader("8");
            if (trace) log.Trace("Sending reject order: " + mbtMsg);
            SendMessage(mbtMsg);
     }

        private void OnRejectCancel(string symbol, string clientOrderId, string origClientOrderId, string error)
        {
            var mbtMsg = (FIXMessage4_2)FixFactory.Create();
            mbtMsg.SetAccount("33006566");
            mbtMsg.SetClientOrderId(clientOrderId);
            mbtMsg.SetOriginalClientOrderId(origClientOrderId);
            mbtMsg.SetOrderStatus("8");
            mbtMsg.SetText(error);
            //mbtMsg.SetSymbol(symbol);
            mbtMsg.SetTransactTime(TimeStamp.UtcNow);
            mbtMsg.AddHeader("9");
            if (trace) log.Trace("Sending reject cancel." + mbtMsg);
            SendMessage(mbtMsg);
       }

        private void SendPositionUpdate(SymbolInfo symbol, int position)
        {
            //var mbtMsg = (FIXMessage4_2) FixFactory.Create();
            //mbtMsg.SetAccount( "33006566");
            //mbtMsg.SetSymbol( symbol.Symbol);
            //if( position <= 0) {
            //    mbtMsg.SetShortQty( position);
            //} else {
            //    mbtMsg.SetLongQty( position);
            //}
            //mbtMsg.AddHeader("AP");
            //SendMessage(mbtMsg);
            //if(trace) log.Trace("Sending position update: " + mbtMsg);
        }

        private void SendExecutionReport(CreateOrChangeOrder order, string status, double price, int orderQty, int cumQty, int lastQty, int leavesQty, TimeStamp time)
        {
            SendExecutionReport(order, status, status, price, orderQty, cumQty, lastQty, leavesQty, time);
        }

	    private void SendExecutionReport(CreateOrChangeOrder order, string status, string executionType, double price, int orderQty, int cumQty, int lastQty, int leavesQty, TimeStamp time) {
            int orderType = 0;
			switch( order.Type) {
                case OrderType.BuyMarket:
                case OrderType.SellMarket:
                    orderType = 1;
                    break;
                case OrderType.BuyLimit:
                case OrderType.SellLimit:
                    orderType = 2;
                    break;
                case OrderType.BuyStop:
                case OrderType.SellStop:
                    orderType = 3;
                    break;
            }
            int orderSide = 0;
			switch( order.Side) {
                case OrderSide.Buy:
                    orderSide = 1;
                    break;
                case OrderSide.Sell:
                    orderSide = 2;
                    break;
                case OrderSide.SellShort:
                    orderSide = 5;
                    break;
            }
            var mbtMsg = (FIXMessage4_2)FixFactory.Create();
            //UNDONE: Lime FIX Message
           mbtMsg.SetAccount("33006566");
            mbtMsg.SetDestination("MBTX");
            mbtMsg.SetOrderQuantity(orderQty);
            mbtMsg.SetLastQuantity(Math.Abs(lastQty));
			if( lastQty != 0) {
                mbtMsg.SetLastPrice(price);
            }
            mbtMsg.SetCumulativeQuantity(Math.Abs(cumQty));
            mbtMsg.SetOrderStatus(status);
            mbtMsg.SetPositionEffect("O");
            mbtMsg.SetOrderType(orderType);
            mbtMsg.SetSide(orderSide);
            mbtMsg.SetClientOrderId(order.BrokerOrder.ToString());
			if( order.OriginalOrder != null) {
                mbtMsg.SetOriginalClientOrderId(order.OriginalOrder.BrokerOrder);
            }
            mbtMsg.SetPrice(order.Price);
            mbtMsg.SetSymbol(order.Symbol.Symbol);
            mbtMsg.SetTimeInForce(0);
            mbtMsg.SetExecutionType(executionType);
            mbtMsg.SetTransactTime(time);
            mbtMsg.SetLeavesQuantity(Math.Abs(leavesQty));
            mbtMsg.AddHeader("8");
            SendMessage(mbtMsg);
            if (trace) log.Trace("Sending execution report: " + mbtMsg);
        }

 		protected override void ResendMessage(FIXTMessage1_1 textMessage)
        {
            var mbtMsg = (FIXMessage4_2) textMessage;
            if( SyncTicks.Enabled && !IsRecovered && mbtMsg.Type == "8")
            {
                switch( mbtMsg.OrderStatus )
                {
                    case "E":
                    case "6":
                    case "A":
                        var symbolInfo = Factory.Symbol.LookupSymbol(mbtMsg.Symbol);
                        var tickSync = SyncTicks.GetTickSync(symbolInfo.BinaryIdentifier);
                        tickSync.AddPhysicalOrder("resend");
                        break;
                    case "2":
                    case "1":
                        symbolInfo = Factory.Symbol.LookupSymbol(mbtMsg.Symbol);
                        tickSync = SyncTicks.GetTickSync(symbolInfo.BinaryIdentifier);
                        tickSync.AddPhysicalFill("resend");
                        break;
                }
                
            }
            ResendMessageProtected(textMessage);
        }

        protected override void RemoveTickSync(MessageFIXT1_1 textMessage)
        {
            var mbtMsg = (MessageFIX4_2)textMessage;
            if (SyncTicks.Enabled && mbtMsg.MessageType == "8")
            {
                //UNDONE: Lime FIX Message
                switch (mbtMsg.OrderStatus)
                {
                    case "E":
                    case "6":
                    case "0":
                        {
                            var symbolInfo = Factory.Symbol.LookupSymbol(mbtMsg.Symbol);
                            var tickSync = SyncTicks.GetTickSync(symbolInfo.BinaryIdentifier);
                            tickSync.RemovePhysicalOrder("offline");
                        }
                        break;
                    case "A":
                        if (mbtMsg.ExecutionType == "D")
                        {
                            // Is it a Forex order?
                            var symbolInfo = Factory.Symbol.LookupSymbol(mbtMsg.Symbol);
                            var tickSync = SyncTicks.GetTickSync(symbolInfo.BinaryIdentifier);
                            tickSync.RemovePhysicalOrder("offline");
                        }
                        break;
                    case "2":
                    case "1":
                        {
                            var symbolInfo = Factory.Symbol.LookupSymbol(mbtMsg.Symbol);
                            var tickSync = SyncTicks.GetTickSync(symbolInfo.BinaryIdentifier);
                            tickSync.RemovePhysicalFill("offline");
                        }
                        break;
                }

            }
        }

        protected override void RemoveTickSync(FIXTMessage1_1 textMessage)
        {
            var mbtMsg = (FIXMessage4_2) textMessage;
            if (SyncTicks.Enabled && mbtMsg.Type == "8")
            {
                switch (mbtMsg.OrderStatus)
                {
                    case "E":
                    case "6":
                    case "0":
                        {
                            var symbolInfo = Factory.Symbol.LookupSymbol(mbtMsg.Symbol);
                            var tickSync = SyncTicks.GetTickSync(symbolInfo.BinaryIdentifier);
                            tickSync.RemovePhysicalOrder("offline");
                        }
                        break;
                    case "A":
                        if (mbtMsg.ExecutionType == "D")
                        {
                            // Is it a Forex order?
                            var symbolInfo = Factory.Symbol.LookupSymbol(mbtMsg.Symbol);
                            var tickSync = SyncTicks.GetTickSync(symbolInfo.BinaryIdentifier);
                            tickSync.RemovePhysicalOrder("offline");
                        }
                        break;
                    case "2":
                    case "1":
                        {
                            var symbolInfo = Factory.Symbol.LookupSymbol(mbtMsg.Symbol);
                            var tickSync = SyncTicks.GetTickSync(symbolInfo.BinaryIdentifier);
                            tickSync.RemovePhysicalFill("offline");
                        }
                        break;
                }

            }
        }


        //UNDONE: Lime FIX
        private void SendLogout()
        {
            var mbtMsg = (FIXMessage4_2)FixFactory.Create();
            mbtMsg.AddHeader("5");
            SendMessage(mbtMsg);
            if (trace) log.Trace("Sending logout confirmation: " + mbtMsg);
        }

        #endregion

        protected override void Dispose(bool disposing)
		{
			if( !isDisposed) {
				if( disposing) {
					base.Dispose(disposing);
				}
			}
		}

        #region LogAware Members

        void LogAware.RefreshLogLevel()
        {
            base.RefreshLogLevel();
            debug = log.IsDebugEnabled;
            trace = log.IsTraceEnabled;
        }

        #endregion

        #region Lime Quotes
        public override void ParseQuotesMessage(Message message)
        {
            var limeMessage = (LimeQuoteMessage)message;

            switch (limeMessage.MessageType)
            {
                case LimeQuotesInterop.limeq_message_type.LOGIN_REQUEST:
                    QuotesLogin(limeMessage);
                    break;
                case LimeQuotesInterop.limeq_message_type.SUBSCRIPTION_REQUEST:
                    SymbolRequest(limeMessage);
                    break;
                default:
                    log.InfoFormat("Unknown Lime Quotes Message Type {0}", limeMessage.MessageType.ToString());
                    break;
            }

        }

        public enum TickState
        {
            Start,
            Tick,
            Finish,
        }

        private class CurrentTick
        {

            public TickState State;
            public SymbolInfo Symbol;
            public TickIO TickIO = Factory.TickUtil.TickIO();
        }

        private void ExtendLastTicks()
        {
            var length = lastTicks.Length == 0 ? 256 : lastTicks.Length * 2;
            Array.Resize(ref lastTicks, length);
            for (var i = 0; i < lastTicks.Length; i++)
            {
                if (lastTicks[i] == null)
                {
                    lastTicks[i] = Factory.TickUtil.TickIO();
                }
            }
        }
        private void ExtendCurrentTicks()
        {
            var length = currentTicks.Length == 0 ? 256 : currentTicks.Length * 2;
            Array.Resize(ref currentTicks, length);
            for (var i = 0; i < currentTicks.Length; i++)
            {
                if (currentTicks[i] == null)
                {
                    currentTicks[i] = new CurrentTick();
                }
            }
        }


        private void OnEndTick(long id)
        {
            if (nextSimulateSymbolId >= currentTicks.Length)
            {
                ExtendCurrentTicks();
            }
            var currentTick = currentTicks[id];
            currentTick.State = TickState.Finish;
            TrySendTick();
        }


        private unsafe Yield SymbolRequest(LimeQuoteMessage message)
        {
            LimeQuotesInterop.subscription_request_msg* subRequest = (LimeQuotesInterop.subscription_request_msg*)message.Ptr;
            String symbol = "";
            for (int i = 0; subRequest->syb_symbols[i] != 0; i++)
                symbol += (char)subRequest->syb_symbols[i];

            var symbolInfo = Factory.Symbol.LookupSymbol(symbol);
            log.Info("Lime: Received symbol request for " + symbolInfo);
            AddSymbol(symbolInfo.Symbol, OnTick, OnEndTick, OnPhysicalFill, OnRejectOrder);

            int symbolID = -1;
            using (_SymbolIDLock.Using())
            {
                if (!_SymbolID.ContainsKey(symbol))
                    _SymbolID.Add(symbol, _SymbolID.Count);
                symbolID = _SymbolID[symbol];
            }

            var writePacket = (LimeQuoteMessage)quoteSocket.MessageFactory.Create();
            LimeQuotesInterop.subscription_reply_msg* reply = (LimeQuotesInterop.subscription_reply_msg*)writePacket.Ptr;
            reply->msg_type = LimeQuotesInterop.limeq_message_type.SUBSCRIPTION_REPLY;
            reply->msg_len = (ushort)sizeof(LimeQuotesInterop.subscription_reply_msg);
            writePacket.Length = reply->msg_len;
            reply->outcome = LimeQuotesInterop.subscription_outcome.SUBSCRIPTION_SUCCESSFUL;
            for (int i = 0; i < 4; i++)
                reply->qsid[i] = subRequest->qsid[i];

            quotePacketQueue.Enqueue(writePacket, message.SendUtcTime);


            var bookRebuildMessage = (LimeQuoteMessage)quoteSocket.MessageFactory.Create();
            LimeQuotesInterop.book_rebuild_msg* book = (LimeQuotesInterop.book_rebuild_msg*)bookRebuildMessage.Ptr;
            book->msg_type = LimeQuotesInterop.limeq_message_type.BOOK_REBUILD;
            book->msg_len = (ushort)sizeof(LimeQuotesInterop.book_rebuild_msg);
            bookRebuildMessage.Length = book->msg_len;
            book->symbol_index = (uint)symbolID;
            for (int i = 0; i < symbol.Length; i++)
                book->symbol[i] = (byte)symbol[i];
            quotePacketQueue.Enqueue(bookRebuildMessage, message.SendUtcTime);

            return Yield.DidWork.Repeat;
        }



        TradeSide _LastSide = TradeSide.Buy;
        private unsafe void OnTick(long id, SymbolInfo anotherSymbol, Tick anotherTick)
        {
            if (trace) log.Trace("OnTick() " + anotherTick);

            if (anotherSymbol.BinaryIdentifier >= lastTicks.Length)
            {
                ExtendLastTicks();
            }

            if (nextSimulateSymbolId >= currentTicks.Length)
            {
                ExtendCurrentTicks();
            }

            var currentTick = currentTicks[id];
            currentTick.TickIO.Inject(anotherTick.Extract());
            currentTick.Symbol = anotherSymbol;
            currentTick.State = TickState.Tick;

            TrySendTick();
        }

        private unsafe void TrySendTick()
        {
            CurrentTick currentTick = null;
            for (var i = 0; i < nextSimulateSymbolId; i++)
            {
                var temp = currentTicks[i];
                switch (temp.State)
                {
                    case TickState.Start:
                        return;
                    case TickState.Tick:
                        if (currentTick == null || temp.TickIO.lUtcTime < currentTick.TickIO.lUtcTime)
                        {
                            currentTick = temp;
                        }
                        break;
                    case TickState.Finish:
                        break;
                }
            }

            if (currentTick == null) return;

            var tick = currentTick.TickIO;
            var symbol = currentTick.Symbol;

            if (trace) log.Trace("Sending tick " + symbol + " " + tick);

            var lastTick = lastTicks[symbol.BinaryIdentifier];

            lastTick.Inject(tick.Extract());

            SendSide(tick, true);
            if (tick.IsQuote)
                SendSide(tick, false);
        }

        unsafe private void SendSide(TickIO tick, bool isBid)
        {
            uint symbolID = 0;
            using (_SymbolIDLock.Using())
            {
                string tickSym = tick.Symbol;
                symbolID = (uint)_SymbolID[tickSym];
            }
 
            var message = QuoteSocket.MessageFactory.Create();
            var quoteMessage = (LimeQuoteMessage)message;
            if (tick.IsTrade)
            {
                var trade = (LimeQuotesInterop.trade_msg*)quoteMessage.Ptr;
                trade->common.msg_type = LimeQuotesInterop.limeq_message_type.TRADE;
                trade->common.msg_len = (ushort)sizeof(LimeQuotesInterop.trade_msg);
                quoteMessage.Length = trade->common.msg_len;
                trade->common.shares = (uint)tick.Size;
                trade->total_volume = (uint)tick.Volume;
                switch (tick.Side)
                {
                    case TradeSide.Buy:
                        trade->common.side = LimeQuotesInterop.quote_side.BUY;
                        break;
                    case TradeSide.Sell:
                        trade->common.side = LimeQuotesInterop.quote_side.SELL;
                        break;
                    default:
                        trade->common.side = LimeQuotesInterop.quote_side.NONE;
                        break;
                }

                Int32 mantissa;
                sbyte exponent;
                LimeQuoteMessage.DoubleToPrice(tick.Price, out mantissa, out exponent);
                trade->common.price_mantissa = mantissa;
                trade->common.price_exponent = exponent;

                trade->common.symbol_index = symbolID;

                // We steal the order_id field to send the upper 32 bits of the timaestamp
                trade->common.timestamp = (uint)tick.UtcTime.Internal;
                trade->common.order_id = (uint)(tick.UtcTime.Internal >> 32);
            }
            else if (tick.IsQuote)
            {
                var order = (LimeQuotesInterop.order_msg*)quoteMessage.Ptr;
                order->common.msg_type = LimeQuotesInterop.limeq_message_type.ORDER;
                order->common.msg_len = (ushort)sizeof(LimeQuotesInterop.order_msg);
                quoteMessage.Length = order->common.msg_len;
                order->common.shares = (uint)Math.Max(tick.Size, 1);
                Int32 mantissa;
                sbyte exponent;
                if (isBid)
                    order->common.side = LimeQuotesInterop.quote_side.BUY;
                else
                    order->common.side = LimeQuotesInterop.quote_side.SELL;

                if (order->common.side == LimeQuotesInterop.quote_side.BUY)
                {
                    LimeQuoteMessage.DoubleToPrice(tick.Bid, out mantissa, out exponent);
                    order->common.price_mantissa = mantissa;
                    order->common.price_exponent = exponent;
                    if (trace) log.TraceFormat("Sending Ask {0}", tick.Bid);
                }
                else if (order->common.side == LimeQuotesInterop.quote_side.SELL)
                {
                    LimeQuoteMessage.DoubleToPrice(tick.Ask, out mantissa, out exponent);
                    order->common.price_mantissa = mantissa;
                    order->common.price_exponent = exponent;
                    if (trace) log.TraceFormat("Sending Bid {0}", tick.Ask);
                }

                order->common.symbol_index = symbolID;
                
                // We steal the order_id field to send the upper 32 bits of the timaestamp
                order->common.timestamp = (uint)tick.UtcTime.Internal;
                order->common.order_id = (uint)(tick.UtcTime.Internal >> 32);
            }
            else
                throw new NotImplementedException("Tick is neither Trade nor Quote");

            quotePacketQueue.Enqueue(quoteMessage, tick.UtcTime.Internal);
            if (trace) log.Trace("Enqueued tick packet: " + new TimeStamp(tick.UtcTime.Internal));
        }

        private void CloseWithQuotesError(LimeQuoteMessage message, string textMessage)
        {
            log.Error(textMessage);
        }

        private unsafe void QuotesLogin(LimeQuoteMessage packetQuotes)
        {
            if (quoteState != ServerState.Startup)
            {
                CloseWithQuotesError(packetQuotes, "Invalid login request. Already logged in.");
            }
            quoteState = ServerState.LoggedIn;

            LimeQuotesInterop.login_request_msg* message = (LimeQuotesInterop.login_request_msg*)packetQuotes.Ptr;
            if (message->msg_len != 80 || message->msg_type != LimeQuotesInterop.limeq_message_type.LOGIN_REQUEST ||
                message->ver_major != LimeQuotesInterop.LIMEQ_MAJOR_VER ||
                message->ver_minor != LimeQuotesInterop.LIMEQ_MINOR_VER ||
                message->session_type != LimeQuotesInterop.app_type.CPP_API ||
                message->auth_type != LimeQuotesInterop.auth_types.CLEAR_TEXT ||
                message->heartbeat_interval != LimeQuotesInterop.heartbeat)
                log.Error("Loging message not matched");
            string userName = ""; ;
            for (int i = 0; i < LimeQuotesInterop.UNAME_LEN && message->uname[i] > 0; i++)
                userName += (char)message->uname[i];
            string password = ""; ;
            for (int i = 0; i < LimeQuotesInterop.PASSWD_LEN && message->passwd[i] > 0; i++)
                password += (char)message->passwd[i];

            var writePacket = (LimeQuoteMessage)quoteSocket.MessageFactory.Create();
            LimeQuotesInterop.login_response_msg* reseponse = (LimeQuotesInterop.login_response_msg*)writePacket.Ptr;
            reseponse->msg_type = LimeQuotesInterop.limeq_message_type.LOGIN_RESPONSE;
            reseponse->msg_len = 8;
            writePacket.Length = 8;
            reseponse->ver_minor = message->ver_minor;
            reseponse->ver_major = message->ver_major;
            reseponse->heartbeat_interval = message->heartbeat_interval;
            reseponse->timeout_interval = message->timeout_interval;
            reseponse->response_code = LimeQuotesInterop.reject_reason_code.LOGIN_SUCCEEDED;

            quotePacketQueue.Enqueue(writePacket, packetQuotes.SendUtcTime);
        }
        #endregion

       
        private void CloseWithFixError(MessageFIX4_2 packet, string textMessage)
        {
            var fixMsg = (FIXMessage4_2)FixFactory.Create();
            TimeStamp timeStamp = TimeStamp.UtcNow;
            fixMsg.SetAccount(packet.Account);
            fixMsg.SetText(textMessage);
            fixMsg.AddHeader("j");
            SendMessage(fixMsg);
        }
#endif

        protected override void ResendMessage(FIXTMessage1_1 textMessage)
        {
            throw new NotImplementedException();
        }

        protected override void RemoveTickSync(MessageFIXT1_1 textMessage)
        {
            throw new NotImplementedException();
        }

        protected override void RemoveTickSync(FIXTMessage1_1 textMessage)
        {
            throw new NotImplementedException();
        }

        #region LogAware Members

        void LogAware.RefreshLogLevel()
        {
            throw new NotImplementedException();
        }

        #endregion

        public override void OnRejectOrder(CreateOrChangeOrder order, string error)
        {
            throw new NotImplementedException();
        }

        public override void OnPhysicalFill(PhysicalFill fill, CreateOrChangeOrder order)
        {
            throw new NotImplementedException();
        }
    }
}


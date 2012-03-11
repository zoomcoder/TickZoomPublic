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
using TickZoom.MBTQuotes;

namespace TickZoom.MBTFIX
{
	public class MBTFIXSimulator : FIXSimulatorSupport, LogAware {
		private static Log log = Factory.SysLog.GetLogger(typeof(MBTFIXSimulator));
        private volatile bool debug;
        private volatile bool trace;
        public override void RefreshLogLevel()
        {
            base.RefreshLogLevel();
            debug = log.IsDebugEnabled;
            trace = log.IsTraceEnabled;
        }
        private ServerState quoteState = ServerState.Startup;
        private Random random = new Random(1234);

        public MBTFIXSimulator(string mode, ProjectProperties projectProperties)
            : base(mode, projectProperties, 6489, 6488, new MessageFactoryFix44(), new MessageFactoryMbtQuotes())
        {
		    log.Register(this);
            InitializeSnippets();
		}

        protected override void OnConnectFIX(Socket socket)
		{
			quoteState = ServerState.Startup;
			base.OnConnectFIX(socket);
		}
		
		public override void ParseFIXMessage(Message message)
		{
			var packetFIX = (MessageFIX4_4) message;
			switch( packetFIX.MessageType) {
				case "AF": // Request Orders
					FIXOrderList( packetFIX);
					break;
				case "AN": // Request Positions
					FIXPositionList( packetFIX);
					break;
				case "G":
					FIXChangeOrder( packetFIX);
					break;
				case "D":
					FIXCreateOrder( packetFIX);
					break;
				case "F":
					FIXCancelOrder( packetFIX);
					break;
				case "0":
					if( debug) log.Debug("Received heartbeat response.");
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
		
		public override void ParseQuotesMessage(Message message)
		{
			var packetQuotes = (MessageMbtQuotes) message;
			char firstChar = (char) packetQuotes.Data.GetBuffer()[packetQuotes.Data.Position];
			switch( firstChar) {
				case 'L': // Login
					QuotesLogin( packetQuotes);
					break;
				case 'S':
					SymbolRequest( packetQuotes);
					break;
			}			
		}
		
		private void FIXOrderList(MessageFIX4_4 packet)
		{
			var mbtMsg = (FIXMessage4_4) FixFactory.Create();
			mbtMsg.SetText("END");
			mbtMsg.AddHeader("8");
            if (debug) log.Debug("Sending end of order list: " + mbtMsg);
            SendMessage(mbtMsg);
        }

		private void FIXPositionList(MessageFIX4_4 packet)
		{
			var mbtMsg = (FIXMessage4_4) FixFactory.Create();
			mbtMsg.SetText("DONE");
			mbtMsg.AddHeader("AO");
            if (debug) log.Debug("Sending end of position list: " + mbtMsg);
            SendMessage(mbtMsg);
		}
		
		private void FIXChangeOrder(MessageFIX4_4 packet) {
            var symbol = Factory.Symbol.LookupSymbol(packet.Symbol);
            var order = ConstructOrder(packet, packet.ClientOrderId);
            if (!IsOrderServerOnline)
            {
                log.Info(symbol + ": Rejected " + packet.ClientOrderId + ". Order server offline.");
                OnRejectOrder(order, true, symbol + ": Order Server Offline.");
                return;
            }
            var simulator = simulators[SimulatorType.RejectSymbol];
            if (FixFactory != null && simulator.CheckFrequencyAndSymbol(symbol))
            {
                if (debug) log.Debug("Simulating create order reject of 35=" + packet.MessageType);
                OnRejectOrder(order, true, "Testing reject of change order.");
                return;
            }
            CreateOrChangeOrder origOrder = null;
			if( debug) log.Debug( "FIXChangeOrder() for " + packet.Symbol + ". Client id: " + packet.ClientOrderId + ". Original client id: " + packet.OriginalClientOrderId);
			try
			{
			    long origClientId;
                if( !long.TryParse(packet.OriginalClientOrderId, out origClientId))
                {
                    log.Error("original client order id " + packet.OriginalClientOrderId + " cannot be converted to long: " + packet);
                    origClientId = 0;
                }
				origOrder = GetOrderById( symbol, origClientId);
			} catch( ApplicationException ex) {
				if( debug) log.Debug( symbol + ": Rejected " + packet.ClientOrderId + ". Cannot change order: " + packet.OriginalClientOrderId + ". Already filled or canceled.  Message: " + ex.Message);
                OnRejectOrder(order, true, symbol + ": Cannot change order. Probably already filled or canceled.");
				return;
			}
		    order.OriginalOrder = origOrder;
#if VERIFYSIDE
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
#endif
			ChangeOrder(order);
            ProcessChangeOrder(order);
		}

        private void ProcessChangeOrder(CreateOrChangeOrder order)
        {
			SendExecutionReport( order, "E", 0.0, 0, 0, 0, (int) order.Size, TimeStamp.UtcNow);
			SendPositionUpdate( order.Symbol, GetPosition(order.Symbol));
			SendExecutionReport( order, "5", 0.0, 0, 0, 0, (int) order.Size, TimeStamp.UtcNow);
			SendPositionUpdate( order.Symbol, GetPosition(order.Symbol));
        }

	    private bool onlineNextTime = false;
        private void FIXRequestSessionStatus(MessageFIX4_4 packet)
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


        private void FIXCancelOrder(MessageFIX4_4 packet)
        {
            var symbol = Factory.Symbol.LookupSymbol(packet.Symbol);
            if( !IsOrderServerOnline)
            {
                if (debug) log.Debug(symbol + ": Cannot cancel order by client id: " + packet.OriginalClientOrderId + ". Order Server Offline.");
                OnRejectCancel(packet.Symbol, packet.ClientOrderId, packet.OriginalClientOrderId, symbol + ": Order Server Offline");
                return;
            }
            var simulator = simulators[SimulatorType.RejectSymbol];
            if (FixFactory != null && simulator.CheckFrequencyAndSymbol(symbol))
            {
                if (debug) log.Debug("Simulating cancel order reject of 35=" + packet.MessageType);
                OnRejectCancel(packet.Symbol, packet.ClientOrderId, packet.OriginalClientOrderId, "Testing reject of cancel order.");
                return;
            }
            if (debug) log.Debug("FIXCancelOrder() for " + packet.Symbol + ". Original client id: " + packet.OriginalClientOrderId);
            CreateOrChangeOrder origOrder = null;
            try
            {
                long origClientId;
                if (!long.TryParse(packet.OriginalClientOrderId, out origClientId))
                {
                    log.Error("original client order id " + packet.OriginalClientOrderId +
                              " cannot be converted to long: " + packet);
                    origClientId = 0;
                }
                origOrder = GetOrderById(symbol, origClientId);
            }
            catch (ApplicationException)
            {
                if (debug) log.Debug(symbol + ": Cannot cancel order by client id: " + packet.OriginalClientOrderId + ". Probably already filled or canceled.");
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

        private void ProcessCancelOrder(CreateOrChangeOrder cancelOrder)
        {
            var origOrder = cancelOrder.OriginalOrder;
		    var randomOrder = random.Next(0, 10) < 5 ? cancelOrder : origOrder;
            SendExecutionReport( randomOrder, "6", 0.0, 0, 0, 0, (int)origOrder.Size, TimeStamp.UtcNow);
            SendPositionUpdate(cancelOrder.Symbol, GetPosition(cancelOrder.Symbol));
            SendExecutionReport( randomOrder, "4", 0.0, 0, 0, 0, (int)origOrder.Size, TimeStamp.UtcNow);
            SendPositionUpdate(cancelOrder.Symbol, GetPosition(cancelOrder.Symbol));
		}

	    private int rejectOrderCount;
        private void FIXCreateOrder(MessageFIX4_4 packet)
        {
            if (debug) log.Debug("FIXCreateOrder() for " + packet.Symbol + ". Client id: " + packet.ClientOrderId);
            var symbol = Factory.Symbol.LookupSymbol(packet.Symbol);
            var order = ConstructOrder(packet, packet.ClientOrderId);
            if (!IsOrderServerOnline)
            {
                if (debug) log.Debug(symbol + ": Rejected " + packet.ClientOrderId + ". Order server offline.");
                OnRejectOrder(order, true, symbol + ": Order Server Offline.");
                return;
            }
            var simulator = simulators[SimulatorType.RejectSymbol];
            if (FixFactory != null && simulator.CheckFrequencyAndSymbol(symbol))
            {
                if (debug) log.Debug("Simulating create order reject of 35=" + packet.MessageType);
                OnRejectOrder(order, true, "Testing reject of create order");
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

	    private void ProcessCreateOrder(CreateOrChangeOrder order) {
			SendExecutionReport( order, "A", 0.0, 0, 0, 0, (int) order.Size, TimeStamp.UtcNow);
			SendPositionUpdate( order.Symbol, GetPosition(order.Symbol));
            if( order.Symbol.FixSimulationType == FIXSimulationType.BrokerHeldStopOrder &&
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
		
		private CreateOrChangeOrder ConstructOrder(MessageFIX4_4 packet, string clientOrderId) {
			if( string.IsNullOrEmpty(clientOrderId)) {
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
		    long clientId;
			var logicalId = 0;
            if (!long.TryParse(clientOrderId, out clientId))
            {
                log.Error("original client order id " + clientOrderId +
                          " cannot be converted to long: " + packet);
                clientId = 0;
            }
            var utcCreateTime = new TimeStamp(packet.TransactionTime);
			var physicalOrder = Factory.Utility.PhysicalOrder(
				OrderAction.Create, OrderState.Active, symbol, side, type, OrderFlags.None, 
				packet.Price, packet.OrderQuantity, logicalId, 0, clientId, null, utcCreateTime);
			if( debug) log.Debug("Received physical Order: " + physicalOrder);
			return physicalOrder;
		}

        private CreateOrChangeOrder ConstructCancelOrder(MessageFIX4_4 packet, string clientOrderId)
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
            var logicalId = 0;
            long clientId;
            if (!long.TryParse(clientOrderId, out clientId))
            {
                log.Error("original client order id " + clientOrderId +
                          " cannot be converted to long: " + packet);
                clientId = 0;
            }
            var utcCreateTime = new TimeStamp(packet.TransactionTime);
            var physicalOrder = Factory.Utility.PhysicalOrder(
                OrderAction.Cancel, OrderState.Active, symbol, side, type, OrderFlags.None,
                0D, 0, logicalId, 0, clientId, null, utcCreateTime);
            if (debug) log.Debug("Received physical Order: " + physicalOrder);
            return physicalOrder;
        }

        protected override FIXTFactory1_1 CreateFIXFactory(int sequence, string target, string sender)
        {
            this.target = target;
            this.sender = sender;
            return new FIXFactory4_4(sequence, target, sender);
        }
		
		private string target;
		private string sender;
		
		private void QuotesLogin(MessageMbtQuotes message) {
			if( quoteState != ServerState.Startup) {
				CloseWithQuotesError(message, "Invalid login request. Already logged in.");
			}
			quoteState = ServerState.LoggedIn;
		    var writePacket = quoteSocket.MessageFactory.Create();
			string textMessage = "G|100=DEMOXJSP;8055=demo01\n";
			if( debug) log.Debug("Login response: " + textMessage);
			writePacket.DataOut.Write(textMessage.ToCharArray());
		    quotePacketQueue.Enqueue(writePacket, message.SendUtcTime);
		}
		
		private void OnPhysicalFill( PhysicalFill fill, CreateOrChangeOrder order) {
            if( order.Symbol.FixSimulationType == FIXSimulationType.BrokerHeldStopOrder &&
                (order.Type == OrderType.BuyStop || order.Type == OrderType.SellStop))
            {
                order.Type = order.Type == OrderType.BuyStop ? OrderType.BuyMarket : OrderType.SellMarket;
                var marketOrder = Factory.Utility.PhysicalOrder(order.Action, order.OrderState,
                                                                order.Symbol, order.Side, order.Type, OrderFlags.None, 0,
                                                                order.Size, order.LogicalOrderId,
                                                                order.LogicalSerialNumber,
                                                                order.BrokerOrder, null, TimeStamp.UtcNow);
                SendExecutionReport(marketOrder, "0", 0.0, 0, 0, 0, (int)marketOrder.Size, TimeStamp.UtcNow);
            }
			if( debug) log.Debug    ("Converting physical fill to FIX: " + fill);
			SendPositionUpdate(order.Symbol, GetPosition(order.Symbol));
			var orderStatus = fill.CumulativeSize == fill.TotalSize ? "2" : "1";
			SendExecutionReport( order, orderStatus, "F", fill.Price, fill.TotalSize, fill.CumulativeSize, fill.Size, fill.RemainingSize, fill.UtcTime);
		}

		private void OnRejectOrder( CreateOrChangeOrder order, bool removeOriginal, string error)
		{
			var mbtMsg = (FIXMessage4_4) FixFactory.Create();
			mbtMsg.SetAccount( "33006566");
			mbtMsg.SetClientOrderId( order.BrokerOrder.ToString());
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
            var mbtMsg = (FIXMessage4_4)FixFactory.Create();
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
            //var mbtMsg = (FIXMessage4_4) FixFactory.Create();
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
			var mbtMsg = (FIXMessage4_4) FixFactory.Create();
			mbtMsg.SetAccount( "33006566");
			mbtMsg.SetDestination("MBTX");
			mbtMsg.SetOrderQuantity( orderQty);
			mbtMsg.SetLastQuantity( Math.Abs(lastQty));
			if( lastQty != 0) {
				mbtMsg.SetLastPrice( price);
			}
			mbtMsg.SetCumulativeQuantity( Math.Abs(cumQty));
			mbtMsg.SetOrderStatus(status);
			mbtMsg.SetPositionEffect( "O");
			mbtMsg.SetOrderType( orderType);
			mbtMsg.SetSide( orderSide);
    		mbtMsg.SetClientOrderId( order.BrokerOrder.ToString());
			if( order.OriginalOrder != null) {
				mbtMsg.SetOriginalClientOrderId( order.OriginalOrder.BrokerOrder.ToString());
			}
			mbtMsg.SetPrice( order.Price);
			mbtMsg.SetSymbol( order.Symbol.Symbol);
			mbtMsg.SetTimeInForce( 0);
			mbtMsg.SetExecutionType( executionType);
			mbtMsg.SetTransactTime( time);
			mbtMsg.SetLeavesQuantity( Math.Abs(leavesQty));
			mbtMsg.AddHeader("8");
            SendMessage(mbtMsg);
			if(trace) log.Trace("Sending execution report: " + mbtMsg);
		}

        protected override void ResendMessage(FIXTMessage1_1 textMessage)
        {
            var mbtMsg = (FIXMessage4_4) textMessage;
            if( SyncTicks.Enabled && !IsRecovered && mbtMsg.Type == "8")
            {
                switch( mbtMsg.OrderStatus )
                {
                    case "E":
                    case "6":
                    case "A":
                        var symbolInfo = Factory.Symbol.LookupSymbol(mbtMsg.Symbol);
                        var tickSync = SyncTicks.GetTickSync(symbolInfo.BinaryIdentifier);
                        //if (symbolInfo.FixSimulationType == FIXSimulationType.BrokerHeldStopOrder &&
                        //    mbtMsg.ExecutionType == "D")  // restated  
                        //{
                        //    // Ignored order count.
                        //}
                        //else
                        //{
                        //    tickSync.AddPhysicalOrder("resend");
                        //}
                        break;
                    case "2":
                    case "1":
                        symbolInfo = Factory.Symbol.LookupSymbol(mbtMsg.Symbol);
                        tickSync = SyncTicks.GetTickSync(symbolInfo.BinaryIdentifier);
                        //tickSync.AddPhysicalFill("resend");
                        break;
                }
                
            }
            ResendMessageProtected(textMessage);
        }

        protected override void RemoveTickSync(MessageFIXT1_1 textMessage)
        {
            var mbtMsg = (MessageFIX4_4)textMessage;
            if (SyncTicks.Enabled && mbtMsg.MessageType == "8")
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
                        if( mbtMsg.ExecutionType == "D")
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
            var mbtMsg = (FIXMessage4_4) textMessage;
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

        private void SendLogout()
        {
            var mbtMsg = (FIXMessage4_4)FixFactory.Create();
            mbtMsg.AddHeader("5");
            SendMessage(mbtMsg);
            if (trace) log.Trace("Sending logout confirmation: " + mbtMsg);
        }
		
		private unsafe Yield SymbolRequest(MessageMbtQuotes message) {
			var symbolInfo = Factory.Symbol.LookupSymbol(message.Symbol);
			log.Info("Received symbol request for " + symbolInfo);
			AddSymbol(symbolInfo.Symbol, OnTick, OnEndTick, OnPhysicalFill, OnRejectOrder);
			switch( message.FeedType) {
				case "20000": // Level 1
					if( symbolInfo.QuoteType != QuoteType.Level1) {
						throw new ApplicationException("Requested data feed of Level1 but Symbol.QuoteType is " + symbolInfo.QuoteType);
					}
					break;
				case "20001": // Level 2
					if( symbolInfo.QuoteType != QuoteType.Level2) {
						throw new ApplicationException("Requested data feed of Level2 but Symbol.QuoteType is " + symbolInfo.QuoteType);
					}
					break;
				case "20002": // Level 1 & Level 2
					if( symbolInfo.QuoteType != QuoteType.Level2) {
						throw new ApplicationException("Requested data feed of Level1 and Level2 but Symbol.QuoteType is " + symbolInfo.QuoteType);
					}
					break;
				case "20003": // Trades
					if( symbolInfo.TimeAndSales != TimeAndSales.ActualTrades) {
						throw new ApplicationException("Requested data feed of Trades but Symbol.TimeAndSale is " + symbolInfo.TimeAndSales);
					}
					break;
				case "20004": // Option Chains
					break;
				default:
					throw new ApplicationException("Sorry, unknown data type: " + message.FeedType);
			}
			return Yield.DidWork.Repeat;
		}

        private SimpleLock quoteBuildersLocker = new SimpleLock();
        //private Dictionary<long, TickIO> lastTicks = new Dictionary<long, TickIO>();
        private SimpleLock lastTicksLocker = new SimpleLock();
        private TickIO[] lastTicks = new TickIO[0];
        private CurrentTick[] currentTicks = new CurrentTick[0];

	    private byte[][] tradeSnippetBytes;
	    private string[] tradeSnippets;
        private byte[][] quoteSnippetBytes;
        private string[] quoteSnippets;
        public unsafe void InitializeSnippets()
        {
            tradeSnippets = new[] {
                               "3|2026=USD;1003=",
                               // symbol
                               ";2037=0;2085=.144;2048=00/00/2009;2049=00/00/2009;2002=",
                               // price
                               ";2007=",
                               // size
                               ";2050=0;",
                               // bid
                               "2051=0;",
                               // ask
                               "2052=00/00/2010;",
                               // bid size;
                               "2053=00/00/2010;",
                               // ask size;
                               "2008=0.0;2056=0.0;2009=0.0;2057=0;2010=0.0;2058=1;2011=0.0;2012=6828928;2013=20021;2014=",                           
                               // time of day
                           };
            tradeSnippetBytes = new byte[tradeSnippets.Length][];
            for( var i=0; i<tradeSnippets.Length;i++)
            {
                tradeSnippetBytes[i] = new byte[tradeSnippets[i].Length];
                for( var pos=0; pos<tradeSnippets[i].Length; pos++)
                {
                    tradeSnippetBytes[i][pos] = (byte) tradeSnippets[i][pos];
                }
            }
            quoteSnippets = new[] {
                               "1|2026=USD;1003=",
                               // symbol
                               ";2037=0;2085=.144;2048=00/00/2009;2049=00/00/2009;2050=0;",
                               // bid, 
                               "2051=0;",
                               // ask
			                   "2052=00/00/2010;",
                               // bid size, 
                               "2053=00/00/2010;",
                               // ask size
			                   "2008=0.0;2056=0.0;2009=0.0;2057=0;2010=0.0;2058=1;2011=0.0;2012=6828928;2013=20021;2014=",
                               // time of day.
                           };
            quoteSnippetBytes = new byte[quoteSnippets.Length][];
            for (var i = 0; i < quoteSnippets.Length; i++)
            {
                quoteSnippetBytes[i] = new byte[quoteSnippets[i].Length];
                for (var pos = 0; pos < quoteSnippets[i].Length; pos++)
                {
                    quoteSnippetBytes[i][pos] = (byte)quoteSnippets[i][pos];
                }
            }
        }

        private byte[] bytebuffer = new byte[64];
        private int ConvertPriceToBytes(long value)
        {
            var pos = 0;
            var anydigit = false;
            for( var i=0; i<9 && value>0L; i++)
            {
                var digit = value%10;
                value /= 10;
                if (digit > 0L || anydigit)
                {
                    anydigit = true;
                    bytebuffer[pos] = (byte) ('0' + digit);
                    pos++;
                }
            }
            if( pos > 0)
            {
                bytebuffer[pos] = (byte) '.';
                pos++;
                if( value == 0L)
                {
                    bytebuffer[pos] = (byte)'0';
                    pos++;
                }
            }
            while( value > 0)
            {
                var digit = value%10;
                value /= 10;
                bytebuffer[pos] = (byte) ('0' + digit);
                pos++;
            }
            return pos;
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


        private void OnEndTick( long id)
        {
            if (nextSimulateSymbolId >= currentTicks.Length)
            {
                ExtendCurrentTicks();
            }
            var currentTick = currentTicks[id];
            currentTick.State = TickState.Finish;
            TrySendTick();
        }

        private unsafe void OnTick( long id, SymbolInfo anotherSymbol, Tick anotherTick)
        {
            if (trace) log.Trace("Sending tick: " + anotherTick);

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

        private void TrySendTick() {
    	    CurrentTick currentTick = null;
            for( var i=0; i< nextSimulateSymbolId; i++)
            {
                var temp = currentTicks[i];
                switch( temp.State)
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
            if( currentTick == null) return;
            currentTick.State = TickState.Start;
            var tick = currentTick.TickIO;
            var symbol = currentTick.Symbol;
            if( trace) log.Trace("TrySendTick( " + symbol + " " + tick + ")");
            var quoteMessage = QuoteSocket.MessageFactory.Create();
		    var lastTick = lastTicks[symbol.BinaryIdentifier];
            var buffer = quoteMessage.Data.GetBuffer();
            var position = quoteMessage.Data.Position;
            quoteMessage.Data.SetLength(1024);
            if( tick.IsTrade)
            {
                var index = 0;
                var snippet = tradeSnippetBytes[index];
                Array.Copy(snippet, 0, buffer, position, snippet.Length);
                position += snippet.Length;

                // Symbol
                var value = symbol.Symbol.ToCharArray();
                for( var i=0; i<value.Length; i++)
                {
                    buffer[position] = (byte) value[i];
                    ++position;
                }

                ++index; snippet = tradeSnippetBytes[index];
                Array.Copy(snippet,0,buffer,position,snippet.Length);
                position += snippet.Length;

                // Price
                var len = ConvertPriceToBytes(tick.lPrice);
                var pos = len;
                for (var i = 0; i < len; i++)
                {
                    buffer[position] = (byte) bytebuffer[--pos];
                    ++position;
                }

                ++index; snippet = tradeSnippetBytes[index];
                Array.Copy(snippet, 0, buffer, position, snippet.Length);
                position += snippet.Length;

                // Size
                value = tick.Size.ToString().ToCharArray();
                for (var i = 0; i < value.Length; i++)
                {
                    buffer[position] = (byte)value[i];
                    ++position;
                }

                ++index; snippet = tradeSnippetBytes[index];
                Array.Copy(snippet, 0, buffer, position, snippet.Length);
                position += snippet.Length;

                // Bid
                if (tick.lBid != lastTick.lBid)
                {
                    value = ("2003=" + tick.Bid + ";").ToCharArray();
                    for (var i = 0; i < value.Length; i++)
                    {
                        buffer[position] = (byte)value[i];
                        ++position;
                    }
                }

                ++index; snippet = tradeSnippetBytes[index];
                Array.Copy(snippet, 0, buffer, position, snippet.Length);
                position += snippet.Length;

                // Ask
                if (tick.lAsk != lastTick.lAsk)
                {
                    value = ("2004=" + tick.Ask + ";").ToCharArray();
                    for (var i = 0; i < value.Length; i++)
                    {
                        buffer[position] = (byte)value[i];
                        ++position;
                    }
                }

                ++index; snippet = tradeSnippetBytes[index];
                Array.Copy(snippet, 0, buffer, position, snippet.Length);
                position += snippet.Length;

                // Ask size
                var askSize = Math.Max((int)tick.AskLevel(0), 1);
                if (askSize != lastTick.AskLevel(0))
                {
                    value = ("2005=" + askSize + ";").ToCharArray();
                    for (var i = 0; i < value.Length; i++)
                    {
                        buffer[position] = (byte)value[i];
                        ++position;
                    }
                }

                ++index; snippet = tradeSnippetBytes[index];
                Array.Copy(snippet, 0, buffer, position, snippet.Length);
                position += snippet.Length;

                // Bid size
                var bidSize = Math.Max((int)tick.BidLevel(0), 1);
                if (bidSize != lastTick.BidLevel(0))
                {
                    value = ("2006=" + bidSize + ";").ToCharArray();
                    for (var i = 0; i < value.Length; i++)
                    {
                        buffer[position] = (byte)value[i];
                        ++position;
                    }
                }

                ++index; snippet = tradeSnippetBytes[index];
                Array.Copy(snippet, 0, buffer, position, snippet.Length);
                position += snippet.Length;

            }
            else
            {
                var index = 0;
                var snippet = quoteSnippetBytes[index];
                Array.Copy(snippet, 0, buffer, position, snippet.Length);
                position += snippet.Length;

                var value = symbol.Symbol.ToCharArray();
                for (var i = 0; i < value.Length; i++)
                {
                    buffer[position] = (byte)value[i];
                    ++position;
                }

                ++index; snippet = quoteSnippetBytes[index];
                Array.Copy(snippet, 0, buffer, position, snippet.Length);
                position += snippet.Length;

                if (tick.lBid != lastTick.lBid)
                {
                    value = ("2003=" + tick.Bid + ";").ToCharArray();
                    for (var i = 0; i < value.Length; i++)
                    {
                        buffer[position] = (byte)value[i];
                        ++position;
                    }
                }

                ++index; snippet = quoteSnippetBytes[index];
                Array.Copy(snippet, 0, buffer, position, snippet.Length);
                position += snippet.Length;

                if (tick.lAsk != lastTick.lAsk)
                {
                    value = ("2004=" + tick.Ask + ";").ToCharArray();
                    for (var i = 0; i < value.Length; i++)
                    {
                        buffer[position] = (byte)value[i];
                        ++position;
                    }
                }

                ++index; snippet = quoteSnippetBytes[index];
                Array.Copy(snippet, 0, buffer, position, snippet.Length);
                position += snippet.Length;

                var askSize = Math.Max((int)tick.AskLevel(0), 1);
			    if( askSize != lastTick.AskLevel(0)) {
                    value = ("2005=" + askSize + ";").ToCharArray();
                    for (var i = 0; i < value.Length; i++)
                    {
                        buffer[position] = (byte)value[i];
                        ++position;
                    }
                }

                ++index; snippet = quoteSnippetBytes[index];
                Array.Copy(snippet, 0, buffer, position, snippet.Length);
                position += snippet.Length;

                var bidSize = Math.Max((int)tick.BidLevel(0), 1);
			    if( bidSize != lastTick.BidLevel(0)) {
                    value = ("2006=" + bidSize + ";").ToCharArray();
                    for (var i = 0; i < value.Length; i++)
                    {
                        buffer[position] = (byte)value[i];
                        ++position;
                    }
                }

                ++index; snippet = quoteSnippetBytes[index];
                Array.Copy(snippet, 0, buffer, position, snippet.Length);
                position += snippet.Length;
            }

            var strValue = (tick.UtcTime.TimeOfDay + "." + tick.UtcTime.Microsecond.ToString("000") + ";2015=" + tick.UtcTime.Month.ToString("00") +
                "/" + tick.UtcTime.Day.ToString("00") + "/" + tick.UtcTime.Year + "\n").ToCharArray();
            for (var i = 0; i < strValue.Length; i++)
            {
                buffer[position] = (byte)strValue[i];
                ++position;
            }

            if( trace)
            {
                var message = Encoding.ASCII.GetString(buffer, 0, (int)position);
                log.Trace("Tick message: " + message);
            }
            quoteMessage.Data.Position = position;
            quoteMessage.Data.SetLength(position);
            lastTick.Inject(tick.Extract());

            if (trace) log.Trace("Added tick to packet: " + tick.UtcTime);
            quoteMessage.SendUtcTime = tick.UtcTime.Internal;

            if (quoteMessage.Data.GetBuffer().Length == 0)
            {
                return;
            }
            quotePacketQueue.Enqueue(quoteMessage, quoteMessage.SendUtcTime);
            if (trace) log.Trace("Enqueued tick packet: " + new TimeStamp(quoteMessage.SendUtcTime));
        }

		private void CloseWithQuotesError(MessageMbtQuotes message, string textMessage) {
		}
		
		private void CloseWithFixError(MessageFIX4_4 packet, string textMessage)
		{
			var fixMsg = (FIXMessage4_4) FixFactory.Create();
			TimeStamp timeStamp = TimeStamp.UtcNow;
			fixMsg.SetAccount(packet.Account);
			fixMsg.SetText( textMessage);
			fixMsg.AddHeader("j");
		    SendMessage(fixMsg);
        }
		
	}
}

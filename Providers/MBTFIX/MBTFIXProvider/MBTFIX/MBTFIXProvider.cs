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

namespace TickZoom.MBTFIX
{
    public class MBTFIXProvider : FIXProviderSupport, PhysicalOrderHandler, LogAware
	{
		private static readonly Log log = Factory.SysLog.GetLogger(typeof(MBTFIXProvider));
		private readonly bool info = log.IsDebugEnabled;
        private volatile bool trace = log.IsTraceEnabled;
        private volatile bool debug = log.IsDebugEnabled;
        private volatile bool verbose = log.IsVerboseEnabled;

        private class SymbolAlgorithm
        {
            public OrderAlgorithm OrderAlgorithm;
        }

        public override void RefreshLogLevel()
        {
            base.RefreshLogLevel();
            if (log != null)
            {
                verbose = log.IsVerboseEnabled;
                debug = log.IsDebugEnabled;
                trace = log.IsTraceEnabled;
            }
        }
        private static long nextConnectTime = 0L;
		private readonly object orderAlgorithmsLocker = new object();
        private Dictionary<long, SymbolAlgorithm> orderAlgorithms = new Dictionary<long, SymbolAlgorithm>();
        long lastLoginTry = long.MinValue;

        public enum RecoverProgress
        {
            InProgress,
            Completed,
            None,
        }
        private string fixDestination = "MBT";

        private MBTFIXProvider(string name) : base( name)
		{
            log.Register(this);
			log.Notice("Using config section: " + name);
			if( name.Contains(".config")) {
				throw new ApplicationException("Please remove .config from config section name.");
			}
		}
		
		public override void OnDisconnect() {
            HeartbeatDelay = int.MaxValue;
            if( ConnectionStatus == Status.PendingLogOut)
            {
                if( debug) log.Debug("Sending RemoteShutdown confirmation back to provider manager.");
            }
            else 
            {
                OrderStore.ForceSnapshot();
                var message = "MBTFIXProvider disconnected.";
                if (SyncTicks.Enabled)
                {
                    log.Notice(message);
                }
                else
                {
                    log.Error(message);
                }
                log.Info("Logging out -- Sending EndBroker event.");
                TrySendEndBroker();
            }
        }

		public override void OnRetry() {
		}

        private void TrySendStartBroker(SymbolInfo symbol, string message)
        {
            SymbolReceiver symbolReceiver;
            if( !symbolsRequested.TryGetValue(symbol.BinaryIdentifier, out symbolReceiver))
            {
                throw new InvalidOperationException("Can't find symbol request for " + symbol);
            }
            if( !IsRecovered)
            {
                if (debug) log.Debug("Attempted StartBroker but IsRecovered is " + IsRecovered);
                return;
            }
            var symbolAlgorithm = GetAlgorithm(symbol.BinaryIdentifier);
            if (symbolAlgorithm.OrderAlgorithm.IsBrokerOnline)
            {
                if (debug) log.Debug("Attempted StartBroker but isBrokerStarted is " + symbolAlgorithm.OrderAlgorithm.IsBrokerOnline);
                return;
            }
            else
            {
                if (debug) log.Debug("Sending StartBroker for " + symbol + ". Reason: " + message);
            }
            TrySend(EventType.StartBroker, symbol, symbolReceiver.Agent);
            symbolAlgorithm.OrderAlgorithm.IsBrokerOnline = true;
        }

        private void TrySend(EventType type, SymbolInfo symbol, Agent agent)
        {
			lock( symbolsRequestedLocker) {
                if( debug) log.Debug("Sending " + type + " for " + symbol + " ...");
                SymbolAlgorithm algorithm;
                if (TryGetAlgorithm(symbol.BinaryIdentifier, out algorithm))
                {
                    var item = new EventItem(symbol, type);
                    agent.SendEvent(item);
                }
                else
                {
                    log.Info("TrySend " + type + " for " + symbol + " but OrderAlgorithm not found for " + symbol + ". Ignoring.");
                }
            }
		}

        public int ProcessOrders()
        {
            return 0;
        }

        public bool IsChanged
        {
            get { return false; }
            set { }
        }
		
		private void TrySendEndBroker() {

            lock (symbolsRequestedLocker)
            {
				foreach(var kvp in symbolsRequested) {
					var symbolReceiver = kvp.Value;
                    TrySendEndBroker(symbolReceiver.Symbol);
				}
			}
		}

        private void TrySendEndBroker( SymbolInfo symbol)
        {
            var symbolReceiver = symbolsRequested[symbol.BinaryIdentifier];
            var algorithm = GetAlgorithm(symbol.BinaryIdentifier);
            if (!algorithm.OrderAlgorithm.IsBrokerOnline)
            {
                if (debug) log.Debug("Tried to send EndBroker for " + symbol + " but broker status is already offline.");
                return;
            }
            var item = new EventItem(symbol, EventType.EndBroker);
            symbolReceiver.Agent.SendEvent(item);
            algorithm.OrderAlgorithm.IsBrokerOnline = false;
        }

        private void RequestSessionUpdate()
        {
            var mbtMsg = FixFactory.Create();
            mbtMsg.SetTradingSessionRequestId(FixFactory.Sender + "-" + mbtMsg.Sequence + "-" + TimeStamp.UtcNow);
            mbtMsg.SetTradingSessionId("TSSTATE");
            mbtMsg.SetSubscriptionRequestType(1);
            mbtMsg.AddHeader("g");
            if (debug)
            {
                log.Debug("Request Session Update: \n" + mbtMsg);
            }
            SendMessage(mbtMsg);
        }

        private void SendLogin(int localSequence)
        {
            lastLoginTry = Factory.Parallel.TickCount;

            FixFactory = new FIXFactory4_4(localSequence+1, UserName, fixDestination);
            var mbtMsg = FixFactory.Create();
            mbtMsg.SetEncryption(0);
            mbtMsg.SetHeartBeatInterval(30);
            if( localSequence == 0)
            {
                mbtMsg.ResetSequence();
            }
            mbtMsg.SetEncoding("554_H1");
            mbtMsg.SetPassword(Password);
            mbtMsg.AddHeader("A");
            if (debug)
            {
                log.Debug("Login message: \n" + mbtMsg);
            }
            SendMessage(mbtMsg);
            if (SyncTicks.Enabled)
            {
                //HeartbeatDelay = int.MaxValue;
                HeartbeatDelay = 2;
                RetryDelay = 1;
                RetryStart = 1;
            }
            else
            {
                HeartbeatDelay = 40;
                RetryDelay = 30;
            }
        }

        public override bool OnLogin()
        {
            if (debug) log.Debug("Login()");

            if (OrderStore.Recover())
            {
                // Reset the order algorithms
                lock( orderAlgorithmsLocker)
                {
                    var symbolIds = new List<long>();
                    foreach( var kvp in orderAlgorithms)
                    {
                        symbolIds.Add(kvp.Key);
                    }
                    orderAlgorithms.Clear();
                    foreach( var symbolId in symbolIds)
                    {
                        CreateAlgorithm(symbolId);
                    }
                }
                if (debug) log.Debug("Recovered from snapshot Local Sequence " + OrderStore.LocalSequence + ", Remote Sequence " + OrderStore.RemoteSequence);
                if (debug) log.Debug("Recovered orders from snapshot: \n" + OrderStore.OrdersToString());
                if (debug) log.Debug("Recovered symbol positions from snapshot:\n" + OrderStore.SymbolPositionsToString());
                if (debug) log.Debug("Recovered strategy positions from snapshot:\n" + OrderStore.StrategyPositionsToString());
                RemoteSequence = OrderStore.RemoteSequence;
                SendLogin(OrderStore.LocalSequence + 500);
                OrderStore.RequestSnapshot();
            }
            else
            {
                if( debug) log.Debug("Unable to recover from snapshot. Beginning full recovery.");
                RemoteSequence = 1;
                SendLogin(0);
            }
            return true;
        }

        public override void OnLogout()
        {
            if (isDisposed)
            {
                if( debug) log.Debug("OnLogOut() already disposed.");
                return;
            }
            var mbtMsg = FixFactory.Create();
            mbtMsg.AddHeader("5");
            SendMessage(mbtMsg);
            log.Info("Logout message sent: " + mbtMsg);
            log.Info("Logging out -- Sending EndBroker event.");
            TrySendEndBroker();
        }
		
		protected override void OnStartRecovery()
		{
			if( !LogRecovery) {
				MessageFIXT1_1.IsQuietRecovery = true;
			}
		    CancelRecovered();
            TryEndRecovery();
        }

        protected override void OnFinishRecovery()
        {
        }

        public override void OnStartSymbol(SymbolInfo symbol)
        {
            var algorithm = CreateAlgorithm(symbol.BinaryIdentifier);
            if (ConnectionStatus == Status.Recovered)
            {
                algorithm.OrderAlgorithm.ProcessOrders();
                if (algorithm.OrderAlgorithm.IsSynchronized)
                {
                    TrySendStartBroker(symbol,"Start symbol");
                }
            }
        }

		public override void OnStopSymbol(SymbolInfo symbol)
		{
            TrySendEndBroker();
        }
	
		private void RequestPositions() {
			var fixMsg = (FIXMessage4_4) FixFactory.Create();
            fixMsg.SetSubscriptionRequestType(1);
            fixMsg.SetAccount(AccountNumber);
			fixMsg.SetPositionRequestId(1);
			fixMsg.SetPositionRequestType(0);
			fixMsg.AddHeader("AN");
			SendMessage(fixMsg);
		}

        private TimeStamp previousHeartbeatTime = default(TimeStamp);
        private TimeStamp recentHeartbeatTime = default(TimeStamp);
        private void SendHeartbeat()
        {
            if (debug) log.Debug("SendHeartBeat Status " + ConnectionStatus + ", Session Status Online " + isOrderServerOnline + ", Resend Complete " + IsResendComplete);
            if (!isOrderServerOnline) RequestSessionUpdate();
            if (!IsRecovered) TryEndRecovery();
            if (IsRecovered)
            {
                lock( orderAlgorithmsLocker)
                {
                    foreach( var kvp in orderAlgorithms)
                    {
                        var algo = kvp.Value;
                        if( !algo.OrderAlgorithm.CheckForPending())
                        {
                            algo.OrderAlgorithm.ProcessHeartBeat();
                        }
                    }
                }
            }
            var fixMsg = (FIXMessage4_4)FixFactory.Create();
			fixMsg.AddHeader("0");
			SendMessage( fixMsg);
            previousHeartbeatTime = recentHeartbeatTime;
            recentHeartbeatTime = TimeStamp.UtcNow;
        }

        private unsafe bool VerifyLoginAck(MessageFIXT1_1 message)
		{
		    var packetFIX = message;
		    if ("A" == packetFIX.MessageType &&
		        "FIX.4.4" == packetFIX.Version &&
		        "MBT" == packetFIX.Sender &&
		        UserName == packetFIX.Target &&
		        "0" == packetFIX.Encryption)
		    {
                return true;
            }
            else
		    {
                var textMessage = new StringBuilder();
                textMessage.AppendLine("Invalid login response:");
                textMessage.AppendLine("  message type = " + packetFIX.MessageType);
                textMessage.AppendLine("  version = " + packetFIX.Version);
                textMessage.AppendLine("  sender = " + packetFIX.Sender);
                textMessage.AppendLine("  target = " + packetFIX.Target);
                textMessage.AppendLine("  encryption = " + packetFIX.Encryption);
                textMessage.AppendLine("  sequence = " + packetFIX.Sequence);
                textMessage.AppendLine("  heartbeat interval = " + packetFIX.HeartBeatInterval);
                textMessage.AppendLine(packetFIX.ToString());
                log.Error(textMessage.ToString());
                return false;
            }
		}

        protected override bool HandleLogon(MessageFIXT1_1 message)
        {
            if (ConnectionStatus != Status.PendingLogin)
            {
                throw new InvalidOperationException("Attempt logon when in " + ConnectionStatus +
                                                    " instead of expected " + Status.PendingLogin);
            }
            if (VerifyLoginAck(message))
            {
                return true;
            }
            else
            {
                RegenerateSocket();
                return false;
            }
        }
		
		protected override void ReceiveMessage(Message message) {
			var packetFIX = (MessageFIX4_4) message;
			switch( packetFIX.MessageType) {
                case "h":
                    if (ConnectionStatus == Status.PendingServerResend)
                    {
                        ConnectionStatus = Status.PendingRecovery;
                        TryEndRecovery();
                    }
                    SessionStatus(packetFIX);
			        break;
                case "AP":
				case "AO":
					PositionUpdate( packetFIX);
					break;
				case "8":
                    if( string.IsNullOrEmpty(packetFIX.TransactionTime))
                    {
                        throw new ApplicationException("Found FIX message with empty transaction time: " + packetFIX);
                    }
                    var transactTime = new TimeStamp(packetFIX.TransactionTime);
                    if( transactTime >= OrderStore.LastSequenceReset)
                    {
                        ExecutionReport(packetFIX);
                    }
                    else
                    {
                        if( debug) log.Debug("Ignoring execution report of sequence " + packetFIX.Sequence + " because transact time " + transactTime + " is earlier than last sequence reset " + OrderStore.LastSequenceReset);
                    }
					break;
				case "9":
					CancelRejected( packetFIX);
                    break;
				case "1":
					SendHeartbeat();
					break;
				case "0":
					// Received heartbeat
					break;
				case "j":
                    BusinessReject(packetFIX);
                    break;
				default:
					log.Warn("Ignoring Message: '" + packetFIX.MessageType + "'\n" + packetFIX);
					break;
			}
		}

        private void BusinessReject(MessageFIX4_4 packetFIX) {
			var lower = packetFIX.Text.ToLower();
			var text = packetFIX.Text;
			var errorOkay = false;
			errorOkay = lower.Contains("server") ? true : errorOkay;
			errorOkay = text.Contains("DEMOORDS") ? true : errorOkay;
			errorOkay = text.Contains("FXORD1") ? true : errorOkay;
			errorOkay = text.Contains("FXORD2") ? true : errorOkay;
			errorOkay = text.Contains("FXORD01") ? true : errorOkay;
			errorOkay = text.Contains("FXORD02") ? true : errorOkay;
            log.Error(packetFIX.Text + " -- Sending EndBroker event.");
            CancelRecovered();
            TrySendEndBroker();
            TryEndRecovery();
            log.Info(packetFIX.Text + " Sent EndBroker event due to Message:\n" + packetFIX);
            if (!errorOkay)
            {
				string message = "FIX Server reported an error: " + packetFIX.Text + "\n" + packetFIX;
				throw new ApplicationException( message);
			}
		}

        protected override void TryEndRecovery()
        {
            if (debug) log.Debug("TryEndRecovery Status " + ConnectionStatus +
                ", Session Status Online " + isOrderServerOnline +
                ", Resend Complete " + IsResendComplete);
            switch (ConnectionStatus)
            {
                case Status.Recovered:
                case Status.PendingLogOut:
                case Status.PendingLogin:
                case Status.PendingServerResend:
                case Status.Disconnected:
                    return;
                case Status.PendingRecovery:
                    if (IsResendComplete && isOrderServerOnline)
                    {
                        OrderStore.RequestSnapshot();
                        EndRecovery();
                        RequestPositions();
                        RequestSessionUpdate();
                        StartPositionSync();
                        return;
                    }
                    break;
                default:
                    throw new ApplicationException("Unexpected connection status for TryEndRecovery: " + ConnectionStatus);
            }
        }

        private string GetOpenOrders()
        {
            var sb = new StringBuilder();
            var list = OrderStore.GetOrders((x) => true);
			foreach( var order in list) {
				sb.Append( "    ");
				sb.Append( order.BrokerOrder);
				sb.Append( " ");
				sb.Append( order);
				sb.AppendLine();
			}
            return sb.ToString();
        }

        private void StartPositionSync()
        {
            if( debug) log.Debug("StartPositionSync()");
            var openOrders = GetOpenOrders();
            if( string.IsNullOrEmpty(openOrders))
            {
                if( debug) log.Debug("Found 0 open orders prior to sync.");
            }
            else
            {
                if (debug) log.Debug("Orders prior to sync:\n" + openOrders);
            }
			MessageFIXT1_1.IsQuietRecovery = false;
            foreach( var kvp in orderAlgorithms)
            {
                var algorithm = kvp.Value;
                algorithm.OrderAlgorithm.ProcessOrders();
                if (algorithm.OrderAlgorithm.IsSynchronized)
                {
                    var symbol = Factory.Symbol.LookupSymbol(kvp.Key);
                    TrySendStartBroker(symbol,"start position sync");
                }
            }
        }

        private Dictionary<string,bool> sessionStatusMap = new Dictionary<string, bool>();
        private volatile bool isOrderServerOnline = false;
        private void SessionStatus(MessageFIX4_4 packetFIX)
        {
            var newIsSessionStatusOnline = false;
            log.Debug("Found session status for " + packetFIX.TradingSessionId + " or " + packetFIX.TradingSessionSubId +
                      ": " + packetFIX.TradingSessionStatus);
            var subId = string.IsNullOrEmpty(packetFIX.TradingSessionSubId)
                            ? packetFIX.TradingSessionId
                            : packetFIX.TradingSessionSubId;
            if( !CompareSession( subId) )
            {
                return;
            }
            switch (packetFIX.TradingSessionStatus)
            {
                case 2:
                    sessionStatusMap[subId] = true;
                    newIsSessionStatusOnline = true;
                    break;
                case 3:
                    sessionStatusMap[subId] = false;
                    break;
                default:
                    log.Warn("Received unknown server session status: " + packetFIX.TradingSessionStatus);
                    break;
            }
            foreach (var status in sessionStatusMap)
            {
                if (!status.Value)
                {
                    newIsSessionStatusOnline = false;
                }
            }
            if( debug) log.Debug("Order server connected (new " + newIsSessionStatusOnline + ", previous " + isOrderServerOnline);
            if (newIsSessionStatusOnline != isOrderServerOnline)
            {
                isOrderServerOnline = newIsSessionStatusOnline;
                if (isOrderServerOnline)
                {
                    CancelRecovered();
                    TrySendEndBroker();
                    TryEndRecovery();
                }
                else
                {
                    var message = "Order server went offline. Attempting to reconnect.";
                    if( SyncTicks.Enabled)
                    {
                        log.Notice(message);
                    } else
                    {
                        log.Error(message);
                    }
                    CancelRecovered();
                    TrySendEndBroker();
                    TryEndRecovery();
                }
            }
            else if( trace)
            {
                var message = "Order server continues offline. Attempting to reconnect.";
                log.Trace(message);
            }
	    }

        public override void CancelRecovered()
        {
            base.CancelRecovered();
            foreach( var kvp in orderAlgorithms)
            {
                var algorithm = kvp.Value;
                algorithm.OrderAlgorithm.IsPositionSynced = false;
            }
        }



        private void PositionUpdate( MessageFIX4_4 packetFIX) {
			if( packetFIX.MessageType == "AO") {
				if(debug) log.Debug("PositionUpdate Complete.");
                TryEndRecovery();
			}
            else 
            {
                var position = packetFIX.LongQuantity + packetFIX.ShortQuantity;
                SymbolInfo symbolInfo;
                try
                {
                    symbolInfo = Factory.Symbol.LookupSymbol(packetFIX.Symbol);
                }
                catch (ApplicationException ex)
                {
                    log.Error("Error looking up " + packetFIX.Symbol + ": " + ex.Message);
                    return;
                }
                SymbolInfo symbol;
                try
                {
                    symbol = Factory.Symbol.LookupSymbol(packetFIX.Symbol);
                }
                catch
                {
                    log.Info("PositionUpdate. But " + packetFIX.Symbol + " was not found in symbol dictionary.");
                    return;
                }
                SymbolAlgorithm algorithm;
                if( TryGetAlgorithm(symbol.BinaryIdentifier, out algorithm))
                {
                    if (debug) log.Debug("PositionUpdate for " + symbolInfo + ": MBT actual =" + position + ", TZ actual=" + algorithm.OrderAlgorithm.ActualPosition);
                }
                else
                {
                    log.Info("PositionUpdate for " + symbolInfo + ": MBT actual =" + position + " but symbol was not requested. Ignoring.");
                }
            }
		}

		private void ExecutionReport( MessageFIX4_4 packetFIX)
		{
		    var clientOrderId = 0L;
		    long.TryParse(packetFIX.ClientOrderId, out clientOrderId);
            var originalClientOrderId = 0L;
		    long.TryParse(packetFIX.ClientOrderId, out originalClientOrderId);
            if (packetFIX.Text == "END")
            {
                throw new ApplicationException("Unexpected END in FIX Text field. Never sent a 35=AF message.");
            }
            SymbolAlgorithm algorithm = null;
		    SymbolInfo symbolInfo = packetFIX.Symbol != null ? Factory.Symbol.LookupSymbol(packetFIX.Symbol) : null;
            if( symbolInfo != null)
            {
                TryGetAlgorithm(symbolInfo.BinaryIdentifier, out algorithm);
            }

            string orderStatus = packetFIX.OrderStatus;
            switch (orderStatus)
            {
                case "0": // New
                    if (debug && (LogRecovery || !IsRecovery))
                    {
                        log.Debug("ExecutionReport New: " + packetFIX);
                    }
                    if (algorithm == null)
                    {
                        log.Info("New order but OrderAlgorithm not found for " + symbolInfo + ". Ignoring.");
                        break;
                    }
                    CreateOrChangeOrder order = null;
                    OrderStore.TryGetOrderById(clientOrderId, out order);
                    if (order != null && symbolInfo.FixSimulationType == FIXSimulationType.BrokerHeldStopOrder) // Stop Order
                    {
                        if( order.Type == OrderType.BuyStop || order.Type == OrderType.SellStop)
                        {
                            if (debug) log.Debug("New order message ignored for Forex Stop: " + packetFIX);
                            return;
                        }
                    }
                    algorithm.OrderAlgorithm.ConfirmCreate(clientOrderId, IsRecovered);
                    if (algorithm.OrderAlgorithm.IsSynchronized)
                    {
                        TrySendStartBroker(symbolInfo, "sync on confirm cancel");
                    }
                    OrderStore.SetSequences(RemoteSequence, FixFactory.LastSequence);
                    break;
                case "1": // Partial
                    if (debug && (LogRecovery || !IsRecovery))
                    {
                        log.Debug("ExecutionReport Partial: " + packetFIX);
                    }
                    //UpdateOrder(packetFIX, OrderState.Active, null);
                    SendFill(packetFIX);
                    OrderStore.SetSequences(RemoteSequence, FixFactory.LastSequence);
                    break;
                case "2":  // Filled 
                    if (debug && (LogRecovery || !IsRecovery))
                    {
                        log.Debug("ExecutionReport Filled: " + packetFIX);
                    }
                    if (packetFIX.CumulativeQuantity < packetFIX.LastQuantity)
                    {
                        log.Warn("Ignoring message due to CumQty " + packetFIX.CumulativeQuantity + " less than " + packetFIX.LastQuantity + ". This is a workaround for a MBT FIX server which sends an extra invalid fill message on occasion.");
                        break;
                    }
                    SendFill(packetFIX);
                    OrderStore.SetSequences(RemoteSequence, FixFactory.LastSequence);
                    break;
                case "5": // Replaced
                    if (debug && (LogRecovery || !IsRecovery))
                    {
                        log.Debug("ExecutionReport Replaced: " + packetFIX);
                    }
                    if (algorithm == null)
                    {
                        log.Info("ConfirmChange but OrderAlgorithm not found for " + symbolInfo + ". Ignoring.");
                        break;
                    }
                    algorithm.OrderAlgorithm.ConfirmChange(clientOrderId, IsRecovered);
                    if (algorithm.OrderAlgorithm.IsSynchronized)
                    {
                        TrySendStartBroker(symbolInfo, "sync on confirm change");
                    }
                    OrderStore.SetSequences(RemoteSequence, FixFactory.LastSequence);
                    break;
                case "4": // Canceled
                    if (debug && (LogRecovery || !IsRecovery))
                    {
                        log.Debug("ExecutionReport Canceled: " + packetFIX);
                    }
                    if (algorithm == null)
                    {
                        log.Info("Order Canceled but OrderAlgorithm not found for " + symbolInfo + ". Ignoring.");
                        break;
                    }
                    if (clientOrderId != 0)
                    {
                        algorithm.OrderAlgorithm.ConfirmCancel(clientOrderId, IsRecovered);
                        if (algorithm.OrderAlgorithm.IsSynchronized)
                        {
                            TrySendStartBroker(symbolInfo, "sync on confirm cancel");
                        }
                    }
                    else if (originalClientOrderId != 0)
                    {
                        algorithm.OrderAlgorithm.ConfirmCancel(originalClientOrderId, IsRecovered);
                        if (algorithm.OrderAlgorithm.IsSynchronized)
                        {
                            TrySendStartBroker(symbolInfo, "sync on confirm cancel orig order");
                        }
                    }
                    OrderStore.SetSequences(RemoteSequence, FixFactory.LastSequence);
                    break;
                case "6": // Pending Cancel
                    if (debug && (LogRecovery || !IsRecovery))
                    {
                        log.Debug("ExecutionReport Pending Cancel: " + packetFIX);
                    }
                    if (!string.IsNullOrEmpty(packetFIX.Text) && packetFIX.Text.Contains("multifunction order"))
                    {
                        if (debug && (LogRecovery || IsRecovered))
                        {
                            log.Debug("Pending cancel of multifunction order, so removing " + packetFIX.ClientOrderId + " and " + packetFIX.OriginalClientOrderId);
                        }
                        if( clientOrderId != 0L) OrderStore.RemoveOrder(clientOrderId);
                        if (originalClientOrderId != 0L) OrderStore.RemoveOrder(originalClientOrderId);
                        break;
                    }
                    OrderStore.SetSequences(RemoteSequence, FixFactory.LastSequence);
                    TryHandlePiggyBackFill(packetFIX);
                    break;
                case "8": // Rejected
                    if (debug && (LogRecovery || !IsRecovery))
                    {
                        log.Debug("ExecutionReport Reject: " + packetFIX);
                    }
                    RejectOrder(packetFIX);
                    break;
                case "9": // Suspended
                    if (debug && (LogRecovery || !IsRecovery))
                    {
                        log.Debug("ExecutionReport Suspended: " + packetFIX);
                    }
                    OrderStore.SetSequences(RemoteSequence, FixFactory.LastSequence);
                    break;
                case "A": // PendingNew
                    if (debug && (LogRecovery || !IsRecovery))
                    {
                        log.Debug("ExecutionReport Pending New: " + packetFIX);
                    }
                    if (algorithm == null)
                    {
                        log.Info("PendingNew but OrderAlgorithm not found for " + symbolInfo + ". Ignoring.");
                        break;
                    }
                        
                    if (symbolInfo.FixSimulationType == FIXSimulationType.BrokerHeldStopOrder &&
                        packetFIX.OrderType == "3" &&  // Stop Order
                        packetFIX.ExecutionType == "D")  // Restated
                    {
                        if (debug) log.Debug("Ignoring restated message 150=D for Forex stop execution report 39=A.");
                    }
                    else
                    {
                        algorithm.OrderAlgorithm.ConfirmActive(originalClientOrderId, IsRecovered);
                        if (algorithm.OrderAlgorithm.IsSynchronized)
                        {
                            TrySendStartBroker(symbolInfo, "sync on confirm cancel orig order");
                        }
                    }
                    OrderStore.SetSequences(RemoteSequence, FixFactory.LastSequence);
                    break;
                case "E": // Pending Replace
                    if (debug && (LogRecovery || !IsRecovery))
                    {
                        log.Debug("ExecutionReport Pending Replace: " + packetFIX);
                    }
                    OrderStore.SetSequences(RemoteSequence, FixFactory.LastSequence);
                    var orderState = OrderState.Pending;
                    TryHandlePiggyBackFill(packetFIX);
                    break;
                case "R": // Resumed.
                    if (debug && (LogRecovery || !IsRecovery))
                    {
                        log.Debug("ExecutionReport Resumed: " + packetFIX);
                    }
                    OrderStore.SetSequences(RemoteSequence, FixFactory.LastSequence);
                    //UpdateOrder(packetFIX, OrderState.Active, null);
                    // Ignore
                    break;
                default:
                    throw new ApplicationException("Unknown order status: '" + orderStatus + "'");
            }
		}

		private void TryHandlePiggyBackFill(MessageFIX4_4 packetFIX) {
			if( packetFIX.LastQuantity > 0) {
                if (debug) log.Debug("TryHandlePiggyBackFill triggering fill because LastQuantity = " + packetFIX.LastQuantity);
                SendFill(packetFIX);
			}
		}
		
		private void CancelRejected( MessageFIX4_4 packetFIX) {
            var clientOrderId = 0L;
            long.TryParse(packetFIX.ClientOrderId, out clientOrderId);
            var originalClientOrderId = 0L;
            long.TryParse(packetFIX.ClientOrderId, out originalClientOrderId);
            if (debug && (LogRecovery || !IsRecovery))
            {
				log.Debug("CancelRejected: " + packetFIX);
			}
			string orderStatus = packetFIX.OrderStatus;
		    bool removeOriginal = false;
			switch( orderStatus) {
				case "8": // Rejected
					var rejectReason = false;
                    if( packetFIX.Text.Contains("Order Server Not Available"))
                    {
                        rejectReason = true;
                        CancelRecovered();
                        TrySendEndBroker();
                        TryEndRecovery();
                    }
                    else if( packetFIX.Text.Contains("Cannot cancel order. Probably already filled or canceled."))
                    {
                        rejectReason = true;
                        log.Warn("RemoveOriginal=FALSE for: " + packetFIX.Text);
                        //removeOriginal = true;
                    }
			        else if( packetFIX.Text.Contains("No such order"))
                    {
                        rejectReason = true;
                        log.Warn("RemoveOriginal=FALSE for: " + packetFIX.Text);
                        //removeOriginal = true;
                    }
                    else if( packetFIX.Text.Contains("Order pending remote") ||
                        packetFIX.Text.Contains("Cancel request already pending") ||
                        packetFIX.Text.Contains("ORDER in pending state") ||
                        packetFIX.Text.Contains("General Order Replace Error"))
                    {
                        rejectReason = true;
                    }
                                      
                    CreateOrChangeOrder order;
                    if( OrderStore.TryGetOrderById( clientOrderId, out order))
                    {
                        var symbol = order.Symbol;
                        SymbolAlgorithm algorithm;
                        if (!TryGetAlgorithm(symbol.BinaryIdentifier, out algorithm))
                        {
                            log.Info("Cancel rejected but OrderAlgorithm not found for " + symbol + ". Ignoring.");
                            break;
                        }
                        var retryImmediately = true;
                        algorithm.OrderAlgorithm.RejectOrder(clientOrderId, removeOriginal, IsRecovered, retryImmediately);
                    }
                    else
                    {
                        if( debug) log.Debug("Order not found for " + clientOrderId + ". Probably allready filled or canceled.");
                    }

                    if (!rejectReason && IsRecovered)
                    {
						var message = "Order Rejected: " + packetFIX.Text + "\n" + packetFIX;
						var stopping = "The cancel reject error message '" + packetFIX.Text + "' was unrecognized. ";
						log.Warn( message);
						log.Error( stopping );
					} else {
						if( LogRecovery || !IsRecovery) {
							log.Info( "CancelReject(" + packetFIX.Text + ") Removed cancel order: " + packetFIX.ClientOrderId);
						}
					}
					break;
				default:
					throw new ApplicationException("Unknown cancel rejected order status: '" + orderStatus + "'");
			}
		}
		
		private int SideToSign( string side) {
			switch( side) {
				case "1": // Buy
					return 1;
				case "2": // Sell
				case "5": // SellShort
					return -1;
				default:
					throw new ApplicationException("Unknown order side: " + side);
			}
		}
		
		public void SendFill( MessageFIX4_4 packetFIX) {
            var clientOrderId = 0L;
            long.TryParse(packetFIX.ClientOrderId, out clientOrderId);
            var originalClientOrderId = 0L;
            long.TryParse(packetFIX.ClientOrderId, out originalClientOrderId);
            if (debug) log.Debug("SendFill( " + packetFIX.ClientOrderId + ")");
			var symbolInfo = Factory.Symbol.LookupSymbol(packetFIX.Symbol);
			var timeZone = new SymbolTimeZone(symbolInfo);
            SymbolAlgorithm algorithm;
            if (!TryGetAlgorithm(symbolInfo.BinaryIdentifier, out algorithm))
            {
                log.Info("Fill received but OrderAlgorithm not found for " + symbolInfo + ". Ignoring.");
                return;
            }
            var fillPosition = packetFIX.LastQuantity * SideToSign(packetFIX.Side);
            if (GetSymbolStatus(symbolInfo))
            {
                CreateOrChangeOrder order;
                if( OrderStore.TryGetOrderById( clientOrderId, out order)) {
				    TimeStamp executionTime;
				    if( UseLocalFillTime) {
					    executionTime = TimeStamp.UtcNow;
				    } else {
					    executionTime = new TimeStamp(packetFIX.TransactionTime);
				    }
				    var configTime = executionTime;
				    configTime.AddSeconds( timeZone.UtcOffset(executionTime));
                    var fill = Factory.Utility.PhysicalFill(fillPosition, packetFIX.LastPrice, configTime, executionTime, order.BrokerOrder, false, packetFIX.OrderQuantity, packetFIX.CumulativeQuantity, packetFIX.LeavesQuantity, IsRecovered, true);
				    if( debug) log.Debug( "Sending physical fill: " + fill);
                    algorithm.OrderAlgorithm.ProcessFill(fill);
                }
                else
                {
                    algorithm.OrderAlgorithm.IncreaseActualPosition(fillPosition);
                    log.Notice("Fill id " + packetFIX.ClientOrderId + " not found. Must have been a manual trade.");
                    if( SyncTicks.Enabled)
                    {
                        var tickSync = SyncTicks.GetTickSync(symbolInfo.BinaryIdentifier);
                        tickSync.RemovePhysicalFill(packetFIX.ClientOrderId);
                    }
                }
                if (algorithm.OrderAlgorithm.IsSynchronized)
                {
                    TrySendStartBroker(symbolInfo,"position sync on fill");
                }
			}
		}

		public void ProcessFill( SymbolInfo symbol, LogicalFillBinary fill)
		{
            SymbolReceiver symbolReceiver;
            if (!symbolsRequested.TryGetValue(symbol.BinaryIdentifier, out symbolReceiver))
            {
                throw new InvalidOperationException("Can't find symbol request for " + symbol);
            }
            var symbolAlgorithm = GetAlgorithm(symbol.BinaryIdentifier);
            if( !symbolAlgorithm.OrderAlgorithm.IsBrokerOnline)
            {
                if (debug) log.Debug("Broker offline but sending fill anyway for " + symbol + " to receiver: " + fill);
            }
		    if (debug) log.Debug("Sending fill event for " + symbol + " to receiver: " + fill);
            var item = new EventItem(symbol, EventType.LogicalFill, fill);
            symbolReceiver.Agent.SendEvent(item);
		}

		public void RejectOrder( MessageFIX4_4 packetFIX)
		{
            var clientOrderId = 0L;
            long.TryParse(packetFIX.ClientOrderId, out clientOrderId);
            var originalClientOrderId = 0L;
            long.TryParse(packetFIX.ClientOrderId, out originalClientOrderId);
            var rejectReason = false;
		    bool removeOriginal = false;
		    if (packetFIX.Text.Contains("Cannot change order. Probably already filled or canceled."))
		    {
		        rejectReason = true;
                //removeOriginal = true;
		    }
		    else if( packetFIX.Text.Contains("No such order"))
		    {
		        rejectReason = true;
                //removeOriginal = true;
            }
		    else if (packetFIX.Text.Contains("Outside trading hours") ||
		             packetFIX.Text.Contains("not accepted this session") ||
		             packetFIX.Text.Contains("Pending live orders") ||
		             packetFIX.Text.Contains("improper setting") ||
                     packetFIX.Text.Contains("No position to close"))
		    {
		        rejectReason = true;
		    }

            else if (packetFIX.Text.Contains("Order Server Offline") ||
                packetFIX.Text.Contains("Trading temporarily unavailable") ||
                packetFIX.Text.Contains("Order Server Not Available"))
            {
                rejectReason = true;
                CancelRecovered();
                TrySendEndBroker();
                TryEndRecovery();
            }

		    if( IsRecovered && !rejectReason ) {
			    var message = "Order Rejected: " + packetFIX.Text + "\n" + packetFIX;
			    var stopping = "The reject error message '" + packetFIX.Text + "' was unrecognized.";
			    log.Warn( message);
		    } else if( LogRecovery || IsRecovered) {
			    log.Info( "RejectOrder(" + packetFIX.Text + ") Removed order: " + clientOrderId);
		    }

		    var symbol = Factory.Symbol.LookupSymbol(packetFIX.Symbol);
            SymbolAlgorithm algorithm;
            if (TryGetAlgorithm(symbol.BinaryIdentifier, out algorithm))
            {
                var retryImmediately = true;
                algorithm.OrderAlgorithm.RejectOrder(clientOrderId, removeOriginal, IsRecovered, retryImmediately);
            }
            else
            {
                log.Info("RejectOrder but OrderAlgorithm not found for " + symbol + ". Ignoring.");
            }
		}
		
		private SymbolAlgorithm CreateAlgorithm(long symbol) {
            SymbolAlgorithm symbolAlgorithm;
			lock( orderAlgorithmsLocker) {
                if (!orderAlgorithms.TryGetValue(symbol, out symbolAlgorithm))
                {
                    var symbolInfo = Factory.Symbol.LookupSymbol(symbol);
                    var orderCache = Factory.Engine.LogicalOrderCache(symbolInfo, false);
                    var algorithm = Factory.Utility.OrderAlgorithm("mbtfix", symbolInfo, this, orderCache, OrderStore);
                    algorithm.EnableSyncTicks = SyncTicks.Enabled;
                    symbolAlgorithm = new SymbolAlgorithm { OrderAlgorithm = algorithm };
                    orderAlgorithms.Add(symbol, symbolAlgorithm);
                    algorithm.OnProcessFill = ProcessFill;
				}
			}
			return symbolAlgorithm;
		}

        private bool TryGetAlgorithm(long symbol, out SymbolAlgorithm algorithm)
        {
            lock (orderAlgorithmsLocker)
            {
                return orderAlgorithms.TryGetValue(symbol, out algorithm);
            }
        }

        private SymbolAlgorithm GetAlgorithm(long symbol)
        {
            SymbolAlgorithm symbolAlgorithm;
            lock (orderAlgorithmsLocker)
            {
                if (!orderAlgorithms.TryGetValue(symbol, out symbolAlgorithm))
                {
                    throw new ApplicationException("OrderAlgorirhm was not found for " + Factory.Symbol.LookupSymbol(symbol));
                }
            }
            return symbolAlgorithm;
        }

        public override void PositionChange(PositionChangeDetail positionChange)
		{
            var symbol = positionChange.Symbol;
			if( debug) log.Debug( "PositionChange " + positionChange);
			var algorithm = GetAlgorithm(symbol.BinaryIdentifier);
            if( algorithm.OrderAlgorithm.PositionChange(positionChange, IsRecovered))
            {
                if (algorithm.OrderAlgorithm.IsSynchronized)
                {
                    TrySendStartBroker(symbol,"position change sync");
                }
            }
		}
		
	    protected override void Dispose(bool disposing)
	    {
            base.Dispose(disposing);
           	nextConnectTime = Factory.Parallel.TickCount + 10000;
	    }    
	        
		Dictionary<int,int> physicalToLogicalOrderMap = new Dictionary<int, int>();
	        
		public bool OnCreateBrokerOrder(CreateOrChangeOrder createOrChangeOrder)
		{
            if (!IsRecovered) return false;
			if( debug) log.Debug( "OnCreateBrokerOrder " + createOrChangeOrder + ". Connection " + ConnectionStatus + ", IsOrderServerOnline " + isOrderServerOnline);
            if( createOrChangeOrder.Action != OrderAction.Create)
            {
                throw new InvalidOperationException("Expected action Create but was " + createOrChangeOrder.Action);
            }
            OnCreateOrChangeBrokerOrder(createOrChangeOrder, false);
		    return true;
		}
	        
		private void OnCreateOrChangeBrokerOrder(CreateOrChangeOrder order, bool resend)
		{
            var fixMsg = (FIXMessage4_4)(resend ? FixFactory.Create(order.Sequence) : FixFactory.Create());
            order.Sequence = fixMsg.Sequence;
            //OrderStore.SetOrder(order);
            OrderStore.SetSequences(RemoteSequence, FixFactory.LastSequence);

            if (order.Size > order.Symbol.MaxOrderSize)
            {
                throw new ApplicationException("Order was greater than MaxOrderSize of " + order.Symbol.MaxPositionSize + " for:\n" + order);
            }

            var orderHandler = GetAlgorithm(order.Symbol.BinaryIdentifier);
		    var orderSize = order.Type == OrderType.SellLimit || order.Type == OrderType.SellMarket || order.Type == OrderType.SellStop ? -order.Size : order.Size;
            if (Math.Abs(orderHandler.OrderAlgorithm.ActualPosition + orderSize) > order.Symbol.MaxPositionSize)
            {
                throw new ApplicationException("Order was greater than MaxPositionSize of " + order.Symbol.MaxPositionSize + " for:\n" + order);
            }

			if( debug) log.Debug( "Adding Order to open order list: " + order);
			if( order.Action == OrderAction.Change) {
                var origBrokerOrder = order.OriginalOrder.BrokerOrder;
                fixMsg.SetClientOrderId(order.BrokerOrder.ToString());
				fixMsg.SetOriginalClientOrderId(origBrokerOrder.ToString());
			    CreateOrChangeOrder origOrder;
				if( OrderStore.TryGetOrderById(origBrokerOrder, out origOrder))
				{
                    origOrder.ReplacedBy = order;
				    if (debug) log.Debug("Setting replace property of " + origBrokerOrder + " to " + order.BrokerOrder);
                }
			} else {
				fixMsg.SetClientOrderId(order.BrokerOrder.ToString());
			}
			fixMsg.SetAccount(AccountNumber);
            if (order.Action == OrderAction.Change)
            {
				fixMsg.AddHeader("G");
			} else {
				fixMsg.AddHeader("D");
				if( order.Symbol.Destination.ToLower() == "default") {
					fixMsg.SetDestination("MBTX");
				} else {
					fixMsg.SetDestination(order.Symbol.Destination);
				}
			}
			fixMsg.SetHandlingInstructions(1);
			fixMsg.SetSymbol(order.Symbol.Symbol);
			fixMsg.SetSide( GetOrderSide(order.Side));
			switch( order.Type) {
				case OrderType.BuyLimit:
					fixMsg.SetOrderType(2);
					fixMsg.SetPrice(order.Price);
                    switch( order.Symbol.TimeInForce)
                    {
                        case TimeInForce.Day:
                            fixMsg.SetTimeInForce(0);
                            break;
                        case TimeInForce.GTC:
                            fixMsg.SetTimeInForce(1);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
					break;
				case OrderType.BuyMarket:
					fixMsg.SetOrderType(1);
					fixMsg.SetTimeInForce(0);
					break;
				case OrderType.BuyStop:
					fixMsg.SetOrderType(3);
					fixMsg.SetPrice(order.Price);
					fixMsg.SetStopPrice(order.Price);
                    switch (order.Symbol.TimeInForce)
                    {
                        case TimeInForce.Day:
                            fixMsg.SetTimeInForce(0);
                            break;
                        case TimeInForce.GTC:
                            fixMsg.SetTimeInForce(1);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                    break;
				case OrderType.SellLimit:
					fixMsg.SetOrderType(2);
					fixMsg.SetPrice(order.Price);
                    switch (order.Symbol.TimeInForce)
                    {
                        case TimeInForce.Day:
                            fixMsg.SetTimeInForce(0);
                            break;
                        case TimeInForce.GTC:
                            fixMsg.SetTimeInForce(1);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                    break;
				case OrderType.SellMarket:
					fixMsg.SetOrderType(1);
					fixMsg.SetTimeInForce(0);
					break;
				case OrderType.SellStop:
					fixMsg.SetOrderType(3);
					fixMsg.SetPrice(order.Price);
					fixMsg.SetStopPrice(order.Price);
                    switch (order.Symbol.TimeInForce)
                    {
                        case TimeInForce.Day:
                            fixMsg.SetTimeInForce(0);
                            break;
                        case TimeInForce.GTC:
                            fixMsg.SetTimeInForce(1);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                    break;
			}
			fixMsg.SetLocateRequired("N");
			fixMsg.SetTransactTime(order.UtcCreateTime);
			fixMsg.SetOrderQuantity((int)order.Size);
			fixMsg.SetOrderCapacity("A");
			fixMsg.SetUserName();
            if (order.Action == OrderAction.Change)
            {
				if( verbose) log.Verbose("Change order: \n" + fixMsg);
			} else {
                if (verbose) log.Verbose("Create new order: \n" + fixMsg);
			}
            if( resend)
            {
                fixMsg.SetDuplicate(true);
            }
			SendMessage( fixMsg);
		}

		private int GetOrderSide( OrderSide side) {
			switch( side) {
				case OrderSide.Buy:
					return 1;
				case OrderSide.Sell:
					return 2;
				case OrderSide.SellShort:
					return 5;
				case OrderSide.SellShortExempt:
					return 6;
				default:
					throw new ApplicationException("Unknown OrderSide: " + side);
			}
		}
		

		private long GetUniqueOrderId() {
			return TimeStamp.UtcNow.Internal;
		}

        protected override void ResendOrder(CreateOrChangeOrder order)
        {
            if( order.Action == OrderAction.Cancel)
            {
                if (debug) log.Debug("Resending cancel order: " + order);
                //if (SyncTicks.Enabled && !IsRecovered)
                //{
                //    TryAddPhysicalOrder(order);
                //}
                SendCancelOrder(order, true);
            }
            else
            {
                if (debug) log.Debug("Resending order: " + order);
                //if (SyncTicks.Enabled && !IsRecovered)
                //{
                //    TryAddPhysicalOrder(order);
                //}
                OnCreateOrChangeBrokerOrder(order, true);
            }
        }

		public bool OnCancelBrokerOrder(CreateOrChangeOrder order)
		{
            if (!IsRecovered) return false;
            if (debug) log.Debug("OnCancelBrokerOrder " + order + ". Connection " + ConnectionStatus + ", IsOrderServerOnline " + isOrderServerOnline);
            OrderStore.SetSequences(RemoteSequence, FixFactory.LastSequence);
            CreateOrChangeOrder createOrChangeOrder;
			try {
                createOrChangeOrder = OrderStore.GetOrderById(order.OriginalOrder.BrokerOrder);
			} catch( ApplicationException ex) {
                if (LogRecovery || !IsRecovery)
                {
                    log.Info("Order probably already canceled. " + ex.Message);
                }
			    if( SyncTicks.Enabled) {
					var tickSync = SyncTicks.GetTickSync(order.Symbol.BinaryIdentifier);
					tickSync.RemovePhysicalOrder();
				}
				return true;
			}
            createOrChangeOrder.ReplacedBy = order;
		    if( !object.ReferenceEquals(order.OriginalOrder,createOrChangeOrder))
            {
                throw new ApplicationException("Different objects!");
            }

            SendCancelOrder(order, false);
            return true;

		}

        private void TryAddPhysicalOrder( CreateOrChangeOrder order)
        {
            var tickSync = SyncTicks.GetTickSync(order.Symbol.BinaryIdentifier);
            tickSync.AddPhysicalOrder(order);
        }

        private void SendCancelOrder( CreateOrChangeOrder order, bool resend)
        {
            var fixMsg = (FIXMessage4_4)(resend ? FixFactory.Create(order.Sequence) : FixFactory.Create());
            order.Sequence = fixMsg.Sequence;
            //OrderStore.SetOrder(order);
            var newClientOrderId = order.BrokerOrder;
            fixMsg.SetOriginalClientOrderId(order.OriginalOrder.BrokerOrder.ToString());
            fixMsg.SetClientOrderId(newClientOrderId.ToString());
            fixMsg.SetAccount(AccountNumber);
            fixMsg.SetSide(GetOrderSide(order.OriginalOrder.Side));
            fixMsg.AddHeader("F");
            fixMsg.SetSymbol(order.Symbol.Symbol);
            fixMsg.SetTransactTime(TimeStamp.UtcNow);
            if (resend)
            {
                fixMsg.SetDuplicate(true);
            }
            SendMessage(fixMsg);
        }
		
		public bool OnChangeBrokerOrder(CreateOrChangeOrder createOrChangeOrder)
		{
            if (!IsRecovered) return false;
            if (debug) log.Debug("OnChangeBrokerOrder( " + createOrChangeOrder + ". Connection " + ConnectionStatus + ", IsOrderServerOnline " + isOrderServerOnline);
            if (createOrChangeOrder.Action != OrderAction.Change)
            {
                throw new InvalidOperationException("Expected action Change but was " + createOrChangeOrder.Action);
            }
            OnCreateOrChangeBrokerOrder(createOrChangeOrder, false);
            return true;
		}

	}
}
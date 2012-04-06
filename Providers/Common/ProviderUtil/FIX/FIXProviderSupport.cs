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
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

using TickZoom.Api;

namespace TickZoom.FIX
{
    public abstract class FIXProviderSupport : AgentPerformer, LogAware
    {
        private PhysicalOrderStore orderStore;
        private readonly Log log;
        private readonly Log fixLog;
        private volatile bool trace;
        private volatile bool debug;
        private volatile bool verbose;
        private volatile bool fixTrace;
        public virtual void RefreshLogLevel()
        {
            if (log != null)
            {
                debug = log.IsDebugEnabled;
                trace = log.IsTraceEnabled;
            }
            if (fixLog != null)
                fixTrace = fixLog.IsTraceEnabled;
        }
        protected readonly object symbolsRequestedLocker = new object();
        public class SymbolReceiver
        {
            public Agent Agent;
            public SymbolInfo Symbol;
        }
        protected Dictionary<long, SymbolReceiver> symbolsRequested = new Dictionary<long, SymbolReceiver>();
		private Socket socket;
		private Task socketTask;
		private string failedFile;
		protected Agent ClientAgent;
		private long retryDelay = 30; // seconds
		private long retryStart = 30; // seconds
        protected bool ignoreRetryDelay = false;  
		private long retryIncrease = 5;
		private long retryMaximum = 30;
		private volatile Status connectionStatus = Status.None;
        private volatile Status bestConnectionStatus = Status.None;
        private string addrStr;
		private ushort port;
		private string userName;
		private	string password;
		private	string accountNumber;
        private string destination;
		public abstract void OnDisconnect();
        public abstract void OnRetry();
		public abstract bool OnLogin();
        private string providerName;
        private TrueTimer retryTimer;
        private TrueTimer heartbeatTimer;
		private long heartbeatDelay;
		private bool logRecovery = true;
        private string configSection;
        private bool useLocalFillTime = true;
		private FIXTFactory fixFactory;
	    private string appDataFolder;
        private TimeStamp lastMessageTime;
        private int remoteSequence = 1;
        private SocketState lastSocketState = SocketState.New;
        private FastQueue<MessageFIXT1_1> resendQueue;
        protected string name;
        private Agent agent;
        public Agent Agent
        {
            get { return agent; }
            set { agent = value; }
        }
        
		public bool UseLocalFillTime {
			get { return useLocalFillTime; }
		}

        public Agent GetReceiver()
        {
            throw new NotImplementedException();
        }

        public void DumpHistory()
        {
            for( var i = 0; i<=fixFactory.LastSequence; i++)
            {
                FIXTMessage1_1 message;
                if( fixFactory.TryGetHistory(i, out message)) {
                    log.Info(message.ToString());
                }
            }
        }

		public FIXProviderSupport(string name)
		{
		    this.name = name;
            configSection = name;
            this.providerName = GetType().Name;
            log = Factory.SysLog.GetLogger(typeof(FIXProviderSupport) + "." + providerName + "." + name);
            log.Register(this);
            fixLog = Factory.SysLog.GetLogger("FIX");
            fixLog.Register(this);
            verbose = log.IsVerboseEnabled;
            debug = log.IsDebugEnabled;
            trace = log.IsTraceEnabled;
            fixTrace = fixLog.IsTraceEnabled;
        }

        public void Start(EventItem eventItem)
        {
            if (debug) log.Debug("Start() Agent: " + eventItem.Agent);
            this.ClientAgent = (Agent) eventItem.Agent;
            log.Info(providerName + " Startup");
            if (CheckFailedLoginFile())
            {
                Dispose();
            }
            else
            {
                RegenerateSocket();
            }
        }

        public void Initialize(Task task)
        {
            socketTask = task;
            socketTask.Scheduler = Scheduler.EarliestTime;
            retryTimer = Factory.Parallel.CreateTimer("Retry", socketTask, RetryTimerEvent);
            heartbeatTimer = Factory.Parallel.CreateTimer("Heartbeat", socketTask, HeartBeatTimerEvent);
            resendQueue = Factory.Parallel.FastQueue<MessageFIXT1_1>(name + "_" + providerName + ".Resend");
            orderStore = Factory.Utility.PhyscalOrderStore(name + "_" + providerName);
            resendQueue.ConnectInbound(socketTask);
            socketTask.Start();
            string logRecoveryString = Factory.Settings["LogRecovery"];
            logRecovery = !string.IsNullOrEmpty(logRecoveryString) && logRecoveryString.ToLower().Equals("true");
            appDataFolder = Factory.Settings["AppDataFolder"];
            if (appDataFolder == null)
            {
                throw new ApplicationException("Sorry, AppDataFolder must be set.");
            }
            if (debug) log.Debug("> SetupFolders.");
            string configFile = appDataFolder + @"/Providers/" + providerName + "/Default.config";
            failedFile = appDataFolder + @"/Providers/" + providerName + "/LoginFailed.txt";
            if (File.Exists(failedFile))
            {
                log.Error("Please correct the username or password error described in " + failedFile + ". Then delete the file before retrying, please.");
                return;
            }

            LoadProperties(configFile);
        }

        protected abstract MessageFactory CreateMessageFactory();

		protected void RegenerateSocket() {
			Socket old = socket;
			if( socket != null && socket.State != SocketState.Closed) {
				socket.Dispose();
                // Wait for graceful socket shutdown.
			    return;
			}
            socket = Factory.Provider.Socket(this.GetType().Name + "Socket", AddrStr, port);
            socket.ReceiveQueue.ConnectInbound(socketTask);
            socket.SendQueue.ConnectOutbound(socketTask);
            socket.OnConnect = OnConnect;
			socket.MessageFactory = CreateMessageFactory();
			if( debug) log.Debug("Created new " + socket);
			ConnectionStatus = Status.New;
			if( trace) {
				string message = "Generated socket: " + socket;
				if( old != null) {
					message += " to replace: " + old;
				}
				log.Trace(message);
			}
            // Initiate socket connection.
            while (true)
            {
                try
                {
                    socket.Connect();
                    if (debug) log.Debug("Requested Connect for " + socket);
                    var startTime = TimeStamp.UtcNow;
                    int fastRetry = 2;
                    startTime.AddSeconds(ignoreRetryDelay ? fastRetry : RetryDelay);
                    retryTimer.Start(startTime);
                    if (ignoreRetryDelay) 
                        log.InfoFormat("Quick retry requested.  Connection will retry in {0} seconds", fastRetry);
                    else
                    log.Info("Connection will timeout and retry in " + RetryDelay + " seconds.");
                    ignoreRetryDelay = false;
                    return;
                }
                catch (SocketErrorException ex)
                {
                    log.Error("Non fatal error while trying to connect: " + ex.Message);
                }
            }
        }

		public enum Status {
            None,
			New,
			Connected,
			PendingLogin,
		    PendingServerResend,
			PendingRecovery,
			Recovered,
            Disconnected,
			PendingRetry,
		    PendingLogOut,
		}
		
		public void WriteFailedLoginFile(string packetString) {
			string message = "Login failed for user name: " + userName + " and password: " + new string('*',password.Length);
			string fileMessage = "Resolve the problem and then delete this file before you retry.";
			string logMessage = "Resolve the problem and then delete the file " + failedFile + " before you retry.";
			if( File.Exists(failedFile)) {
				File.Delete(failedFile);
			}
			using( var fileOut = new StreamWriter(failedFile)) {
				fileOut.WriteLine(message);
				fileOut.WriteLine(fileMessage);
				fileOut.WriteLine("Actual FIX message for login that failed:");
				fileOut.WriteLine(packetString);
			}
			log.Error(message + " " + logMessage + "\n" + packetString);
		}
		
		private void OnDisconnect( Socket socket) {
			if( !this.socket.Equals(socket)) {
				log.Info("OnDisconnect( " + this.socket + " != " + socket + " ) - Ignored.");
				return;
			}
			log.Info("OnDisconnect( " + socket + " ) status " + ConnectionStatus);
            if (isDisposed)
            {
                isFinalized = true;
                return;
            }
            OnDisconnect();
            switch (ConnectionStatus)
            {
                case Status.Connected:
                case Status.Disconnected:
                case Status.New:
                case Status.PendingRecovery:
                case Status.Recovered:
                case Status.PendingLogin:
                case Status.PendingServerResend:
                case Status.PendingRetry:
                    orderStore.ForceSnapshot();
                    ConnectionStatus = Status.Disconnected;
                    var startTime = TimeStamp.UtcNow;
                    startTime.AddSeconds(RetryDelay);
                    retryTimer.Start(startTime);
                    break;
                case Status.PendingLogOut:
                    ConnectionStatus = Status.Disconnected;
                    Dispose();
                    break;
                default:
                    log.Warn("Unexpected connection status when socket disconnected: " + ConnectionStatus + ". Shutting down the provider.");
                    Dispose();
                    break;
            }
        }

        private void OnConnect(Socket socket)
        {
            if (!this.socket.Equals(socket))
            {
                log.Info("OnConnect( " + this.socket + " != " + socket + " ) - Ignored.");
                return;
            }
            log.Info("OnConnect( " + socket + " ) ");
            retryTimer.Cancel();
            ConnectionStatus = Status.Connected;
            IsResendComplete = true;
            using (OrderStore.BeginTransaction())
            {
                if (OnLogin())
                {
                    ConnectionStatus = Status.PendingLogin;
                    IncreaseHeartbeatTimeout();
                }
                else
                {
                    RegenerateSocket();
                }
            }
        }

        public bool IsInterrupted
        {
			get {
				return isDisposed || socket.State != SocketState.Connected;
			}
		}

        public virtual void CancelRecovered()
        {
            ConnectionStatus = Status.PendingRecovery;
        }

		public void StartRecovery() {
            CancelRecovered();
			OnStartRecovery();
		}
		
		public void EndRecovery() {
			ConnectionStatus = Status.Recovered;
		    bestConnectionStatus = Status.Recovered;
            OrderStore.ResetLastChange();
            OnFinishRecovery();
        }
		
		public bool IsRecovered {
			get {
                return ConnectionStatus == Status.Recovered;
			}
		}
		
		private void SetupRetry() {
			OnRetry();
			RegenerateSocket();
		}
		
		public bool IsRecovery {
			get {
				return ConnectionStatus == Status.PendingRecovery;
			}
		}

        private bool SnapshotBusy
        {
            get { return orderStore.IsBusy; }
        }

        private Yield RetryTimerEvent()
        {
            log.Info("Connection Timeout");
            RetryDelay += retryIncrease;
            RetryDelay = Math.Min(RetryDelay, retryMaximum);
            SetupRetry();
            return Yield.DidWork.Repeat;
        }

        private int frozenHeartbeatCounter;
        private Yield HeartBeatTimerEvent()
        {
            var typeStr = ConnectionStatus == Status.PendingLogin ? "Login Timeout" : "Heartbeat timeout";
            log.Info(typeStr + ". Last Message UTC Time: " + lastMessageTime + ", current UTC Time: " + TimeStamp.UtcNow);
            log.Error("FIXProvider " + typeStr);
            SyncTicks.LogStatus();
            if (SyncTicks.Frozen)
            {
                frozenHeartbeatCounter++;
                if (frozenHeartbeatCounter > 3)
                {
                    if (debug) log.Debug("More than 3 heart beats sent after frozen.  Ending heartbeats.");
                }
                else
                {
                    SetupRetry();
                }
            }
            else
            {
                SetupRetry();
            }
            IncreaseHeartbeatTimeout();
            return Yield.DidWork.Repeat;
        }

        public void Shutdown()
        {
            LogOut();
        }

        public Yield Invoke()
        {
            if (isDisposed) return Yield.NoWork.Repeat;

            EventItem eventItem;
            if( socketTask.Filter.Receive(out eventItem))
            {
                switch (eventItem.EventType)
                {
                    case EventType.Connect:
                        Start(eventItem);
                        socketTask.Filter.Pop();
                        break;
                    case EventType.Disconnect:
                        Stop(eventItem);
                        socketTask.Filter.Pop();
                        break;
                    case EventType.StartSymbol:
                        StartSymbol(eventItem);
                        socketTask.Filter.Pop();
                        break;
                    case EventType.StopSymbol:
                        StopSymbol(eventItem);
                        socketTask.Filter.Pop();
                        break;
                    case EventType.PositionChange:
                        var positionChange = (PositionChangeDetail) eventItem.EventDetail;
                        PositionChange(positionChange);
                        socketTask.Filter.Pop();
                        break;
                    case EventType.Shutdown:
                        LogOut();
                        socketTask.Filter.Pop();
                        break;
                    case EventType.Terminate:
                        Dispose();
                        socketTask.Filter.Pop();
                        break;
                    default:
                        throw new ApplicationException("Unexpected event type: " + eventItem.EventType);
                }
            }

            if (SnapshotBusy) return Yield.DidWork.Repeat;

            if (socket == null) return Yield.DidWork.Repeat;

            var transaction = OrderStore.BeginTransaction();
            try
            {
                if (socket.State != lastSocketState)
                {
                    if (debug) log.Debug("SocketState changed to " + socket.State);
                    lastSocketState = socket.State;
                }
                switch (socket.State)
                {
                    case SocketState.New:
                        return Yield.NoWork.Repeat;
                    case SocketState.PendingConnect:
                        return Yield.NoWork.Repeat;
                    case SocketState.Connected:
                        return TryProcessMessage();
                    case SocketState.Disconnected:
                    case SocketState.Closed:
                        return TrySetupRetry();
                    case SocketState.ShuttingDown:
                    case SocketState.Closing:
                        return Yield.NoWork.Repeat;
                    default:
                        string textMessage = "Unknown socket state: " + socket.State;
                        log.Error(textMessage);
                        throw new ApplicationException(textMessage);
                }
            }
            finally
            {
                transaction.Dispose();
                orderStore.TrySnapshot();
            }
		}

        private Yield TrySetupRetry()
        {
                        switch (ConnectionStatus)
                        {
                case Status.PendingLogin:
                case Status.PendingServerResend:
                case Status.PendingRecovery:
                case Status.Recovered:
                    return TryProcessMessage();
                case Status.Connected:
                case Status.Disconnected:
                            case Status.New:
                    ConnectionStatus = Status.PendingRetry;
                    RetryDelay += retryIncrease;
                    RetryDelay = Math.Min(RetryDelay, retryMaximum);
                    return Yield.NoWork.Repeat;
                case Status.PendingRetry:
                    return Yield.NoWork.Repeat;
                case Status.PendingLogOut:
                    Dispose();
                    return Yield.NoWork.Repeat;
                default:
                    log.Warn("Unexpected connection status when socket disconnected: " + ConnectionStatus + ". Shutting down the provider.");
                    Dispose();
                    return Yield.NoWork.Repeat;
            }
        }

        private Yield TryProcessMessage()
        {
            switch (ConnectionStatus)
            {
                case Status.New:
                case Status.Connected:
                    return Yield.NoWork.Repeat;
                case Status.PendingLogin:
                case Status.PendingLogOut:
                case Status.PendingServerResend:
                case Status.PendingRecovery:
                case Status.Recovered:
                    if (RetryDelay != RetryStart)
                    {
                        RetryDelay = RetryStart;
                        log.Info("(retryDelay reset to " + RetryDelay + " seconds.)");
                    }

                    MessageFIXT1_1 messageFIX = null;

                    if (ConnectionStatus != Status.PendingLogin)
                    {
                        while (resendQueue.Count > 0)
                        {
                            MessageFIXT1_1 tempMessage;
                            resendQueue.Peek(out tempMessage);
                            if (tempMessage.Sequence > remoteSequence && IsResendComplete)
                            {
                                if (debug) log.Debug("Found sequence " + tempMessage.Sequence + " on the resend queue. Requesting resend from " + remoteSequence + " to " + (tempMessage.Sequence - 1));
                                TryRequestResend(remoteSequence, tempMessage.Sequence - 1);
                                break;
                            }
                            if (tempMessage.Sequence == remoteSequence)
                            {
                                resendQueue.Dequeue(out messageFIX);
                                break;
                            }
                            if (tempMessage.Sequence < remoteSequence)
                            {
                                resendQueue.Dequeue(out messageFIX);
                            }
                            if (tempMessage.Sequence > remoteSequence)
                            {
                                break;
                            }
                        }
                    }

                    if (messageFIX == null)
                    {
                        Message message = null;
                        if (Socket.TryGetMessage(out message))
                        {
                            var disconnect = message as DisconnectMessage;
                            if (disconnect == null)
                            {
                                messageFIX = (MessageFIXT1_1)message;
                            }
                            else
                            {
                                OnDisconnect(disconnect.Socket);
                            }
                        }
                    }

                    if (messageFIX != null)
                    {
                        if (fixTrace)
                            LogMessage(messageFIX.ToString(), true);
                        if (debug) log.Debug("Received FIX Message: " + messageFIX);
                        if (messageFIX.MessageType == "A")
                        {
                            if (messageFIX.Sequence == 1 || messageFIX.Sequence < remoteSequence)
                            {
                                remoteSequence = messageFIX.Sequence;
                                if (debug) log.Debug("FIX Server login message sequence was lower than expected. Resetting to " + remoteSequence);
                                orderStore.LastSequenceReset = new TimeStamp(messageFIX.SendUtcTime);
                            }
                            if (HandleLogon(messageFIX))
                            {
                                ConnectionStatus = Status.PendingServerResend;
                                TryEndRecovery();
                            }
                        }
                    }

                    if (messageFIX != null)
                    {
                        lastMessageTime = TimeStamp.UtcNow;
                        //switch (messageFIX.MessageType)
                        //{
                        //    case "2":
                        //        break;
                        //}
                        var releaseFlag = true;
                        var message4_4 = messageFIX as MessageFIX4_4;
                        // Workaround for bug in MBT FIX Server that sends message from prior to 
                        // last sequence number reset.
                        if (messageFIX.IsPossibleDuplicate &&
                            message4_4 != null && message4_4.OriginalSendTime != null &&
                            new TimeStamp(message4_4.OriginalSendTime) < OrderStore.LastSequenceReset)
                        {
                            log.Info("Ignoring message with sequence " + messageFIX.Sequence + " as work around to MBT FIX server defect because original send time (122) " + new TimeStamp(message4_4.OriginalSendTime) + " is prior to last sequence reset " + OrderStore.LastSequenceReset);
                        }
                        else
                        {

                            if (!CheckForMissingMessages(messageFIX, out releaseFlag))
                            {
                                switch (messageFIX.MessageType)
                                {
                                    case "A": //logon
                                        // Already handled and sequence incremented.
                                        break;
                                    case "2": // resend
                                        HandleResend(messageFIX);
                                        break;
                                    case "4": // gap fill
                                        HandleGapFill(messageFIX);
                                        break;
                                    case "3": // reject
                                        HandleReject(messageFIX);
                                        break;
                                    case "5": // log off confirm
                                        if (debug) log.Debug("Logout message received.");
                                        if (ConnectionStatus == Status.PendingLogOut)
                                        {
                                            Dispose();
                                        }
                                        else
                                        {
                                            HandleUnexpectedLogout(messageFIX);
                                        }
                                        break;
                                    default:
                                        ReceiveMessage(messageFIX);
                                        break;
                                }
                                orderStore.UpdateRemoteSequence(remoteSequence);
                            }
                        }
                        if (releaseFlag)
                        {
                            Socket.MessageFactory.Release(messageFIX);
                        }
                        orderStore.IncrementUpdateCount();
                        IncreaseHeartbeatTimeout();
                        return Yield.DidWork.Repeat;
                    }
                    else
                    {
                        return Yield.NoWork.Repeat;
                    }
                case Status.Disconnected:
                    return Yield.NoWork.Repeat;
                default:
                    throw new InvalidOperationException("Unknown connection status: " + ConnectionStatus);
            }
        }

        protected virtual void HandleUnexpectedLogout(MessageFIXT1_1 message) {
            socket.Dispose();
        }

        protected virtual bool HandleLogon(MessageFIXT1_1 message)
        {
            throw new NotImplementedException();
        }

        private void HandleGapFill(MessageFIXT1_1 packetFIX)
        {
            if (!packetFIX.IsGapFill)
            {
                throw new InvalidOperationException("Only gap fill sequence reset supportted: \n" + packetFIX);
            }
            if (packetFIX.NewSeqNum < RemoteSequence)
            {
                throw new InvalidOperationException("Reset new sequence number must be greater than or equal to the next sequence: " + RemoteSequence + ".\n" + packetFIX);
            }
            RemoteSequence = packetFIX.NewSeqNum;
            if (packetFIX.Sequence > RemoteSequence)
            {
                HandleResend(packetFIX.Sequence, packetFIX);
            }
            if (debug) log.Debug("Received gap fill. Setting next sequence = " + RemoteSequence);
        }

        private void HandleReject(MessageFIXT1_1 packetFIX)
        {
            if (packetFIX.Text.Contains("Sending Time Accuracy problem"))
            {
                if (debug) log.Debug("Found Sending Time Accuracy message request for message: " + packetFIX.ReferenceSequence );
                FIXTMessage1_1 textMessage;
                if (!fixFactory.TryGetHistory(packetFIX.ReferenceSequence, out textMessage))
                {
                    log.Warn("Unable to find message " + packetFIX.ReferenceSequence + ". This is probably due to being prior to recent restart. Ignoring.");
                }
                else
                {
                    if( debug) log.Debug("Sending Time Accuracy Problem -- Resending Message.");
                    textMessage.Sequence = fixFactory.GetNextSequence();
                    textMessage.SetDuplicate(true);
                    SendMessageInternal( textMessage);
                }
                return;
            }
            else
            {
                if (debug) log.Debug("Starting history dump");
                if ( debug) DumpHistory();
                if (debug) log.Debug("Fiinished history dump");
                throw new ApplicationException("Received administrative reject message with unrecognized error message: '" + packetFIX.Text + "'");
            }
        }

        private bool CheckFailedLoginFile()
	    {
            return File.Exists(failedFile);
        }

	    private int expectedResendSequence;
	    private bool isResendComplete = true;

        private bool CheckForMissingMessages(MessageFIXT1_1 messageFIX, out bool releaseFlag)
        {
            releaseFlag = true;
            if( messageFIX.MessageType == "A")
            {
                if (messageFIX.Sequence == RemoteSequence)
                {
                    RemoteSequence = messageFIX.Sequence + 1;
                    if (debug) log.Debug("Login sequence matched. Incrementing remote sequence to " + RemoteSequence);
                }
                else
                {
                    if (debug) log.Debug("Login remote sequence " + messageFIX.Sequence + " mismatch expected sequence " + RemoteSequence + ". Resend needed.");
                }
                return false;
            }
            else if (ConnectionStatus == Status.PendingLogin && messageFIX.MessageType != "5")
            {
                resendQueue.Enqueue(messageFIX, TimeStamp.UtcNow.Internal);
                releaseFlag = false;
                return true;
            }
            else if (messageFIX.Sequence > RemoteSequence)
            {
                HandleResend(messageFIX.Sequence, messageFIX);
                releaseFlag = false;
                return true;
            }
            else if( messageFIX.Sequence < RemoteSequence)
            {
                if (debug) log.Debug("Already received sequence " + messageFIX.Sequence + ". Expecting " + RemoteSequence + " as next sequence. Ignoring. \n" + messageFIX);
                return true;
            }
            else
            {
                if (!IsResendComplete && messageFIX.Sequence >= expectedResendSequence)
                {
                    IsResendComplete = true;
                    TryEndRecovery();
                }
                RemoteSequence = messageFIX.Sequence + 1;
                if( debug) log.Debug("Incrementing remote sequence to " + RemoteSequence);
                return false;
            }
        }

        private void HandleResend(int sequence, MessageFIXT1_1 messageFIX) {
            if (debug) log.Debug("Sequence is " + sequence + " but expected sequence is " + RemoteSequence + ". Buffering message.");
            resendQueue.Enqueue(messageFIX, TimeStamp.UtcNow.Internal);
            TryRequestResend(remoteSequence,sequence - 1);
        }

        private void TryRequestResend(int from, int to)
        {
            if (IsResendComplete)
            {
                IsResendComplete = false;
                expectedResendSequence = to;
                if (debug) log.Debug("Expected resend sequence set to " + expectedResendSequence);
                var mbtMsg = fixFactory.Create();
                mbtMsg.AddHeader("2");
                mbtMsg.SetBeginSeqNum(from);
                mbtMsg.SetEndSeqNum(to);
                if (verbose) log.Verbose(" Sending resend request: " + mbtMsg);
                SendMessage(mbtMsg);
            }
        }

        private FIXTMessage1_1 GapFillMessage(int currentSequence, int newSequence)
        {
            var endText = newSequence == 0 ? "infinity" : newSequence.ToString();
            if (debug) log.Debug("Sending gap fill message " + currentSequence + " to " + endText);
            var message = fixFactory.Create(currentSequence);
            message.SetGapFill();
            message.SetNewSeqNum(newSequence);
            message.AddHeader("4");
            return message;
        }


	    protected abstract void ResendOrder(CreateOrChangeOrder order);


        private bool HandleResend(Message message)
        {
			var messageFIX = (MessageFIXT1_1) message;
			int end = messageFIX.EndSeqNum == 0 ? fixFactory.LastSequence : messageFIX.EndSeqNum;
            if (messageFIX.BegSeqNum == 1)
            {
                // Resend request was from 1. This means that the server restarted the sequence numbers.
                // So we don't want to resend the active orders, simply fill the gap.
                var textMessage = GapFillMessage(messageFIX.BegSeqNum, end + 1);
                SendMessageInternal(textMessage);
                return true;
            }
            if (debug) log.Debug("Found resend request for " + messageFIX.BegSeqNum + " to " + end + ": " + messageFIX);
            if (messageFIX.BegSeqNum <= end)
            {
                var previous = messageFIX.BegSeqNum;
                for( var i = previous; i<=end; i++)
                {
                    CreateOrChangeOrder order;
                    FIXTMessage1_1 missingMessage;
                    var sentFlag = false;
                    if( orderStore.TryGetOrderBySequence( i, out order))
                    {
                        if( previous < i)
                        {
                            missingMessage = GapFillMessage(previous, i);
                            SendMessageInternal(missingMessage);
                        }
                        ResendOrder(order);
                        sentFlag = true;
                    }
                    else if( fixFactory.TryGetHistory( i, out missingMessage))
                    {
                        missingMessage.SetDuplicate(true);
                        switch (missingMessage.Type)
                        {
                            case "g":
                            case "5": // Logoff
                                if (previous < i)
                                {
                                    var gapMessage = GapFillMessage(previous, i);
                                    SendMessageInternal(gapMessage);
                                }
                                SendMessageInternal(missingMessage);
                                sentFlag = true;
                                break;
                            case "A":
                            case "2":
                            case "0":
                            case "AN":
                            case "G":
                            case "F":
                                break;
                            default:
                                log.Warn("Message type " + missingMessage.Type + " skipped during resend: " + missingMessage);
                                break;
                        }
                    }
                    if( sentFlag)
                    {
                        previous = i + 1;
                    }
                }
                if( previous < end)
                {
                    var textMessage = GapFillMessage(previous, end + 1);
                    SendMessageInternal(textMessage);
                }
            }
            return true;

            //var firstSequence = fixFactory.FirstSequence;
            //var lastSequence = fixFactory.LastSequence;
            //FIXTMessage1_1 textMessage;
            //if (messageFIX.BegSeqNum < firstSequence)
            //{
            //    textMessage = GapFillMessage(messageFIX.BegSeqNum, firstSequence);
            //    SendMessageInternal(textMessage);
            //}
            //int first = messageFIX.BegSeqNum < firstSequence ? firstSequence : messageFIX.BegSeqNum;
            //for( int i = first; i <= end; i++) {
            //    if( !fixFactory.TryGetHistory(i,out textMessage))
            //    {
            //        textMessage = GapFillMessage(i);
            //    }
            //    else
            //    {
            //        switch (textMessage.Type)
            //        {
            //            case "A": // Logon
            //            case "5": // Logoff
            //            case "0": // Heartbeat
            //            case "1": // Heartbeat
            //            case "2": // Resend request.
            //            case "4": // Reset sequence.
            //                textMessage = GapFillMessage(i);
            //                break;
            //            default:
            //                textMessage.SetDuplicate(true);
            //                if (debug) log.Debug("Resending message " + i + "...");
            //                break;
            //        }
            //    }
            //    SendMessageInternal(textMessage);
            //}
            //if( messageFIX.EndSeqNum > lastSequence)
            //{
            //    textMessage = GapFillMessage(lastSequence + 1, messageFIX.EndSeqNum);
            //    SendMessageInternal(textMessage);
            //}
            //isResendComplete = true;  // Force resending any "resend requests".
            //return true;
        }

		protected void IncreaseHeartbeatTimeout()
		{
		    var heartbeatTime = TimeStamp.UtcNow;
		    heartbeatTime.AddSeconds(heartbeatDelay);
			heartbeatTimer.Start(heartbeatTime);
		}

        protected abstract void ReceiveMessage(Message message);
		
		private void OnException( Exception ex) {
			// Attempt to propagate the exception.
			log.Error("Exception occurred: ", ex);
			SendError( ex.Message);
            Dispose();
		}
		
        public void Stop(EventItem eventItem) {
        	
        }

        public void StartSymbol(EventItem eventItem)
        {
        	log.Info("StartSymbol( " + eventItem.Symbol + ")");
        	// This adds a new order handler.
            TryAddSymbol(eventItem.Symbol,eventItem.Agent);
            using( orderStore.BeginTransaction())
            {
                OnStartSymbol(eventItem.Symbol);
            }
        }
        
        public abstract void OnStartSymbol( SymbolInfo symbol);
        
        public void StopSymbol(EventItem eventItem)
        {
        	log.Info("StopSymbol( " + eventItem.Symbol + ")");
            if (TryRemoveSymbol(eventItem.Symbol))
            {
                OnStopSymbol(eventItem.Symbol);
        	}
        }
        
        public abstract void OnStopSymbol(SymbolInfo symbol);
	        
        private void LoadProperties(string configFilePath) {
	        log.Notice("Using section " + configSection + " in file: " + configFilePath);
	        var configFile = new ConfigFile(configFilePath);
        	configFile.AssureValue("EquityDemo/UseLocalFillTime","true");
            configFile.AssureValue("EquityDemo/ServerAddress", "127.0.0.1");
            configFile.AssureValue("EquityDemo/ServerPort", "5679");
        	configFile.AssureValue("EquityDemo/UserName","CHANGEME");
        	configFile.AssureValue("EquityDemo/Password","CHANGEME");
        	configFile.AssureValue("EquityDemo/AccountNumber","CHANGEME");
            configFile.AssureValue("EquityDemo/SessionIncludes", "*");
            configFile.AssureValue("EquityDemo/SessionExcludes", "");
            configFile.AssureValue("EquityDemo/Destination", "7");
            configFile.AssureValue("ForexDemo/UseLocalFillTime", "true");
        	configFile.AssureValue("ForexDemo/ServerAddress","127.0.0.1");
        	configFile.AssureValue("ForexDemo/ServerPort","5679");
        	configFile.AssureValue("ForexDemo/UserName","CHANGEME");
        	configFile.AssureValue("ForexDemo/Password","CHANGEME");
        	configFile.AssureValue("ForexDemo/AccountNumber","CHANGEME");
            configFile.AssureValue("ForexDemo/SessionIncludes", "*");
            configFile.AssureValue("ForexDemo/SessionExcludes", "");
            configFile.AssureValue("EquityLive/UseLocalFillTime", "true");
        	configFile.AssureValue("EquityLive/ServerAddress","127.0.0.1");
        	configFile.AssureValue("EquityLive/ServerPort","5680");
        	configFile.AssureValue("EquityLive/UserName","CHANGEME");
        	configFile.AssureValue("EquityLive/Password","CHANGEME");
        	configFile.AssureValue("EquityLive/AccountNumber","CHANGEME");
            configFile.AssureValue("EquityLive/SessionIncludes", "*");
            configFile.AssureValue("EquityLive/SessionExcludes", "");
            configFile.AssureValue("EquityLive/Destination", "7");
            configFile.AssureValue("ForexLive/UseLocalFillTime", "true");
        	configFile.AssureValue("ForexLive/ServerAddress","127.0.0.1");
        	configFile.AssureValue("ForexLive/ServerPort","5680");
        	configFile.AssureValue("ForexLive/UserName","CHANGEME");
        	configFile.AssureValue("ForexLive/Password","CHANGEME");
        	configFile.AssureValue("ForexLive/AccountNumber","CHANGEME");
            configFile.AssureValue("ForexLive/SessionIncludes", "*");
            configFile.AssureValue("ForexLive/SessionExcludes", "");
            configFile.AssureValue("Simulate/UseLocalFillTime", "false");
            configFile.AssureValue("Simulate/ServerAddress", "127.0.0.1");
            configFile.AssureValue("Simulate/ServerPort", "6489");
            configFile.AssureValue("Simulate/UserName", "Simulate1");
            configFile.AssureValue("Simulate/Password", "only4sim");
            configFile.AssureValue("Simulate/AccountNumber", "11111111");
            configFile.AssureValue("Simulate/SessionIncludes", "*");
            configFile.AssureValue("Simulate/SessionExcludes", "");
            configFile.AssureValue("ClientTest/UseLocalFillTime", "false");
            configFile.AssureValue("ClientTest/ServerAddress", "127.0.0.1");
            configFile.AssureValue("ClientTest/ServerPort", "6489");
            configFile.AssureValue("ClientTest/UserName", "Simulate1");
            configFile.AssureValue("ClientTest/Password", "only4sim");
            configFile.AssureValue("ClientTest/AccountNumber", "11111111");
            configFile.AssureValue("ClientTest/SessionIncludes", "*");
            configFile.AssureValue("ClientTest/SessionExcludes", "");
            configFile.AssureValue("MarketTest/UseLocalFillTime", "false");
            configFile.AssureValue("MarketTest/ServerAddress", "127.0.0.1");
            configFile.AssureValue("MarketTest/ServerPort", "6489");
            configFile.AssureValue("MarketTest/UserName", "Simulate1");
            configFile.AssureValue("MarketTest/Password", "only4sim");
            configFile.AssureValue("MarketTest/AccountNumber", "11111111");
            configFile.AssureValue("MarketTest/SessionIncludes", "*");
            configFile.AssureValue("MarketTest/SessionExcludes", "");
			ParseProperties(configFile);
		}

        private void ParseProperties(ConfigFile configFile)
        {
			var value = GetField("UseLocalFillTime",configFile, false);
			if( !string.IsNullOrEmpty(value)) {
				useLocalFillTime = value.ToLower() != "false";
        	}
			
        	AddrStr = GetField("ServerAddress",configFile, true);
        	var portStr = GetField("ServerPort",configFile, true);
			if( !ushort.TryParse(portStr, out port)) {
				Exception( "ServerPort", configFile);
			}
			userName = GetField("UserName",configFile, true);
			password = GetField("Password",configFile, true);
			accountNumber = GetField("AccountNumber",configFile, true);
            destination = GetField("Destination", configFile, false);
            var includeString = GetField("SessionIncludes", configFile, false);
            var excludeString = GetField("SessionExcludes", configFile, false);
            sessionMatcher = new IncludeExcludeMatcher(includeString, excludeString);
        }

        private IncludeExcludeMatcher sessionMatcher;
        
        private string GetField( string field, ConfigFile configFile, bool required) {
			var result = configFile.GetValue(configSection + "/" + field);
			if( required && string.IsNullOrEmpty(result)) {
				Exception( field, configFile);
			}
			return result;
        }

        public bool CompareSession( string session)
        {
            return sessionMatcher.Compare(session);
        }
        
        //TODO: Needs to change to reflect other providers
        private void Exception( string field, ConfigFile configFile) {
        	var sb = new StringBuilder();
        	sb.AppendLine("Sorry, an error occurred finding the '" + field +"' setting.");
        	sb.AppendLine("Please either set '" + field +"' in section '"+configSection+"' of '"+configFile+"'.");
            sb.AppendLine("Otherwise you may choose a different section within the config file.");
            sb.AppendLine("You can choose the section either in your project.tzproj file or");
            sb.AppendLine("if you run a standalone ProviderService, in the ProviderServer\\Default.config file.");
            sb.AppendLine("In either case, you may set the ProviderAssembly value as <AssemblyName>/<Section>");
            sb.AppendLine("For example, MBTFIXProvider/EquityDemo will choose the MBTFIXProvider.exe assembly");
            sb.AppendLine("with the EquityDemo section within the MBTFIXProvider\\Default.config file for that assembly.");
            throw new ApplicationException(sb.ToString());
        }
        
		private string UpperFirst(string input)
		{
			string temp = input.Substring(0, 1);
			return temp.ToUpper() + input.Remove(0, 1);
		}        
		
		public void SendError(string error) {
			if( ClientAgent!= null) {
				ErrorDetail detail = new ErrorDetail();
				detail.ErrorMessage = error;
				log.Error(detail.ErrorMessage);
			}
		}
		
		public bool GetSymbolStatus(SymbolInfo symbol) {
			lock( symbolsRequestedLocker) {
				return symbolsRequested.ContainsKey(symbol.BinaryIdentifier);
			}
		}
		
		private bool TryAddSymbol(SymbolInfo symbol, Agent agent) {
			lock( symbolsRequestedLocker) {
				if( !symbolsRequested.ContainsKey(symbol.BinaryIdentifier)) {
					symbolsRequested.Add(symbol.BinaryIdentifier, new SymbolReceiver { Symbol = symbol, Agent = agent});
					return true;
				}
			}
			return false;
		}
		
		private bool TryRemoveSymbol(SymbolInfo symbol) {
			lock( symbolsRequestedLocker) {
				if( symbolsRequested.ContainsKey(symbol.BinaryIdentifier)) {
					symbolsRequested.Remove(symbol.BinaryIdentifier);
					return true;
				}
			}
			return false;
		}

        private bool isFinalized;

	 	protected volatile bool isDisposed = false;
        protected readonly object orderAlgorithmsLocker = new object();
        protected Dictionary<long, SymbolAlgorithm> orderAlgorithms = new Dictionary<long, SymbolAlgorithm>();

	    public void Dispose() 
	    {
	        Dispose(true);
	        GC.SuppressFinalize(this);      
	    }
	
	    protected virtual void Dispose(bool disposing)
	    {
       		if( !isDisposed) {
	            isDisposed = true;   
	            if (disposing) {
	            	if( debug) log.Debug("Dispose()");
                    if (socketTask != null)
                    {
		            	socketTask.Stop();
                        socketTask.Join();
	            	}
                    if (socket != null)
                    {
                        socket.Dispose();
                    }
                    if( heartbeatTimer != null)
                    {
                        heartbeatTimer.Dispose();
                    }
                    if (retryTimer != null)
                    {
                        retryTimer.Dispose();
                    }
                    if (orderStore != null)
                    {
                        orderStore.Dispose();
                    }
	                isFinalized = true;
	            }
    		}
	    }    

	    public void SendMessage(FIXTMessage1_1 fixMsg) {
            if( !fixMsg.IsDuplicate)
            {
                fixFactory.AddHistory(fixMsg);
            }
            SendMessageInternal(fixMsg);
            OrderStore.UpdateLocalSequence(FixFactory.LastSequence);
        }
		
	    private void SendMessageInternal(FIXTMessage1_1 fixMsg) {
			var fixString = fixMsg.ToString();
            if (fixTrace)
                LogMessage(fixString, false);
            else if (debug)
            {
				string view = fixString.Replace(FIXTBuffer.EndFieldStr,"  ");
				log.Debug("Send FIX message: \n" + view);
			}

	        var packet = Socket.MessageFactory.Create();
	        packet.SendUtcTime = TimeStamp.UtcNow.Internal;
			packet.DataOut.Write(fixString.ToCharArray());
			var end = Factory.Parallel.TickCount + (long)heartbeatDelay * 1000L;
			while( !Socket.TrySendMessage(packet)) {
				if( IsInterrupted) return;
				if( Factory.Parallel.TickCount > end) {
					throw new ApplicationException("Timeout while sending message.");
				}
				Factory.Parallel.Yield();
			}
            lastMessageTime = TimeStamp.UtcNow;
        }

        protected virtual void TryEndRecovery()
        {
            throw new NotImplementedException();
        }

        public void LogOut()
        {
            if (bestConnectionStatus != Status.Recovered)
            {
                Dispose();
                return;
            }
            if(!isDisposed)
            {
                if( debug) log.Debug("LogOut() status " + ConnectionStatus);
                switch( ConnectionStatus)
                {
                    case Status.Connected:
                    case Status.Disconnected:
                    case Status.New:
                    case Status.None:
                    case Status.PendingLogin:
                    case Status.PendingRetry:
                        Dispose();
                        break;
                    case Status.PendingLogOut:
                        break;
                    case Status.Recovered:
                    case Status.PendingServerResend:
                    case Status.PendingRecovery:
                        ConnectionStatus = Status.PendingLogOut;
                        using (orderStore.BeginTransaction())
                        {
                            if (debug) log.Debug("Calling OnLogOut()");
                            OnLogout();
                        }
                        break;
                    default:
                        throw new ApplicationException("Unexpected connection status for log out: " + ConnectionStatus);
                }
            }
        }

        public Socket Socket
        {
			get { return socket; }
		}
		
		public string AddrStr {
			get { return addrStr; }
			set { addrStr = value; }
		}
		
		public ushort Port {
			get { return port; }
			set { port = value; }
		}
		
		public string UserName {
			get { return userName; }
			set { userName = value; }
		}
		
		public string Password {
			get { return password; }
			set { password = value; }
		}
		
		public string ProviderName {
			get { return providerName; }
			set { providerName = value; }
		}
		
		public long RetryIncrease {
			get { return retryIncrease; }
			set { retryIncrease = value; }
		}
		
		public long RetryMaximum {
			get { return retryMaximum; }
			set { retryMaximum = value; }
		}
		
		public long HeartbeatDelay {
	    	get { return heartbeatDelay; }
			set {
                heartbeatDelay = value;
				IncreaseHeartbeatTimeout();
			}
		}
		
		public bool LogRecovery {
			get { return logRecovery; }
		}

        public string Destination
        {
            get { return destination; }
        }

		public string AccountNumber {
			get { return accountNumber; }
		}
		
		public FIXTFactory FixFactory {
			get { return fixFactory; }
			set { fixFactory = value; }
		}

	    public int RemoteSequence
	    {
	        get { return remoteSequence; }
	        set { remoteSequence = value; }
	    }

	    public PhysicalOrderStore OrderStore
	    {
	        get { return orderStore; }
	    }

	    public bool IsResendComplete
	    {
	        get { return isResendComplete; }
	        set
	        {
	            if( isResendComplete != value)
	            {
                    if (debug) log.Debug("Resend Complete changed to " + value);
	                isResendComplete = value;
	            }
	        }
	    }

        public long RetryDelay
        {
            get { return retryDelay; }
            set { retryDelay = value; }
        }

        public Status ConnectionStatus
        {
            get { return connectionStatus; }
            set
            {
                if( connectionStatus != value)
                {
                    if (debug) log.Debug("ConnectionStatus changed from " + connectionStatus + " to " + value);
                    connectionStatus = value;
                }
            }
        }

        public bool IsFinalized
        {
            get { return isFinalized; }
        }

        public long RetryStart
        {
            get { return retryStart; }
            set { retryStart = value; }
        }

        private unsafe void LogMessage(string messageFIX, bool received)
        {
            if (fixTrace)
                fixLog.TraceFormat("{0}: {1}", received ? "RCV" : "SND", messageFIX);
    }

        protected class SymbolAlgorithm
        {
            public OrderAlgorithm OrderAlgorithm;
        }

        protected SymbolAlgorithm GetAlgorithm(long symbol)
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

        protected bool TryGetAlgorithm(long symbol, out SymbolAlgorithm algorithm)
        {
            lock (orderAlgorithmsLocker)
            {
                return orderAlgorithms.TryGetValue(symbol, out algorithm);
            }
        }

        protected void TrySend(EventType type, SymbolInfo symbol, Agent agent)
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

        protected void TryRequestPosition(SymbolInfo symbol)
        {
            SymbolReceiver symbolReceiver;
            if (!symbolsRequested.TryGetValue(symbol.BinaryIdentifier, out symbolReceiver))
            {
                throw new InvalidOperationException("Can't find symbol request for " + symbol);
            }
            if (!IsRecovered)
            {
                if (debug) log.Debug("Attempted RequestPosition but IsRecovered is " + IsRecovered);
                return;
            }
            var symbolAlgorithm = GetAlgorithm(symbol.BinaryIdentifier);
            if (symbolAlgorithm.OrderAlgorithm.IsBrokerOnline)
            {
                if (debug) log.Debug("Attempted RequestPosition but isBrokerStarted is " + symbolAlgorithm.OrderAlgorithm.IsBrokerOnline);
                return;
            }
            TrySend(EventType.RequestPosition, symbol, symbolReceiver.Agent);
        }

        protected void TrySendStartBroker(SymbolInfo symbol, string message)
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
            var algorithm = GetAlgorithm(symbol.BinaryIdentifier);
            if (algorithm.OrderAlgorithm.ReceivedDesiredPosition)
            {
                if (algorithm.OrderAlgorithm.IsSynchronized)
                {
                    if (algorithm.OrderAlgorithm.IsBrokerOnline)
                    {
                        if (debug) log.Debug("Attempted StartBroker but isBrokerStarted is already " + algorithm.OrderAlgorithm.IsBrokerOnline);
                        return;
                    }
                    if (debug) log.Debug("Sending StartBroker for " + symbol + ". Reason: " + message);
                    TrySend(EventType.StartBroker, symbol, symbolReceiver.Agent);
                    algorithm.OrderAlgorithm.IsBrokerOnline = true;
                }
                else
                {
                    if (debug) log.Debug("Attempted StartBroker but OrderAlgorithm not yet synchronized");
                }
            }
            else
            {
                TryRequestPosition(symbol);
            }
        }

        public void TrySendEndBroker() {

            lock (symbolsRequestedLocker)
            {
                foreach(var kvp in symbolsRequested) {
                    var symbolReceiver = kvp.Value;
                    if (!symbolReceiver.Symbol.DisableRealtimeSimulation)
                        TrySendEndBroker(symbolReceiver.Symbol);
                }
            }
        }

        protected void TrySendEndBroker( SymbolInfo symbol)
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
            if (debug) log.Debug("Sent EndBroker for " + symbol + ".");
        }

        protected int GetOrderSide( OrderSide side) {
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

        public virtual void PositionChange(PositionChangeDetail positionChange)
        {
            var symbol = positionChange.Symbol;
            if( debug) log.Debug( "PositionChange " + positionChange);
            var algorithm = GetAlgorithm(symbol.BinaryIdentifier);
            if( algorithm.OrderAlgorithm.PositionChange(positionChange, IsRecovered))
            {
                if( algorithm.OrderAlgorithm.RejectRepeatCounter == 0)
                {
                    TrySendStartBroker(symbol, "position change sync");
                }
            }
        }

        protected int SideToSign( string side) {
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

        public virtual void OnLogout()
        {
            if (isDisposed)
            {
                if (debug) log.Debug("LimeProvider.OnLogOut() already disposed.");
                return;
            }
            var limeMessage = FixFactory.Create();
            limeMessage.AddHeader("5");
            SendMessage(limeMessage);
            log.Info("Logout message sent: " + limeMessage);
            log.Info("Logging out -- Sending EndBroker event.");
            TrySendEndBroker();
        }

        protected virtual void OnStartRecovery()
        {
            if( !LogRecovery) {
                MessageFIXT1_1.IsQuietRecovery = true;
            }
            CancelRecovered();
            TryEndRecovery();
        }

        protected virtual void OnFinishRecovery()
        {
        }
    }
}
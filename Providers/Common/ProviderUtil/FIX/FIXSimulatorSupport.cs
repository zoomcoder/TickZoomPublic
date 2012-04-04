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
using System.Diagnostics;
using System.Text;
using System.Threading;
using TickZoom.Api;

namespace TickZoom.FIX
{
    public abstract class FIXSimulatorSupport : FIXSimulator, LogAware
	{
		private string localAddress = "0.0.0.0";
		private static Log log = Factory.SysLog.GetLogger(typeof(FIXSimulatorSupport));
        private volatile bool debug;
        private volatile bool trace;
        private volatile bool verbose;
        public virtual void RefreshLogLevel()
        {
            debug = log.IsDebugEnabled;
            trace = log.IsTraceEnabled;
            verbose = log.IsVerboseEnabled;
        }

        private ProviderSimulatorSupport providerSimulator;
        private FIXTFactory1_1 fixFactory;
		private long realTimeOffset;
		private object realTimeOffsetLocker = new object();
		private YieldMethod MainLoopMethod;
        private int heartbeatDelay = 1;
        //private int heartbeatDelay = int.MaxValue;
        private ServerState fixState = ServerState.Startup;
        private readonly int maxFailures = 5;
        private bool allTests;
        private bool simulateReceiveFailed;
        private bool simulateSendFailed;

        private bool isConnectionLost = false;

		// FIX fields.
		private ushort fixPort = 0;
		private Socket fixListener;
		protected Socket fixSocket;
		private Message _fixReadMessage;
		private Message _fixWriteMessage;
		private Task task;
		private bool isFIXSimulationStarted = false;
        private MessageFactory currentMessageFactory;
		private FastQueue<Message> fixPacketQueue = Factory.Parallel.FastQueue<Message>("SimulatorFIX");
        private QueueFilter filter;
        private int frozenHeartbeatCounter;

        private TrueTimer heartbeatTimer;
        private TimeStamp isHeartbeatPending = TimeStamp.MaxValue;
        private Agent agent;
        private bool isResendComplete = true;
        public Agent Agent
        {
            get { return agent; }
            set { agent = value; }
        }

        public Dictionary<SimulatorType,SimulatorInfo> simulators = new Dictionary<SimulatorType, SimulatorInfo>();

        public FIXSimulatorSupport(string mode, ProjectProperties projectProperties, ProviderSimulatorSupport providerSimulator, ushort fixPort, MessageFactory createMessageFactory)
        {
		    this.fixPort = fixPort;
            this.providerSimulator = providerSimulator;
		    var randomSeed = new Random().Next(int.MaxValue);
            if (heartbeatDelay > 1)
            {
                log.Error("Heartbeat delay is " + heartbeatDelay);
            }

		    if (randomSeed != 1234)
		    {
		        Console.WriteLine("Random seed for fix simulator:" + randomSeed);
		        log.Info("Random seed for fix simulator:" + randomSeed);
		    }
		    random = new Random(randomSeed);
		    log.Register(this);
		    switch (mode)
		    {
		        case "PlayBack":
		            break;
                default:
		            break;
		    }

            allTests = projectProperties.Simulator.EnableNegativeTests;

            foreach (SimulatorType simulatorType in Enum.GetValues(typeof(SimulatorType)))
            {
                var simulator = new SimulatorInfo(simulatorType, random, () => ProviderSimulator.Count);
                simulator.Enabled = false;
                simulator.MaxFailures = maxFailures;
                simulators.Add(simulatorType, simulator);
            }
            simulators[SimulatorType.BlackHole].Enabled = allTests;   // Passed individually.
            simulators[SimulatorType.CancelBlackHole].Enabled = allTests;   // Passed individually.
            simulators[SimulatorType.RejectSymbol].Enabled = allTests;   // Passed individually.
            simulators[SimulatorType.RejectAll].Enabled = false;
            simulateReceiveFailed = allTests;    // Passed individually.
            simulateSendFailed = allTests;      // Passed individually.
            simulators[SimulatorType.SendServerOffline].Enabled = allTests; // Passed individually.
            simulators[SimulatorType.ReceiveServerOffline].Enabled = allTests;  // Passed individually.
            simulators[SimulatorType.ServerOfflineReject].Enabled = allTests;  // Passed individually.
            simulators[SimulatorType.SendDisconnect].Enabled = allTests;        // Passed individually.
            simulators[SimulatorType.ReceiveDisconnect].Enabled = allTests;     // Passed individually.
            simulators[SimulatorType.SystemOffline].Enabled = allTests;     // Passed individually.

            {
                simulators[SimulatorType.ReceiveServerOffline].Frequency = 10;
                simulators[SimulatorType.SendServerOffline].Frequency = 20;
                simulators[SimulatorType.SendDisconnect].Frequency = 15;
                simulators[SimulatorType.ReceiveDisconnect].Frequency = 8;
                simulators[SimulatorType.CancelBlackHole].Frequency = 10;
                simulators[SimulatorType.BlackHole].Frequency = 10;
                simulators[SimulatorType.SystemOffline].Frequency = 10;

                simulators[SimulatorType.ServerOfflineReject].Frequency = 10;

                var simulator = simulators[SimulatorType.RejectSymbol];
                simulator.Frequency = 10;
                simulator.MaxRepetitions = 1;
            }

            foreach( var kvp in projectProperties.Simulator.NegativeSimulatorMinimums)
            {
                var type = kvp.Key;
                var minimum = kvp.Value;
                if( minimum == 0)
                {
                    simulators[type].Minimum = minimum;
                    simulators[type].Enabled = false;
                }
                else
                {
                    simulators[type].Minimum = minimum;
                }
            }

            this.currentMessageFactory = createMessageFactory;
        }

        public void Initialize(Task task)
        {
            this.task = task;
            filter = task.GetFilter();
            task.Scheduler = Scheduler.EarliestTime;
            fixPacketQueue.ConnectInbound(task);
            heartbeatTimer = Factory.Parallel.CreateTimer("Heartbeat", task, HeartbeatTimerEvent);
            IncreaseHeartbeat();
            task.Start();
            ListenToFIX();
            MainLoopMethod = Invoke;
            if (debug) log.Debug("Starting FIX Simulator.");
            if (allTests)
            {
                foreach( var kvp in simulators)
                {
                    var simulator = kvp.Value;
                    if( !simulator.Enabled && simulator.Minimum > 0)
                    {
                        log.Error(simulator + " is disabled");
                    }
                }
                if (!simulateReceiveFailed)
                {
                    log.Error("SimulateReceiveFailed is disabled.");
                }
                if (!simulateSendFailed)
                {
                    log.Error("SimulateSendFailed is disabled.");
                }
            }
        }

        public void DumpHistory()
        {
            for (var i = 0; i <= FixFactory.LastSequence; i++)
            {
                FIXTMessage1_1 message;
                if( FixFactory.TryGetHistory(i, out message))
                {
                    log.Info(message.ToString());
                }
            }
        }

        private void ListenToFIX()
		{
            fixListener = Factory.Provider.Socket(typeof(FIXSimulatorSupport).Name, localAddress, fixPort);
			fixListener.Bind();
			fixListener.Listen( 5);
			fixListener.OnConnect = OnConnectFIX;
			fixPort = fixListener.Port;
			log.Info("Listening for FIX to " + localAddress + " on port " + fixPort);
		}

		protected virtual void OnConnectFIX(Socket socket)
		{
			fixSocket = socket;
            fixState = ServerState.Startup;
		    fixSocket.MessageFactory = currentMessageFactory;
			log.Info("Received FIX connection: " + socket);
			StartFIXSimulation();
			fixSocket.ReceiveQueue.ConnectInbound( task);
            fixSocket.SendQueue.ConnectOutbound(task);
		}

        private Yield HeartbeatTimerEvent()
        {
            if (isDisposed) return Yield.Terminate;
            var currentTime = TimeStamp.UtcNow;
            if (verbose) log.Verbose("Heartbeat occurred at " + currentTime);
            if (isConnectionLost)
            {
                if (debug) log.Debug("FIX connection was lost, closing FIX socket.");
                CloseFIXSocket();
                return Yield.NoWork.Repeat;
            }
            if (fixState != ServerState.Startup)
            {
                var now = TimeStamp.UtcNow;
                if( now > isHeartbeatPending)
                {
                    log.Error("HeartBeat response was never received.\n" + Factory.Parallel.GetStats());
                    log.Error("All stack traces follow...");
                    Factory.Parallel.StackTrace();
                    throw new ApplicationException("HeartBeat response was never received.");
                }
                isHeartbeatPending = TimeStamp.UtcNow;
                isHeartbeatPending.AddSeconds(heartbeatResponseTimeoutSeconds);
                if (SyncTicks.Frozen)
                {
                    frozenHeartbeatCounter++;
                    if( frozenHeartbeatCounter > 3)
                    {
                        if (debug) log.Debug("More than 3 heart beats sent after frozen.  Ending heartbeats.");
                    }
                    else
                    {
                        OnHeartbeat();
                    }
                }
                else
                {
                    OnHeartbeat();
                }
            }
            else
            {
                if( debug) log.Debug("Skipping heartbeat because fix state: " + fixState);
            }
            IncreaseHeartbeat();
            return Yield.DidWork.Repeat;
        }

        private void OnDisconnectFIX(Socket socket)
		{
			if (this.fixSocket.Equals(socket)) {
				log.Info("FIX socket disconnect: " + socket);
                StopFIXSimulation();
                CloseFIXSocket();
			}
		}

        protected virtual void StopFIXSimulation()
        {
            isFIXSimulationStarted = false;
            if( resetSequenceNumbersNextDisconnect)
            {
                resetSequenceNumbersNextDisconnect = false;
                if (debug) log.Debug("Resetting sequence numbers because simulation rollover.");
                remoteSequence = 1;
                fixFactory = CreateFIXFactory(0, FixFactory.Sender, FixFactory.Destination);
            }
        }

        protected void CloseFIXSocket()
        {
            if (fixPacketQueue != null && fixPacketQueue.Count > 0)
            {
                fixPacketQueue.Clear();
            }
            if (fixSocket != null)
            {
                fixSocket.Dispose();
            }
        }

		protected void CloseSockets()
		{
            if (task != null)
            {
                task.Stop();
                task.Join();
            }
            CloseFIXSocket();
		}

		public virtual void StartFIXSimulation()
		{
			isFIXSimulationStarted = true;
            isConnectionLost = false;
        }

        public void Shutdown()
        {
            Dispose();
        }
		
		private enum State { Start, ProcessFIX, WriteFIX, Return };
		private State state = State.Start;
		private bool hasQuotePacket = false;
		private bool hasFIXPacket = false;
		public Yield Invoke()
		{
            if( isConnectionLost)
            {
                if (!fixPacketQueue.IsFull && FIXReadLoop())
                {
                    return Yield.DidWork.Repeat;
                }
                CloseFIXSocket();
                return Yield.NoWork.Repeat;
            }
			var result = false;
			switch( state) {
				case State.Start:
					if( !fixPacketQueue.IsFull && FIXReadLoop())
					{
						result = true;
					}
				ProcessFIX:
					hasFIXPacket = ProcessFIXPackets();
					if( hasFIXPacket ) {
						result = true;
					}
				WriteFIX:
					if( hasFIXPacket) {
						if( !WriteToFIX()) {
							state = State.WriteFIX;
							return Yield.NoWork.Repeat;
						}
                        //if( fixPacketQueue.Count > 0) {
                        //    state = State.ProcessFIX;
                        //    return Yield.DidWork.Repeat;
                        //}
					}
			        break;
				case State.ProcessFIX:
					goto ProcessFIX;
				case State.WriteFIX:
					goto WriteFIX;
			}
			state = State.Start;
			if( result) {
				return Yield.DidWork.Repeat;
			} else {
				return Yield.NoWork.Repeat;
			}
		}

		private bool ProcessFIXPackets() {
			if( _fixWriteMessage == null && fixPacketQueue.Count == 0) {
				return false;
			}
			if( trace) log.Trace("ProcessFIXPackets( " + fixPacketQueue.Count + " packets in queue.)");
			if( fixPacketQueue.TryDequeue(out _fixWriteMessage)) {
				return true;
			} else {
				return false;
			}
		}

        private FIXTMessage1_1 GapFillMessage(int currentSequence)
        {
            var message = FixFactory.Create(currentSequence);
            message.SetGapFill();
            message.SetNewSeqNum(currentSequence + 1);
            message.AddHeader("4");
            return message;
        }

        private bool HandleResend(MessageFIXT1_1 messageFIX)
        {
            int end = messageFIX.EndSeqNum == 0 ? FixFactory.LastSequence : messageFIX.EndSeqNum;
            if (debug) log.Debug("Found resend request for " + messageFIX.BegSeqNum + " to " + end + ": " + messageFIX);
            for (int i = messageFIX.BegSeqNum; i <= end; i++)
            {
                FIXTMessage1_1 textMessage;
                var gapFill = false;
                if (!FixFactory.TryGetHistory(i, out textMessage))
                {
                    gapFill = true;
                    textMessage = GapFillMessage(i);
                }
                else
                {
                    switch (textMessage.Type)
                    {
                        case "A": // Logon
                        case "0": // Heartbeat
                        case "1": // Heartbeat
                        case "2": // Resend request.
                        case "4": // Reset sequence.
                            textMessage = GapFillMessage(i);
                            gapFill = true;
                            break;
                        default:
                            textMessage.SetDuplicate(true);
                            break;
                    }

                }

                if (gapFill)
                {
                    if (debug)
                    {
                        var fixString = textMessage.ToString();
                        string view = fixString.Replace(FIXTBuffer.EndFieldStr, "  ");
                        log.Debug("Sending Gap Fill message " + i + ": \n" + view);
                    }
                    ResendMessageProtected(textMessage);
                }
                else
                {
                    ResendMessage(textMessage);
                }
            }
            return true;
        }

        protected abstract void ResendMessage(FIXTMessage1_1 textMessage);
        protected abstract void RemoveTickSync(MessageFIXT1_1 textMessage);
        protected abstract void RemoveTickSync(FIXTMessage1_1 textMessage);

        public bool SendSessionStatus(string status)
        {
            switch( status)
            {
                case "2":
                    ProviderSimulator.SetOrderServerOnline();
                    break;
                case "3":
                    ProviderSimulator.SetOrderServerOffline();
                    break;
                default:
                    throw new ApplicationException("Unknown session status:" + status);
            }
            var mbtMsg = FixFactory.Create();
            mbtMsg.AddHeader("h");
            mbtMsg.SetTradingSessionId("TSSTATE");
            mbtMsg.SetTradingSessionStatus(status);
            if (debug) log.Debug("Sending order server status: " + mbtMsg);
            SendMessage(mbtMsg);
            return true;
        }

        private bool Resend(MessageFIXT1_1 messageFix)
		{
            if( !isResendComplete) return true;
			var mbtMsg = FixFactory.Create();
			mbtMsg.AddHeader("2");
			mbtMsg.SetBeginSeqNum(RemoteSequence);
			mbtMsg.SetEndSeqNum(0);
			if( debug) log.Debug("Sending resend request: " + mbtMsg);
            SendMessage(mbtMsg);
		    return true;
		}

        private Random random;
		private int remoteSequence = 1;
        private int recoveryRemoteSequence = 1;
		private bool FIXReadLoop()
		{
			if (isFIXSimulationStarted)
			{
			    Message message;
                if (fixSocket.TryGetMessage(out message))
                {
                    var disconnect = message as DisconnectMessage;
                    if (disconnect == null)
                    {
                        _fixReadMessage = message;
                    }
                    else
                    {
                        OnDisconnectFIX(disconnect.Socket);
                        return false;
                    }
                }
                else
                {
                    return false;
                }
			    var packetFIX = (MessageFIXT1_1)_fixReadMessage;
                IncreaseHeartbeat();
                if (debug) log.Debug("Received FIX message: " + packetFIX);
			    isHeartbeatPending = TimeStamp.MaxValue;
                try
                {
                    switch( fixState)
                    {
                        case ServerState.Startup:
                            if (packetFIX.MessageType != "A")
                            {
                                throw new InvalidOperationException("Invalid FIX message type " + packetFIX.MessageType + ". Not yet logged in.");
                            }
                            if (!packetFIX.IsResetSeqNum && packetFIX.Sequence < RemoteSequence)
                            {
                                throw new InvalidOperationException("Login sequence number was " + packetFIX.Sequence + " less than expected " + RemoteSequence + ".");
                            }
                            HandleFIXLogin(packetFIX);
                            if (packetFIX.Sequence > RemoteSequence)
                            {
                                if (debug) log.Debug("Login packet sequence " + packetFIX.Sequence + " was greater than expected " + RemoteSequence);
                                recoveryRemoteSequence = packetFIX.Sequence;
                                return Resend(packetFIX);
                            }
                            else
                            {
                                if (debug) log.Debug("Login packet sequence " + packetFIX.Sequence + " was less than or equal to expected " + RemoteSequence + " so updating remote sequence...");
                                RemoteSequence = packetFIX.Sequence + 1;
                                SendSessionStatusOnline();
                                fixState = ServerState.Recovered;
                                // Setup disconnect simulation.
                            }
                            break;
                        case ServerState.LoggedIn:
                        case ServerState.Recovered:
                            switch (packetFIX.MessageType)
                            {
                                case "A":
                                    throw new InvalidOperationException("Invalid FIX message type " + packetFIX.MessageType + ". Already logged in.");
                                case "2":
                                    HandleResend(packetFIX);
                                    break;
                            }
                            if (packetFIX.Sequence > RemoteSequence)
                            {
                                if (debug) log.Debug("packet sequence " + packetFIX.Sequence + " greater than expected " + RemoteSequence);
                                return Resend(packetFIX);
                            }
                            if (packetFIX.Sequence < RemoteSequence)
                            {
                                if (debug) log.Debug("Already received packet sequence " + packetFIX.Sequence + ". Ignoring.");
                                return true;
                            }
                            else
                            {
                                if( packetFIX.Sequence >= recoveryRemoteSequence)
                                {
                                    isResendComplete = true;
                                    if (fixState == ServerState.LoggedIn)
                                    {
                                        // Sequences are synchronized now. Send TradeSessionStatus.
                                        fixState = ServerState.Recovered;
                                        if (requestSessionStatus)
                                        {
                                            SendSessionStatusOnline();
                                        }
                                        // Setup disconnect simulation.
                                        simulators[SimulatorType.SendDisconnect].UpdateNext(FixFactory.LastSequence);
                                    }
                                }
                                switch (packetFIX.MessageType)
                                {
                                    case "2": // resend request
                                        // already handled prior to sequence checking.
                                        if (debug) log.Debug("Resend request with sequence " + packetFIX.Sequence + ". So updating remote sequence...");
                                        RemoteSequence = packetFIX.Sequence + 1;
                                        break;
                                    case "4":
                                        return HandleGapFill(packetFIX);
                                    default:
                                        return ProcessMessage(packetFIX);
                                }
                            }
                            break;
                    }
                }
                finally
                {
                    fixSocket.MessageFactory.Release(_fixReadMessage);
                }
			}
			return false;
		}
	    protected bool requestSessionStatus;

        private bool resetSequenceNumbersNextDisconnect;
        private void SendSystemOffline()
        {
            var mbtMsg = (FIXMessage4_4)FixFactory.Create();
            mbtMsg.AddHeader("5");
            mbtMsg.SetText("System offline");
            SendMessage(mbtMsg);
            if (trace) log.Trace("Sending system offline simulation: " + mbtMsg);
            //resetSequenceNumbersNextDisconnect = true;
        }

        public void SendSessionStatusOnline()
        {
            if (debug) log.Debug("Sending session status online.");
            var wasOrderServerOnline = ProviderSimulator.IsOrderServerOnline;
            SendSessionStatus("2");
            if( !wasOrderServerOnline)
            {
                ProviderSimulator.SwitchBrokerState("online",true);
            }            
            ProviderSimulator.FlushFillQueues();
        }


        protected bool HandleFIXLogin(MessageFIXT1_1 packet)
        {
            if (fixState != ServerState.Startup)
            {
                throw new InvalidOperationException("Invalid login request. Already logged in: \n" + packet);
            }
            fixState = ServerState.LoggedIn;
            if (packet.IsResetSeqNum)
            {
                if( packet.Sequence != 1)
                {
                    throw new InvalidOperationException("Found reset sequence number flag is true but sequence was " + packet.Sequence + " instead of 1.");
                }
                if (debug) log.Debug("Found reset seq number flag. Resetting seq number to " + packet.Sequence);
                fixFactory = CreateFIXFactory(packet.Sequence, packet.Target, packet.Sender);
                RemoteSequence = packet.Sequence;
            }
            else if (FixFactory == null)
            {
                throw new InvalidOperationException(
                    "FIX login message specified tried to continue with sequence number " + packet.Sequence +
                    " but simulator has no sequence history.");
            }

            simulators[SimulatorType.SendDisconnect].UpdateNext(FixFactory.LastSequence);
            simulators[SimulatorType.ReceiveDisconnect].UpdateNext(packet.Sequence);
            simulators[SimulatorType.SendServerOffline].UpdateNext(FixFactory.LastSequence);
            simulators[SimulatorType.ReceiveServerOffline].UpdateNext(packet.Sequence);
            simulators[SimulatorType.SystemOffline].UpdateNext(packet.Sequence);

            var mbtMsg = CreateLoginResponse();
            if (debug) log.Debug("Sending login response: " + mbtMsg);
            SendMessage(mbtMsg);
            return true;
        }

        private FIXTMessage1_1 CreateLoginResponse()
        {
            var mbtMsg = (FIXTMessage1_1)FixFactory.Create();
            mbtMsg.SetEncryption(0);
            mbtMsg.SetHeartBeatInterval(HeartbeatDelay);
            mbtMsg.AddHeader("A");
            mbtMsg.SetSendTime(new TimeStamp(1800, 1, 1));
            return mbtMsg;
        }

        protected virtual FIXTFactory1_1 CreateFIXFactory(int sequence, string target, string sender)
        {
            throw new NotImplementedException();
        }

	    private bool HandleGapFill( MessageFIXT1_1 packetFIX)
        {
            if (!packetFIX.IsGapFill)
            {
                throw new InvalidOperationException("Only gap fill sequence reset supportted: \n" + packetFIX);
            }
            if (packetFIX.NewSeqNum <= RemoteSequence)  // ResetSeqNo
            {
                throw new InvalidOperationException("Reset new sequence number must be greater than current sequence: " + RemoteSequence + ".\n" + packetFIX);
            }
            RemoteSequence = packetFIX.NewSeqNum;
            if (debug) log.Debug("Received gap fill. Setting next sequence = " + RemoteSequence);
            return true;
        }

        private bool ProcessMessage(MessageFIXT1_1 packetFIX)
        {
            if( isConnectionLost)
            {
                if (debug) log.Debug("Ignoring message: " + packetFIX);
                RemoveTickSync(packetFIX);
                return true;
            }
            var simulator = simulators[SimulatorType.ReceiveDisconnect];
            if (FixFactory != null && simulator.CheckSequence(packetFIX.Sequence))
            {
                if (debug) log.Debug("Ignoring message: " + packetFIX);
                // Ignore this message. Pretend we never received it AND disconnect.
                // This will test the message recovery.)
                ProviderSimulator.SwitchBrokerState("disconnect",false);
                isConnectionLost = true;
                return true;
            }
            if (simulateReceiveFailed && FixFactory != null && random.Next(50) == 1)
            {
                // Ignore this message. Pretend we never received it.
                // This will test the message recovery.
                if (debug) log.Debug("Ignoring fix message sequence " + packetFIX.Sequence);
                return Resend(packetFIX);
            }
            simulator = simulators[SimulatorType.ReceiveServerOffline];
            if (IsRecovered && FixFactory != null && simulator.CheckSequence(packetFIX.Sequence))
            {
                if (debug) log.Debug("Skipping message: " + packetFIX);
                ProviderSimulator.SwitchBrokerState("disconnect", false);
                ProviderSimulator.SetOrderServerOffline();
                if( requestSessionStatus)
                {
                    SendSessionStatus("3"); //offline
                }
                else
                {
                    log.Info("RequestSessionStatus is false so not sending order server offline message.");
                }
                return true;
            }

            simulator = simulators[SimulatorType.SystemOffline];
            if (IsRecovered && FixFactory != null && simulator.CheckSequence(packetFIX.Sequence))
            {
                SendSystemOffline();
                return true;
            }

            if (debug) log.Debug("Processing message with " + packetFIX.Sequence + ". So updating remote sequence...");
            RemoteSequence = packetFIX.Sequence + 1;
            switch (packetFIX.MessageType)
            {
                case "G":
                case "D":
                    simulator = simulators[SimulatorType.BlackHole];
                    break;
                case "F":
                    simulator = simulators[SimulatorType.CancelBlackHole];
                    break;
            }
            if (FixFactory != null && simulator.CheckFrequency())
            {
                if (debug) log.Debug("Simulating order 'black hole' of 35=" + packetFIX.MessageType + " by incrementing sequence to " + RemoteSequence + " but ignoring message with sequence " + packetFIX.Sequence);
                return true;
            }
            ParseFIXMessage(_fixReadMessage);
            return true;
        }


	    public long GetRealTimeOffset( long utcTime) {
			lock( realTimeOffsetLocker) {
				if( realTimeOffset == 0L) {
					var currentTime = TimeStamp.UtcNow;
					var tickUTCTime = new TimeStamp(utcTime);
				   	log.Info("First historical playback tick UTC tick time is " + tickUTCTime);
				   	log.Info("Current tick UTC time is " + currentTime);
				   	realTimeOffset = currentTime.Internal - utcTime;
				   	var microsecondsInMinute = 1000L * 1000L * 60L;
                    var extra = realTimeOffset % microsecondsInMinute;
                    realTimeOffset -= extra;
                    realTimeOffset += microsecondsInMinute;
				   	var elapsed = new Elapsed( realTimeOffset);
				   	log.Info("Setting real time offset to " + elapsed);
				}
			}
			return realTimeOffset;
		}

		public virtual void ParseFIXMessage(Message message)
		{
		}

		public bool WriteToFIX()
		{
			if (!isFIXSimulationStarted || _fixWriteMessage == null) return true;
		    var result = SendMessageInternal(_fixWriteMessage);
            if( result)
            {
                _fixWriteMessage = null;
            }
		    return result;
		}

        protected void ResendMessageProtected(FIXTMessage1_1 fixMessage)
        {
            if (isConnectionLost)
            {
                return;
            }
            var writePacket = fixSocket.MessageFactory.Create();
            var message = fixMessage.ToString();
            writePacket.DataOut.Write(message.ToCharArray());
            writePacket.SendUtcTime = TimeStamp.UtcNow.Internal;
            if (debug) log.Debug("Resending simulated FIX Message: " + fixMessage);
            SendMessageInternal(writePacket);
        }

        private bool SendMessageInternal( Message message)
        {
            if (fixSocket.TrySendMessage(message))
            {
                IncreaseHeartbeat();
                if (trace) log.Trace("Local Write: " + message);
                return true;
            }
            log.Error("Failed to Write: " + message);
            Thread.Sleep(1000);
            Environment.Exit(1);
            return false;
        }

		private void IncreaseHeartbeat()
		{
		    var timeStamp = TimeStamp.UtcNow;
		    timeStamp.AddSeconds(HeartbeatDelay);
            if (debug) log.Debug("Setting next heartbeat for " + timeStamp);
            heartbeatTimer.Start(timeStamp);
		}		

        public void SendMessage(FIXTMessage1_1 fixMessage)
        {

            FixFactory.AddHistory(fixMessage);
            if (isConnectionLost)
            {
                RemoveTickSync(fixMessage);
                return;
            }
            var simulator = simulators[SimulatorType.SendDisconnect];
            if (simulator.CheckSequence(fixMessage.Sequence) )
            {
                if (debug) log.Debug("Ignoring message: " + fixMessage);
                ProviderSimulator.SwitchBrokerState("disconnect",false);
                isConnectionLost = true;
                return;
            }
            if (simulateSendFailed && IsRecovered && random.Next(50) == 4)
            {
                if (debug) log.Debug("Skipping send of sequence # " + fixMessage.Sequence + " to simulate lost message. " + fixMessage);
                if( fixMessage.Type == "1")
                {
                    isHeartbeatPending = TimeStamp.MaxValue;
                }
                if (debug) log.Debug("Message type is: " + fixMessage.Type);
                return;
            }
            var writePacket = fixSocket.MessageFactory.Create();
            var message = fixMessage.ToString();
            writePacket.DataOut.Write(message.ToCharArray());
            writePacket.SendUtcTime = TimeStamp.UtcNow.Internal;
            if( debug) log.Debug("Simulating FIX Message: " + fixMessage);
            try
            {
                fixPacketQueue.Enqueue(writePacket, writePacket.SendUtcTime);
            }
            catch( QueueException ex)
            {
                if( ex.EntryType == EventType.Terminate)
                {
                    log.Warn("fix packet queue returned queue exception " + ex.EntryType + ". Dropping message due to dispose.");
                    Dispose();
                }
                else
                {
                    throw;
                }
            }

            simulator = simulators[SimulatorType.SendServerOffline];
            if (IsRecovered && FixFactory != null && simulator.CheckSequence( fixMessage.Sequence))
            {
                if (debug) log.Debug("Skipping message: " + fixMessage);
                ProviderSimulator.SwitchBrokerState("offline",false);
                ProviderSimulator.SetOrderServerOffline();
                if( requestSessionStatus)
                {
                    SendSessionStatus("3");
                }
                else
                {
                    log.Info("RequestSessionStatus is false so not sending order server offline message.");
                }
            }
        }

        protected virtual Yield OnHeartbeat()
        {
			if( fixSocket != null && FixFactory != null)
			{
				var mbtMsg = (FIXTMessage1_1) FixFactory.Create();
				mbtMsg.AddHeader("1");
				if( trace) log.Trace("Requesting heartbeat: " + mbtMsg);
                SendMessage(mbtMsg);
			}
			return Yield.DidWork.Return;
		}
		
		public void OnException(Exception ex)
		{
			log.Error("Exception occurred", ex);
		}

		protected volatile bool isDisposed = false;
        private int heartbeatResponseTimeoutSeconds = 15;

        public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (!isDisposed) {
				isDisposed = true;
				if (disposing) {
                    if (debug) log.Debug("Dispose()");
				    var sb = new StringBuilder();
				    var countFailedTests = 0;
                    foreach( var kvp in simulators)
                    {
                        var sim = kvp.Value;
                        if( sim.Enabled)
                        {
                            if( sim.Counter < sim.Minimum)
                            {
                                SyncTicks.Success = false;
                                ++countFailedTests;
                            }
                            sb.AppendLine(SyncTicks.CurrentTestName + ": " + sim.Type + " attempts " + sim.AttemptCounter + ", count " + sim.Counter);
                        }
                    }
                    if( countFailedTests > 0)
                    {
                        log.Error( countFailedTests + " negative simulators occured less than 2 times:\n" + sb);
                    }
                    else
                    {
                        log.Info("Active negative test simulator results:\n" + sb);
                    }
                    if( !ProviderSimulator.IsOrderServerOnline)
                    {
                        SyncTicks.Success = false;
                        log.Error("The FIX order server ended in offline state.");
                    }
                    else 
                    {
                        log.Info("The FIX order server finished up online.");
                    }
                    if (isConnectionLost)
                    {
                        SyncTicks.Success = false;
                        log.Error("The FIX order server ended in connection loss state.");
                    }
                    else
                    {
                        log.Info("The FIX order server finished up connected.");
                    }
                    CloseSockets();
                    if (fixListener != null)
                    {
                        fixListener.Dispose();
                    }
                    if( task != null)
                    {
                        task.Stop();
                    }
                }
			}
		}

        public bool IsRecovered
        {
            get { return fixState == ServerState.Recovered;  }
        }

		public ushort FIXPort {
			get { return fixPort; }
		}

		public long RealTimeOffset {
			get { return realTimeOffset; }
		}

	    public int HeartbeatDelay
	    {
	        get { return heartbeatDelay; }
	    }

        public FIXTFactory1_1 FixFactory
        {
            get { return fixFactory; }
        }

        public int RemoteSequence
        {
            get { return remoteSequence; }
            set
            {
                if( remoteSequence != value)
                {
                    if (debug) log.Debug("Remote sequence changed from " + remoteSequence + " to " + value);
                    remoteSequence = value;
                }
            }
        }

        public ProviderSimulatorSupport ProviderSimulator
        {
            get { return providerSimulator; }
        }

        public abstract void OnRejectOrder(CreateOrChangeOrder order, string error);
        public abstract void OnPhysicalFill(PhysicalFill fill, CreateOrChangeOrder order);
	}
}

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



namespace TickZoom.MBTQuotes
{
	public abstract class MBTQuoteProviderSupport : AgentPerformer, LogAware
	{
		private readonly Log log;
		private volatile bool debug;
        private volatile bool trace;
        public virtual void RefreshLogLevel()
        {
            debug = log.IsDebugEnabled;
            trace = log.IsTraceEnabled;
        }
        private static long nextConnectTime = 0L;
        protected class SymbolReceiver
        {
            internal SymbolInfo Symbol;
            internal Agent Agent;
        }
		protected readonly object symbolsRequestedLocker = new object();
        protected Dictionary<long, SymbolReceiver> symbolsRequested = new Dictionary<long, SymbolReceiver>();
		private Socket socket;
        protected Task socketTask;
		private string failedFile;
		protected Agent ClientAgent;
		private long retryDelay = 30; // seconds
		private long retryStart = 30; // seconds
		private long retryIncrease = 5;
		private long retryMaximum = 30;
		private volatile Status connectionStatus = Status.New;
		private string addrStr;
		private ushort port;
		private string userName;
		private	string password;
		public abstract void OnDisconnect();
		public abstract void OnRetry();
		public abstract void SendLogin();
        public abstract bool VerifyLogin();
        private string providerName;
		private long heartbeatTimeout;
		private int heartbeatDelay;
		private bool logRecovery = false;
	    private string configFilePath;
	    private string configSection;
        private bool useLocalTickTime = true;
        private volatile bool debugDisconnect = false;
	    private int timeSeconds = 10;
	    private TrueTimer taskTimer;
        private Agent agent;
        public Agent Agent
        {
            get { return agent; }
            set { agent = value; }
        }

		public MBTQuoteProviderSupport(string name)
		{
		    configSection = name;
			log = Factory.SysLog.GetLogger(typeof(MBTQuoteProviderSupport)+"."+GetType().Name);
		    log.Register(this);
            providerName = GetType().Name;
            RefreshLogLevel();
            string logRecoveryString = Factory.Settings["LogRecovery"];
            logRecovery = !string.IsNullOrEmpty(logRecoveryString) && logRecoveryString.ToLower().Equals("true");
            if( timeSeconds > 30)
            {
                log.Error("MBTQuotesProvider retry time greater then 30 seconds: " + int.MaxValue);
            }
        }

        public void Initialize(Task task)
        {
            socketTask = task;
            socketTask.Scheduler = Scheduler.EarliestTime;
            taskTimer = Factory.Parallel.CreateTimer("Task", socketTask, TimerTask);
            if (debug) log.Debug("Created timer. (Default startTime: " + taskTimer.StartTime + ")");
            filter = socketTask.GetFilter();
            socketTask.Start();
            if (debug) log.Debug("> Initialize.");
            var appDataFolder = Factory.Settings["AppDataFolder"];
            if (appDataFolder == null)
            {
                throw new ApplicationException("Sorry, AppDataFolder must be set in the app.config file.");
            }
            var configFile = appDataFolder + @"/Providers/" + providerName + "/Default.config";
            failedFile = appDataFolder + @"/Providers/" + providerName + "/LoginFailed.txt";

            LoadProperties(configFile);

            if (File.Exists(failedFile))
            {
                throw new ApplicationException("Please correct the username or password error described in " + failedFile + ". Then delete the file retrying, please.");
            }
        }

        private void RegenerateSocket()
        {
			Socket old = socket;
			if( socket != null) {
				socket.Dispose();
			}
            socket = Factory.Provider.Socket("MBTQuoteSocket",addrStr, port);
			socket.OnDisconnect = OnDisconnect;
            socket.OnConnect = OnConnect;
            socket.MessageFactory = new MessageFactoryMbtQuotes();
			socket.ReceiveQueue.ConnectInbound( socketTask);
            socket.SendQueue.ConnectOutbound( socketTask);
            if (debug) log.Debug("Created new " + socket);
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
                    log.Info("Requested Connect for " + socket);
                    return;
                }
                catch (SocketErrorException ex)
                {
                    log.Error("Non fatal error while trying to connect: " + ex.Message);
                }
            }
        }
		
		
		public enum Status {
			New,
			Connected,
			PendingLogin,
			PendingRecovery,
			Recovered,
			Disconnected,
			PendingRetry
		}
		
		public void FailLogin(string packetString) {
			string message = "Login failed for user name: " + userName + " and password: " + new string('*',password.Length);
			string fileMessage = "Resolve the problem and then delete this file before you retry.";
			string logMessage = "Resolve the problem and then delete the file " + failedFile + " before you retry.";
			if( File.Exists(failedFile)) {
				File.Delete(failedFile);
			}
			using( var fileOut = new StreamWriter(failedFile)) {
				fileOut.WriteLine(message);
				fileOut.WriteLine(fileMessage);
			}
			log.Error(message + " " + logMessage + "\n" + packetString);
			throw new ApplicationException(message + " " + logMessage);
		}

        private void OnConnect(Socket socket)
        {
            if (!this.socket.Equals(socket))
            {
                log.Warn("OnConnect( " + this.socket + " != " + socket + " ) - Ignored.");
                return;
            }
            log.Info("OnConnect( " + socket + " ) ");
            ConnectionStatus = Status.Connected;
            if (debug) log.Debug("ConnectionStatus changed to: " + ConnectionStatus);
            SendLogin();
            ConnectionStatus = Status.PendingLogin;
            if (debug) log.Debug("ConnectionStatus changed to: " + ConnectionStatus);
            IncreaseRetryTimeout();
        }

        private void OnDisconnect(Socket socket)
        {
            if( !this.socket.Equals(socket))
            {
                log.Warn("OnDisconnect( " + this.socket + " != " + socket + " ) - Ignored.");
                return;
            }
			if( !this.socket.Port.Equals(socket.Port)) {
			}
			log.Info("OnDisconnect( " + socket + " ) ");
			ConnectionStatus = Status.Disconnected;
		    debugDisconnect = true;
            if (debug) log.Debug("ConnectionStatus changed to: " + ConnectionStatus);
            if (debug) log.Debug("Socket state now: " + socket.State);
            if( isDisposed)
            {
                Finish();
            }
            else
            {
                log.Error("MBTQuoteProvider disconnected.");
            }
        }
	
		public bool IsInterrupted {
			get {
				return isDisposed || socket.State != SocketState.Connected;
			}
		}
	
		public void StartRecovery() {
			ConnectionStatus = Status.PendingRecovery;
			if( debug) log.Debug("ConnectionStatus changed to: " + ConnectionStatus);
			OnStartRecovery();
		}
		
		public void EndRecovery() {
			ConnectionStatus = Status.Recovered;
			if( debug) log.Debug("ConnectionStatus changed to: " + ConnectionStatus);
		}
		
		public bool IsRecovered {
			get { 
				return ConnectionStatus == Status.Recovered;
			}
		}
		
		private void SetupRetry() {
			OnRetry();
			RegenerateSocket();
			if( trace) log.Trace("ConnectionStatus changed to: " + ConnectionStatus);
		}
		
		public bool IsRecovering {
			get {
				return ConnectionStatus == Status.PendingRecovery;
			}
		}

	    private Status lastStatus;
        private SocketState lastSocketState;

        private Yield TimerTask()
        {
            if (isDisposed) return Yield.NoWork.Repeat;
            TimeStamp currentTime = TimeStamp.UtcNow;
            currentTime.AddSeconds(timeSeconds);
            taskTimer.Start(currentTime);
            if (debug) log.Debug("Created timer. (Default startTime: " + taskTimer.StartTime + ")");
            return Invoke();
        }

        public void Shutdown()
        {
            Dispose();
        }

        public Yield Invoke()
        {
			if( isDisposed ) return Yield.NoWork.Repeat;
            EventItem eventItem;
            if( filter.Receive(out eventItem))
            {
                switch( eventItem.EventType)
                {
                    case EventType.Connect:
                        Start(eventItem);
                        filter.Pop();
                        break;
                    case EventType.Disconnect:
                        Stop(eventItem);
                        filter.Pop();
                        break;
                    case EventType.StartSymbol:
                        StartSymbol(eventItem);
                        filter.Pop();
                        break;
                    case EventType.StopSymbol:
                        StopSymbol(eventItem);
                        filter.Pop();
                        break;
                    case EventType.PositionChange:
                        PositionChange(eventItem);
                        filter.Pop();
                        break;
                    case EventType.Shutdown:
                    case EventType.Terminate:
                        Dispose();
                        filter.Pop();
                        break;
                    default:
                        throw new ApplicationException("Unexpected event: " + eventItem);
                }
            }
            if (!isStarted) return Yield.NoWork.Repeat;

            if (debugDisconnect)
            {
                if( debug) log.Debug("Invoke() Current socket state: " + socket.State + ", " + socket);
                if (debug) log.Debug("Invoke: Current connection status: " + ConnectionStatus);
                debugDisconnect = false;
            }
            if( socket.State != lastSocketState)
            {
                if( debug) log.Debug("Socket state changed to: " + socket.State);
                lastSocketState = socket.State;
            }
            if (ConnectionStatus != lastStatus)
            {
                if (debug) log.Debug("Connection status changed to: " + ConnectionStatus);
                lastStatus = ConnectionStatus;
            }
            switch (socket.State)
            {
				case SocketState.New:
    				return Yield.NoWork.Repeat;
				case SocketState.PendingConnect:
					if( Factory.Parallel.TickCount >= retryTimeout) {
                        log.Info("MBTQuoteProvider connect timed out. Retrying.");
						SetupRetry();
						retryDelay += retryIncrease;
                        IncreaseRetryTimeout();
						return Yield.DidWork.Repeat;
					} else {
						return Yield.NoWork.Repeat;
					}
				case SocketState.Connected:
					switch( ConnectionStatus) {
						case Status.Connected:
                            return Yield.DidWork.Repeat;
                        case Status.PendingLogin:
                            if( VerifyLogin())
                            {
                                StartRecovery();
                                return Yield.DidWork.Repeat;
                            }
                            else
                            {
                                return Yield.NoWork.Repeat;
                            }
						case Status.PendingRecovery:
						case Status.Recovered:
							if( retryDelay != retryStart) {
								retryDelay = retryStart;
								log.Info("(RetryDelay reset to " + retryDelay + " seconds.)");
							}
							if( Factory.Parallel.TickCount >= heartbeatTimeout) {
                                if( !isPingSent)
                                {
                                    isPingSent = true;
                                    SendPing();
                                    IncreaseRetryTimeout();
                                }
                                else
                                {
                                    isPingSent = false;
                                    log.Warn("MBTQuotesProvider ping timed out.");
                                    SetupRetry();
                                    IncreaseRetryTimeout();
                                    return Yield.DidWork.Repeat;
                                }
							}
					        Message rawMessage;
							var receivedMessage = false;
                            if (Socket.TryGetMessage(out rawMessage))
                            {
								receivedMessage = true;
                                var message = (MessageMbtQuotes) rawMessage;
                                message.BeforeRead();
                                if (trace)
                                {
                                    log.Trace("Received tick: " + new string(message.DataIn.ReadChars(message.Remaining)));
                                }
                                if (message.MessageType == '9')
                                {
                                    // Received the ping response.
                                    if( trace) log.Trace("Ping successfully received."); 
                                    isPingSent = false;
                                }
                                else
                                {
                                    try
                                    {
                                        ReceiveMessage(message);
                                    }
                                    catch( Exception ex)
                                    {
                                        var loggingString = Encoding.ASCII.GetString(message.Data.GetBuffer(), 0, (int)message.Data.Length);
                                        log.Error("Unable to process this message:\n" + loggingString, ex);
                                    }
                                }
                                Socket.MessageFactory.Release(rawMessage);
                            }
							if( receivedMessage) {
	                           	IncreaseRetryTimeout();
                            }

					        return receivedMessage ? Yield.DidWork.Repeat : Yield.NoWork.Repeat;
						default:
							throw new ApplicationException("Unexpected connection status: " + ConnectionStatus);
					}
                case SocketState.ShuttingDown:
                case SocketState.Closing:
                case SocketState.Closed:
                case SocketState.Disconnected:
					switch( ConnectionStatus) {
						case Status.Disconnected:
							retryTimeout = Factory.Parallel.TickCount + retryDelay * 1000;
							ConnectionStatus = Status.PendingRetry;
							if( debug) log.Debug("ConnectionStatus changed to: " + ConnectionStatus + ". Retrying in " + retryDelay + " seconds.");
							retryDelay += retryIncrease;
							retryDelay = retryDelay > retryMaximum ? retryMaximum : retryDelay;
							return Yield.NoWork.Repeat;
						case Status.PendingRetry:
							if( Factory.Parallel.TickCount >= retryTimeout) {
                                log.Info("MBTQuoteProvider retry time elapsed. Retrying.");
                                OnRetry();
								RegenerateSocket();
								if( trace) log.Trace("ConnectionStatus changed to: " + ConnectionStatus);
								return Yield.DidWork.Repeat;
							} else {
								return Yield.NoWork.Repeat;
							}
						default:
                            log.Warn("Unexpected state for quotes connection: " + ConnectionStatus);
					        ConnectionStatus = Status.Disconnected;
                            log.Warn("Forces connection state to be: " + ConnectionStatus);
                            return Yield.NoWork.Repeat;
					}
                default:
					string errorMessage = "Unknown socket state: " + socket.State;
                    log.Error(errorMessage);
                    throw new ApplicationException(errorMessage);
			}
		}

	    private bool isPingSent = false;
	    private void SendPing()
	    {
            Message message = Socket.MessageFactory.Create();
            string textMessage = "9|\n";
            if (trace) log.Trace("Ping request: " + textMessage);
            message.DataOut.Write(textMessage.ToCharArray());
            while (!Socket.TrySendMessage(message))
            {
                if (IsInterrupted) return;
                Factory.Parallel.Yield();
            }
	    }

		protected void IncreaseRetryTimeout() {
			retryTimeout = Factory.Parallel.TickCount + retryDelay * 1000;
			heartbeatTimeout = Factory.Parallel.TickCount + (long)heartbeatDelay * 1000L;
		}
		
		protected abstract void OnStartRecovery();
		
		protected abstract void ReceiveMessage(MessageMbtQuotes message);
		
		private long retryTimeout;
		
		private void OnException( Exception ex) {
			// Attempt to propagate the exception.
			log.Error("Exception occurred", ex);
			SendError( ex.Message + "\n" + ex.StackTrace);
			Dispose();
		}

	    private QueueFilter filter;
	    private bool isStarted;
        public void Start(EventItem eventItem)
        {
        	this.ClientAgent = eventItem.Agent;
            log.Info(providerName + " Startup");

            TimeStamp currentTime = TimeStamp.UtcNow;
            currentTime.AddSeconds(timeSeconds);
            taskTimer.Start(currentTime);
            if (debug) log.Debug("Created timer. (Default startTime: " + taskTimer.StartTime + ")");

            RegenerateSocket();
            retryTimeout = Factory.Parallel.TickCount + retryDelay * 1000;
            log.Info("Connection will timeout and retry in " + retryDelay + " seconds.");
            isStarted = true;
        }
        
        public void Stop(EventItem eventItem) {
        	
        }
	
        public void StartSymbol(EventItem eventItem)
        {
        	log.Info("StartSymbol( " + eventItem.Symbol+ ")");
        	// This adds a new order handler.
            TryAddSymbol(eventItem.Symbol, eventItem.Agent);
            OnStartSymbol(eventItem.Symbol, eventItem.Agent);
        }
        
        public abstract void OnStartSymbol( SymbolInfo symbol, Agent symbolAgent);
        
        public void StopSymbol(EventItem eventItem)
        {
        	log.Info("StopSymbol( " + eventItem.Symbol + ")");
            if (TryRemoveSymbol(eventItem.Symbol))
            {
                OnStopSymbol(eventItem.Symbol, eventItem.Agent);
        	}
        }
        
        public abstract void OnStopSymbol(SymbolInfo symbol, Agent symbolAgent);

	    private bool alreadyLoggedSectionAndFile = false;
	    private void LoadProperties(string configFilePath) {
	        this.configFilePath = configFilePath;
            if( !alreadyLoggedSectionAndFile)
            {
                log.Notice("Using section " + configSection + " in file: " + configFilePath);
                alreadyLoggedSectionAndFile = true;
            }
	        var configFile = new ConfigFile(configFilePath);
        	configFile.AssureValue("EquityDemo/UseLocalTickTime","true");
        	configFile.AssureValue("EquityDemo/ServerAddress","216.52.236.111");
            configFile.AssureValue("EquityDemo/ServerPort","5020");
        	configFile.AssureValue("EquityDemo/UserName","CHANGEME");
            configFile.AssureValue("EquityDemo/Password","CHANGEME");
        	configFile.AssureValue("ForexDemo/UseLocalTickTime","true");
        	configFile.AssureValue("ForexDemo/ServerAddress","216.52.236.111");
            configFile.AssureValue("ForexDemo/ServerPort","5020");
        	configFile.AssureValue("ForexDemo/UserName","CHANGEME");
            configFile.AssureValue("ForexDemo/Password","CHANGEME");
        	configFile.AssureValue("EquityLive/UseLocalTickTime","true");
            configFile.AssureValue("EquityLive/ServerAddress", "216.52.236.129");
            configFile.AssureValue("EquityLive/ServerPort","5020");
        	configFile.AssureValue("EquityLive/UserName","CHANGEME");
            configFile.AssureValue("EquityLive/Password","CHANGEME");
        	configFile.AssureValue("ForexLive/UseLocalTickTime","true");
            configFile.AssureValue("ForexLive/ServerAddress", "216.52.236.129");
            configFile.AssureValue("ForexLive/ServerPort","5020");
        	configFile.AssureValue("ForexLive/UserName","CHANGEME");
            configFile.AssureValue("ForexLive/Password","CHANGEME");
        	configFile.AssureValue("Simulate/UseLocalTickTime","false");
        	configFile.AssureValue("Simulate/ServerAddress","127.0.0.1");
            configFile.AssureValue("Simulate/ServerPort","6488");
        	configFile.AssureValue("Simulate/UserName","simulate1");
            configFile.AssureValue("Simulate/Password","only4sim");
			
			ParseProperties(configFile);
		}
	        
        private void ParseProperties(ConfigFile configFile) {
			var value = GetField("UseLocalTickTime",configFile, false);
			if( !string.IsNullOrEmpty(value)) {
				useLocalTickTime = value.ToLower() != "false";
        	}
			
			AddrStr = GetField("ServerAddress",configFile,true);
			var portStr = GetField("ServerPort",configFile,true);
			if( !ushort.TryParse(portStr, out port)) {
				Exception("ServerPort",configFile);
			}
			userName = GetField("UserName",configFile,true);
			password = GetField("Password",configFile,true);
			
			if( File.Exists(failedFile) ) {
				throw new ApplicationException("Please correct the username or password error described in " + failedFile + ". Then delete the file before retrying, please.");
			}
        }
        
        private string GetField( string field, ConfigFile configFile, bool required) {
			var result = configFile.GetValue(configSection + "/" + field);
			if( required && string.IsNullOrEmpty(result)) {
				Exception( field, configFile);
			}
			return result;
        }
        
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
		
		private bool TryAddSymbol(SymbolInfo symbol, Agent symbolAgent) {
			lock( symbolsRequestedLocker) {
				if( !symbolsRequested.ContainsKey(symbol.BinaryIdentifier))
				{
				    symbolsRequested.Add(symbol.BinaryIdentifier, new SymbolReceiver {Symbol = symbol, Agent = symbolAgent});
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
		
		public void PositionChange(EventItem eventItem)
		{
		    
		}

        private volatile bool isFinished;
        public bool IsFinalized
        {
            get { return isFinished && (socketTask == null || !socketTask.IsAlive); }
        }

        public void Finish()
        {
            isFinished = true;
            if (socketTask != null)
            {
                socketTask.Stop();
                if (debug) log.Debug("Stopped socket task.");
            }
        }

        protected volatile bool isDisposed = false;
	    public void Dispose() 
	    {
	        Dispose(true);
	        GC.SuppressFinalize(this);      
	    }
	
	    protected virtual void Dispose(bool disposing)
	    {
       		if( !isDisposed) {
                if (debug) log.Debug("Dispose()");
	            isDisposed = true;   
	            if (disposing) {
                    if (socket != null)
                    {
                        socket.Dispose();
                    }
                    if (taskTimer != null)
                    {
                        taskTimer.Dispose();
                        if (debug) log.Debug("Stopped task timer.");
                    }
                    if( socketTask != null)
                    {
                        socketTask.Stop();
                    }
	            	nextConnectTime = Factory.Parallel.TickCount + 10000;
	            }
    		}
	    }
		
		public Socket Socket {
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
		
		public long RetryStart {
			get { return retryStart; }
			set { retryStart = retryDelay = value; }
		}
		
		public long RetryIncrease {
			get { return retryIncrease; }
			set { retryIncrease = value; }
		}
		
		public long RetryMaximum {
			get { return retryMaximum; }
			set { retryMaximum = value; }
		}
		
		public int HeartbeatDelay {
			get { return heartbeatDelay; }
			set { heartbeatDelay = value;
                if( heartbeatDelay > 10)
                {
                    log.Error("Heartbeat delay is " + heartbeatDelay);
                }
				IncreaseRetryTimeout();
			}
		}
		
		public bool LogRecovery {
			get { return logRecovery; }
		}
		
		public MBTQuoteProviderSupport.Status ConnectionStatus
		{
		    get { return connectionStatus; }
		    set
		    {
		        if( connectionStatus != value)
		        {
		            if( debug) log.Debug("Connection status changed from " + connectionStatus + " to " + value);
		            connectionStatus = value;
		        }
		    }
		}

	    public bool UseLocalTickTime {
			get { return useLocalTickTime; }
		}
	}
}

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
	public abstract class MBTQuoteProviderSupport : Provider, LogAware
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
		protected readonly object symbolsRequestedLocker = new object();
		protected Dictionary<long,SymbolInfo> symbolsRequested = new Dictionary<long, SymbolInfo>();
		private Socket socket;
        protected Task socketTask;
		private string failedFile;
		protected Receiver receiver;
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
	    private TrueTimer taskTimer;

		public MBTQuoteProviderSupport(string name)
		{
		    configSection = name;
			log = Factory.SysLog.GetLogger(typeof(MBTQuoteProviderSupport)+"."+GetType().Name);
		    log.Register(this);
            providerName = GetType().Name;
            RefreshLogLevel();
            string logRecoveryString = Factory.Settings["LogRecovery"];
            logRecovery = !string.IsNullOrEmpty(logRecoveryString) && logRecoveryString.ToLower().Equals("true");
        }
		
		private void RegenerateSocket() {
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
			connectionStatus = Status.New;
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
		
		protected void Initialize() {
	        try { 
				if( debug) log.Debug("> Initialize.");
				var appDataFolder = Factory.Settings["AppDataFolder"];
				if( appDataFolder == null) {
					throw new ApplicationException("Sorry, AppDataFolder must be set in the app.config file.");
				}
				var configFile = appDataFolder+@"/Providers/"+providerName+"/Default.config";
				failedFile = appDataFolder+@"/Providers/"+providerName+"/LoginFailed.txt";
				
				LoadProperties(configFile);
				
				if( File.Exists(failedFile) ) {
					throw new ApplicationException("Please correct the username or password error described in " + failedFile + ". Then delete the file retrying, please.");
				}
				
	        } catch( Exception ex) {
	        	log.Error(ex.Message,ex);
	        	throw;
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
            if (!this.socket.Port.Equals(socket.Port))
            {
                log.Warn("OnConnect( " + this.socket + " != " + socket + " ) - Ignored.");
                return;
            }
            log.Info("OnConnect( " + socket + " ) ");
            connectionStatus = Status.Connected;
            if (debug) log.Debug("ConnectionStatus changed to: " + connectionStatus);
            SendLogin();
            connectionStatus = Status.PendingLogin;
            if (debug) log.Debug("ConnectionStatus changed to: " + connectionStatus);
            IncreaseRetryTimeout();
        }

        private void OnDisconnect(Socket socket)
        {
			if( !this.socket.Port.Equals(socket.Port)) {
				log.Warn("OnDisconnect( " + this.socket + " != " + socket + " ) - Ignored.");
				return;
			}
			log.Info("OnDisconnect( " + socket + " ) ");
		    log.Error("MBTQuoteProvider disconnected.");
			connectionStatus = Status.Disconnected;
		    debugDisconnect = true;
            if (debug) log.Debug("ConnectionStatus changed to: " + connectionStatus);
            if (debug) log.Debug("Socket state now: " + socket.State);
            if( isDisposed)
            {
                Finalize();
            }
        }
	
		public bool IsInterrupted {
			get {
				return isDisposed || socket.State != SocketState.Connected;
			}
		}
	
		public void StartRecovery() {
			connectionStatus = Status.PendingRecovery;
			if( debug) log.Debug("ConnectionStatus changed to: " + connectionStatus);
			OnStartRecovery();
		}
		
		public void EndRecovery() {
			connectionStatus = Status.Recovered;
			if( debug) log.Debug("ConnectionStatus changed to: " + connectionStatus);
		}
		
		public bool IsRecovered {
			get { 
				return connectionStatus == Status.Recovered;
			}
		}
		
		private void SetupRetry() {
			OnRetry();
			RegenerateSocket();
			if( trace) log.Trace("ConnectionStatus changed to: " + connectionStatus);
		}
		
		public bool IsRecovering {
			get {
				return connectionStatus == Status.PendingRecovery;
			}
		}

	    private Status lastStatus;
        private SocketState lastSocketState;

        private Yield TimerTask()
        {
            if (isDisposed) return Yield.NoWork.Repeat;
            TimeStamp currentTime = TimeStamp.UtcNow;
            currentTime.AddSeconds(1);
            taskTimer.Start(currentTime);
            return SocketTask();
        }

	    private Yield SocketTask() {
			if( isDisposed ) return Yield.NoWork.Repeat;
            if (debugDisconnect)
            {
                if( debug) log.Debug("SocketTask: Current socket state: " + socket.State + ", connection status: " + connectionStatus);
                debugDisconnect = false;
            }
            if( socket.State != lastSocketState)
            {
                if( debug) log.Debug("Socket state changed to: " + socket.State);
                lastSocketState = socket.State;
            }
            if (connectionStatus != lastStatus)
            {
                if (debug) log.Debug("Connection status changed to: " + connectionStatus);
                lastStatus = connectionStatus;
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
					switch( connectionStatus) {
						case Status.Connected:
                            return Yield.DidWork.Repeat;
                        case Status.PendingLogin:
                            if( VerifyLogin())
                            {
                                StartRecovery();
                            }
					        return Yield.DidWork.Repeat;
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
							return Yield.NoWork.Repeat;
					}
                case SocketState.Closing:
				case SocketState.Disconnected:
					switch( connectionStatus) {
						case Status.Disconnected:
							OnDisconnect();
							retryTimeout = Factory.Parallel.TickCount + retryDelay * 1000;
							connectionStatus = Status.PendingRetry;
							if( debug) log.Debug("ConnectionStatus changed to: " + connectionStatus + ". Retrying in " + retryDelay + " seconds.");
							retryDelay += retryIncrease;
							retryDelay = retryDelay > retryMaximum ? retryMaximum : retryDelay;
							return Yield.NoWork.Repeat;
						case Status.PendingRetry:
							if( Factory.Parallel.TickCount >= retryTimeout) {
                                log.Info("MBTQuoteProvider retry time elapsed. Retrying.");
                                OnRetry();
								RegenerateSocket();
								if( trace) log.Trace("ConnectionStatus changed to: " + connectionStatus);
								return Yield.DidWork.Repeat;
							} else {
								return Yield.NoWork.Repeat;
							}
						default:
                            log.Warn("Unexpected state for quotes connection: " + connectionStatus);
					        connectionStatus = Status.Disconnected;
                            log.Warn("Forces connection state to be: " + connectionStatus);
                            return Yield.NoWork.Repeat;
					}
                case SocketState.Closed:
                    return Yield.DidWork.Repeat;
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

        public void Start(Receiver receiver)
        {
        	this.receiver = (Receiver) receiver;
            log.Info(providerName + " Startup");
            socketTask = Factory.Parallel.Loop("MBTQuotesProvider", OnException, SocketTask);
            socketTask.Scheduler = Scheduler.EarliestTime;
            taskTimer = Factory.Parallel.CreateTimer("Task", socketTask, TimerTask);
            socketTask.Start();

            TimeStamp currentTime = TimeStamp.UtcNow;
            currentTime.AddSeconds(1);
            taskTimer.Start(currentTime);

            Initialize();
            RegenerateSocket();
            retryTimeout = Factory.Parallel.TickCount + retryDelay * 1000;
            log.Info("Connection will timeout and retry in " + retryDelay + " seconds.");
        }
        
        public void Stop(Receiver receiver) {
        	
        }
	
        public void StartSymbol(Receiver receiver, SymbolInfo symbol, StartSymbolDetail detail)
        {
        	log.Info("StartSymbol( " + symbol + ")");
        	if( this.receiver != receiver) {
        		throw new ApplicationException("Invalid receiver. Only one receiver allowed for " + this.GetType().Name);
        	}
        	// This adds a new order handler.
        	TryAddSymbol(symbol);
        	OnStartSymbol(symbol);
        }
        
        public abstract void OnStartSymbol( SymbolInfo symbol);
        
        public void StopSymbol(Receiver receiver, SymbolInfo symbol)
        {
        	log.Info("StopSymbol( " + symbol + ")");
        	if( this.receiver != receiver) {
        		throw new ApplicationException("Invalid receiver. Only one receiver allowed for " + this.GetType().Name);
        	}
        	if( TryRemoveSymbol(symbol)) {
        		OnStopSymbol(symbol);
        	}
        }
        
        public abstract void OnStopSymbol(SymbolInfo symbol);

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
			if( receiver!= null) {
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
		
		private bool TryAddSymbol(SymbolInfo symbol) {
			lock( symbolsRequestedLocker) {
				if( !symbolsRequested.ContainsKey(symbol.BinaryIdentifier)) {
					symbolsRequested.Add(symbol.BinaryIdentifier,symbol);
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
		
		public abstract void PositionChange(Receiver receiver, SymbolInfo symbol, double signal, Iterable<LogicalOrder> orders);

        private volatile bool isFinalized;
        public bool IsFinalized
        {
            get { return isFinalized && (socketTask == null || !socketTask.IsAlive); }
        }

        private void Finalize()
        {
            isFinalized = true;
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
                        //while (socket.ReceiveQueueCount > 0)
                        //{
                        //    Factory.Parallel.Yield();
                        //}
                        socket.Dispose();
                    }
                    if (taskTimer != null)
                    {
                        taskTimer.Dispose();
                        if (debug) log.Debug("Stopped task timer.");
                    }
	            	nextConnectTime = Factory.Parallel.TickCount + 10000;
	            }
    		}
	    }    
	        
		public void SendEvent( Receiver receiver, SymbolInfo symbol, int eventType, object eventDetail) {
			switch( (EventType) eventType) {
				case EventType.Connect:
					Start(receiver);
					break;
				case EventType.Disconnect:
					Stop(receiver);
					break;
				case EventType.StartSymbol:
					StartSymbol(receiver,symbol, (StartSymbolDetail) eventDetail);
					break;
				case EventType.StopSymbol:
					StopSymbol(receiver,symbol);
					break;
				case EventType.PositionChange:
					PositionChangeDetail positionChange = (PositionChangeDetail) eventDetail;
					PositionChange(receiver,symbol,positionChange.Position,positionChange.Orders);
					break;
                case EventType.RemoteShutdown:
				case EventType.Terminate:
					Dispose();
					break; 
				default:
					throw new ApplicationException("Unexpected event type: " + (EventType) eventType);
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
				IncreaseRetryTimeout();
			}
		}
		
		public bool LogRecovery {
			get { return logRecovery; }
		}
		
		public MBTQuoteProviderSupport.Status ConnectionStatus {
			get { return connectionStatus; }
		}
	    
		public bool UseLocalTickTime {
			get { return useLocalTickTime; }
		}
	}
}

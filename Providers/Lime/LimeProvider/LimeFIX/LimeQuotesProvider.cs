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
using System.Runtime.InteropServices;
using System.Net;
using TickZoom.Api;
using System.Text;

namespace TickZoom.LimeQuotes
{
    [SkipDynamicLoad]
    public class LimeQuotesProvider : LimeQuoteProviderSupport, LogAware
    {
        private static Log log = Factory.SysLog.GetLogger(typeof(LimeQuotesProvider));
        private static LimeQuotesProvider provider;
        private volatile bool debug;
        private volatile bool trace;

        private object symbolHandlersLocker = new object();
        private Dictionary<long, SymbolHandler> symbolHandlers = new Dictionary<long, SymbolHandler>();
        private Dictionary<long, SymbolHandler> symbolOptionHandlers = new Dictionary<long, SymbolHandler>();

        private Dictionary<string, uint> _SymbolID = new Dictionary<string, uint>();
        private Dictionary<uint, string> _IDToSymbol = new Dictionary<uint, string>();
        private Dictionary<uint, bool> _IDToBookRebuild = new Dictionary<uint, bool>();
        private SimpleLock _SymbolIDLock = new SimpleLock();


        public LimeQuotesProvider(string name)
            : base(name)
        {
            provider = this;
            log.Register(this);
            if (name.Contains(".config"))
            {
                throw new ApplicationException("Please remove .config from config section name.");
            }
            RetryStart = 1;
            RetryIncrease = 1;
            RetryMaximum = 30;
            if (SyncTicks.Enabled)
            {
                HeartbeatDelay = int.MaxValue;
            }
            else
            {
                HeartbeatDelay = 10;
            }
            log.InfoFormat("Constructed LimeQuotesProvider( {0} )", name);
        }

        //TODO: Old code
#if OLD_CODE
        public override void PositionChange(Receiver receiver, SymbolInfo symbol, double signal, Iterable<LogicalOrder> orders)
        {
        }
#endif

        public override void RefreshLogLevel()
        {
            base.RefreshLogLevel();
            debug = log.IsDebugEnabled;
            trace = log.IsTraceEnabled;
        }

        public override void OnDisconnect()
        {
        }

        public override void OnRetry()
        {
        }

        private ushort Swap(ushort value)
        {
            return (ushort)Reverse(value);
        }

        public unsafe override void SendLogin()
        {
            Socket.MessageFactory = new MessageFactoryLimeQuotes();
            LimeQuoteMessage message = (LimeQuoteMessage)Socket.MessageFactory.Create();
            LimeQuotesInterop.login_request_msg* loginRequest =
                (LimeQuotesInterop.login_request_msg*)message.Ptr;
            ushort msgLength = (ushort) sizeof(LimeQuotesInterop.login_request_msg);
            loginRequest->msg_len = Swap(msgLength);
            loginRequest->msg_type = LimeQuotesInterop.limeq_message_type.LOGIN_REQUEST;
            loginRequest->auth_type = LimeQuotesInterop.auth_types.CLEAR_TEXT;

            message.Length = msgLength;
            for (int i = 0; i < UserName.Length; i++)
                loginRequest->uname[i] = (byte)UserName[i];
            for (int i = 0; i < Password.Length; i++)
                loginRequest->passwd[i] = (byte)Password[i];
            for (int i = 0; i < LimeQuotesInterop.HOST_ID_LEN; i++)
                loginRequest->host_id[i] = 0;
            loginRequest->session_type = LimeQuotesInterop.app_type.CPP_API;
            loginRequest->heartbeat_interval = LimeQuotesInterop.heartbeat;
            loginRequest->timeout_interval = LimeQuotesInterop.heartbeatTimeout;
            loginRequest->ver_major = LimeQuotesInterop.majorVersion;
            loginRequest->ver_minor = LimeQuotesInterop.minorVersion;

            if (trace) log.Trace("Sending: " + UserName);
            if (debug) log.Debug("Sending: " + UserName);

            while (!Socket.TrySendMessage(message))
            {
                if (IsInterrupted) return;
                Factory.Parallel.Yield();
            }
        }

        public unsafe override bool VerifyLogin()
        {
            bool verified = false;
            Message message;
            if (!Socket.TryGetMessage(out message)) return verified;
            LimeQuoteMessage limeMessage = (LimeQuoteMessage)message;
            if (limeMessage.MessageType == LimeQuotesInterop.limeq_message_type.LOGIN_RESPONSE)
            {
                LimeQuotesInterop.login_response_msg* response = (LimeQuotesInterop.login_response_msg*)limeMessage.Ptr;
                if (response->response_code == LimeQuotesInterop.reject_reason_code.LOGIN_SUCCEEDED)
                {
                    log.Info("Lime Login verified");
                    verified = true;
                }
                else
                {
                    log.ErrorFormat("Lime Quotes Login Failed: {0}", response->response_code.ToString());
                }

            }
            else if (limeMessage.MessageType == LimeQuotesInterop.limeq_message_type.LIMEQ_CONTROL)
            {
                var response = (LimeQuotesInterop.limeq_control_msg*)limeMessage.Ptr;
                log.InfoFormat("Lime Control {0}", response->code);
            }
            else
                log.ErrorFormat("Lime unexpected message {0}", limeMessage.MessageType);

            return verified;
        }

        public override void OnStartSymbol(SymbolInfo symbol, Agent symbolAgent)
        {
            if (IsRecovering || IsRecovered)
            {
                RequestStartSymbol(symbol, symbolAgent);
            }
        }

        //UNDONE: Must change to match Lime quotes provider
        static readonly string BATS = "BATS"; // Citirus Demo server
        private unsafe void RequestStartSymbol(SymbolInfo symbol, Agent symbolAgent)
        {
            StartSymbolHandler(symbol, symbolAgent);
            if (symbol.OptionChain != OptionChain.None)
            {
                //TODO: Implement options
                throw new NotSupportedException();
                //StartSymbolOptionHandler(symbol, symbolAgent);
            }

            LimeQuoteMessage message = (LimeQuoteMessage)Socket.MessageFactory.Create();
            LimeQuotesInterop.subscription_request_msg* subRequest = (LimeQuotesInterop.subscription_request_msg*)message.Ptr;

            subRequest->msg_type = LimeQuotesInterop.limeq_message_type.SUBSCRIPTION_REQUEST;
            ushort msgLength = (ushort)(sizeof(LimeQuotesInterop.subscription_request_msg) - 64 + symbol.Symbol.Length + 1);
            subRequest->msg_len = Reverse(msgLength);
            message.Length = msgLength;

            //TODO: Fix to use user selected qsid
            for (int i = 0; i < 4; i++)
                subRequest->qsid[i] = (byte)BATS[i];
            subRequest->flags = LimeQuotesInterop.subscription_flags.SUBSCRIPTION_FLAG_MARKET_DATA;
            subRequest->num_symbols = 1;
            for (int i = 0; i < symbol.Symbol.Length; i++)
                subRequest->syb_symbols[i] = (byte)symbol.Symbol[i];
            subRequest->syb_symbols[symbol.Symbol.Length] = 0;
            log.InfoFormat("Sending subscrption request for {0}", symbol.Symbol);
            while (!Socket.TrySendMessage(message))
            {
                if (IsInterrupted) return;
                Factory.Parallel.Yield();
            }

            //TODO: Options not yet implemented
            var item = new EventItem(symbol, EventType.StartBroker);
            symbolAgent.SendEvent(item);

            item = new EventItem(symbol, EventType.StartRealTime);
            symbolAgent.SendEvent(item);
        }

        public override void OnStopSymbol(SymbolInfo symbol, Agent agent)
        {
            RequestStopSymbol(symbol, agent);
        }

        private void RequestStopSymbol(SymbolInfo symbol, Agent symbolAgent)
        {
            SymbolHandler handler = symbolHandlers[symbol.BinaryIdentifier];
            handler.Stop();
            var item = new EventItem(symbol, EventType.EndRealTime);
            symbolAgent.SendEvent(item);

            //TODO: Send unsubscribe to Lime
        }

        protected override void OnStartRecovery()
        {
            SendStartRealTime();
            EndRecovery();
        }

        protected override void ReceiveMessage(LimeQuoteMessage message)
        {
            switch (message.MessageType)
            {
                case LimeQuotesInterop.limeq_message_type.SUBSCRIPTION_REPLY:
                    SubscriptionReply(message);
                    break;
                case LimeQuotesInterop.limeq_message_type.BOOK_REBUILD:
                    BookRebuild(message);
                    break;
                case LimeQuotesInterop.limeq_message_type.TRADE:
                    TradeUpdate(message);
                    break;
                case LimeQuotesInterop.limeq_message_type.ORDER:
                    OrderUpdate(message);
                    break;
                case LimeQuotesInterop.limeq_message_type.MOD_EXECUTION:
                    ModExecution(message);
                    break;
                default:
                    log.InfoFormat("Message {0} not handled", message.MessageType);
                    break;
            }
        }

        private unsafe void ModExecution(LimeQuoteMessage message)
        {
            var modExecute = (LimeQuotesInterop.mod_execution_msg*) message.Ptr;
            uint symbolIndex = Reverse(modExecute->common.symbol_index);
            var symbol = FindSymbol(symbolIndex);
            var symbolInfo = Factory.Symbol.LookupSymbol(symbol);
            var handler = symbolHandlers[symbolInfo.BinaryIdentifier];
            double price = LimeQuoteMessage.priceToDouble(modExecute->common.price_mantissa, modExecute->common.price_exponent);
            uint totalVolume = Reverse(modExecute->total_volume);

            handler.Last = price;
            handler.LastSize = (int)totalVolume;

            if (UseLocalTickTime)
            {
                SetLocalTickTIme(handler);
                if (trace) log.TraceFormat("Trade {0} at {1} ", symbol, price);
                //handler.SendQuote();
            }
            else
            {
                // is simulator.
                var ordertime = modExecute->common.timestamp | ((long)modExecute->common.order_id << 32);
                SetSimulatorTime(handler, ordertime);
                if (trace) log.TraceFormat("Trade {0} at {1} time: {2}", symbol, price, new TimeStamp(ordertime));
                //handler.SendQuote();
            }

            handler.SendTimeAndSales();
        }


        private unsafe void BookRebuild(LimeQuoteMessage message)
        {
            //UNDONE: Use the symbol_len field from the message and the action to filter
            LimeQuotesInterop.book_rebuild_msg* bookMsg = (LimeQuotesInterop.book_rebuild_msg*)message.Ptr;
            int last = 0;
            for (int i = 0; i < LimeQuotesInterop.SYMBOL_LEN; i++)
                if (bookMsg->symbol[i] == 0)
                {
                    last = i;
                    break;
                }
            string symbol = new string((sbyte*)bookMsg->symbol, 0, last);

            log.InfoFormat("Book Rebuild for {0} id {1} flags {2}", symbol, bookMsg->symbol_index, bookMsg->symbol_flags);

            using (_SymbolIDLock.Using())
            {
                uint id = Reverse(bookMsg->symbol_index);
                if (!_SymbolID.ContainsKey(symbol))
                {
                    _SymbolID.Add(symbol, id);
                    _IDToSymbol.Add(id, symbol);
                    _IDToBookRebuild.Add(id, (bookMsg->symbol_flags & 1) == 0 );
                }
                else
                {
                    _SymbolID[symbol] = id;
                    _IDToSymbol[id] = symbol;
                    _IDToBookRebuild[id] = (bookMsg->symbol_flags & 1) == 0;

                }
            }
        }

        private unsafe void SubscriptionReply(LimeQuoteMessage message)
        {
            LimeQuotesInterop.subscription_reply_msg* subReply = (LimeQuotesInterop.subscription_reply_msg*)message.Ptr;
            if (subReply->outcome != LimeQuotesInterop.subscription_outcome.SUBSCRIPTION_SUCCESSFUL)
                log.ErrorFormat("Subscription request failed: error code {0}", subReply->outcome.ToString());
            else
                log.InfoFormat("Subscription started");
        }

        unsafe private string FindSymbol(uint symbolID)
        {
            string symbol = "";
            using (_SymbolIDLock.Using())
            {
                if (!_IDToSymbol.TryGetValue(symbolID, out symbol))
                {
                    log.WarnFormat("Lime Quotes: Unknown Symbol index {0}", symbolID);
                    symbol = "<unknwon>";
                }
            }
            if (trace) log.TraceFormat("Mapped from {0} to {1}", symbolID, symbol);
            return symbol;
        }

        private unsafe void TradeUpdate(LimeQuoteMessage message)
        {
            var trade = (LimeQuotesInterop.trade_msg*)message.Ptr;
            uint symbolIndex = Reverse(trade->common.symbol_index);
            var symbol = FindSymbol(symbolIndex);

            var symbolInfo = Factory.Symbol.LookupSymbol(symbol);
            var handler = symbolHandlers[symbolInfo.BinaryIdentifier];
            double price = LimeQuoteMessage.priceToDouble(trade->common.price_mantissa, trade->common.price_exponent);
            uint totalVolume = Reverse(trade->total_volume);

            handler.Last = price;
            handler.LastSize = (int)totalVolume;

            if (UseLocalTickTime)
            {
                SetLocalTickTIme(handler);
                if (trace) log.TraceFormat("Trade {0} at {1} ", symbol, price);
                //handler.SendQuote();
            }
            else
            {
                // is simulator.
                var ordertime = trade->common.timestamp | ((long)trade->common.order_id << 32);
                SetSimulatorTime(handler, ordertime);
                if (trace) log.TraceFormat("Trade {0} at {1} time: {2}", symbol, price, new TimeStamp(ordertime));
                //handler.SendQuote();
            }

            handler.SendTimeAndSales();
        }

        private unsafe void OrderUpdate(LimeQuoteMessage message)
        {
            LimeQuotesInterop.order_msg* order = (LimeQuotesInterop.order_msg*)message.Ptr;
            bool isTopOfBook = (order->common.quote_flags & 1) == 1;
            uint symbolIndex = Reverse(order->common.symbol_index);
            var symbol = FindSymbol(symbolIndex);
            bool bookUpdate = true;
            if ( !_IDToBookRebuild.TryGetValue( symbolIndex, out bookUpdate ) )
                bookUpdate = true;
            if (isTopOfBook)
            {
                SymbolHandler handler;
                try
                {
                    SymbolInfo symbolInfo = Factory.Symbol.LookupSymbol(symbol);
                    handler = symbolHandlers[symbolInfo.BinaryIdentifier];
                }
                catch (ApplicationException)
                {
                    log.Info("Received tick: " + new string(message.DataIn.ReadChars(message.Remaining)));
                    throw;
                }

                // For the simulator, it should send the Ask first, then the bid.  However, the test code expects
                // both prices to appear in one quote.  So we cache the ask, then send the quote on the bid.
                bool sendQuote = false;
                double price = LimeQuoteMessage.priceToDouble(order->common.price_mantissa, order->common.price_exponent);
                long ordertime = order->common.timestamp | ((long)order->common.order_id << 32);
                if (order->common.side == LimeQuotesInterop.quote_side.SELL)
                {
                    handler.Ask = price;
                    handler.AskSize = (int)Reverse(order->common.shares);
                    sendQuote = true;
                    if (trace) log.TraceFormat("Ask {0} at {1} size {2} time: {3}", symbol, price, handler.BidSize, new TimeStamp(ordertime));
                }
                else
                {
                    handler.Bid = price;
                    handler.BidSize = (int)Reverse(order->common.shares);
                    if (trace) log.TraceFormat("Bid {0} at {1} size {2} time: {3}", symbol, price, handler.BidSize, new TimeStamp(ordertime));
                }

                //TODO: Translate Cirtris timestamp to internal
                if (UseLocalTickTime)
                {
                    if (trace) log.TraceFormat("{0}: Bid {1} Ask: {2} BidShares {3} AskShares: {4}", symbol,
                      handler.Bid, handler.Ask, handler.BidSize, handler.AskSize);
                    SetLocalTickTIme(handler);
                    handler.SendQuote();
                }
                else
                {
                    if (sendQuote)
                    {
                        // is simulator.
                        SetSimulatorTime(handler, ordertime);
                        handler.SendQuote();
                    }
                }
            }
            else
                if (trace) log.TraceFormat("Quote not top of book");
        }

        unsafe private static void SetSimulatorTime(SymbolHandler handler, long ordertime)
        {
            var currentTime = new TimeStamp(ordertime);
            if (currentTime <= handler.Time)
            {
                currentTime.Internal = handler.Time.Internal + 1;
            }
            handler.Time = currentTime;
        }

        unsafe private static void SetLocalTickTIme(SymbolHandler handler)
        {
            var currentTime = TimeStamp.UtcNow;
            if (currentTime <= handler.Time)
            {
                currentTime.Internal = handler.Time.Internal + 1;
            }
            handler.Time = currentTime;
        }

        private unsafe void TimeAndSalesUpdate(LimeQuoteMessage message)
        {
            throw new NotImplementedException();
        }

        private void OnException(Exception ex)
        {
            log.Error("Exception occurred", ex);
        }

        private void SendStartRealTime()
        {
            lock (symbolsRequestedLocker)
            {
                foreach (var kvp in symbolsRequested)
                {
                    var symbol = kvp.Value;
                    RequestStartSymbol(symbol.Symbol, symbol.Agent);
                }
            }
        }

        private void SendEndRealTime()
        {
            lock (symbolsRequestedLocker)
            {
                foreach (var kvp in symbolsRequested)
                {
                    var symbol = kvp.Value;
                    RequestStopSymbol(symbol.Symbol, symbol.Agent);
                }
            }
        }

        private void StartSymbolHandler(SymbolInfo symbol, Agent agent)
        {
            lock (symbolHandlersLocker)
            {
                SymbolHandler symbolHandler;
                if (symbolHandlers.TryGetValue(symbol.BinaryIdentifier, out symbolHandler))
                {
                    symbolHandler.Start();
                }
                else
                {
                    symbolHandler = Factory.Utility.SymbolHandler(symbol, agent);
                    symbolHandlers.Add(symbol.BinaryIdentifier, symbolHandler);
                    symbolHandler.Start();
                }
            }
        }

        private void StartSymbolOptionHandler(SymbolInfo symbol, Agent agent)
        {
            lock (symbolHandlersLocker)
            {
                SymbolHandler symbolHandler;
                if (symbolOptionHandlers.TryGetValue(symbol.BinaryIdentifier, out symbolHandler))
                {
                    symbolHandler.Start();
                }
                else
                {
                    symbolHandler = Factory.Utility.SymbolHandler(symbol, agent);
                    symbolOptionHandlers.Add(symbol.BinaryIdentifier, symbolHandler);
                    symbolHandler.Start();
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (debug)
            {
                foreach (var handler in symbolHandlers)
                {
                    log.Debug(handler.Value.Symbol + " received " + handler.Value.TickCount + " ticks.");
                }
            }
            base.Dispose(disposing);
        }

        // reverse byte order (16-bit)
        public static UInt16 Reverse(UInt16 value)
        {
            return (UInt16)((value & 0xFFU) << 8 | (value & 0xFF00U) >> 8);
        }

        // reverse byte order (32-bit)
        public static UInt32 Reverse(UInt32 value)
        {
            return (value & 0x000000FFU) << 24 | (value & 0x0000FF00U) << 8 |
                   (value & 0x00FF0000U) >> 8 | (value & 0xFF000000U) >> 24;
        }

        // reverse byte order (64-bit)
        public static UInt64 Reverse(UInt64 value)
        {
            return (value & 0x00000000000000FFUL) << 56 | (value & 0x000000000000FF00UL) << 40 |
                   (value & 0x0000000000FF0000UL) << 24 | (value & 0x00000000FF000000UL) << 8 |
                   (value & 0x000000FF00000000UL) >> 8 | (value & 0x0000FF0000000000UL) >> 24 |
                   (value & 0x00FF000000000000UL) >> 40 | (value & 0xFF00000000000000UL) >> 56;
        }

        private static string GetSymbol(uint index)
        {
            using (provider._SymbolIDLock.Using())
            {
                uint id = Reverse(index);
                string symbol;
                if (provider._IDToSymbol.TryGetValue(id, out symbol))
                    return symbol;
                return string.Format("unknown({0})", id);

            }
        }

        public unsafe static void LogMessage(byte* message, Log log)
        {
          
            ushort length = (ushort)((message[0] << 8) | message[1]);
            if (length < 3)
                return;

            var messageType = (LimeQuotesInterop.limeq_message_type)message[2];
            string logMessage = "";
            string symbol = "";
            switch (messageType)
            {
                case LimeQuotesInterop.limeq_message_type.LOGIN_RESPONSE:
                    var lrm = (LimeQuotesInterop.login_response_msg*)message;
                    logMessage = string.Format("Login Response ({1} bytes) Ver={5}.{6}, Resp={3}, heartbeat={2}, timeout={5}", messageType, length,
                        lrm->heartbeat_interval, lrm->response_code, lrm->timeout_interval,
                        lrm->ver_major, lrm->ver_minor);
                    break;
                case LimeQuotesInterop.limeq_message_type.BOOK_REBUILD:
                    var br = (LimeQuotesInterop.book_rebuild_msg*)message;
                    int symLength = 0;
                    for ( int i = 0; i < 21; i++ )
                        if (br->symbol[i] == 0)
                        {
                            symLength = i;
                            break;
                        }
                    symbol = new string((sbyte*)br->symbol, 0, symLength);
                    logMessage = string.Format("Book rebuild symbol '{0}' flags: {1} index {2}",
                       symbol, br->symbol_flags, Reverse(br->symbol_index));
                    
                    break;
                case LimeQuotesInterop.limeq_message_type.ORDER:
                    var order = (LimeQuotesInterop.order_msg*)message;
                    logMessage = string.Format("Order id {0}: symbol '{1}' price {6} timestamp {2} side {3} shares {4} flags {5}",
                        order->common.order_id, GetSymbol(order->common.symbol_index), order->common.timestamp, order->common.side,
                        Reverse(order->common.shares), order->common.quote_flags,
                        LimeQuoteMessage.priceToDouble(order->common.price_mantissa, order->common.price_exponent));
                    break;
                case LimeQuotesInterop.limeq_message_type.TRADE:
                    var trade = (LimeQuotesInterop.trade_msg*)message;
                    logMessage = string.Format("Trade id {0}: symbol '{1}' price {10} timestamp {2} size {3} shared {4} flags {5} fill {6} fill2 {7} flags {8} total vol {9}",
                        trade->common.order_id, GetSymbol(trade->common.symbol_index), trade->common.timestamp, trade->common.side,
                        Reverse(trade->common.shares), trade->common.quote_flags,
                        trade->fill1, Reverse(trade->fill2), trade->flags,
                        Reverse(trade->total_volume),
                        LimeQuoteMessage.priceToDouble(trade->common.price_mantissa, trade->common.price_exponent)
                        );
                    break;
                default:
                    logMessage = string.Format("Message {0} ({1} bytes)", messageType, length );

                    break;
            }
            log.Trace(logMessage);
            log.Trace( "Message HexDump: " + Environment.NewLine + HexDump( message, length, 32 ) );
        }

        //Source: http://www.codeproject.com/Articles/36747/Quick-and-Dirty-HexDump-of-a-Byte-Array
        public static unsafe string HexDump(byte* bytes, int bytesLength, int bytesPerLine)
        {
            if (bytes == null) return "<null>";
            if (bytesPerLine == 0)
                bytesPerLine = 32;

            char[] HexChars = "0123456789ABCDEF".ToCharArray();

            int firstHexColumn =
                  8                   // 8 characters for the address
                + 3;                  // 3 spaces

            int firstCharColumn = firstHexColumn
                + bytesPerLine * 3       // - 2 digit for the hexadecimal value and 1 space
                + (bytesPerLine - 1) / 8 // - 1 extra space every 8 characters from the 9th
                + 2;                  // 2 spaces 

            int lineLength = firstCharColumn
                + bytesPerLine           // - characters to show the ascii value
                + Environment.NewLine.Length; // Carriage return and line feed (should normally be 2)

            char[] line = (new String(' ', lineLength - 2) + Environment.NewLine).ToCharArray();
            int expectedLines = (bytesLength + bytesPerLine - 1) / bytesPerLine;
            StringBuilder result = new StringBuilder(expectedLines * lineLength);

            for (int i = 0; i < bytesLength; i += bytesPerLine)
            {
                line[0] = HexChars[(i >> 28) & 0xF];
                line[1] = HexChars[(i >> 24) & 0xF];
                line[2] = HexChars[(i >> 20) & 0xF];
                line[3] = HexChars[(i >> 16) & 0xF];
                line[4] = HexChars[(i >> 12) & 0xF];
                line[5] = HexChars[(i >> 8) & 0xF];
                line[6] = HexChars[(i >> 4) & 0xF];
                line[7] = HexChars[(i >> 0) & 0xF];

                int hexColumn = firstHexColumn;
                int charColumn = firstCharColumn;

                for (int j = 0; j < bytesPerLine; j++)
                {
                    if (j > 0 && (j & 7) == 0) hexColumn++;
                    if (i + j >= bytesLength)
                    {
                        line[hexColumn] = ' ';
                        line[hexColumn + 1] = ' ';
                        line[charColumn] = ' ';
                    }
                    else
                    {
                        byte b = bytes[i + j];
                        line[hexColumn] = HexChars[(b >> 4) & 0xF];
                        line[hexColumn + 1] = HexChars[b & 0xF];
                        line[charColumn] = (b < 32 ? '·' : (char)b);
                    }
                    hexColumn += 3;
                    charColumn++;
                }
                result.Append(line);
            }
            return result.Remove( result.Length-2, 2).ToString();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using TickZoom.Api;
using TickZoom.FIX;
using TickZoom.LimeQuotes;

namespace TickZoom.LimeFIX
{
    public class LimeQuotesSimulator : QuoteSimulatorSupport 
    {
        private static Log log = Factory.SysLog.GetLogger(typeof (LimeQuotesSimulator));
        private volatile bool debug;
        private volatile bool trace;
        public override void RefreshLogLevel() {
            base.RefreshLogLevel();
            debug = log.IsDebugEnabled;
            trace = log.IsTraceEnabled;
        }

        public LimeQuotesSimulator(string mode, ProjectProperties projectProperties,
                                   ProviderSimulatorSupport providerSimulator)
            : base(providerSimulator, 6488, new MessageFactoryLimeQuotes()) 
        {
            log.Register(this);

        }

        protected override void ParseQuotesMessage(Message quoteReadMessage)
        {
            var limeMessage = (LimeQuoteMessage)quoteReadMessage;

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

        private unsafe void QuotesLogin(LimeQuoteMessage packetQuotes)
        {
            LimeQuotesInterop.login_request_msg* message = (LimeQuotesInterop.login_request_msg*)packetQuotes.Ptr;
            if (Reverse( message->msg_len ) != 80 || message->msg_type != LimeQuotesInterop.limeq_message_type.LOGIN_REQUEST ||
                message->ver_major != LimeQuotesInterop.LIMEQ_MAJOR_VER ||
                message->ver_minor != LimeQuotesInterop.LIMEQ_MINOR_VER ||
                message->session_type != LimeQuotesInterop.app_type.CPP_API ||
                message->auth_type != LimeQuotesInterop.auth_types.CLEAR_TEXT ||
                message->heartbeat_interval != LimeQuotesInterop.heartbeat)
                log.Error("Login message not matched");
            string userName = ""; ;
            for (int i = 0; i < LimeQuotesInterop.UNAME_LEN && message->uname[i] > 0; i++)
                userName += (char)message->uname[i];
            string password = ""; ;
            for (int i = 0; i < LimeQuotesInterop.PASSWD_LEN && message->passwd[i] > 0; i++)
                password += (char)message->passwd[i];

            var writePacket = (LimeQuoteMessage)QuoteSocket.MessageFactory.Create();
            LimeQuotesInterop.login_response_msg* reseponse = (LimeQuotesInterop.login_response_msg*)writePacket.Ptr;
            reseponse->msg_type = LimeQuotesInterop.limeq_message_type.LOGIN_RESPONSE;
            reseponse->msg_len = Reverse(8);
            writePacket.Length = 8;
            reseponse->ver_minor = message->ver_minor;
            reseponse->ver_major = message->ver_major;
            reseponse->heartbeat_interval = message->heartbeat_interval;
            reseponse->timeout_interval = message->timeout_interval;
            reseponse->response_code = LimeQuotesInterop.reject_reason_code.LOGIN_SUCCEEDED;

            QuotePacketQueue.Enqueue(writePacket, packetQuotes.SendUtcTime);
        }

        // reverse byte order (16-bit)
        public static UInt16 Reverse(UInt16 value)
        {
            return (UInt16)((value & 0xFFU) << 8 | (value & 0xFF00U) >> 8);
        }


        private unsafe void SymbolRequest(LimeQuoteMessage message)
        {
            LimeQuotesInterop.subscription_request_msg* subRequest = (LimeQuotesInterop.subscription_request_msg*)message.Ptr;
            String symbol = "";
            for (int i = 0; subRequest->syb_symbols[i] != 0; i++)
                symbol += (char)subRequest->syb_symbols[i];

            var symbolInfo = Factory.Symbol.LookupSymbol(symbol);
            log.Info("Lime: Received symbol request for " + symbolInfo);

            ProviderSimulator.AddSymbol(symbolInfo.Symbol);
 
            var writePacket = (LimeQuoteMessage)QuoteSocket.MessageFactory.Create();
            LimeQuotesInterop.subscription_reply_msg* reply = (LimeQuotesInterop.subscription_reply_msg*)writePacket.Ptr;
            reply->msg_type = LimeQuotesInterop.limeq_message_type.SUBSCRIPTION_REPLY;
            var msg_len = (ushort)sizeof(LimeQuotesInterop.subscription_reply_msg);
            reply->msg_len = Reverse(msg_len);
            writePacket.Length = msg_len;
            reply->outcome = LimeQuotesInterop.subscription_outcome.SUBSCRIPTION_SUCCESSFUL;
            for (int i = 0; i < 4; i++)
                reply->qsid[i] = subRequest->qsid[i];

            QuotePacketQueue.Enqueue(writePacket, message.SendUtcTime);


            var bookRebuildMessage = (LimeQuoteMessage)QuoteSocket.MessageFactory.Create();
            LimeQuotesInterop.book_rebuild_msg* book = (LimeQuotesInterop.book_rebuild_msg*)bookRebuildMessage.Ptr;
            book->msg_type = LimeQuotesInterop.limeq_message_type.BOOK_REBUILD;
            msg_len = (ushort)sizeof(LimeQuotesInterop.book_rebuild_msg);
            book->msg_len = Reverse(msg_len);
            bookRebuildMessage.Length = msg_len;
            book->symbol_index = (uint)symbolInfo.BinaryIdentifier;
            for (int i = 0; i < symbol.Length; i++)
                book->symbol[i] = (byte)symbol[i];
            QuotePacketQueue.Enqueue(bookRebuildMessage, message.SendUtcTime);

        }

        private Dictionary<long,bool> isFirstTick = new Dictionary<long, bool>();
        protected override void TrySendTick(SymbolInfo symbol, TickIO tick) {
            SendSide(symbol, tick, true);
            if (tick.IsQuote) {
#if NOTUSED
                bool result;
                if( !isFirstTick.TryGetValue(symbol.BinaryIdentifier, out result)) {
                    isFirstTick.Add(symbol.BinaryIdentifier,true);
                }
                else
                {
                    var tickSync = SyncTicks.GetTickSync(symbol.BinaryIdentifier);
                    tickSync.AddTick(tick);
                }
#endif
                SendSide(symbol, tick, false);
            }
            var lastTick = lastTicks[symbol.BinaryIdentifier];
            lastTick.Inject(tick.Extract());
        }

        unsafe private void SendSide(SymbolInfo symbol, TickIO tick, bool isSell)
        {
            var symbolID = symbol.BinaryIdentifier;
            var lastTick = lastTicks[symbol.BinaryIdentifier];
            var message = QuoteSocket.MessageFactory.Create();
            var quoteMessage = (LimeQuoteMessage)message;
            var isTrade = tick.IsTrade;
            var isQuote = tick.IsQuote;
            if ( trace ) log.TraceFormat("quote isTrade={0} isQuote={1}", isTrade, isQuote);
            if (isTrade)
            {
                var trade = (LimeQuotesInterop.trade_msg*)quoteMessage.Ptr;
                trade->common.msg_type = LimeQuotesInterop.limeq_message_type.TRADE;
                var msg_len = (ushort) sizeof (LimeQuotesInterop.trade_msg);
                trade->common.msg_len = Reverse(msg_len);
                quoteMessage.Length = msg_len;
                trade->common.shares = LimeQuotesProvider.Reverse( (uint)tick.Size );
                trade->total_volume =  LimeQuotesProvider.Reverse( (uint)tick.Volume );
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

                trade->common.symbol_index = (uint) symbolID;

                // We steal the order_id field to send the upper 32 bits of the timaestamp
                trade->common.timestamp = (uint)tick.UtcTime.Internal;
                trade->common.order_id = (uint)(tick.UtcTime.Internal >> 32);
            }
            else if (isQuote) {
                bool priceChanged = true;
                if (isSell && tick.Bid != lastTick.Bid)
                    priceChanged = true;
                else if (!isSell && tick.Ask != lastTick.Ask)
                    priceChanged = true;
                if (priceChanged) {
                    var order = (LimeQuotesInterop.order_msg*) quoteMessage.Ptr;
                    order->common.msg_type = LimeQuotesInterop.limeq_message_type.ORDER;
                    var msg_len = (ushort) sizeof (LimeQuotesInterop.order_msg);
                    order->common.msg_len = Reverse(msg_len);
                    quoteMessage.Length = msg_len;
                    order->common.quote_flags = 1;
                    order->common.shares = LimeQuotesProvider.Reverse( (uint) Math.Max(tick.Size, 1 ));
                    Int32 mantissa;
                    sbyte exponent;
                    if (isSell)
                        order->common.side = LimeQuotesInterop.quote_side.SELL;
                    else
                        order->common.side = LimeQuotesInterop.quote_side.BUY;

                    if (order->common.side == LimeQuotesInterop.quote_side.BUY) {
                        LimeQuoteMessage.DoubleToPrice(tick.Bid, out mantissa, out exponent);
                        order->common.price_mantissa = mantissa;
                        order->common.price_exponent = exponent;
                        if (trace) log.TraceFormat("Sending Ask {0}", tick.Bid);
                    } else if (order->common.side == LimeQuotesInterop.quote_side.SELL) {
                        LimeQuoteMessage.DoubleToPrice(tick.Ask, out mantissa, out exponent);
                        order->common.price_mantissa = mantissa;
                        order->common.price_exponent = exponent;
                        if (trace) log.TraceFormat("Sending Bid {0}", tick.Ask);
                    }

                    order->common.symbol_index = (uint) symbolID;

                    // We steal the order_id field to send the upper 32 bits of the timaestamp
                    order->common.timestamp = (uint) tick.UtcTime.Internal;
                    order->common.order_id = (uint) (tick.UtcTime.Internal >> 32);

                    QuotePacketQueue.Enqueue(quoteMessage, tick.UtcTime.Internal);
                    if (trace) log.Trace("Enqueued tick packet: " + new TimeStamp(tick.UtcTime.Internal));
                } 

            } else
                throw new NotImplementedException("Tick is neither Trade nor Quote");
        }
    }
}

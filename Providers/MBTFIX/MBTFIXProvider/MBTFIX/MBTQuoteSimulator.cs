using System;
using System.Text;
using TickZoom.Api;
using TickZoom.FIX;
using TickZoom.MBTQuotes;

namespace TickZoom.MBTFIX
{
    public class MBTQuoteSimulator : QuoteSimulatorSupport
    {
        private static Log log = Factory.SysLog.GetLogger(typeof(MBTQuoteSimulator));
        private volatile bool debug;
        private volatile bool trace;
        public override void RefreshLogLevel()
        {
            base.RefreshLogLevel();
            debug = log.IsDebugEnabled;
            trace = log.IsTraceEnabled;
        }

        public MBTQuoteSimulator(string mode, ProjectProperties projectProperties, ProviderSimulatorSupport providerSimulator)
            : base(providerSimulator, 6488, new MessageFactoryMbtQuotes())
        {
            log.Register(this);
            InitializeSnippets();
        }

        protected override void ParseQuotesMessage(Message message)
        {
            var packetQuotes = (MessageMbtQuotes)message;
            char firstChar = (char)packetQuotes.Data.GetBuffer()[packetQuotes.Data.Position];
            switch (firstChar)
            {
                case 'L': // Login
                    QuotesLogin(packetQuotes);
                    break;
                case 'S':
                    SymbolRequest(packetQuotes);
                    break;
                case '9':
                    RespondPing();
                    break;
                default:
                    throw new ArgumentOutOfRangeException("Unexpected quotes message: " + firstChar);
            }
        }

        private void QuotesLogin(MessageMbtQuotes message)
        {
            var writePacket = QuoteSocket.MessageFactory.Create();
            string textMessage = "G|100=DEMOXJSP;8055=demo01\n";
            if (debug) log.Debug("Login response: " + textMessage);
            writePacket.DataOut.Write(textMessage.ToCharArray());
            QuotePacketQueue.Enqueue(writePacket, message.SendUtcTime);
        }

        private void SymbolRequest(MessageMbtQuotes message)
        {
            var symbolInfo = Factory.Symbol.LookupSymbol(message.Symbol);
            log.Info("Received symbol request for " + symbolInfo);
            ProviderSimulator.AddSymbol(symbolInfo.Symbol);
            switch (message.FeedType)
            {
                case "20000": // Level 1
                    if (symbolInfo.QuoteType != QuoteType.Level1)
                    {
                        throw new ApplicationException("Requested data feed of Level1 but Symbol.QuoteType is " + symbolInfo.QuoteType);
                    }
                    break;
                case "20001": // Level 2
                    if (symbolInfo.QuoteType != QuoteType.Level2)
                    {
                        throw new ApplicationException("Requested data feed of Level2 but Symbol.QuoteType is " + symbolInfo.QuoteType);
                    }
                    break;
                case "20002": // Level 1 & Level 2
                    if (symbolInfo.QuoteType != QuoteType.Level2)
                    {
                        throw new ApplicationException("Requested data feed of Level1 and Level2 but Symbol.QuoteType is " + symbolInfo.QuoteType);
                    }
                    break;
                case "20003": // Trades
                    if (symbolInfo.TimeAndSales != TimeAndSales.ActualTrades)
                    {
                        throw new ApplicationException("Requested data feed of Trades but Symbol.TimeAndSale is " + symbolInfo.TimeAndSales);
                    }
                    break;
                case "20004": // Option Chains
                    break;
                default:
                    throw new ApplicationException("Sorry, unknown data type: " + message.FeedType);
            }
        }

        private void RespondPing()
        {
            var writePacket = QuoteSocket.MessageFactory.Create();
            string textMessage = "9|\n";
            writePacket.DataOut.Write(textMessage.ToCharArray());
            if (QuoteSocket.TrySendMessage(writePacket))
            {
                if (trace) log.Trace("Local Write: " + writePacket);
            }
        }

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
            for (var i = 0; i < tradeSnippets.Length; i++)
            {
                tradeSnippetBytes[i] = new byte[tradeSnippets[i].Length];
                for (var pos = 0; pos < tradeSnippets[i].Length; pos++)
                {
                    tradeSnippetBytes[i][pos] = (byte)tradeSnippets[i][pos];
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
            for (var i = 0; i < 9 && value > 0L; i++)
            {
                var digit = value % 10;
                value /= 10;
                if (digit > 0L || anydigit)
                {
                    anydigit = true;
                    bytebuffer[pos] = (byte)('0' + digit);
                    pos++;
                }
            }
            if (pos > 0)
            {
                bytebuffer[pos] = (byte)'.';
                pos++;
                if (value == 0L)
                {
                    bytebuffer[pos] = (byte)'0';
                    pos++;
                }
            }
            while (value > 0)
            {
                var digit = value % 10;
                value /= 10;
                bytebuffer[pos] = (byte)('0' + digit);
                pos++;
            }
            return pos;
        }

        protected override void TrySendTick(SymbolInfo symbol, TickIO tick)
        {
            if (trace) log.Trace("TrySendTick( " + symbol + " " + tick + ")");
            var quoteMessage = QuoteSocket.MessageFactory.Create();
            var lastTick = lastTicks[symbol.BinaryIdentifier];
            var buffer = quoteMessage.Data.GetBuffer();
            var position = quoteMessage.Data.Position;
            quoteMessage.Data.SetLength(1024);
            if (tick.IsTrade)
            {
                var index = 0;
                var snippet = tradeSnippetBytes[index];
                Array.Copy(snippet, 0, buffer, position, snippet.Length);
                position += snippet.Length;

                // Symbol
                var value = symbol.Symbol.ToCharArray();
                for (var i = 0; i < value.Length; i++)
                {
                    buffer[position] = (byte)value[i];
                    ++position;
                }

                ++index; snippet = tradeSnippetBytes[index];
                Array.Copy(snippet, 0, buffer, position, snippet.Length);
                position += snippet.Length;

                // Price
                var len = ConvertPriceToBytes(tick.lPrice);
                var pos = len;
                for (var i = 0; i < len; i++)
                {
                    buffer[position] = (byte)bytebuffer[--pos];
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
                if (askSize != lastTick.AskLevel(0))
                {
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
                if (bidSize != lastTick.BidLevel(0))
                {
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

            if (trace)
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
            QuotePacketQueue.Enqueue(quoteMessage, quoteMessage.SendUtcTime);
            if (trace) log.Trace("Enqueued tick packet: " + new TimeStamp(quoteMessage.SendUtcTime));
        }

    }
}
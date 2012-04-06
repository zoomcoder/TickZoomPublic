using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Runtime.InteropServices;

using TickZoom.Api;
using System.Net;

namespace TickZoom.LimeQuotes
{
    public unsafe class LimeQuoteMessage :Message,IDisposable
    {
        private static readonly Log log = Factory.SysLog.GetLogger(typeof(LimeQuoteMessage));
        private static readonly bool trace = log.IsTraceEnabled;
        
        static int NextMessageID = 1;
        protected int MessageID;
        const int MaxMessageSize = 4096;
        byte[] _MessageBuffer;        // Pre allocated buffer
        GCHandle _Handle ;
        byte* _MessagePtr;
        MemoryStream _Data;
        ushort _Length;

        public LimeQuoteMessage()
        {
            BuildMessageBuffer(MaxMessageSize, false);
            MessageID = NextMessageID++;
        }

        private void BuildMessageBuffer(int size, bool realloc)
        {
            if ( realloc) {
                _Handle.Free();
            }
            _MessageBuffer = new byte[size];
            _Handle = GCHandle.Alloc(_MessageBuffer, GCHandleType.Pinned);
            _MessagePtr = (byte*)_Handle.AddrOfPinnedObject();
            _Data = new MemoryStream(_MessageBuffer, 0, size, true, true);
        }

        #region IDisposable Members

        public void Dispose()
        {
            _Data.Dispose();
            _Handle.Free();
        }

        #endregion

        public unsafe byte* Ptr { get { return _MessagePtr; } }

        #region Message Members

        public void Clear()
        {
            Array.Clear(_MessageBuffer, 0, (int) _Data.Length);
            _Data.Position = 0;
            _Data.SetLength(3);
            _Length = 0;
        }

        internal LimeQuotesInterop.limeq_message_type MessageType
        {
            get { 
                long position = _Data.Position;
                _Data.Position = 2;

                LimeQuotesInterop.limeq_message_type msgType = (LimeQuotesInterop.limeq_message_type)_Data.ReadByte();

                _Data.Position = position;

                return msgType;
            } 
        }
        public void BeforeWrite()
        {
            _Data.Position = 0;
            _Data.SetLength(0);
        }

        public void BeforeRead()
        {
            _Data.Position = 0;
        }
		

        public void CreateHeader(int packetCounter)
        {
        }

        public void Verify()
        {
        }

        public void SetReadableBytes(int bytes)
        {
            if (trace) log.Trace("SetReadableBytes(" + bytes + ")");
            _Data.SetLength(_Data.Position + bytes);
        }

        public bool TrySplit(MemoryStream other)
        {
            bool split = false;
            if (other.Length > 3)
            {
                long position = other.Position;
                //ushort length = (ushort)(other.ReadByte() | other.ReadByte() << 8);

                ushort length = (ushort)(((ushort)other.ReadByte() << 8) | other.ReadByte());
                other.Position = position;
                if (other.Length >= position + length)
                {
                    if (length >= MaxMessageSize)
                    {
                        log.WarnFormat("Lime Message of {0} bytes is larger the max message size of {1}.  Buffer reallocated", length, MaxMessageSize);
                        // Build a new message buffer with a little room to spare.
                        BuildMessageBuffer(length + 128, true);
                    }
                    split = true;
                    _Length = length;
                    _Data.SetLength(_Length);
                    _Data.Position = 0;

                    // Read must be last as SetLength clears the buffer :(
                    other.Read(_MessageBuffer, 0, length);
                    if (trace) LimeQuotesProvider.LogMessage(Ptr, log);
                }
            }
            return split;
        }

        public bool IsComplete
        {
            get { throw new NotImplementedException(); }
        }

        public int Id
        {
            get { throw new NotImplementedException(); }
        }

        public int Remaining
        {
            get { return Length - Position; }
        }

        public bool HasAny
        {
            get { throw new NotImplementedException(); }
        }

        public bool IsFull
        {
            get { throw new NotImplementedException(); }
        }

        public int Position
        {
            get;
            set;
        }

        public int Length
        {
            get { return _Length; }
            set { 
                _Length = (ushort) value;
                _Data.SetLength(value);
            }
        }

        public long GetTickUtcTime()
        {
            throw new NotImplementedException();
        }
 

        public long SendUtcTime
        {
            get;
            set;
        }

        public long RecvUtcTime
        {
            get;
            set;

        }

        public BinaryReader DataIn
        {
            get { throw new NotImplementedException(); }
        }

        public BinaryWriter DataOut
        {
            get { throw new NotImplementedException(); }
        }

        public MemoryStream Data
        {
            get { return _Data; }
        }

        #endregion

        #region Lime Price Conversion
        static readonly double[] powers_of_ten = {
            1.0,
            10.0,
            100.0,
            1000.0,
            10000.0,
            100000.0,
            1000000.0,
            10000000.0,
            100000000.0,
            1000000000.0
        };

        /* Not Part of API
         *  Converts a price and exponent into a double using the powers_of_ten[]
         *  table.
         *
         * @param price
         *  The mantissa part of a price, where <tt>price = mantissa x
         *  10^exponent</tt>.
         *
         * @param exponent
         *  The exponents part of a price, where <tt>price = mantissa x
         *  10^exponent</tt>.
         *
         * @return
         *  The double representing the price where <tt>price = mantissa x
         *  10^exponent</tt>.
         */
        internal static double priceToDouble(Int32 price, sbyte exponent)
        {
            price = IPAddress.HostToNetworkOrder(price);

            Int32 max_exponent = powers_of_ten.Length;
            if (exponent < -1 * max_exponent || exponent > max_exponent)
            {
                //Invalid price conversion
                return -1;
            }
            if (exponent < 0)
            {
                return (double)price / powers_of_ten[-1 * exponent];
            }
            return (double)price * powers_of_ten[exponent];

        }

        internal static void DoubleToPrice(double price, out Int32 mantissa, out sbyte exponent)
        {
            exponent = 0;
            while (Math.Round( price - Math.Floor(price), 8) != 0)
            {
                price = Math.Round(price * 10, 8);
                exponent--;
            }
            mantissa = IPAddress.NetworkToHostOrder( (Int32)price );
        }
        #endregion

        public override string ToString()
        {
            return String.Format("Lime Message {0} of {1} bytes",
               (LimeQuotesInterop.limeq_message_type)MessageType,
               Length);
        }

    }
}

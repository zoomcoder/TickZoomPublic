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
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

using TickZoom.Api;

namespace TickZoom.FIX
{
    public unsafe class MessageFIXT1_1 : Message
    {
		private const byte EndOfField = 1;
		private const byte NegativeSign = (byte) '-';
		private const byte DecimalPoint = 46;
		private const byte EqualSign = 61;
		private const byte ZeroChar = 48;
		private const int maxSize = 4096;
		private static readonly Log log = Factory.SysLog.GetLogger(typeof(MessageFIXT1_1));
		private static readonly bool debug = log.IsDebugEnabled;
		private static readonly bool trace = log.IsTraceEnabled;
        private static readonly bool verbose = log.IsVerboseEnabled;
        private MemoryStream data = new MemoryStream();
		private BinaryReader dataIn;
		private BinaryWriter dataOut;
		private static int packetIdCounter = 0;
        private int heartBeatInterval = 0;
        private string encryption = null;
        private int id = 0;
        private byte* ptr;
        private byte* end;
        private long transactTime;
        private long sendUtcTime;
        private long recvUtcTime;
		private string version = null;
		private int begSeqNum;
		private int endSeqNum;
        string text = null;
        private int length = 0;
		private string messageType = null;
		private string sender = null;
		private string target = null;
		private int sequence = 0;
        private int referenceSequence = 0;
        private string timeStamp = null;
		private int checkSum = 0;
		private bool isPossibleDuplicate = false;
		public static bool IsQuietRecovery = false;
        private int newSeqNum;
        private bool isGapFill;
        private bool isResetSeqNum;
        //public SimpleLock sequenceLocker = new SimpleLock();
		
		public MessageFIXT1_1()
		{
			dataIn = new BinaryReader(data, Encoding.ASCII);
			dataOut = new BinaryWriter(data, Encoding.ASCII);
            Clear();
		}

        public virtual void Clear()
        {
			id = ++packetIdCounter;
            data.Position = 0;
            data.SetLength(0);
            transactTime = 0L;
            sendUtcTime = 0L;
            recvUtcTime = 0L;
            heartBeatInterval = 0;
            isResetSeqNum = false;
            encryption = null;
		    version = null;
            text = null;
            begSeqNum = 0;
		    endSeqNum = 0;
		    length = 0;
		    messageType = null;
            //if (sequence > 0 && sequenceLocker.IsLocked)
            //{
            //    throw new InvalidOperationException("Can't clear from " + sender + " sequence " + sequence + " yet.");
            //}
            sender = null;
		    target = null;
		    sequence = 0;
		    timeStamp = null;
		    checkSum = 0;
		    isPossibleDuplicate = false;
        }

        public void SetReadableBytes(int bytes)
        {
            //if( trace) log.Trace("SetReadableBytes(" + bytes + ")");
            //data.SetLength( data.Position + bytes);
		}
	
		public void Verify() {
			
		}
		
		public void BeforeWrite() {
			data.Position = 0;
			data.SetLength(0);
		}
		
		public void BeforeRead() {
			data.Position = 0;
		}
		
		public override string ToString()
		{
		    var position = data.Position;
			data.Position = 0;
			string response = new string(dataIn.ReadChars((int)data.Length));
			//var result = response.Replace(FIXTBuffer.EndFieldStr,"  ");
		    data.Position = position;
		    return response;
		} 
		
		public int Remaining {
			get { return Length - Position; }
		}
		
		public bool IsFull {
			get { return Length > 0; }
		}
		
		public bool HasAny {
			get { return Length - 0 > 0; }
		}
		
		public unsafe void CreateHeader(int counter) {
		}
		
		private int FindSplitAt(MemoryStream buffer) {
		    if( verbose) log.Verbose("Processing Keys: " + this);
		    var position = (int) buffer.Position;
		    var handle = GCHandle.Alloc(buffer.GetBuffer(), GCHandleType.Pinned);
		    var beg = ptr = (byte*) handle.AddrOfPinnedObject() + position;
            end = (byte*)handle.AddrOfPinnedObject() + buffer.Length;
            try
            {
			    int key;
			    while( ptr < end && GetKey( out key)) {
                    if (verbose) log.Verbose("HandleKey(" + key + ")");
				    HandleKey(key);
				    if( key == 10 ) {
					    isComplete = true;
				        position = (int) (ptr - beg);
					    if( verbose) log.Verbose("Copying buffer at " + position);
					    return position;
				    }
			    }
			    // Never found a complete checksum tag so we need more bytes.
			    isComplete = false;
			    return 0;
            }
            catch( Exception ex)
            {
                log.Error("TrySplitFailed() data.Position " + data.Position + ", data.Length " + data.Length + ", ptr offset " + (ptr - beg) + ", end offset " + (end - beg) + ". Packet contents follow:\n" + ToHex());
                throw new ApplicationException("FindSplitAt failed.", ex);
            }
            finally
            {
                handle.Free();
            }
		}
		
		private bool isComplete;
		
		public bool IsComplete {
			get { return isComplete; }
		}
		
		
		private void LogMessage() {
			if( trace &&
			   !IsQuietRecovery &&
			   messageType != "1" && messageType != "0") {
				log.Trace("Reading message: \n" + this);
			}
		}
		
		public bool TrySplit(MemoryStream buffer)
		{
		    var position = (int) buffer.Position;
            int copyTo = FindSplitAt(buffer);
            if (copyTo > 0)
            {
                data.Write(buffer.GetBuffer(), position, (int)copyTo);
                buffer.Position += copyTo;
                if( verbose) log.Verbose("Copied buffer: " + this);
                return true;
            }
            LogMessage();
            return false;
		}
		
		protected unsafe bool GetKey(out int val) {
			byte *bptr = ptr;
	        val = *ptr - ZeroChar;
	        while (*(++ptr) != EqualSign) {
	        	if( ptr >= end) return false;
	        	val = val * 10 + *ptr - ZeroChar;
	        }
	        ++ptr;
	        return true;
		}
	        
		protected unsafe bool GetInt(out int val) {
			byte *bptr = ptr;
			bool negative = *ptr == NegativeSign;
			if( negative) {
				++ptr;
			}
		    val = 0;
	        while (*(ptr) != EndOfField) {
	        	val = val * 10 + *ptr - ZeroChar;
	            ptr++;
                if (ptr >= end) return false;
            }
	        ++ptr;
	        if( negative) val *= -1;
            if (verbose) log.Verbose("int = " + val);
	        return true;
		}
		
		protected unsafe bool GetDouble(out double result) {
			byte *bptr = ptr;
	        int val = 0;
	        result = 0D;
	        while (*(ptr) != DecimalPoint && *(ptr) != EndOfField) {
	        	if( ptr >= end) return false;
	        	val = val * 10 + *ptr - ZeroChar;
	        	++ptr;
	        }
	        if( *(ptr) == EndOfField) {
		        ++ptr;
				result = val;
				if( trace) log.Trace("double = " + result);
		        return true;
	        } else {
		        ++ptr;
		        int divisor = 10;
		        int fract = *ptr - ZeroChar;
		        while (*(++ptr) != EndOfField) {
		        	if( ptr >= end) return false;
		        	fract = fract * 10 + *ptr - ZeroChar;
		        	divisor *= 10;
		        }
		        ++ptr;
		        result = val + (double) fract / divisor;
                if (verbose) log.Verbose("double = " + result);
				return true;
	        }
		}
		
		protected unsafe bool GetString(out string result) {
			var sptr = (sbyte*) ptr;
			result = null;
			while (*ptr != EndOfField) {
                ptr++;
	        	if( ptr >= end) return false;
			}
	        var length = (int) (ptr - (byte*) sptr);
	        ++ptr;
            result = new string(sptr, 0, length);
            if (verbose) log.Verbose("string = " + result);
			return true;
		}
	        
		protected unsafe bool SkipValue() {
			byte *bptr = ptr;
			while (*(++ptr) != EndOfField) {
	        	if( ptr >= end) return false;
			}
	        ++ptr;
	        int length = (int) (ptr - bptr);
			if( verbose) log.Verbose("skipping " + length + " bytes.");
			return true;
		}
		
        private string ToHex()
        {
            var sb = new StringBuilder();
            var offset = 0;
            while (offset < data.Length)
            {
                var rowSize = (int)Math.Min(16, data.Length - offset);
                var bytes = new byte[rowSize];
                Array.Copy(Data.GetBuffer(), offset, bytes, 0, rowSize);
                for (int i = 0; i < bytes.Length; i++)
                {
                    sb.Append(bytes[i].ToString("X").PadLeft(2, '0'));
                    sb.Append(" ");
                }
                offset += rowSize;
                sb.AppendLine();
                sb.AppendLine(Encoding.UTF8.GetString(bytes));
            }
            return sb.ToString();
        }

		
		protected virtual bool HandleKey(int key) {
			bool result = false;
			switch( key) {
				case 7:
					result = GetInt(out begSeqNum);
					break;
				case 8:
					result = GetString(out version);
					break;
				case 9:
					result = GetInt(out length);
					break;
                case 10:
                    result = GetInt(out checkSum);
                    break;
                case 16:
					result = GetInt(out endSeqNum);
					break;
                case 34:
                    result = GetInt(out sequence);
                    break;
                case 35:
					result = GetString(out messageType);
					break;
                case 36:
                    result = GetInt(out newSeqNum);
                    break;
                case 43:
                    string value;
                    result = GetString(out value);
                    isPossibleDuplicate = value == "Y";
                    break;
                case 45:
                    result = GetInt(out referenceSequence);
                    break;
                case 49:
					result = GetString(out sender);
					break;
                case 52:
					result = GetString(out timeStamp);
					if( result)
					{
					    try
					    {
                            sendUtcTime = new TimeStamp(timeStamp).Internal;
                        }
                        catch( Exception)
                        {
                            if( debug) log.Debug("Sending time accuracy problem: " + sendUtcTime + "  Ignoring by using current time instead.");
                            sendUtcTime = TickZoom.Api.TimeStamp.UtcNow.Internal;
                        }
					}
					break;
                case 56:
                    result = GetString(out target);
                    break;
                case 58:
                    result = GetString(out text);
                    break;
                case 60:
                    result = GetString(out timeStamp);
                    if (result)
                    {
                        try
                        {
                            transactTime = new TimeStamp(timeStamp).Internal;
                        }
                        catch (Exception)
                        {
                            if (debug) log.Debug("Transaction time accuracy problem: " + TransactTime + "  Ignoring by using current time instead.");
                            transactTime = TickZoom.Api.TimeStamp.UtcNow.Internal;
                        }
                    }
                    break;
                case 98:
                    result = GetString(out encryption);
                    break;
                case 108:
                    result = GetInt(out heartBeatInterval);
                    break;
                case 123:
                    result = GetString(out value);
                    isGapFill = value == "Y";
                    break;
                case 141:
                    result = GetString(out value);
                    isResetSeqNum = value == "Y";
                    break;
				default:
					result = SkipValue();
					break;
			}
			return result;
		}		
		
		public string Version {
			get { return version; }
		}
		
		public string MessageType {
			get { return messageType; }
		}
		
		public string Sender {
			get { return sender; }
		}
		
		public string Target {
			get { return target; }
		}
		
		public int Sequence {
			get { return sequence; }
		}

        public int ReferenceSequence
        {
            get { return referenceSequence; }
        }

        public string Encryption
        {
            get { return encryption; }
        }

        public string TimeStamp
        {
			get { return timeStamp; }
		}

		public unsafe int Position { 
			get { return (int) data.Position; }
			set { data.Position = value; }
		}
		
		public int Length {
			get { return (int) data.Length; }
		}
		
		public BinaryReader DataIn {
			get { return dataIn; }
		}
		
		public BinaryWriter DataOut {
			get { return dataOut; }
		}
		
		public MemoryStream Data {
			get { return data; }
		}
		
		public int Id {
			get { return id; }
		}

        public int HeartBeatInterval
        {
            get { return heartBeatInterval; }
        }

        public int BegSeqNum
        {
			get { return begSeqNum; }
		}
		
		public int EndSeqNum {
			get { return endSeqNum; }
		}
		
		public int MaxSize {
			get { return maxSize; }
		}
		
		public long SendUtcTime {
			get { return sendUtcTime; }
			set { sendUtcTime = value; }
		}

	    public long RecvUtcTime
	    {
	        get { return recvUtcTime; }
	        set { recvUtcTime = value; }
	    }

        public bool IsPossibleDuplicate
        {
            get { return isPossibleDuplicate; }
        }

        public int NewSeqNum
        {
            get { return newSeqNum; }
        }

        public bool IsGapFill
        {
            get { return isGapFill; }
        }

        /// <summary>
        /// 58 Error or other message text from FIX server.
        /// </summary>
        public string Text
        {
            get { return text; }
        }

        public bool IsResetSeqNum
        {
            get { return isResetSeqNum; }
        }

        public long TransactTime
        {
            get { return transactTime; }
        }
    }
}

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
using System.Security.Cryptography;
using System.Threading;

using TickZoom.Api;

namespace TickZoom.FIX
{
	public class FIXTMessage1_1 : FIXTMessage
	{
	    private TimeStamp sendTime;
		public FIXTMessage1_1(string version, string sender, string target) : base(version) {
			this.target = target;
			this.sender = sender;
		}

        /// <summary>
        /// 52 Send Time
        /// </summary>
        public void SetSendTime(TimeStamp sendTime)
        {
            this.sendTime = sendTime;
        }
		
		/// <summary>
		/// 98 Encryption. 0= NO encryption
		/// </summary>
		public void SetEncryption(int value) {
			Append(98,value);  
		}

        /// <summary>
        /// 336 TradingSessionID
        /// </summary>
        public void SetTradingSessionId(string sessionId)
        {
            Append(336, sessionId);
        }

        /// <summary>
        /// 625 TradingSessionID
        /// </summary>
        public void SetTradingSubSessionId(string subSessionId)
        {
            Append(625, subSessionId);
        }

        /// <summary>
        /// 340 TradingSessionStatus
        /// </summary>
        public void SetTradingSessionStatus(string status)
        {
            Append(340, status);
        }

        /// <summary>
        /// 263 SubscriptionRequestType
        /// </summary>
        public void SetSubscriptionRequestType(int requestType)
        {
            Append(263, requestType);
        }

        /// <summary>
        /// 335 TradingSessionRequestId
        /// </summary>
        public void SetTradingSessionRequestId(string requestID)
        {
            Append(335, requestID);
        }

        /// <summary>
		/// 7 BeginSeqNumber
		/// </summary>
		public void SetBeginSeqNum(int value) {
			Append(7,value);  
		}
		/// <summary>
		/// 16 EndSeqNumber
		/// </summary>
		public void SetEndSeqNum(int value) {
			Append(16,value);  
		}
        /// <summary>
        /// 36 NewSeqNumber used to skip messages.
        /// </summary>
        /// <param name="value"></param>
        public void SetNewSeqNum(int value)
        {
            Append(36,value);
        }
		/// <summary>
		/// 43 Possible Duplicate
		/// </summary>
		public void SetDuplicate(bool value) {
			duplicate = value;
		}
		/// <summary>
		/// 108 HeartBeatInterval. In seconds.
		/// </summary>
		public void SetHeartBeatInterval(int value) {
			Append(108,value); 
		}
        /// <summary>
        /// 123=Y Means this is a gap fill message.
        /// </summary>
        public void SetGapFill()
        {
            Append(123, "Y");
        }
        /// <summary>
		/// 141 Reset sequence number
		/// </summary>
		public void ResetSequence() {
			Append(141,"Y"); 
		}
		/// <summary>
		/// 347 encoding.  554_H1 for MBTrading hashed password.
		/// </summary>
		public void SetEncoding(string encoding) {
			this.encoding = encoding;
			Append(347,encoding); // Message Encoding (for hashed password)
		}
		/// <summary>
		/// 554 password in plain text. Will be hashed automatically.
		/// </summary>
		public void SetPassword(string password) {
			if( encoding == "554_H1") {
				password = Hash(password);
			}
			Append(554,password);
		}

        /// <summary>
        ///	553 end user who entered the trade should have their username specified here	
        /// This method uses the "sender" field name as the username here.
        /// </summary>
        public void SetUserName(string username)
        {
            Append(553, username);
        }

		public override void AddHeader(string type)
		{
			this.type = type;
		}
		
		public override void CreateHeader() {
			header.Clear();
			header.Append(35,Type);
			header.Append(49,sender);
			header.Append(56,target);
			header.Append(34,Sequence);
            if( sendTime == default(TimeStamp))
            {
                header.Append(52, TimeStamp.UtcNow);
            }
            else
            {
                header.Append(52, sendTime);
            }
			if( duplicate) {
				header.Append(43,"Y");  
			} else {
				header.Append(43,"N");  
			}
		}
		
		public static string Hash(string password) {
			SHA256 hash = new SHA256Managed();
			char[] chars = password.ToCharArray();
			byte[] bytes = new byte[chars.Length];
			for( int i=0; i<chars.Length; i++) {
				bytes[i] = (byte) chars[i];
			}
			byte[] hashBytes = hash.ComputeHash(bytes);
			string hashString = BitConverter.ToString(hashBytes);
			return hashString.Replace("-","");
		}

	    public bool IsDuplicate
	    {
            get { return duplicate;  }
	    }

	    /// <summary>
	    ///	58 Error or other message text.
	    /// </summary>
	    public void SetText(string value ) {
	        Append(58,value);
	    }
	}
}

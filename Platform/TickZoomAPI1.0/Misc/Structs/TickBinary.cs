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
using System.Text;

namespace TickZoom.Api
{
	/// <summary>
	/// Description of TickBinary.
	/// </summary>
	[CLSCompliant(false)]
	unsafe public struct TickBinary
	{
		public const int DomLevels = 5;
		public const int SymbolSize = 8;
		public const int minTickSize = 256;
		
		public long Symbol;
		public byte contentMask;
	    public long Id;
		public long UtcTime;
        public long UtcOptionExpiration;
        public long Strike;
        public long Bid;
		public long Ask;
		public byte Side;
		public long Price;
		public int Size;
		public fixed ushort DepthAskLevels[DomLevels];
		public fixed ushort DepthBidLevels[DomLevels];

        public bool IsQuote
        {
            get { return (contentMask & ContentBit.Quote) > 0; }
            set
            {
                if (value)
                {
                    contentMask |= ContentBit.Quote;
                }
                else
                {
                    contentMask &= (0xFF & ~ContentBit.Quote);
                }
            }
        }

        public bool IsSimulateTicks
        {
            get { return (contentMask & ContentBit.SimulateTicks) > 0; }
            set
            {
                if (value)
                {
                    contentMask |= ContentBit.SimulateTicks;
                }
                else
                {
                    contentMask &= (0xFF & ~ContentBit.SimulateTicks);
                }
            }
        }

        public bool IsTrade
        {
            get { return (contentMask & ContentBit.TimeAndSales) > 0; }
            set
            {
                if (value)
                {
                    contentMask |= ContentBit.TimeAndSales;
                }
                else
                {
                    contentMask &= (0xFF & ~ContentBit.TimeAndSales);
                }
            }
        }

        public bool IsOption
        {
            get { return (contentMask & ContentBit.Option) > 0; }
            set
            {
                if (value)
                {
                    contentMask |= ContentBit.Option;
                }
                else
                {
                    contentMask &= (0xFF & ~ContentBit.Option);
                }
            }
        }

        public OptionType OptionType
        {
            get { return (contentMask & ContentBit.CallOrPut) > 0 ? OptionType.Call : OptionType.Put; }
            set
            {
                if (value == OptionType.Call)
                {
                    contentMask |= ContentBit.CallOrPut;
                }
                else
                {
                    contentMask &= (0xFF & ~ContentBit.CallOrPut);
                }
            }
        }

        public bool HasDepthOfMarket
        {
            get { return (contentMask & ContentBit.DepthOfMarket) > 0; }
            set
            {
                if (value)
                {
                    contentMask |= ContentBit.DepthOfMarket;
                }
                else
                {
                    contentMask &= (0xFF & ~ContentBit.DepthOfMarket);
                }
            }
        }

        public void SetQuote(double dBid, double dAsk)
        {
            SetQuote(dBid.ToLong(), dAsk.ToLong());
        }

        public void SetQuote(double dBid, double dAsk, short bidSize, short askSize)
        {
            try
            {
                SetQuote(dBid.ToLong(), dAsk.ToLong(), bidSize, askSize);
            }
            catch (OverflowException)
            {
                throw new ApplicationException("Overflow exception occurred when converting either bid: " + dBid + " or ask: " + dAsk + " to long.");
            }
        }

        public void SetQuote(long lBid, long lAsk)
        {
            IsQuote = true;
            Bid = lBid;
            Ask = lAsk;
        }

        public void SetQuote(long lBid, long lAsk, short bidSize, short askSize)
        {
            IsQuote = true;
            HasDepthOfMarket = true;
            Bid = lBid;
            Ask = lAsk;
            fixed (ushort* b = DepthBidLevels)
            fixed (ushort* a = DepthAskLevels)
            {
                *b = (ushort)bidSize;
                *a = (ushort)askSize;
            }
        }

        public void SetTrade(double price, int size)
        {
            SetTrade(TradeSide.Unknown, price.ToLong(), size);
        }

        public void SetTrade(TradeSide side, double price, int size)
        {
            SetTrade(side, price.ToLong(), size);
        }

        public void SetTrade(TradeSide side, long lPrice, int size)
        {
            IsTrade = true;
            Side = (byte)side;
            Price = lPrice;
            Size = size;
        }

        public void SetOption(OptionType optionType, double strikePrice, TimeStamp utcOptionExpiration)
        {
            this.IsOption = true;
            Strike = strikePrice.ToLong();
            UtcOptionExpiration = utcOptionExpiration.Internal;
            this.OptionType = optionType;
        }

        public void SetDepth(short[] bidSize, short[] askSize)
        {
            HasDepthOfMarket = true;
            fixed (ushort* b = DepthBidLevels)
            fixed (ushort* a = DepthAskLevels)
            {
                for (int i = 0; i < TickBinary.DomLevels; i++)
                {
                    *(b + i) = (ushort)bidSize[i];
                    *(a + i) = (ushort)askSize[i];
                }
            }
        }

        public void SetSymbol(long lSymbol)
        {
            Symbol = lSymbol;
        }

        public int BidDepth
        {
            get
            {
                int total = 0;
                fixed (ushort* p = DepthBidLevels)
                {
                    for (int i = 0; i < TickBinary.DomLevels; i++)
                    {
                        total += *(p + i);
                    }
                }
                return total;
            }
        }

        public int AskDepth
        {
            get
            {
                int total = 0;
                fixed (ushort* p = DepthAskLevels)
                {
                    for (int i = 0; i < TickBinary.DomLevels; i++)
                    {
                        total += *(p + i);
                    }
                }
                return total;
            }
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append("UtcTime " + new TimeStamp(UtcTime) + ", ContentMask " + contentMask);
            sb.Append(", Bid " + Bid + ", Ask " + Ask);
            sb.Append(", Price " + Price + ", Size " + Size);
            sb.Append(", Strike " + Strike + ", UtcOptionExpiration " + UtcOptionExpiration);
            sb.Append(", BidSizes ");
            fixed (ushort* usptr = DepthBidLevels)
                for (int i = 0; i < TickBinary.DomLevels; i++)
                {
                    var size = *(usptr + i);
                    if (i != 0) sb.Append(",");
                    sb.Append(size);
                }
            sb.Append(", AskSizes ");
            fixed (ushort* usptr = DepthAskLevels)
                for (int i = 0; i < TickBinary.DomLevels; i++)
                {
                    var size = *(usptr + i);
                    if (i != 0) sb.Append(",");
                    sb.Append(size);
                }
            return sb.ToString();
        }
    }
}

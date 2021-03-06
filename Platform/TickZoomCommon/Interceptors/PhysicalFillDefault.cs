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
using TickZoom.Api;

namespace TickZoom.Interceptors
{
	public class PhysicalFillDefault : PhysicalFill
	{
		private int size;
		private double price;
        private TimeStamp time;
		private TimeStamp utcTime;
		private long brokerOrder;
		private bool isSimulated = false;
	    private int totalSize;
	    private int cumulativeSize;
	    private int remainingSize;
	    private bool isRealTime;
	    private bool isActual;

	    public PhysicalFillDefault(int size, double price, TimeStamp time, TimeStamp utcTime, long brokerOrder, 
	                               bool isSimulated, int totalSize, int cumulativeSize, int remainingSize, bool isRealTime, bool isActual)
        {
			this.size = size;
			this.price = price;
			this.time = time;
			this.utcTime = utcTime;
	        this.brokerOrder = brokerOrder;
			this.isSimulated = isSimulated;
	        this.totalSize = totalSize;
	        this.cumulativeSize = cumulativeSize;
	        this.remainingSize = remainingSize;
	        this.isRealTime = isRealTime;
	        this.isActual = isActual;
        }
		
		public override string ToString()
		{
			StringBuilder sb = new StringBuilder();
			sb.Append( "Filled ");
			sb.Append( size );
			sb.Append( " at ");
			sb.Append( price);
			sb.Append( " on ");
			sb.Append( time);
			sb.Append( " for order: " );
			sb.Append( brokerOrder);
			return sb.ToString();
		}

		public TimeStamp Time {
			get { return time; }
		}

		public TimeStamp UtcTime {
			get { return utcTime; }
		}

		public double Price {
			get { return price; }
		}

		public int Size {
			get { return size; }
		}

		public long BrokerOrder {
			get { return brokerOrder; }
		}
				
		public bool IsSimulated {
			get { return isSimulated; }
		}

	    public int TotalSize
	    {
	        get { return totalSize; }
	    }

	    public int CumulativeSize
	    {
	        get { return cumulativeSize; }
	    }

	    public int RemainingSize
	    {
	        get { return remainingSize; }
	    }

	    public bool IsRealTime
	    {
	        get { return isRealTime; }
	    }

	    public bool IsActual
	    {
	        get { return isActual; }
	    }

	}
}

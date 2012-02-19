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

namespace TickZoom.Api
{
	public enum OrderState {
		Pending,
        PendingNew,
		Active,
		Suspended,
		Filled,
		Lost,
	}

    public enum OrderAction
    {
        Create = 1,
        Change = 2,
        Cancel = 4
    }

    public interface Order
    {
        OrderType Type { get; }
        double Price { get; }
    }

    public interface PhysicalOrder : Order
    {
        OrderAction Action { get; }

        SymbolInfo Symbol { get; }

        OrderState OrderState
        {
            get;
            set;
        }

        long BrokerOrder
        {
            get;
            set;
        }

        string Tag { get; }

        object Reference
        {
            get;
            set;
        }

        CreateOrChangeOrder ReplacedBy
        {
            get;
            set;
        }

        CreateOrChangeOrder OriginalOrder
        {
            get;
            set;
        }

        TimeStamp LastModifyTime { get; }

        TimeStamp LastReadTime { get; set;  }

        void ResetLastChange();

        TimeStamp UtcCreateTime { get; }

        int Sequence { get; set; }

        OrderFlags OrderFlags { get;  }

        bool OffsetTooLateToCancel { get; }
    }

	public interface CreateOrChangeOrder : PhysicalOrder
	{

	    OrderSide Side { get; }

	    int LogicalOrderId { get; }

	    long LogicalSerialNumber { get; }

	    CreateOrChangeOrder Clone();
	    void ResetLastChange(TimeStamp lastChange);

        int Size { get; set;  }
    }
}
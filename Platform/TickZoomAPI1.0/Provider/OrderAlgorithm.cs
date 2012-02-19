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
    public interface PhysicalOrderConfirm
    {
        void ConfirmActive(long brokerOrder, bool isRecovered);
        void ConfirmCreate(long brokerOrder, bool isRecovered);
        void ConfirmCancel(long brokerOrder, bool isRecovered);
        void ConfirmChange(long brokerOrder, bool isRecovered);
        void RejectOrder(long brokerOrder, bool removeOriginal, bool isRealTime, bool retryImmediately);
        int RejectRepeatCounter { get; }
    }

	public interface OrderAlgorithm : PhysicalOrderConfirm
	{
        bool PositionChange(PositionChangeDetail change, bool isRecovered);
        void SetDesiredPosition(int position);
        void SetStrategyPositions(Iterable<StrategyPosition> strategyPositions);
        void SetLogicalOrders(Iterable<LogicalOrder> logicalOrders);
        void ProcessFill(PhysicalFill fill);
		void SetActualPosition(long position);
        void IncreaseActualPosition(int position);
        void TrySyncPosition(Iterable<StrategyPosition> strategyPositions);
        bool HandleSimulatedExits { get; set; }
        PhysicalOrderHandler PhysicalOrderHandler { get; }
        Action<SymbolInfo, LogicalFillBinary> OnProcessFill { get; set; }
        long ActualPosition { get; }
        bool IsPositionSynced { get; set; }
	    bool EnableSyncTicks { get; set; }
	    bool IsBrokerOnline { get; set; }
	    bool IsSynchronized { get; }
	    int ProcessOrders();
	    void RemovePending(CreateOrChangeOrder order, bool isRealTime);
	    bool CheckForPending();
	    void ProcessHeartBeat();
	}
}

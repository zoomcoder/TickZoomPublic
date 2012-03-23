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

#region Namespaces
using System;
using System.ComponentModel;
using System.Drawing;

using TickZoom.Api;
using TickZoom.Common;

#endregion

namespace TickZoom.Examples
{

	public class ExampleOrderStrategy : Strategy
	{
		double multiplier = 1.0D;
		double minimumTick;
		int tradeSize;
	    private bool isShortOnly;

		public ExampleOrderStrategy() {
			Performance.GraphTrades = true;
			Performance.Equity.GraphEquity = true;
			ExitStrategy.ControlStrategy = false;
		}
		
		public override void OnInitialize()
		{
			tradeSize = Data.SymbolInfo.Level2LotSize * 10;
			minimumTick = multiplier * Data.SymbolInfo.MinimumTick;
			ExitStrategy.BreakEven = 30 * minimumTick;
			ExitStrategy.StopLoss = 45 * minimumTick;
		}
		
		public override bool OnIntervalClose()
		{
			// Example log message.
			if( IsTrace) Log.Trace( "close: " + Ticks[0] + " " + Bars.Close[0] + " " + Bars.Time[0]);
            var trades = Performance.ComboTrades;
		    var isFlat = Position.IsFlat && (trades.Count == 0 || trades.Tail.Completed);
			
			double close = Bars.Close[0];
            if (IsDebug) Log.Debug("isFlat " + isFlat + ", Position.IsFlat " + Position.IsFlat + ", trades.Count " + trades.Count + ", Completed " + (trades.Count == 0 || trades.Tail.Completed));
            //if (IsDebug) Log.Debug("Close " + Bars.Close[0] + ", Open " + Bars.Open[0] + ", Close[1] " + Bars.Close[1]);
            if (IsDebug) Log.Debug("Close " + Bars.Close[0] + ", Open " + Bars.Open[0]);
            if (Bars.Close[0] < Bars.Open[0] && Bars.Open[0] < Bars.Close[1])
            {
                if (isFlat)
                {
                    Orders.Enter.NextBar.SellMarket(tradeSize);
                    ExitStrategy.StopLoss = 15 * minimumTick;
                    return true;
                }
            }
            if (Bars.Close[0] > Bars.Open[0])
            {
                if (isFlat)
                {
					if( !isShortOnly)
					{
					    Orders.Enter.NextBar.BuyStop(Bars.Close[0] + 10 * minimumTick,tradeSize);
                        Orders.Exit.NextBar.SellStop(Bars.Close[0] - 10 * minimumTick);
                    }
				    return true;
				}
				if( Position.IsShort) {
					Orders.Exit.NextBar.BuyLimit(Bars.Close[0] - 3 * minimumTick);
                    return true;
                }
			}
			if( Position.IsLong) {
				Orders.Exit.NextBar.SellStop(Bars.Close[0] - 10 * minimumTick);
                return true;
            }
			if( Bars.Close[0] < Bars.Open[0]) {
                if (isFlat)
                {
					Orders.Enter.NextBar.SellLimit(Bars.Close[0] + 30 * minimumTick,tradeSize);
					ExitStrategy.StopLoss = 45 * minimumTick;
                    return true;
                }
			}
			return true;
		}

        public override bool OnWriteReport(string folder)
        {
            return false;
        }

		public double Multiplier {
			get { return multiplier; }
			set { multiplier = value; }
		}

	    public bool IsShortOnly
	    {
	        get { return isShortOnly; }
	        set { isShortOnly = value; }
	    }
	}
}
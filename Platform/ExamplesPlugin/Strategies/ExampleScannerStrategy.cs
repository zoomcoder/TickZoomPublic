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
using System.Drawing;
using TickZoom.Api;
using TickZoom.Common;
#endregion

namespace TickZoom.Examples
{
	public class ExampleScannerStrategy : Portfolio
	{
		
		public ExampleScannerStrategy() {
			Performance.GraphTrades = true;
			Performance.Equity.GraphEquity = true;
		}
		
		public override void OnInitialize()
		{
			foreach( Strategy strategy in Strategies) {
				strategy.Performance.GraphTrades = true;
				strategy.Performance.Equity.GraphEquity = true;
			}
		}
		
		public override bool OnIntervalClose()
		{
			// Example log message.
			//Log.WriteLine( "close: " + Ticks[0] + " " + Minutes.Close[0] + " " + Minutes.Time[0]);
			
			for( int i=0; i<Strategies.Count; i++) {
				if( Strategies[i].Position.HasPosition) {
					Strategies[i].Orders.Exit.ActiveNow.GoFlat();
				}
				if( Strategies[i].Position.IsFlat) {
					double close = Strategies[i].Bars.Close[0];
					double high = Strategies[i].Bars.High[1];
					if( Strategies[i].Bars.Close[0] > Strategies[i].Bars.High[1]) {
						Strategies[i].Orders.Enter.ActiveNow.BuyMarket();
					}
					if( Strategies[i].Bars.Close[0] < Strategies[i].Bars.Low[1]) {
						Strategies[i].Orders.Enter.ActiveNow.SellMarket();
					}
				}
			}
			return true;
		}
	}
}
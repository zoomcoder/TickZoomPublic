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
using System.ComponentModel;
using System.Drawing;

using TickZoom.Api;
using TickZoom.Common;

namespace TickZoom.Interceptors
{
	public class ExitStrategy : StrategySupport
	{
		private static readonly Log log = Factory.SysLog.GetLogger(typeof(ExitStrategy));
		private bool controlStrategy = false;
		private double strategySignal = 0;
		private LogicalOrder buyStopLossOrder;
		private LogicalOrder sellStopLossOrder;
		private LogicalOrder breakEvenBuyStopOrder;
		private LogicalOrder breakEvenSellStopOrder;
		private LogicalOrder marketOrder;
		private double stopLoss = 0;
		private double targetProfit = 0;
		private double breakEven = 0;
		private double entryPrice = 0;
		private double trailStop = 0;
		private double dailyMaxProfit = 0;
		private double dailyMaxLoss = 0;
		private double weeklyMaxProfit = 0;
		private double weeklyMaxLoss = 0;
		private double monthlyMaxProfit = 0;
		private double monthlyMaxLoss = 0;
		private double breakEvenStop = 0;
		private double pnl = 0;
		private double maxPnl = 0;
		bool stopTradingToday = false;
		bool stopTradingThisWeek = false;
		bool stopTradingThisMonth = false;
		PositionCommon position;
		
		public ExitStrategy(Strategy strategy) : base( strategy) {
			position = new PositionCommon(strategy);
			strategy.RequestEvent(EventType.Tick);
		}
		
		EventContext context;
		public override void Intercept(EventContext context, EventType eventType, object eventDetail)
		{
			if( eventType == EventType.Initialize) {
				Strategy.AddInterceptor( EventType.Tick, this);
				Strategy.AddInterceptor( EventType.LogicalFill, this);
				OnInitialize();
			}
			context.Invoke();
			this.context = context;
			if( eventType == EventType.Tick || eventType == EventType.LogicalFill) {
				OnProcessPosition(eventType);
			}
		}
				
		public void OnInitialize()
		{
            marketOrder = Factory.Engine.LogicalOrder(Strategy.Data.SymbolInfo, Strategy);
			marketOrder.TradeDirection = TradeDirection.ExitStrategy;
			marketOrder.Tag = "ExitStrategy" ;
			Strategy.AddOrder(marketOrder);
            breakEvenBuyStopOrder = Factory.Engine.LogicalOrder(Strategy.Data.SymbolInfo, Strategy);
			breakEvenBuyStopOrder.TradeDirection = TradeDirection.ExitStrategy;
			breakEvenBuyStopOrder.Type = OrderType.BuyStop;
			breakEvenBuyStopOrder.Tag = "ExitStrategy" ;
			Strategy.AddOrder(breakEvenBuyStopOrder);
            breakEvenSellStopOrder = Factory.Engine.LogicalOrder(Strategy.Data.SymbolInfo, Strategy);
			breakEvenSellStopOrder.TradeDirection = TradeDirection.ExitStrategy;
			breakEvenSellStopOrder.Type = OrderType.SellStop;
			breakEvenSellStopOrder.Tag = "ExitStrategy" ;
			Strategy.AddOrder(breakEvenSellStopOrder);
            buyStopLossOrder = Factory.Engine.LogicalOrder(Strategy.Data.SymbolInfo, Strategy);
			buyStopLossOrder.TradeDirection = TradeDirection.ExitStrategy;
			buyStopLossOrder.Type = OrderType.BuyStop;
			buyStopLossOrder.Tag = "ExitStrategy" ;
			Strategy.AddOrder(buyStopLossOrder);
            sellStopLossOrder = Factory.Engine.LogicalOrder(Strategy.Data.SymbolInfo, Strategy);
			sellStopLossOrder.TradeDirection = TradeDirection.ExitStrategy;
			sellStopLossOrder.Type = OrderType.SellStop;
			sellStopLossOrder.Tag = "ExitStrategy" ;
			Strategy.AddOrder(sellStopLossOrder);
			if( IsTrace) Log.Trace(Strategy.FullName+".Initialize()");
			Strategy.Drawing.Color = Color.Black;
		}
		
		public void OnProcessPosition(EventType eventType) {
			Tick tick = Strategy.Data.Ticks[0];
			
			if( stopTradingToday || stopTradingThisWeek || stopTradingThisMonth ) {
				return; 
			}
			
			if( (strategySignal>0) != context.Position.IsLong || (strategySignal<0) != context.Position.IsShort ) {
				strategySignal = context.Position.Current;
				entryPrice = context.Position.Price;
				maxPnl = 0;
				position.Copy(context.Position);
				trailStop = 0;
				breakEvenStop = 0;
				CancelOrders();
			} 
			
			if( position.HasPosition ) {
				// copy signal in case of increased position size
				double exitPrice;
				if( strategySignal > 0) {
					exitPrice = tick.IsQuote ? tick.Bid : tick.Price;
					pnl = (exitPrice - entryPrice).Round();
				} else {
					exitPrice = tick.IsQuote ? tick.Ask : tick.Price;
					pnl = (entryPrice - exitPrice).Round();
				}
				maxPnl = pnl > maxPnl ? pnl : maxPnl;
                if( stopLoss > 0) processStopLoss(tick);
                //if( breakEven > 0) processBreakEven(tick);
			}
			
			context.Position.Copy(position);
		}
		
		private void CancelOrders() {
			marketOrder.Status = OrderStatus.AutoCancel;
			breakEvenBuyStopOrder.Status = OrderStatus.AutoCancel;
			breakEvenSellStopOrder.Status = OrderStatus.AutoCancel;
			buyStopLossOrder.Status = OrderStatus.AutoCancel;
			sellStopLossOrder.Status = OrderStatus.AutoCancel;
		}
		
		private void processStopLoss(Tick tick) {
			if( position.IsShort) {
				var price = entryPrice + stopLoss;
				if( double.IsNaN(price) || double.IsInfinity(price)) {
					throw new OverflowException( "StopLoss value " + stopLoss + " from entry price " + entryPrice + " created an overflow.");
				}
				buyStopLossOrder.Price = price;
				buyStopLossOrder.Status = OrderStatus.Active;
			} else {
				buyStopLossOrder.Status = OrderStatus.Inactive;
			}
			if( position.IsLong) {
				var price = entryPrice - stopLoss;
				if( double.IsNaN(price) || double.IsInfinity(price)) {
					throw new OverflowException( "StopLoss value " + stopLoss + " from entry price " + entryPrice + " created an overflow.");
				}
				sellStopLossOrder.Price = price;
				sellStopLossOrder.Status = OrderStatus.Active;
			} else {
				sellStopLossOrder.Status = OrderStatus.Inactive;
			}
		}
		
		public bool OnIntervalOpen(Interval interval) {
			if( interval.Equals(Intervals.Day1)) {
				stopTradingToday = false;
			}
			if( interval.Equals(Intervals.Week1)) {
				stopTradingThisWeek = false;
			}
			if( interval.Equals(Intervals.Month1)) {
				stopTradingThisMonth = false;
			}
			return true;
		}
	
		private void processBreakEven(Tick tick) {
			if( pnl >= breakEven) {
				if( position.IsLong ) {
					if( !breakEvenSellStopOrder.IsActive) {
						breakEvenSellStopOrder.Price = entryPrice + breakEvenStop;
						breakEvenSellStopOrder.Status = OrderStatus.Active;
					}
				} else {
					breakEvenSellStopOrder.Status = OrderStatus.Inactive;
				}
					
				if( position.IsShort ) {
					if( !breakEvenBuyStopOrder.IsActive) {
						breakEvenBuyStopOrder.Price = entryPrice - breakEvenStop;
						breakEvenBuyStopOrder.Status = OrderStatus.Active;
					}
				} else {
					breakEvenBuyStopOrder.Status = OrderStatus.Inactive;
				}
			}
		}
		
		private void LogExit(string description) {
			if( Strategy.Chart.IsDynamicUpdate) {
				if( IsDebug) Log.Debug(Strategy.Ticks[0].Time + ", Bar="+Strategy.Chart.ChartBars.CurrentBar+", " + description);
			} else if( !Strategy.IsOptimizeMode) {
				if( IsDebug) Log.Debug(Strategy.Ticks[0].Time + ", Bar="+Strategy.Chart.ChartBars.CurrentBar+", " + description);
			}
		}

        #region Properties
        
        [DefaultValue(0d)]
        public double StopLoss
        {
            get { return stopLoss; }
            set { // log.WriteFile(GetType().Name+".StopLoss("+value+")");
            	  stopLoss = Math.Max(0, value); }
        }		

        [DefaultValue(0d)]
		public double TrailStop
        {
            get { return trailStop; }
            set { trailStop = Math.Max(0, value); }
        }		
		
        [DefaultValue(0d)]
		public double TargetProfit
        {
            get { return targetProfit; }
            set { if( IsTrace) Log.Trace(GetType().Name+".TargetProfit("+value+")");
            	  targetProfit = Math.Max(0, value); }
        }		
		
        [DefaultValue(0d)]
		public double BreakEven
        {
            get { return breakEven; }
            set { breakEven = Math.Max(0, value); }
        }	
		
        [DefaultValue(false)]
		public bool ControlStrategy {
			get { return controlStrategy; }
			set { controlStrategy = value; }
		}
		
        [DefaultValue(0d)]
		public double WeeklyMaxProfit {
			get { return weeklyMaxProfit; }
			set { weeklyMaxProfit = value; }
		}
		
        [DefaultValue(0d)]
		public double WeeklyMaxLoss {
			get { return weeklyMaxLoss; }
			set { weeklyMaxLoss = value; }
		}
		
        [DefaultValue(0d)]
		public double DailyMaxProfit {
			get { return dailyMaxProfit; }
			set { dailyMaxProfit = value; }
		}
		
        [DefaultValue(0d)]
		public double DailyMaxLoss {
			get { return dailyMaxLoss; }
			set { dailyMaxLoss = value; }
		}
		
        [DefaultValue(0d)]
		public double MonthlyMaxLoss {
			get { return monthlyMaxLoss; }
			set { monthlyMaxLoss = value; }
		}
		
        [DefaultValue(0d)]
		public double MonthlyMaxProfit {
			get { return monthlyMaxProfit; }
			set { monthlyMaxProfit = value; }
		}
		#endregion
	
//		public override string ToString()
//		{
//			return Strategy.FullName;
//		}
		
		public PositionCommon Position {
			get { return position; }
			set { position = value; }
		}
	}

	[Obsolete("Please use ExitStrategy instead.",true)]
	public class ExitStrategyCommon : ExitStrategy
	{
		public ExitStrategyCommon(Strategy strategy) : base( strategy) {
			
		}
	}
		
}

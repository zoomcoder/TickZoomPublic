using System;
using System.Drawing;
using System.Threading;
using TickZoom.Api;
using TickZoom.Common;

namespace TickZoom.Examples
{
    [Flags]
    public enum StrategyState
    {
        None,
        Active = 0x01,
        HighRisk = 0x02,
        EndForWeek = 0x04,
        OverSize = 0x08,
        ProcessSizing = Active | HighRisk,
        ProcessOrders = Active | OverSize | HighRisk,
    }

    public static class LocalExtensions
    {
        public static bool AnySet( this Enum input, Enum matchInfo)
        {
            var inputInt = Convert.ToUInt32(input);
            var matchInt = Convert.ToUInt32(matchInfo);
            return ((inputInt & matchInt) != 0);
        }
    }
    
    public class OtherStrategy : BaseSimpleStrategy
    {
        #region Fields
        private int increaseSpreadInTicks = 3;
        private int reduceSpreadInTicks = 3;
        private int volatileSpreadInTicks = 3;
        private double volatileSpread;
        private double reduceSpread;
        private ActiveList<LocalFill> fills = new ActiveList<LocalFill>();
        private long totalVolume = 0;
        private int maxLots = int.MaxValue;
        private int lastSize = 0;
        private Direction stretchDirection = Direction.Sideways;
        private double maxStretch;
        private IndicatorCommon rubberBand;
        private IndicatorCommon waitRetraceLine;
        private IndicatorCommon bestIndifferenceLine;
        private IndicatorCommon retraceLine;
        private IndicatorCommon maxExcursionLine;
        private int nextIncreaseLots;

        private bool enableManageRisk = false;
        private bool enableSizing = false;
        private bool enablePegging = true;
        private bool limitSize = false;
        private TradeSide oversizeSide;

        private readonly long commission = -0.0000195D.ToLong();
        private int retraceErrorMarginInTicks = 25;
        private int maxTradeSize;
        private double maxEquity;
        private double drawDown;
        private double maxDrawDown;
        #endregion


        #region Initialize
        public OtherStrategy()
        {
            RequestUpdate(Intervals.Second1);
        }

        public override void OnInitialize()
        {
            base.OnInitialize();

            minimumTick = Data.SymbolInfo.MinimumTick;
            increaseSpread = increaseSpreadInTicks * minimumTick;
            reduceSpread = reduceSpreadInTicks * minimumTick;
            volatileSpread = volatileSpreadInTicks*minimumTick;

            waitRetraceLine = Formula.Indicator();
            waitRetraceLine.Name = "Wait Retrace";
            waitRetraceLine.Drawing.IsVisible = false;
            waitRetraceLine.Drawing.Color = Color.ForestGreen;

            rubberBand = Formula.Indicator();
            rubberBand.Name = "Rubber Band";
            rubberBand.Drawing.IsVisible = IsVisible;
            rubberBand.Drawing.Color = Color.Plum;

            bestIndifferenceLine = Formula.Indicator();
            bestIndifferenceLine.Name = "Best Indifference";
            bestIndifferenceLine.Drawing.IsVisible = IsVisible;
            bestIndifferenceLine.Drawing.Color = Color.Orange;

            retraceLine = Formula.Indicator();
            retraceLine.Name = "Retrace";
            retraceLine.Drawing.IsVisible = IsVisible;
            retraceLine.Drawing.Color = Color.Magenta;

            maxExcursionLine = Formula.Indicator();
            maxExcursionLine.Name = "Excursion";
            maxExcursionLine.Drawing.IsVisible = enableSizing && IsVisible;
            maxExcursionLine.Drawing.Color = Color.Magenta;


        }
        #endregion

        private void ResetRubberBand()
        {
            maxStretch = rubberBand[0] = midPoint;
        }

        public override bool OnWriteReport(string folder)
        {
            return false;
        }

        #region UpdateIndicators
        protected override void UpdateIndicators(Tick tick)
        {
            base.UpdateIndicators(tick);
            if( double.IsNaN(maxExcursionLine[0]))
            {
                maxExcursionLine[0] = midPoint;
            }
            var equity = Performance.Equity.CurrentEquity;
            if( equity > maxEquity)
            {
                maxEquity = Performance.Equity.CurrentEquity;
                drawDown = 0D;
            }
            else
            {
                drawDown = maxEquity - equity;
                if( drawDown > maxDrawDown)
                {
                    maxDrawDown = drawDown;
                }
			}
            var rubber = rubberBand[0];
            if (double.IsNaN(rubber))
            {
                ResetRubberBand();
                rubber = rubberBand[0];
            }
            switch (stretchDirection)
            {
                case Direction.UpTrend:
                    if (MarketBid > maxStretch)
                    {
                        var increase = (MarketBid - maxStretch) / 2;
                        rubberBand[0] += increase;
                        maxStretch = MarketBid;
                    }
                    else if (MarketAsk <= rubberBand[0])
                    {
                        stretchDirection = Direction.Sideways;
                        goto Sideways;
                    }
                    break;
                case Direction.DownTrend:
                    if (MarketAsk < maxStretch)
                    {
                        var decrease = (maxStretch - MarketAsk) / 2;
                        rubberBand[0] -= decrease;
                        maxStretch = MarketAsk;
                    }
                    else if (MarketBid >= rubberBand[0])
                    {
                        stretchDirection = Direction.Sideways;
                        goto Sideways;
                    }
                    break;
                case Direction.Sideways:
            Sideways:
                    if (midPoint > rubberBand[0])
                    {
                        stretchDirection = Direction.UpTrend;
                    }
                    else
                    {
                        stretchDirection = Direction.DownTrend;
                    }
                    ResetRubberBand();
                    break;
            }

            var comboTrades = Performance.ComboTrades;
            if (comboTrades.Count > 0 && !comboTrades.Tail.Completed)
            {
                var lots = Position.Size/lotSize;
                var currentTrade = comboTrades.Tail;
                var retracePercent = 0.50;
                var waitRetracePercent = 0.10;

                if (double.IsNaN(retraceLine[0]))
                {
                    retraceLine[0] = midPoint;
                    waitRetraceLine[0] = midPoint;
                }

                var retraceAmount = (currentTrade.EntryPrice - maxExcursionLine[0]) * retracePercent;
                var retraceLevel = maxExcursionLine[0] + retraceAmount;
                var waitRetraceAmount = (currentTrade.EntryPrice - maxExcursionLine[0]) * waitRetracePercent;
                var waitRetraceLevel = maxExcursionLine[0] + waitRetraceAmount;
                if (Position.IsLong)
                {
                    if (retraceLevel < retraceLine[0])
                    {
                        retraceLine[0] = retraceLevel;
                    }
                    if (waitRetraceLevel < waitRetraceLine[0])
                    {
                        waitRetraceLine[0] = waitRetraceLevel;
                    }
               
                }
                if (Position.IsShort)
                {
                    if (retraceLevel > retraceLine[0])
                    {
                        retraceLine[0] = retraceLevel;
                    }
                    if (waitRetraceLevel > waitRetraceLine[0])
                    {
                        waitRetraceLine[0] = waitRetraceLevel;
                    }
                }

            } 
            else
            {
                retraceLine[0] = double.NaN;
            }

        }
        #endregion

        #region OnProcessTick
        public override bool OnProcessTick(Tick tick)
        {
            if (!tick.IsQuote)
            {
                throw new InvalidOperationException("This strategy requires bid/ask quote data to operate.");
            }

            Orders.SetAutoCancel();

            CalcMarketPrices(tick);

            UpdateIndicators(tick);

            if( enableManageRisk) HandleRisk(tick);

            if( enablePegging) HandlePegging(tick);

            if (state.AnySet( StrategyState.ProcessSizing ))
            {
                PerformSizing(tick);
            }

            if (limitSize) ManageOverSize(tick);

            HandleWeekendRollover(tick);

            if( state.AnySet(StrategyState.ProcessOrders))
            {
                ProcessOrders(tick);
            }
            return true;
        }
        #endregion

        private double riskRetrace;
        private void HandleRisk(Tick tick)
        {
            if( state == StrategyState.HighRisk)
            {
                if( Position.IsLong)
                {
                    if( MarketAsk < riskRetrace)
                    {
                        riskRetrace = MarketAsk;
                    }
                    if( MarketBid > riskRetrace)
                    {
                        state = StrategyState.Active;
                        SetupBidAsk(maxExcursionLine[0]);
                    }
                }
                if( Position.IsShort)
                {
                    if( MarketBid > riskRetrace)
                    {
                        riskRetrace = MarketBid;
                    }
                    if( MarketAsk < riskRetrace)
                    {
                        state = StrategyState.Active;
                        SetupBidAsk(maxExcursionLine[0]);
                    }
                }
            }
            else
            {
                riskRetrace = midPoint;
            }
            //if( drawDown >= 2000)
            //{
            //    Reset();
            //}
        }

        private int lastLots;
        private void ManageOverSize( Tick tick)
        {
            var lots = Position.Size/lotSize;
            if( Position.IsShort)
            {
                if (lots >= maxLots)
                {
                    SellSize = 0;
                    BuySize = lots; // Math.Max(1, maxLots / 20);
                    state = StrategyState.OverSize;
                    oversizeSide = TradeSide.Sell;
                }
                else if (state == StrategyState.OverSize)
                {
                    if( Position.IsFlat || oversizeSide == TradeSide.Buy)
                    {
                        state = StrategyState.Active;
                    }
                    else
                    {
                        SellSize = 0;
                        BuySize= Math.Max(1, maxLots / 20);
                        BuySize = Math.Min(lots, BuySize);
                    }
                }
            }

            if( Position.IsLong)
            {
                if( lots != lastLots)
                {
                    lastLots = lots;
                }
                if (lots >= maxLots)
                {
                    BuySize = 0;
                    SellSize = lots; // Math.Max(1, maxLots / 20);
                    state = StrategyState.OverSize;
                    oversizeSide = TradeSide.Buy;
                }
                else if (state == StrategyState.OverSize)
                {
                    if (Position.IsFlat || oversizeSide == TradeSide.Sell)
                    {
                        state = StrategyState.Active;
                    }
                    else
                    {
                        BuySize = 0;
                        SellSize = Math.Max(1, maxLots / 20);
                        SellSize = Math.Min(lots, SellSize);
                    }
                }
            }
        }

        private void PerformSizing(Tick tick)
        {
            var lots = Position.Size/lotSize;
            SellSize = 1;
            BuySize = 1;

            if( Performance.ComboTrades.Count <= 0) return;

            var size = Math.Max(2, lots / 5);
            var currentTrade = Performance.ComboTrades.Tail;
            var indifferenceCompare = retraceLine[0];
            if (Position.IsShort)
            {
                var buyIndifference = CalcIndifferenceUpdate(BreakEvenPrice, Position.Size, bid, -BuySize * lotSize);
                var retraceDelta = indifferenceCompare - buyIndifference;
                BuySize = 0;

                if (!enableSizing || lots <= 10) return;

                if ( ask > maxExcursionLine[0] &&
                    BreakEvenPrice < indifferenceCompare)
                {
                    SellSize = CalcAdjustmentSize(BreakEvenPrice, Position.Size, indifferenceCompare + retraceErrorMarginInTicks * minimumTick, ask);
                    SellSize = Math.Min(SellSize, 10000);
                    if( limitSize)
                    {
                        SellSize = Math.Min(SellSize, maxLots - lots);
                    }

                }
            }

            if (Position.IsLong)
            {
                var sellIndifference = CalcIndifferenceUpdate(BreakEvenPrice, Position.Size, ask, -SellSize * lotSize);
                var retraceDelta = sellIndifference - indifferenceCompare;
                SellSize = 0;

                if (!enableSizing || lots <= 10) return;

                if (bid < maxExcursionLine[0] &&
                    BreakEvenPrice > indifferenceCompare)
                {
                    BuySize = CalcAdjustmentSize(BreakEvenPrice, Position.Size, indifferenceCompare - retraceErrorMarginInTicks * minimumTick, bid);
                    BuySize = Math.Min(BuySize, 10000);
                    if (limitSize)
                    {
                        BuySize = Math.Min(BuySize, maxLots - lots);
                    }
                }
            }
        }

        protected override void SetupAsk(double price)
        {
            var sequentialAdjustment = throttleIncreasing ? sequentialIncreaseCount * 10 * minimumTick : 0;
            var lots = Position.Size / lotSize;
            var tempVolatileSpread = Math.Min(1000 * increaseSpread, Math.Pow(1.1D, lots - 1) * increaseSpread);
            var tempIncreaseSpread = state == StrategyState.HighRisk ? tempVolatileSpread : volatileSpread;
            var myAsk = Position.IsLong ? price + reduceSpread : maxExcursionLine[0] + tempIncreaseSpread + sequentialAdjustment;
            ask = Math.Max(myAsk, MarketAsk);
            askLine[0] = ask;
        }

        protected override void SetupBid(double price)
        {
            var sequentialAdjustment = throttleIncreasing ? sequentialIncreaseCount * 10 * minimumTick : 0;
            var lots = Position.Size / lotSize;
            var tempVolatileSpread = Math.Min(1000 * increaseSpread, Math.Pow(1.1D, lots - 1) * increaseSpread);
            var tempIncreaseSpread = state == StrategyState.HighRisk ? tempVolatileSpread : volatileSpread;
            var myBid = Position.IsLong ? (maxExcursionLine[0] - tempIncreaseSpread) - sequentialAdjustment : price - reduceSpread;
            bid = Math.Min(myBid, MarketBid);
            bidLine[0] = bid;
        }

        private int CalcAdjustmentSize(double indifference, int size, double desiredIndifference, double currentPrice)
        {
            var lots = size/lotSize;
            var delta = Math.Abs(desiredIndifference - currentPrice);
            if (delta < minimumTick || lots > 100000)
            {
                return 1;
            }
            var result = (size*indifference - size*desiredIndifference)/(delta);
            result = Math.Abs(result);
            if( result >= int.MaxValue)
            {
                System.Diagnostics.Debugger.Break();
            }
            return Math.Max(1,(int) (result / lotSize));
        }

        private double CalcIndifferenceUpdate(double indifference, int size, double currentPrice, int changeSize)
        {
            var result = (size*indifference + changeSize*currentPrice)/(size + changeSize);
            return result;
        }

        public override void OnEndHistorical()
        {
            if( Performance.ComboTrades.Count > 0)
            {
                var lastTrade = Performance.ComboTrades.Tail;
                totalVolume += lastTrade.Volume;
            }
            Log.Notice("Total volume was " + totalVolume + ". With commission paid of " + ((totalVolume / 1000) * 0.02D));
        }

        public class LocalFill
        {
            public int Size;
            public double Price;
            public TimeStamp Time;
            public LocalFill(LogicalFill fill)
            {
                Size = Math.Abs(fill.Position);
                Price = fill.Price;
                Time = fill.Time;
            }
            public LocalFill(int size, double price, TimeStamp time)
            {
                Size = size;
                Price = price;
                Time = time;
            }
            public override string ToString()
            {
                return Size + " at " + Price;
            }
        }

        public override void OnEnterTrade(TransactionPairBinary comboTrade, LogicalFill fill, LogicalOrder filledOrder)
        {
            lastSize = Math.Abs(comboTrade.CurrentPosition);
            nextIncreaseLots = 50;
            bestIndifferenceLine[0] = CalcIndifferencePrice(comboTrade);
            fills.AddFirst(new LocalFill(fill));
            maxExcursionLine[0] = fill.Price;
            sequentialIncreaseCount = 0;
            lastMarketAsk = MarketAsk;
            lastMarketBid = MarketBid;
            ResetRubberBand();
            maxEquity = Performance.Equity.CurrentEquity;
            maxDrawDown = 0D;
            drawDown = 0D;
            SetupBidAsk(fill.Price);
            LogFills("OnEnterTrade");
        }

        private void LogFills(string onChange)
        {
            if( IsDebug)
            {
                Log.Debug(onChange + " fills");
                for (var current = fills.First; current != null; current = current.Next)
                {
                    var fill = current.Value;
                    Log.Debug("Fill: " + fill.Size + " at " + fill.Price + " " + fill.Time);
                }
            }
        }

        public override void OnChangeTrade(TransactionPairBinary comboTrade, LogicalFill fill, LogicalOrder filledOrder)
        {
            var tick = Ticks[0];
            var midpoint = (tick.Ask + tick.Bid)/2;
            if (!fill.IsComplete) return;
            if (Position.Size > maxTradeSize)
            {
                maxTradeSize = Position.Size;
            }
            if (comboTrade.CurrentPosition > 0 && fill.Price < maxExcursionLine[0])
            {
                maxExcursionLine[0] = fill.Price;
            }
            if (comboTrade.CurrentPosition < 0 && fill.Price > maxExcursionLine[0])
            {
                maxExcursionLine[0] = fill.Price;
            }
            var size = Math.Abs(comboTrade.CurrentPosition);
            var lots = size/lotSize;
            var change = size - lastSize;
            lastSize = size;
            if (change > 0)
            {
                if( enableManageRisk)
                {
                    state = StrategyState.HighRisk;
                }
                var tempIndifference = CalcIndifferencePrice(comboTrade);
                if( Position.IsLong && tempIndifference < bestIndifferenceLine[0])
                {
                    bestIndifferenceLine[0] = tempIndifference;
                }
                if( Position.IsShort && tempIndifference > bestIndifferenceLine[0])
                {
                    bestIndifferenceLine[0] = tempIndifference;
                }
                if (lots > nextIncreaseLots)
                {
                    nextIncreaseLots = (nextIncreaseLots * 3) / 2;
                }
                sequentialIncreaseCount++;
                var changed = false;
                change = Math.Abs(change);
                if (fills.First != null)
                {
                    var firstFill = fills.First.Value;
                    if (firstFill.Size + change <= lotSize)
                    {
                        firstFill.Size += change;
                        changed = true;
                    }
                }
                if( !changed)
                {
                    fills.AddFirst(new LocalFill(change, fill.Price, fill.Time));
                }
                SetupBidAsk(fill.Price);
            }
            else
            {
                sequentialIncreaseCount=0;
                change = Math.Abs(change);
                var prevFill = fills.First.Value;
                if (change <= prevFill.Size)
                {
                    prevFill.Size -= change;
                    if (prevFill.Size == 0)
                    {
                        fills.RemoveFirst();
                        if (fills.Count > 0)
                        {
                            SetupBidAsk(fill.Price);
                        }
                    }
                    return;
                }
                change -= prevFill.Size;
                fills.RemoveFirst();
                SetupBidAsk(fill.Price);
                return;

                //for (var current = fills.Last; current != null; current = current.Previous)
                //{
                //    prevFill = current.Value;
                //    if (change > prevFill.Size)
                //    {
                //        change -= prevFill.Size;
                //        fills.Remove(current);
                //        if (fills.Count > 0)
                //        {
                //            SetupBidAsk(fill.Price);
                //        }
                //    }
                //    else
                //    {
                //        prevFill.Size -= change;
                //        if (prevFill.Size == 0)
                //        {
                //            fills.Remove(current);
                //            if (fills.Count > 0)
                //            {
                //                SetupBidAsk(fill.Price);
                //            }
                //        }
                //        break;
                //    }
                //}
            }
            LogFills("OnChange");
        }

        private long lessThan100Count;
        public override void OnExitTrade(TransactionPairBinary comboTrade, LogicalFill fill, LogicalOrder filledOrder)
        {            
            fills.Clear();
            SetFlatBidAsk();
            bestIndifferenceLine[0] = double.NaN;
            if (!comboTrade.Completed)
            {
                throw new InvalidOperationException("Trade must be completed.");
            }
            totalVolume += comboTrade.Volume;
            maxExcursionLine[0] = double.NaN;
            if( maxDrawDown < 500.00)
            {
                lessThan100Count++;
            }
            else
            {
                var pnl = Performance.ComboTrades.CurrentProfitLoss;
                Log.Info(Math.Round(maxDrawDown,2)+","+Math.Round(pnl,2)+","+Performance.Equity.CurrentEquity+","+lessThan100Count+","+comboTrade.EntryTime+","+comboTrade.ExitTime);
            }
            //if (maxTradeSize <= 100 * lotSize)
            //{
            //    lessThan100Count++;
            //}
            //else
            //{
            //    Log.Info((maxTradeSize / lotSize) + "," + lessThan100Count + "," + fill.Time);
            //}
            maxTradeSize = 0;
            LogFills("OnEnterTrade");
        }

    }
}
using System;
using System.Drawing;
using TickZoom.Api;
using TickZoom.Common;

namespace TickZoom.Examples
{
    public class BaseSimpleStrategy : Strategy
    {
        protected IndicatorCommon bidLine;
        protected IndicatorCommon askLine;
        private IndicatorCommon position;
        private IndicatorCommon averagePrice;
        protected bool filterIndifference = false;
        protected double ask, marketAsk;
        protected double bid, marketBid;
        protected double midPoint;
        protected double lastMarketBid;
        protected double lastMarketAsk;
        protected double increaseSpread;
        protected double lastMidPoint;
        protected double indifferencePrice;
        protected bool throttleIncreasing = false;
        private bool isVisible = false;
        protected int sequentialIncreaseCount;
        protected double minimumTick;
        protected int lotSize = 1000;
        protected volatile StrategyState beforeWeekendState = StrategyState.Active;
        protected StrategyState state = StrategyState.Active;
        protected int positionPriorToWeekend = 0;
        bool isFirstTick = true;
        private int buySize = 1;
        private int sellSize = 1;
        private int closeProfitInTicks = 20;

        public override void OnInitialize()
        {
            Performance.Equity.GraphEquity = true; // Graphed by portfolio.
            Performance.GraphTrades = IsVisible;

            askLine = Formula.Indicator();
            askLine.Name = "Ask";
            askLine.Drawing.IsVisible = isVisible;

            bidLine = Formula.Indicator();
            bidLine.Name = "Bid";
            bidLine.Drawing.IsVisible = isVisible;

            averagePrice = Formula.Indicator();
            averagePrice.Name = "BE";
            averagePrice.Drawing.IsVisible = isVisible;
            averagePrice.Drawing.Color = Color.Black;

            position = Formula.Indicator();
            position.Name = "Position";
            position.Drawing.PaneType = PaneType.Secondary;
            position.Drawing.GroupName = "Position";
            position.Drawing.IsVisible = IsVisible;
        }

        protected void Reset()
        {
            if (Position.HasPosition)
            {
                BuySize = 0;
                SellSize = 0;
                Orders.Exit.ActiveNow.GoFlat();
            }
        }

        protected virtual void UpdateIndicators(Tick tick)
        {
            var comboTrades = Performance.ComboTrades;
            if (comboTrades.Count > 0)
            {
                var comboTrade = comboTrades.Tail;
                indifferencePrice = CalcIndifferencePrice(comboTrade);
                if (filterIndifference)
                {
                    var avgDivergence = Math.Abs(tick.Bid - indifferencePrice) / minimumTick;
                    if (avgDivergence > 150 || Position.IsFlat)
                    {
                        averagePrice[0] = double.NaN;
                    }
                    else
                    {
                        averagePrice[0] = indifferencePrice;
                    }
                }
                else
                {
                    averagePrice[0] = indifferencePrice;
                }
            }
            else
            {
                indifferencePrice = (tick.Ask + tick.Bid) / 2;
                averagePrice[0] = indifferencePrice;
            }

            if (bidLine.Count > 0)
            {
                position[0] = Position.Current / lotSize;
            }
        }

        protected void CalcMarketPrices(Tick tick)
        {
            // Calculate market prics.
            marketAsk = Math.Max(tick.Ask, tick.Bid);
            marketBid = Math.Min(tick.Ask, tick.Bid);
            midPoint = (tick.Ask + tick.Bid) / 2;
        }

        protected void HandlePegging(Tick tick)
        {
            if (marketAsk < bid || marketBid > ask)
            {
                SetupBidAsk(midPoint);
            }
            if (marketAsk < lastMarketAsk)
            {
                lastMarketAsk = marketAsk;
                if (marketAsk < ask - increaseSpread && midPoint > lastMidPoint)
                {
                    SetupAsk(midPoint);
                }
            }

            if (marketBid > lastMarketBid)
            {
                lastMarketBid = marketBid;
                if (marketBid > bid + increaseSpread && midPoint < lastMidPoint)
                {
                    SetupBid(midPoint);
                }
            }
        }

        protected void SetFlatBidAsk()
        {
            var tick = Ticks[0];
            var midPoint = (tick.Bid + tick.Ask) / 2;
            var myAsk = midPoint + increaseSpread / 2;
            var myBid = midPoint - increaseSpread / 2;
            marketAsk = Math.Max(tick.Ask, tick.Bid);
            marketBid = Math.Min(tick.Ask, tick.Bid);
            ask = Math.Max(myAsk, marketAsk);
            bid = Math.Min(myBid, marketBid);
            bidLine[0] = bid;
            askLine[0] = ask;
        }

        protected void SetupBidAsk(double price)
        {
            if (Performance.ComboTrades.Count > 0)
            {
                indifferencePrice = CalcIndifferencePrice(Performance.ComboTrades.Tail);
            }
            else
            {
                indifferencePrice = price;
            }
            lastMidPoint = midPoint;
            SetupBidAsk();
        }

        protected virtual void SetupBidAsk()
        {
            SetupAsk(midPoint);
            SetupBid(midPoint);
        }

        protected virtual void SetupAsk(double price)
        {
            var myAsk = midPoint;
            ask = Math.Max(myAsk, marketAsk);
            askLine[0] = ask;
        }

        protected virtual void SetupBid(double price)
        {
            var myBid = midPoint;
            bid = Math.Min(myBid, marketBid);
            bidLine[0] = bid;
        }

        protected double CalcIndifferencePrice(TransactionPairBinary comboTrade)
        {
            var size = Math.Abs(comboTrade.CurrentPosition);
            if (size == 0)
            {
                return midPoint;
            }
            var sign = -Math.Sign(comboTrade.CurrentPosition);
            var openPoints = comboTrade.AverageEntryPrice.ToLong() * size;
            var closedPoints = comboTrade.ClosedPoints.ToLong() * sign;
            var grossProfit = openPoints + closedPoints;
            var transaction = 0; // size * commission * sign;
            var expectedTransaction = 0; // size * commission * sign;
            var result = (grossProfit - transaction - expectedTransaction) / size;
            result = ((result + 5000) / 10000) * 10000;
            return result.ToDouble();
        }

        protected void HandleWeekendRollover(Tick tick)
        {
            var time = tick.Time;
            var utcTime = tick.UtcTime;
            var dayOfWeek = time.GetDayOfWeek();
            switch (state)
            {
                default:
                    if (dayOfWeek == 5)
                    {
                        var hour = time.Hour;
                        var minute = time.Minute;
                        if (hour == 16 && minute > 30)
                        {
                            beforeWeekendState = state;
                            state = StrategyState.EndForWeek;
                            goto EndForWeek;
                        }
                    }
                    break;
                case StrategyState.EndForWeek:
                    EndForWeek:
                    if (dayOfWeek == 5)
                    {
                        if (Position.Current != 0)
                        {
                            positionPriorToWeekend = Position.Current;
                            if (positionPriorToWeekend > 0)
                            {
                                Orders.Change.ActiveNow.SellMarket(positionPriorToWeekend);
                            }
                            else if (positionPriorToWeekend < 0)
                            {
                                Orders.Change.ActiveNow.BuyMarket(Math.Abs(positionPriorToWeekend));
                            }
                        }
                        return;
                    }
                    if (Position.Current == positionPriorToWeekend)
                    {
                        state = beforeWeekendState;
                    }
                    else
                    {
                        if (positionPriorToWeekend > 0)
                        {
                            Orders.Change.ActiveNow.BuyMarket(positionPriorToWeekend);
                        }
                        if (positionPriorToWeekend < 0)
                        {
                            Orders.Change.ActiveNow.SellMarket(Math.Abs(positionPriorToWeekend));
                        }
                    }
                    break;
            }
        }

        protected void ProcessOrders(Tick tick)
        {
            if (Position.IsFlat)
            {
                OnProcessFlat(tick);
            }
            else if (Position.IsLong)
            {
                OnProcessLong(tick);
            }
            else if (Position.IsShort)
            {
                OnProcessShort(tick);
            }

        }
        private void OnProcessFlat(Tick tick)
        {
            if (isFirstTick)
            {
                isFirstTick = false;
                SetFlatBidAsk();
            }
            var comboTrades = Performance.ComboTrades;
            if (comboTrades.Count == 0 || comboTrades.Tail.Completed)
            {
                if (BuySize > 0)
                {
                    Orders.Enter.ActiveNow.BuyLimit(bid, BuySize * lotSize);
                }

                if (SellSize > 0)
                {
                    Orders.Enter.ActiveNow.SellLimit(ask, SellSize * lotSize);
                }
            }
            else
            {
                if (BuySize > 0)
                {
                    Orders.Change.ActiveNow.BuyLimit(bid, BuySize * lotSize);
                }
                if (SellSize > 0)
                {
                    Orders.Change.ActiveNow.SellLimit(ask, SellSize * lotSize);
                }
            }
        }

        private void OnProcessLong(Tick tick)
        {
            var lots = Position.Size / lotSize;
            if (BuySize > 0)
            {
                Orders.Change.ActiveNow.BuyLimit(bid, BuySize * lotSize);
            }
            if (SellSize > 0)
            {
                if (lots == SellSize)
                {
                    //Orders.Reverse.ActiveNow.SellLimit(ask, SellSize * lotSize);
                    Orders.Exit.ActiveNow.SellLimit(ask);
                }
                else
                {
                    Orders.Change.ActiveNow.SellLimit(ask, SellSize * lotSize);
                    //Orders.Reverse.ActiveNow.SellLimit(indifferencePrice + closeProfitInTicks * minimumTick, SellSize * lotSize);
                }
            }
            else
            {
                Orders.Reverse.ActiveNow.SellLimit(indifferencePrice + closeProfitInTicks * minimumTick, SellSize * lotSize);
            }
        }

        private void OnProcessShort(Tick tick)
        {
            var lots = Position.Size / lotSize;
            if (SellSize > 0)
            {
                Orders.Change.ActiveNow.SellLimit(ask, SellSize * lotSize);
            }
            if (BuySize > 0)
            {
                if (lots == BuySize)
                {
                    //Orders.Reverse.ActiveNow.BuyLimit(bid, BuySize * lotSize);
                    Orders.Exit.ActiveNow.BuyLimit(bid);
                }
                else
                {
                    Orders.Change.ActiveNow.BuyLimit(bid, BuySize * lotSize);
                    //Orders.Reverse.ActiveNow.BuyLimit(indifferencePrice - closeProfitInTicks * minimumTick, BuySize * lotSize);
                }
            }
            else
            {
                Orders.Reverse.ActiveNow.BuyLimit(indifferencePrice - closeProfitInTicks * minimumTick, BuySize * lotSize);
            }
        }
        public bool IsVisible
        {
            get { return isVisible; }
            set { isVisible = value; }
        }

        public int BuySize
        {
            get { return buySize; }
            set { buySize = value; }
        }

        public int SellSize
        {
            get { return sellSize; }
            set { sellSize = value; }
        }
    }
}
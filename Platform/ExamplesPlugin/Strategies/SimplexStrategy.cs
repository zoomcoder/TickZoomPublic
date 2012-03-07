using System;
using TickZoom.Api;
using System.Drawing;
using TickZoom.Common;

namespace TickZoom.Examples
{
    public class SimplexStrategy : BaseSimpleStrategy
    {
        private IndicatorCommon hedge;
        private IndicatorCommon ratio;
        private double startingSpread;
        private double addInventorySpread;
        private double bidSpread;
        private double offerSpread;
        private double extremePrice;
        private int maxLots;
        private Func<int> getHedgePosition;
        private Func<double> getRatio;

        public Func<int> GetHedgePosition
        {
            get { return getHedgePosition; }
            set { getHedgePosition = value; }
        }

        public Func<double> GetRatio
        {
            get { return getRatio; }
            set { getRatio = value; }
        }

        public override void OnInitialize()
        {
            lotSize = 1;
            base.OnInitialize();
            bidSpread = offerSpread = startingSpread = 3 * Data.SymbolInfo.MinimumTick;
            addInventorySpread = 3 * Data.SymbolInfo.MinimumTick;
            BuySize = SellSize = 1000;

            hedge = Formula.Indicator();
            hedge.Name = "Hedge";
            hedge.Drawing.PaneType = PaneType.Secondary;
            hedge.Drawing.GroupName = "Position";
            hedge.Drawing.IsVisible = IsVisible;
            hedge.Drawing.Color = Color.Blue;

            ratio = Formula.Indicator();
            ratio.Name = "Ratio";
            ratio.Drawing.PaneType = PaneType.Secondary;
            ratio.Drawing.GroupName = "Ratio";
            ratio.Drawing.IsVisible = IsVisible;
            ratio.Drawing.Color = Color.Blue;
        }


        public override bool OnProcessTick(Tick tick)
        {
            var temp = GetHedgePosition();
            hedge[0] = temp;
            ratio[0] = GetRatio();
            if (!tick.IsQuote)
            {
                throw new InvalidOperationException("This strategy requires bid/ask quote data to operate.");
            }
            Orders.SetAutoCancel();

            CalcMarketPrices(tick);

            SetupBidAsk();

            //HandleWeekendRollover(tick);

            if (state.AnySet(StrategyState.ProcessOrders))
            {
                ProcessOrders(tick);
            }

            UpdateIndicators(tick);
            UpdateIndicators();

            return true;
        }

        protected override void SetFlatBidAsk()
        {
            SetBidOffer();
        }

        protected override void SetupBidAsk()
        {

        }

        private void SetBidOffer()
        {
            bid = midPoint - bidSpread;
            ask = midPoint + offerSpread;
            if( Position.IsLong && ask > BreakEvenPrice)
            {
                ask = BreakEvenPrice + 20*Data.SymbolInfo.MinimumTick;
                SellSize = Position.Size;
            }
            if( Position.IsShort && bid < BreakEvenPrice)
            {
                bid = BreakEvenPrice - 20*Data.SymbolInfo.MinimumTick;
                BuySize = Position.Size;
            }
        }

        private void UpdateIndicators()
        {
            bidLine[0] = bid.Round();
            askLine[0] = ask.Round();
        }

        private int previousPosition;
        private void ProcessChange(TransactionPairBinary comboTrade)
        {
            var lots = (Math.Abs(comboTrade.CurrentPosition)/1000);
            if( lots > maxLots)
            {
                maxLots = lots;
            }
            var extension = Math.Abs(BreakEvenPrice - midPoint);
            var tick = Ticks[0];
            var change = comboTrade.CurrentPosition - previousPosition;
            if( comboTrade.CurrentPosition > 0)
            {
                BuySize = SellSize = 1000;
                if (comboTrade.ExitPrice < BreakEvenPrice)
                {
                    offerSpread = bidSpread = startingSpread + addInventorySpread * maxLots;
                }
                else
                {
                    bidSpread = offerSpread = startingSpread;
                }
            }
            else if( comboTrade.CurrentPosition < 0)
            {
                BuySize = SellSize = 1000;
                if (comboTrade.ExitPrice > BreakEvenPrice)
                {
                    bidSpread = offerSpread = startingSpread + addInventorySpread * maxLots;
                }
                else
                {
                    bidSpread = offerSpread = startingSpread;
                }
            }
            else
            {
                maxLots = 0;
                bidSpread = startingSpread;
                offerSpread = startingSpread;
                BuySize = SellSize = 1000;
            }
            SetBidOffer();
            previousPosition = comboTrade.CurrentPosition;
        }

        public override void OnEnterTrade(TransactionPairBinary comboTrade, LogicalFill fill, LogicalOrder filledOrder)
        {
            ProcessChange(comboTrade);
        }

        public override void OnExitTrade(TransactionPairBinary comboTrade, LogicalFill fill, LogicalOrder filledOrder)
        {
            ProcessChange(comboTrade);
        }

        public override void  OnChangeTrade(TransactionPairBinary comboTrade, LogicalFill fill, LogicalOrder filledOrder)
        {
            ProcessChange(comboTrade);
        }
    }
}
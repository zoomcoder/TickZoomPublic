using System;
using TickZoom.Api;
using System.Drawing;
using TickZoom.Common;

namespace TickZoom.Examples
{
    public class SimplexStrategy : BaseSimpleStrategy
    {
        private InventoryGroup inventory;
        private IndicatorCommon beginningPrice;
        private IndicatorCommon averagePrice;
        private IndicatorCommon extremePrice;

        public override void OnInitialize()
        {
            lotSize = 1;
            base.OnInitialize();
            inventory = (InventoryGroup)new InventoryGroupDefault(Data.SymbolInfo);
            inventory.Retrace = .60;
            inventory.StartingLotSize = 1000;
            inventory.RoundLotSize = 1000;
            inventory.MinimumLotSize = 1000;
            inventory.MaximumLotSize = inventory.MinimumLotSize * 10;
            inventory.Goal = 10000;

            beginningPrice = Formula.Indicator();
            beginningPrice.Name = "Begin";
            beginningPrice.Drawing.IsVisible = isVisible;
            beginningPrice.Drawing.Color = Color.Orange;

            averagePrice = Formula.Indicator();
            averagePrice.Name = "Avg";
            averagePrice.Drawing.IsVisible = isVisible;
            averagePrice.Drawing.Color = Color.Orange;

            extremePrice = Formula.Indicator();
            extremePrice.Name = "Extreme";
            extremePrice.Drawing.IsVisible = isVisible;
            extremePrice.Drawing.Color = Color.Orange;
        }

        public override bool OnProcessTick(Tick tick)
        {
            if (!tick.IsQuote)
            {
                throw new InvalidOperationException("This strategy requires bid/ask quote data to operate.");
            }
            Orders.SetAutoCancel();

            CalcMarketPrices(tick);

            inventory.UpdateBidAsk(marketBid, marketAsk);

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
            inventory.CalculateBidOffer(marketBid, marketAsk);
            bid = inventory.Bid;
            BuySize = inventory.BidSize;
            ask = inventory.Offer;
            SellSize = inventory.OfferSize;
        }

        private void UpdateIndicators()
        {
            bidLine[0] = bid.Round();
            askLine[0] = ask.Round();
            beginningPrice[0] = inventory.BeginningPrice == 0D ? double.NaN : inventory.BeginningPrice.Round();
            averagePrice[0] = inventory.BreakEven == 0D ? double.NaN : inventory.BreakEven.Round();
            extremePrice[0] = inventory.ExtremePrice == 0D ? double.NaN : inventory.ExtremePrice.Round();
        }

        private int previousPosition;

        private TimeStamp debugTime = new TimeStamp("2011-08-03 17:58");
        public override void OnEnterTrade(TransactionPairBinary comboTrade, LogicalFill fill, LogicalOrder filledOrder)
        {
            if( Data.Ticks[0].Time > debugTime)
            {
                int x = 0;
            }
            var change = comboTrade.CurrentPosition - previousPosition;
            inventory.Change(fill.Price, change);
            SetBidOffer();
            previousPosition = comboTrade.CurrentPosition;
        }

        public override void OnExitTrade(TransactionPairBinary comboTrade, LogicalFill fill, LogicalOrder filledOrder)
        {
            if (Data.Ticks[0].Time > debugTime)
            {
                int x = 0;
            }
            var change = comboTrade.CurrentPosition - previousPosition;
            inventory.Change(fill.Price, change);
            SetBidOffer();
            previousPosition = comboTrade.CurrentPosition;
        }

        public override void  OnChangeTrade(TransactionPairBinary comboTrade, LogicalFill fill, LogicalOrder filledOrder)
        {
            if (Data.Ticks[0].Time > debugTime)
            {
                int x = 0;
            }
            var change = comboTrade.CurrentPosition - previousPosition;
            inventory.Change(fill.Price, change);
            SetBidOffer();
            previousPosition = comboTrade.CurrentPosition;
        }
    }
}
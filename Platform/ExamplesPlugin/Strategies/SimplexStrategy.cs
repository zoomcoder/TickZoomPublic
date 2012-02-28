using System;
using TickZoom.Api;

namespace TickZoom.Examples
{
    public class SimplexStrategy : BaseSimpleStrategy
    {
        private InventoryGroup inventory;

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
        }

        public override bool OnProcessTick(Tick tick)
        {
            if (!tick.IsQuote)
            {
                throw new InvalidOperationException("This strategy requires bid/ask quote data to operate.");
            }
            Orders.SetAutoCancel();

            CalcMarketPrices(tick);

            UpdateIndicators(tick);

            SetupBidAsk();

            //HandleWeekendRollover(tick);

            if (state.AnySet(StrategyState.ProcessOrders))
            {
                ProcessOrders(tick);
            }
            return true;
        }

        protected override void SetupBidAsk()
        {
            if( Position.IsFlat)
            {
                SetBidOffer();
            }
        }

        private void SetBidOffer()
        {
            inventory.CalculateBidOffer(marketBid, marketAsk);
            bid = inventory.Bid;
            bidLine[0] = bid;
            BuySize = inventory.BidSize;
            ask = inventory.Offer;
            askLine[0] = ask;
            SellSize = inventory.OfferSize;
        }

        private int previousPosition;

        public override void OnEnterTrade(TransactionPairBinary comboTrade, LogicalFill fill, LogicalOrder filledOrder)
        {
            var change = comboTrade.CurrentPosition - previousPosition;
            inventory.Change(fill.Price, change);
            SetBidOffer();
            previousPosition = comboTrade.CurrentPosition;
        }

        public override void OnExitTrade(TransactionPairBinary comboTrade, LogicalFill fill, LogicalOrder filledOrder)
        {
            var change = comboTrade.CurrentPosition - previousPosition;
            inventory.Change(fill.Price, change);
            SetBidOffer();
            previousPosition = comboTrade.CurrentPosition;
        }

        public override void  OnChangeTrade(TransactionPairBinary comboTrade, LogicalFill fill, LogicalOrder filledOrder)
        {
            var change = comboTrade.CurrentPosition - previousPosition;
            inventory.Change(fill.Price, change);
            SetBidOffer();
            previousPosition = comboTrade.CurrentPosition;
        }
    }
}
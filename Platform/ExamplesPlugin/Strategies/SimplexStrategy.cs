using System;
using TickZoom.Api;
using System.Drawing;
using TickZoom.Common;

namespace TickZoom.Examples
{
    public class SimplexStrategy : BaseSimpleStrategy
    {
        public SpreadIncrease bidSpread;
        public SpreadIncrease offerSpread;

        public override void OnInitialize()
        {
            lotSize = 1;
            base.OnInitialize();
            bidSpread = new SpreadIncrease(3*Data.SymbolInfo.MinimumTick, 3 * Data.SymbolInfo.MinimumTick);
            offerSpread = new SpreadIncrease(3 * Data.SymbolInfo.MinimumTick, 3 * Data.SymbolInfo.MinimumTick);
        }

        public override bool OnProcessTick(Tick tick)
        {
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
            //inventory.CalculateBidOffer(marketBid, marketAsk);
            bid = midPoint - bidSpread.CurrentSpread;
            BuySize = 1000;
            ask = midPoint + offerSpread.CurrentSpread;
            SellSize = 1000;
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
            var tick = Ticks[0];
            var change = comboTrade.CurrentPosition - previousPosition;
            if( comboTrade.CurrentPosition > 0)
            {
                bidSpread.CurrentSpread = bidSpread.StartingSpread + bidSpread.IncreaseSpread*lots;
                var divisor = Math.Max(1, lots / 4);
                offerSpread.CurrentSpread = (breakEvenPrice - tick.Bid) / divisor;
            }
            if( comboTrade.CurrentPosition < 0)
            {
                offerSpread.CurrentSpread = offerSpread.StartingSpread + offerSpread.IncreaseSpread * lots;
                var divisor = Math.Max(1, lots / 4);
                bidSpread.CurrentSpread = (tick.Ask - breakEvenPrice) / divisor;
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
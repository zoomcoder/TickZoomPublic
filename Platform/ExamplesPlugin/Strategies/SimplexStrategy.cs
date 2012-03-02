using System;
using TickZoom.Api;
using System.Drawing;
using TickZoom.Common;

namespace TickZoom.Examples
{
    public class SimplexStrategy : BaseSimpleStrategy
    {
        private double startingSpread;
        private double addInventorySpread;
        private double removeInventorySpread;
        private double bidSpread;
        private double offerSpread;
        private double extremePrice;
        private int maxLots;
        public override void OnInitialize()
        {
            lotSize = 1;
            base.OnInitialize();
            bidSpread = offerSpread = startingSpread = 3 * Data.SymbolInfo.MinimumTick;
            addInventorySpread = 3 * Data.SymbolInfo.MinimumTick;
            removeInventorySpread = addInventorySpread*10;
            BuySize = SellSize = 1000;
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
            bid = midPoint - bidSpread;
            ask = midPoint + offerSpread;
            if( Position.IsLong && ask > breakEvenPrice)
            {
                ask = breakEvenPrice + 20*Data.SymbolInfo.MinimumTick;
                SellSize = Position.Size;
            }
            if( Position.IsShort && bid < breakEvenPrice)
            {
                bid = breakEvenPrice - 20*Data.SymbolInfo.MinimumTick;
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
            var extension = Math.Abs(breakEvenPrice - midPoint);
            var tick = Ticks[0];
            var change = comboTrade.CurrentPosition - previousPosition;
            if( comboTrade.CurrentPosition > 0) 
            {
                BuySize = SellSize = 1000;
                if (comboTrade.ExitPrice < breakEvenPrice)
                {
                    bidSpread = startingSpread + addInventorySpread * lots;
                    var divisor = Math.Max(1, lots / 4);
                    var removedLots = maxLots - lots;
                    offerSpread = removeInventorySpread + removeInventorySpread * removedLots;
                    //offerSpread = extension / divisor;
                }
                else
                {
                    bidSpread = offerSpread = startingSpread;
                }
            }
            else if( comboTrade.CurrentPosition < 0)
            {
                BuySize = SellSize = 1000;
                if (comboTrade.ExitPrice > breakEvenPrice)
                {
                    offerSpread = startingSpread + addInventorySpread * lots;
                    var divisor = Math.Max(1, lots / 4);
                    var removedLots = maxLots - lots;
                    bidSpread = removeInventorySpread + removeInventorySpread * removedLots;
                    //bidSpread = extension / divisor;
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
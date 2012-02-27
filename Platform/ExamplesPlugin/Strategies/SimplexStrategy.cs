using System;
using TickZoom.Api;

namespace TickZoom.Examples
{
    public class SimplexStrategy : BaseSimpleStrategy
    {
        private InventoryGroup inventory;
        public SimplexStrategy()
        {
            var inventory = (InventoryGroup)new InventoryGroupDefault(Data.SymbolInfo);
            inventory.Retrace = .60;
            inventory.StartingLotSize = 1000;
            inventory.RoundLotSize = 1000;
            inventory.MinimumLotSize = 1000;
            inventory.MaximumLotSize = inventory.MinimumLotSize * 10;
            inventory.Goal = 1000;
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

            HandlePegging(tick);

            HandleWeekendRollover(tick);

            if (state.AnySet(StrategyState.ProcessOrders))
            {
                ProcessOrders(tick);
            }
            return true;
        }

        public override void OnEnterTrade(TransactionPairBinary comboTrade, LogicalFill fill, LogicalOrder filledOrder)
        {
            
        }
    }
}
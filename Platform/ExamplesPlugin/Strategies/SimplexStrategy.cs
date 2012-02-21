using System;
using TickZoom.Api;

namespace TickZoom.Examples
{
    public class SimplexStrategy : BaseSimpleStrategy
    {
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

    }
}
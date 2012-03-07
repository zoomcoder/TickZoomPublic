using TickZoom.Common;
using TickZoom.Api;

namespace TickZoom.Examples
{
    public class SimplexPortfolio : Portfolio
    {
        private SimplexStrategy gbpUsd;
        private SimplexStrategy eurUsd;
        private SimplexStrategy eurGbp;
        public SimplexPortfolio()
        {
        }

        public override void OnInitialize()
        {
            Performance.Equity.GraphEquity = true;
            gbpUsd = Strategies[0] as SimplexStrategy;
            gbpUsd.Name = "Simplex Strategy";
            gbpUsd.GetHedgePosition = GetGbpUsd;
            gbpUsd.GetRatio = GetRatio;
            eurUsd = Strategies[1] as SimplexStrategy;
            eurUsd.Name = "Simplex Strategy";
            eurUsd.GetHedgePosition = GetEurUsd;
            eurUsd.GetRatio = GetRatio;
            eurGbp = Strategies[2] as SimplexStrategy;
            eurGbp.Name = "Simplex Strategy";
            eurGbp.GetHedgePosition = GetEurGbp;
            eurGbp.GetRatio = GetRatio;
        }

        public int GetGbpUsd()
        {
            OnTick();
            return (int)(eurGbp.BreakEvenPrice * eurGbp.Position.Current);
        }

        public int GetEurUsd()
        {
            OnTick();
            return eurGbp.BreakEvenPrice == 0 ? 0 : (int)(gbpUsd.BreakEvenPrice*gbpUsd.Position.Current* (1/(eurGbp.BreakEvenPrice)));
        }

        public int GetEurGbp()
        {
            OnTick();
            return - eurUsd.Position.Current;
        }

        private double ratio = 1;
        public double GetRatio()
        {
            return ratio;
        }
        public void OnTick()
        {
            var ratio1 = gbpUsd.MarketAsk * eurGbp.MarketAsk / eurUsd.MarketBid;
            var ratio2 = gbpUsd.MarketBid * eurGbp.MarketBid / eurUsd.MarketAsk;
            if (gbpUsd.Position.HasPosition && eurUsd.Position.HasPosition && eurGbp.Position.HasPosition)
            {
                ratio = gbpUsd.BreakEvenPrice * eurGbp.BreakEvenPrice / eurUsd.BreakEvenPrice;
                var gbpUsdPrice = eurUsd.BreakEvenPrice / eurGbp.BreakEvenPrice;
                var eurUsdPrice = gbpUsd.BreakEvenPrice*eurGbp.BreakEvenPrice;
                var eurGbpPrice = eurUsd.BreakEvenPrice/gbpUsd.BreakEvenPrice;
                var gbpUsdPosition = gbpUsd.Position.Current;
                var eurUsdPosition = eurUsd.Position.Current;
                var eurGbpPosition = eurGbp.Position.Current;
                if (ratio >= 1.0003D)
                {
                    int x = 0;
                }
            }
        }
    }
}
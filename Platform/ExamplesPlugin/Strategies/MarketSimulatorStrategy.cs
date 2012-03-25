using TickZoom.Api;
using TickZoom.Common;

namespace TickZoom.Examples
{
    public class MarketSimulatorStrategy : Strategy
    {
        double multiplier = 1.0D;
        double minimumTick;
        int tradeSize;
		
        public MarketSimulatorStrategy() {
            Performance.GraphTrades = true;
            Performance.Equity.GraphEquity = true;
            ExitStrategy.ControlStrategy = false;
        }
		
        public override void OnInitialize()
        {
            tradeSize = Data.SymbolInfo.Level2LotSize * 10;
            minimumTick = multiplier * Data.SymbolInfo.MinimumTick;
            //ExitStrategy.BreakEven = 30 * minimumTick;
            //ExitStrategy.StopLoss = 45 * minimumTick;
        }

        public override bool OnProcessTick(Tick tick)
        {
            var midPoint = (tick.Ask + tick.Bid)/2;
            var bid = midPoint - Data.SymbolInfo.MinimumTick;
            var ask = midPoint + Data.SymbolInfo.MinimumTick;
            if (Position.IsFlat) 
            {
                if( Performance.ComboTrades.Count <= 0 || Performance.ComboTrades.Tail.Completed)
                {
                    Orders.Enter.ActiveNow.BuyLimit(bid, tradeSize);
                    Orders.Enter.ActiveNow.SellLimit(ask, tradeSize);
                }
                else
                {
                    Orders.Change.ActiveNow.BuyLimit(bid, tradeSize);
                    Orders.Change.ActiveNow.SellLimit(ask, tradeSize);
                }
            }
            else if (Position.HasPosition)
            {
                Orders.Change.ActiveNow.BuyLimit(bid, tradeSize);
                Orders.Change.ActiveNow.SellLimit(ask, tradeSize);
            }
            return true;
        }

        public override bool OnWriteReport(string folder)
        {
            return false;
        }

        public double Multiplier {
            get { return multiplier; }
            set { multiplier = value; }
        }
    }
}
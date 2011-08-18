using TickZoom.Api;
using TickZoom.Common;

namespace TickZoom.Examples
{
    public class ExampleOptionStrategy : Portfolio
    {

        public ExampleOptionStrategy()
        {
            //Performance.GraphTrades = true;
            //Performance.Equity.GraphEquity = true;
        }

        public override void OnInitialize()
        {
        }

        public override bool OnIntervalClose()
        {
            return true;
        }

        public override bool OnProcessTick(Tick tick)
        {
            if( tick.IsOption)
            {
                var strike = tick.Strike;
            }
            return true;
        }
    }
}
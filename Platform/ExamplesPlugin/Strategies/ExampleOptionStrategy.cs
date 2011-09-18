using System.Drawing;
using TickZoom.Api;
using TickZoom.Common;

namespace TickZoom.Examples
{
    public class ExampleOptionStrategy : Portfolio
    {
        //private TimeStamp expiration = new TimeStamp( );
        private IndicatorCommon optionBid;
        private IndicatorCommon optionAsk;
        public ExampleOptionStrategy()
        {
        }

        public override void OnInitialize()
        {
            optionAsk = Formula.Indicator();
            optionAsk.Drawing.PaneType = PaneType.Secondary;
            optionAsk.Drawing.IsVisible = true;
            optionBid = Formula.Indicator();
            optionBid.Drawing.PaneType = PaneType.Secondary;
            optionBid.Drawing.IsVisible = true;
            optionBid.Drawing.Color = Color.Blue;
        }

        public override bool OnIntervalClose()
        {
            return true;
        }

        public override bool OnProcessTick(Tick tick)
        {
            if( tick.IsOption)
            {
                if (tick.OptionType == OptionType.Put && tick.UtcOptionExpiration.Month == 9 && tick.Strike == 144.00D)
                {
                    if (tick.IsQuote)
                    {
                        optionBid[0] = tick.Bid;
                        optionAsk[0] = tick.Ask;
                    }
                }
            }
            return true;
        }
    }
}
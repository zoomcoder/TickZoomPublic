using TickZoom.Api;
using TickZoom.FIX;

namespace TickZoom.MBTFIX
{
    public class MBTProviderSimulator : ProviderSimulatorSupport
    {
        public MBTProviderSimulator(string mode, ProjectProperties projectProperties)
            : base(mode, projectProperties, typeof(MBTFIXSimulator), typeof(MBTQuoteSimulator))
        {
            
        }
    }
}
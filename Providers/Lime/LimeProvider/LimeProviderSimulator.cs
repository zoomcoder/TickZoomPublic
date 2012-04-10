using TickZoom.Api;
using TickZoom.FIX;

namespace TickZoom.LimeFIX 
{
    class LimeProviderSimulator : ProviderSimulatorSupport
    {
        public LimeProviderSimulator(string mode, ProjectProperties projectProperties)
            : base(mode, projectProperties, typeof(LimeFIXSimulator), typeof(LimeQuotesSimulator))
        {
            
        } }
}

using TickZoom.Api;

namespace TickZoom.Examples
{
    public class ExampleMixedTruePartial : ExampleMixedLoader
    {
        public ExampleMixedTruePartial()
        {
            category = "Example";
            name = "True Partial: Multi-Symbol, Multi-Strategy";
        }
        public override void OnLoad(ProjectProperties properties)
        {
            foreach (var symbol in properties.Starter.SymbolProperties)
            {
                symbol.PartialFillSimulation = PartialFillSimulation.PartialFillsIncomplete;
            }
            base.OnLoad(properties);
        }
    }
}
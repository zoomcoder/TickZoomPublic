using TickZoom.Api;
using TickZoom.Common;

namespace TickZoom.Examples
{
    public class ExampleLimitTruePartialLoader : ExampleLimitOrderLoader
    {
        public ExampleLimitTruePartialLoader()
        {
            category = "Example";
            name = "True Partial LimitOrders";
        }
        public override void OnInitialize(ProjectProperties properties) {
        }
		
        public override void OnLoad(ProjectProperties properties) {
            foreach (var symbol in properties.Starter.SymbolProperties)
            {
                symbol.PartialFillSimulation = PartialFillSimulation.PartialFillsTillComplete;
            }
            TopModel = GetStrategy("ExampleOrderStrategy");
        }
    }

    /// <summary>
    /// Description of Starter.
    /// </summary>
    public class ExampleLimitOrderLoader : ModelLoaderCommon
    {
        public ExampleLimitOrderLoader() {
            /// <summary>
            /// IMPORTANT: You can personalize the name of each model loader.
            /// </summary>
            category = "Example";
            name = "Limit Orders";
        }
		
        public override void OnInitialize(ProjectProperties properties) {
        }
		
        public override void OnLoad(ProjectProperties properties) {
            TopModel = GetStrategy("ExampleOrderStrategy");
        }
    }
}
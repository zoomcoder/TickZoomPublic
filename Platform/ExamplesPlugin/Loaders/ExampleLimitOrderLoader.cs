using TickZoom.Api;
using TickZoom.Common;

namespace TickZoom.Examples
{
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
            name = "Simulated Ticks";
        }
		
        public override void OnInitialize(ProjectProperties properties) {
        }
		
        public override void OnLoad(ProjectProperties properties) {
            TopModel = GetStrategy("ExampleOrderStrategy");
        }
    }
}
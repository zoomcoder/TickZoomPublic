using TickZoom.Api;
using TickZoom.Common;

namespace TickZoom.Examples
{
    public class ExampleOptionLoader : ModelLoaderCommon
    {
        public ExampleOptionLoader()
        {
            /// <summary>
            /// IMPORTANT: You can personalize the name of each model loader.
            /// </summary>
            category = "Example";
            name = "Option Single-Symbol";
        }

        public override void OnInitialize(ProjectProperties properties)
        {
        }

        public override void OnLoad(ProjectProperties properties)
        {
            var strategy = new ExampleOptionStrategy();
            TopModel = strategy;
        }
    }
}
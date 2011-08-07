using TickZoom.Api;
using TickZoom.Common;

namespace TickZoom.Examples
{
    public class LimitChangeOffsetDisabledLoader : ModelLoaderCommon
    {
        public LimitChangeOffsetDisabledLoader()
        {
            /// <summary>
            /// IMPORTANT: You can personalize the name of each model loader.
            /// </summary>
            category = "Example";
            name = "Limit Change Offset Disabled Single-Symbol";
        }

        public override void OnInitialize(ProjectProperties properties)
        {
            foreach (var symbol in properties.Starter.SymbolProperties)
            {
                symbol.OffsetTooLateToCancel = false;
            }
        }

        public override void OnLoad(ProjectProperties model)
        {
            TopModel = new LimitChangeStrategy();
        }

    }
}
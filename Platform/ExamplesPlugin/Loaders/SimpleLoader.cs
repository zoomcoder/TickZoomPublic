using System.Collections.Generic;
using TickZoom.Api;
using TickZoom.Common;

namespace TickZoom.Examples
{
    public class SimpleLoader : ModelLoaderCommon
    {
        public SimpleLoader()
        {
            /// <summary>
            /// IMPORTANT: You can personalize the name of each model loader.
            /// </summary>
            category = "Example";
            name = "Simple Multi-Symbol";
        }

        public override void OnInitialize(ProjectProperties properties)
        {
        }

        public override void OnLoad(ProjectProperties properties)
        {
            var strategies = new List<Strategy>();
            foreach (var symbol in properties.Starter.SymbolProperties)
            {
                var strategy = new OtherStrategy();
                strategy.SymbolDefault = symbol.Symbol;
                strategy.IsActive = true;
                strategy.IsVisible = true;
                strategies.Add(strategy);
            }

            if (strategies.Count == 1)
            {
                TopModel = strategies[0];
            }
            else
            {
                var portfolio = new Portfolio();
                foreach (var strategy in strategies)
                {
                    portfolio.AddDependency(strategy);
                }
                TopModel = portfolio;
            }
        }
    }
}
using System.Collections.Generic;
using TickZoom.Api;
using TickZoom.Common;

namespace TickZoom.Examples
{
    public class HedgeLoader : ModelLoaderCommon
    {
        public HedgeLoader()
        {
            /// <summary>
            /// IMPORTANT: You can personalize the name of each model loader.
            /// </summary>
            category = "Example";
            name = "Hedge Multi-Symbol";
        }

        public override void OnInitialize(ProjectProperties properties)
        {
            properties.Starter.PortfolioSyncInterval = Intervals.Second1;
        }

        public override void OnLoad(ProjectProperties properties)
        {
            var portfolio = new SimplexPortfolio();
            foreach (var symbol in properties.Starter.SymbolProperties)
            {
                symbol.LimitOrderQuoteSimulation = LimitOrderQuoteSimulation.OppositeQuoteTouch;
                var strategy = new SimplexStrategy();
                strategy.SymbolDefault = symbol.Symbol;
                strategy.IsActive = true;
                strategy.IsVisible = true;
                portfolio.AddDependency(strategy);
            }
            TopModel = portfolio;
        }
    }
}
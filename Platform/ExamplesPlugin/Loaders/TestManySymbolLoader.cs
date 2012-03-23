using TickZoom.Api;
using TickZoom.Common;

namespace TickZoom.Examples
{
    public class TestManySymbolLoader : ModelLoaderCommon
    {
        public TestManySymbolLoader() {
            /// <summary>
            /// You can personalize the name of each model loader.
            /// </summary>
            category = "Test";
            name = "Limit Order Many-Symbol";
        }
		
        public override void OnInitialize(ProjectProperties properties) {
        }
	
        public override void OnLoad(ProjectProperties properties)
        {
            var portfolio = new Portfolio();
            foreach( ISymbolProperties symbol in properties.Starter.SymbolProperties) {
                var strategy = new ExampleOrderStrategy();
                strategy.Name = strategy.Name + "-" + symbol.Symbol;
                switch( symbol.Symbol)
                {
                    case "MSFT":
                    case "CSCO":
                    case "SPY":
                    case "BAC":
                    case "INTC":
                    case "PFE":
                    case "T":
                        strategy.IsShortOnly = true;
                        break;
                }
                strategy.SymbolDefault = symbol.Symbol;
                strategy.Performance.Equity.GraphEquity = false;
                portfolio.AddDependency(strategy);
            }
	
            TopModel = portfolio;
        }
    }
}
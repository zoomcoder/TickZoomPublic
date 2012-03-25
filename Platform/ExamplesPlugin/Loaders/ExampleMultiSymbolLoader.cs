using TickZoom.Api;
using TickZoom.Common;

namespace TickZoom.Examples
{
    public class ExampleMultiSymbolLoader : ModelLoaderCommon
    {
        public ExampleMultiSymbolLoader() {
            /// <summary>
            /// You can personalize the name of each model loader.
            /// </summary>
            category = "Example";
            name = "Reversal Multi-Symbol";
        }
		
        public override void OnInitialize(ProjectProperties properties) {
        }
	
        public override void OnLoad(ProjectProperties properties) {
            foreach( ISymbolProperties symbol in properties.Starter.SymbolProperties) {
                string name = symbol.Symbol;				
                Strategy strategy = CreateStrategy("ExampleReversalStrategy","ExampleReversal-"+name);
                strategy.SymbolDefault = name;
                strategy.Performance.Equity.GraphEquity = false;
                AddDependency( "Portfolio", strategy);
            }
	
            TopModel = GetPortfolio("Portfolio");
        }
    }
}
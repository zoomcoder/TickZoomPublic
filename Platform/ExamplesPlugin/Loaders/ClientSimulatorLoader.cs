using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TickZoom.Api;
using TickZoom.Common;
using TickZoom.Examples.Strategies;

namespace TickZoom.Examples.Loaders
{
    public class ClientSimulatorLoader : ModelLoaderCommon
    {
        public ClientSimulatorLoader()
        {
            
            category = "Example";
            name = "Client Simulator";
        }

        public override void OnInitialize(ProjectProperties properties)
        {
        }

        public override void OnLoad(ProjectProperties properties)
        {
            foreach (ISymbolProperties symbol in properties.Starter.SymbolProperties)
            {
                string name = symbol.Symbol;
                Strategy strategy = new ClientSimulatorStrategy();
                strategy.SymbolDefault = name;
                strategy.Performance.Equity.GraphEquity = false;
                AddDependency("Portfolio", strategy);
            }

            TopModel = GetPortfolio("Portfolio");
        }}
}

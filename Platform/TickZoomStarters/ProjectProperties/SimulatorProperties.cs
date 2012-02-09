using System.Collections.Generic;
using TickZoom.Api;
using TickZoom.Symbols;

namespace TickZoom.Properties
{
    /// <summary>
    /// Description of SimulatorProperties
    /// </summary>
    public class SimulatorProperties : PropertiesBase, TickZoom.Api.SimulatorProperties
    {
        private readonly static Log log = Factory.SysLog.GetLogger(typeof(SimulatorProperties));
        private readonly bool debug = log.IsDebugEnabled;
        private readonly bool trace = log.IsTraceEnabled;
        private  bool enableNegativeTests;
        private PartialFillSimulation partialFillSimulation = PartialFillSimulation.PartialFillsTillComplete;

        public PartialFillSimulation PartialFillSimulation
        {
            get { return partialFillSimulation; }
            set { partialFillSimulation = value; }
        }

        public bool EnableNegativeTests
        {
            get { return enableNegativeTests; }
            set { enableNegativeTests = value; }
        }
    }
}
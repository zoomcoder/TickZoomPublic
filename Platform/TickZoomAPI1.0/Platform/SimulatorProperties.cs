using System.Collections.Generic;

namespace TickZoom.Api
{
    public interface SimulatorProperties
    {
        PartialFillSimulation PartialFillSimulation { get; set;  }
        bool EnableNegativeTests { get; set; }
        TimeStamp WarmStartTime { get; set; }
        Dictionary<SimulatorType, int> NegativeSimulatorMinimums { get; }
    }

}
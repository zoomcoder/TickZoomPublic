namespace TickZoom.Api
{
    public interface SimulatorProperties
    {
        PartialFillSimulation PartialFillSimulation { get; set;  }
        bool EnableNegativeTests { get; set; }
    }
}
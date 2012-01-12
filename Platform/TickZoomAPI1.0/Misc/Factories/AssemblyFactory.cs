namespace TickZoom.Api
{
    public interface AssemblyFactory
    {
        AgentPerformer CreatePerformer(string className, params object[] args);
    }
}
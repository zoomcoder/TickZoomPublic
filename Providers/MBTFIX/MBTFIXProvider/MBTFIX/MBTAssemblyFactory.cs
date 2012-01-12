using System;
using System.Reflection;
using TickZoom.Api;

namespace TickZoom.MBTFIX
{
    public class MBTAssemblyFactory : AssemblyFactory
    {
        public AgentPerformer CreatePerformer(string className, params object[] args)
        {
            var typeToSpawn = Type.GetType(className);
            var assembly = Assembly.GetExecutingAssembly();
            foreach (var t in assembly.GetTypes())
            {
                if (t.IsClass && !t.IsAbstract && !t.IsInterface)
                {
					if (t.GetInterface(typeof(AgentPerformer).FullName) != null)
					{
					    if (t.FullName.Contains(className))
					    {
                            return (AgentPerformer)Factory.Parallel.Spawn(t, args);
                        }
					}
                }
            }
            throw new ApplicationException("Unexpected type to construct: " + className);
        }
    }
}
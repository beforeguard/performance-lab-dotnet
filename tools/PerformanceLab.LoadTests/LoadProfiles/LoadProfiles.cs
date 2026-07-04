using NBomber.CSharp;
using NBomber.Contracts;

namespace PerformanceLab.LoadTests.LoadProfiles;

public static class LoadProfiles
{
    public static LoadSimulation[] CapacityCurve(int secondsPerStep)
    {
        return new[]
        {
            Simulation.Inject(10,  TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(secondsPerStep)),
            Simulation.Inject(25,  TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(secondsPerStep)),
            Simulation.Inject(50,  TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(secondsPerStep)),
            Simulation.Inject(100, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(secondsPerStep)),
            Simulation.Inject(200, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(secondsPerStep)),
        };
    }

    public static LoadSimulation[] SteadyState(int rps, int seconds)
    {
        return new[]
        {
            Simulation.Inject(rps, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(seconds))
        };
    }
}
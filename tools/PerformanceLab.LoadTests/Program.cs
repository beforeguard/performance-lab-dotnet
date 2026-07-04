using NBomber.CSharp;
using PerformanceLab.LoadTests.Scenarios;

NBomberRunner
    .RegisterScenarios(
        UsersScenarios.Baseline(),
        UsersScenarios.CapacityCurve()
    )
    .Run();
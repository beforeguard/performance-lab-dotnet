using NBomber.CSharp;
using NBomber.Contracts;
using PerformanceLab.LoadTests.Http;
using PerformanceLab.LoadTests.LoadProfiles;

namespace PerformanceLab.LoadTests.Scenarios;

public static class UsersScenarios
{
    public static ScenarioProps Baseline()
    {
        var client = HttpClientFactory.Create();

        return Scenario.Create("users_baseline", async context =>
        {
            var response = await client.GetAsync("http://localhost:5206/users");

            response.EnsureSuccessStatusCode();
            await response.Content.ReadAsByteArrayAsync();

            return Response.Ok();
        })
        .WithLoadSimulations(
            LoadProfiles.LoadProfiles.SteadyState(50, 60)
        );
    }

    public static ScenarioProps CapacityCurve()
    {
        var client = HttpClientFactory.Create();

        return Scenario.Create("users_capacity_curve", async context =>
        {
            var response = await client.GetAsync("http://localhost:5206/users");

            response.EnsureSuccessStatusCode();
            await response.Content.ReadAsByteArrayAsync();

            return Response.Ok();
        })
        .WithLoadSimulations(
            LoadProfiles.LoadProfiles.CapacityCurve(secondsPerStep: 15)
        );
    }
}
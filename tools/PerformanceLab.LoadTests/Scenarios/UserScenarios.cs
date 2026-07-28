using NBomber.CSharp;
using NBomber.Contracts;
using PerformanceLab.LoadTests.Http;
using PerformanceLab.LoadTests.LoadProfiles;
using PerformanceLab.LoadTests.Metrics;

namespace PerformanceLab.LoadTests.Scenarios;

public static class UsersScenarios
{
    private static async Task<IResponse> ExecuteRequest(HttpClient client, string url)
    {
        // Use ResponseHeadersRead to avoid buffering - measures TTFB accurately
        var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

        response.EnsureSuccessStatusCode();
        
        // Extract TTFB from response header (if present)
        double? ttfbMs = null;
        if (response.Headers.TryGetValues("X-TTFB-Ms", out var ttfbValues))
        {
            if (double.TryParse(ttfbValues.FirstOrDefault(), out var parsed))
            {
                ttfbMs = parsed;
                TtfbTracker.Record(parsed); // Track for analysis
            }
        }
        
        // Read response body and measure actual wire size
        var responseBytes = await response.Content.ReadAsByteArrayAsync();
        
        // Extract compression type
        string compressionType = "none";
        if (response.Content.Headers.ContentEncoding.Any())
        {
            compressionType = string.Join(", ", response.Content.Headers.ContentEncoding);
        }
        
        // Track actual response size (wire bytes after compression)
        ResponseSizeTracker.Record(responseBytes.Length, compressionType);

        return ttfbMs.HasValue
            ? Response.Ok(payload: ttfbMs.Value) // Track TTFB as payload for custom metrics
            : Response.Ok();
    }

    // Baseline scenario - unchanged from original implementation
    public static ScenarioProps Baseline()
    {
        var client = HttpClientFactory.Create();

        return Scenario.Create("users_baseline", async context =>
        {
            return await ExecuteRequest(client, "http://localhost:5206/users");
        })
        .WithLoadSimulations(
            LoadProfiles.LoadProfiles.SteadyState(50, 60)
        );
    }

    // Parameterized pagination scenario for scalability curve testing
    public static ScenarioProps Paginated(int limit)
    {
        var client = HttpClientFactory.Create();
        var scenarioName = $"users_paginated_{limit}";
        var url = $"http://localhost:5206/users?offset=0&limit={limit}";

        return Scenario.Create(scenarioName, async context =>
        {
            return await ExecuteRequest(client, url);
        })
        .WithLoadSimulations(
            LoadProfiles.LoadProfiles.SteadyState(50, 60)
        );
    }

    // Capacity curve scenario - unchanged from original implementation
    public static ScenarioProps CapacityCurve()
    {
        var client = HttpClientFactory.Create();

        return Scenario.Create("users_capacity_curve", async context =>
        {
            return await ExecuteRequest(client, "http://localhost:5206/users");
        })
        .WithLoadSimulations(
            LoadProfiles.LoadProfiles.CapacityCurve(secondsPerStep: 15)
        );
    }

    // Paginated capacity curve scenario - parameterized for flexibility
    public static ScenarioProps CapacityCurvePaginated(int limit = 100)
    {
        var client = HttpClientFactory.Create();
        var scenarioName = $"users_capacity_curve_paginated_{limit}";
        var url = $"http://localhost:5206/users?offset=0&limit={limit}";

        return Scenario.Create(scenarioName, async context =>
        {
            return await ExecuteRequest(client, url);
        })
        .WithLoadSimulations(
            LoadProfiles.LoadProfiles.CapacityCurve(secondsPerStep: 15)
        );
    }
}
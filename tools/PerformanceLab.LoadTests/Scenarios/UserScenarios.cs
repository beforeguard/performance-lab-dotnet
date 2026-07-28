using NBomber.CSharp;
using NBomber.Contracts;
using PerformanceLab.LoadTests.Http;
using PerformanceLab.LoadTests.LoadProfiles;
using PerformanceLab.LoadTests.Metrics;

namespace PerformanceLab.LoadTests.Scenarios;

public static class UsersScenarios
{
    public static ScenarioProps Baseline()
    {
        var client = HttpClientFactory.Create();

        return Scenario.Create("users_baseline", async context =>
        {
            // Use ResponseHeadersRead to avoid buffering - measures TTFB accurately
            var response = await client.GetAsync("http://localhost:5206/users", 
                HttpCompletionOption.ResponseHeadersRead);

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
            
            // Extract response size from header (if present)
            long? responseSizeBytes = null;
            if (response.Headers.TryGetValues("X-Response-Size-Bytes", out var sizeValues))
            {
                if (long.TryParse(sizeValues.FirstOrDefault(), out var size))
                {
                    responseSizeBytes = size;
                }
            }
            
            // Extract compression type
            string compressionType = "none";
            if (response.Content.Headers.ContentEncoding.Any())
            {
                compressionType = string.Join(", ", response.Content.Headers.ContentEncoding);
            }
            
            // Track response size and compression
            if (responseSizeBytes.HasValue)
            {
                ResponseSizeTracker.Record(responseSizeBytes.Value, compressionType);
            }
            
            // Complete reading the response body
            await response.Content.ReadAsByteArrayAsync();

            return ttfbMs.HasValue
                ? Response.Ok(payload: ttfbMs.Value) // Track TTFB as payload for custom metrics
                : Response.Ok();
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
            // Use ResponseHeadersRead to avoid buffering - measures TTFB accurately
            var response = await client.GetAsync("http://localhost:5206/users", 
                HttpCompletionOption.ResponseHeadersRead);

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
            
            // Extract response size from header (if present)
            long? responseSizeBytes = null;
            if (response.Headers.TryGetValues("X-Response-Size-Bytes", out var sizeValues))
            {
                if (long.TryParse(sizeValues.FirstOrDefault(), out var size))
                {
                    responseSizeBytes = size;
                }
            }
            
            // Extract compression type
            string compressionType = "none";
            if (response.Content.Headers.ContentEncoding.Any())
            {
                compressionType = string.Join(", ", response.Content.Headers.ContentEncoding);
            }
            
            // Track response size and compression
            if (responseSizeBytes.HasValue)
            {
                ResponseSizeTracker.Record(responseSizeBytes.Value, compressionType);
            }
            
            // Complete reading the response body
            await response.Content.ReadAsByteArrayAsync();

            return ttfbMs.HasValue
                ? Response.Ok(payload: ttfbMs.Value) // Track TTFB as payload for custom metrics
                : Response.Ok();
        })
        .WithLoadSimulations(
            LoadProfiles.LoadProfiles.CapacityCurve(secondsPerStep: 15)
        );
    }
}
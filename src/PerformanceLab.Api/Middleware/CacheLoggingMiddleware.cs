using System.Diagnostics;

namespace PerformanceLab.Api.Middleware;

public class CacheLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CacheLoggingMiddleware> _logger;

    public CacheLoggingMiddleware(RequestDelegate next, ILogger<CacheLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        
        await _next(context);
        
        sw.Stop();

        // Detect cache hit/miss via Age header
        var isCacheHit = context.Response.Headers.TryGetValue("Age", out var ageValue);
        var cacheStatus = isCacheHit ? "HIT" : "MISS";
        var cacheAge = isCacheHit ? ageValue.ToString() : "N/A";
        
        // Log query string if present
        var queryString = context.Request.QueryString.HasValue 
            ? context.Request.QueryString.Value 
            : "(none)";

        _logger.LogInformation(
            "Request: {Method} {Path} | Query: {Query} | Cache: {Status} | Latency: {Latency}ms | Age: {Age}s",
            context.Request.Method,
            context.Request.Path,
            queryString,
            cacheStatus,
            sw.ElapsedMilliseconds,
            cacheAge);
    }
}
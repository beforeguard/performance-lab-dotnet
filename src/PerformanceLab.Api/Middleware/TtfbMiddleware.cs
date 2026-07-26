using System.Diagnostics;

namespace PerformanceLab.Api.Middleware;

public class TtfbMiddleware
{
    private readonly RequestDelegate _next;

    public TtfbMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        
        context.Response.OnStarting(() =>
        {
            var ttfb = sw.Elapsed;
            context.Response.Headers["X-TTFB-Ms"] = ttfb.TotalMilliseconds.ToString("F2");
            return Task.CompletedTask;
        });
        
        await _next(context);
    }
}

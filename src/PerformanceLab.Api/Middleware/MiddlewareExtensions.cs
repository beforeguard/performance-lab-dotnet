namespace PerformanceLab.Api.Middleware;

public static class MiddlewareExtensions
{
    public static IApplicationBuilder UseCacheLogging(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<CacheLoggingMiddleware>();
    }
}
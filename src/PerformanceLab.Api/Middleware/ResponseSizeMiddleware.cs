namespace PerformanceLab.Api.Middleware;

public class ResponseSizeMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ResponseSizeMiddleware> _logger;

    public ResponseSizeMiddleware(RequestDelegate next, ILogger<ResponseSizeMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Let the response flow through the pipeline normally (doesn't break compression)
        await _next(context);
        
        // Read the Content-Length header set by ASP.NET (wire size after compression)
        if (context.Response.ContentLength.HasValue)
        {
            var responseSize = context.Response.ContentLength.Value;
            
            // Add header for NBomber to track
            context.Response.Headers["X-Response-Size-Bytes"] = responseSize.ToString();
            
            // Check if compression was applied
            var compressionType = context.Response.Headers.ContentEncoding.FirstOrDefault() ?? "none";
            
            // Log response metrics
            _logger.LogInformation(
                "Response: {Method} {Path} | Size: {Size} bytes | Compression: {Compression}",
                context.Request.Method,
                context.Request.Path,
                responseSize,
                compressionType);
        }
    }
}

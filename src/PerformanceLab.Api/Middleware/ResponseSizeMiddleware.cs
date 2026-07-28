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
        // Wrap response stream with counting wrapper (doesn't buffer, just counts)
        var originalBodyStream = context.Response.Body;
        var countingStream = new CountingStream(originalBodyStream);
        context.Response.Body = countingStream;
        
        // Register callback to set header just before response starts
        context.Response.OnStarting(() =>
        {
            // Set header before response is sent
            context.Response.Headers["X-Response-Size-Bytes"] = countingStream.BytesWritten.ToString();
            return Task.CompletedTask;
        });
        
        try
        {
            // Let response flow through the pipeline (compression can work normally)
            await _next(context);
            
            // After response completes, log the metrics
            var responseSize = countingStream.BytesWritten;
            var compressionType = context.Response.Headers.ContentEncoding.FirstOrDefault() ?? "none";
            
            _logger.LogInformation(
                "Response: {Method} {Path} | Size: {Size} bytes | Compression: {Compression}",
                context.Request.Method,
                context.Request.Path,
                responseSize,
                compressionType);
        }
        finally
        {
            // Restore original stream
            context.Response.Body = originalBodyStream;
        }
    }
}

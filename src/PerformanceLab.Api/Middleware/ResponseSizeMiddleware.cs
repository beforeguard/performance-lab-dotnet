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
        // Capture original response body stream
        var originalBodyStream = context.Response.Body;
        
        using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;
        
        await _next(context);
        
        // Measure response size
        var responseSize = responseBody.Length;
        
        // Add headers for observability
        context.Response.Headers["X-Response-Size-Bytes"] = responseSize.ToString();
        
        // Check if compression was applied
        var isCompressed = context.Response.Headers.ContainsKey("Content-Encoding");
        var compressionType = isCompressed 
            ? context.Response.Headers["Content-Encoding"].ToString() 
            : "none";
        
        // Log response size
        _logger.LogInformation(
            "Response: {Method} {Path} | Size: {Size} bytes | Compression: {Compression}",
            context.Request.Method,
            context.Request.Path,
            responseSize,
            compressionType);
        
        // Copy response back to original stream
        responseBody.Seek(0, SeekOrigin.Begin);
        await responseBody.CopyToAsync(originalBodyStream);
    }
}

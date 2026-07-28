using PerformanceLab.Shared.Configuration;
using PerformanceLab.Api.Middleware;
using PerformanceLab.Application.Users;
using PerformanceLab.Application.Users.Abstractions;
using PerformanceLab.Infrastructure.Users;
using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;

var builder = WebApplication.CreateBuilder(args);

// Bind configuration
var perfFeatures = builder.Configuration
    .GetSection("PerformanceFeatures")
    .Get<PerformanceFeatures>() ?? new PerformanceFeatures();

builder.Services.AddControllers();

// Register PerformanceFeatures as singleton for injection
builder.Services.AddSingleton(perfFeatures);

builder.Services.AddScoped<UserService>();
builder.Services.AddSingleton<IUserRepository, UserRepository>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Conditionally add output caching
if (perfFeatures.EnableOutputCaching)
{
    builder.Services.AddOutputCache(options =>
    {
        options.AddPolicy("UsersCachePolicy", builder => 
            builder.Expire(TimeSpan.FromSeconds(perfFeatures.CacheDurationSeconds))
                   .Tag("users")
                   .SetLocking(true)); 
    });
}

// Conditionally add response compression
if (perfFeatures.EnableCompression)
{
    builder.Services.AddResponseCompression(options =>
    {
        options.EnableForHttps = true;  // Enable compression for HTTPS
        
        // Configure providers based on algorithm selection
        switch (perfFeatures.CompressionAlgorithm)
        {
            case CompressionAlgorithm.Gzip:
                options.Providers.Add<GzipCompressionProvider>();
                break;
            case CompressionAlgorithm.Brotli:
                options.Providers.Add<BrotliCompressionProvider>();
                break;
            case CompressionAlgorithm.Both:
                options.Providers.Add<BrotliCompressionProvider>();
                options.Providers.Add<GzipCompressionProvider>();
                break;
        }
        
        // Compress JSON responses
        options.MimeTypes = new[] { "application/json" };
    });
    
    // Configure compression levels
    builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
    {
        options.Level = CompressionLevel.Fastest;  // Balance speed vs ratio
    });
    
    builder.Services.Configure<GzipCompressionProviderOptions>(options =>
    {
        options.Level = CompressionLevel.Fastest;
    });
}

var app = builder.Build();

// TTFB (Time to First Byte) measurement middleware
app.UseTtfb();

// Response size tracking (wraps stream to count bytes - must be BEFORE compression)
// Middleware executes in reverse order for responses, so this wraps the compressed stream
if (perfFeatures.EnableCompression)
{
    app.UseResponseSize();
}

// Response compression (BEFORE caching to cache compressed responses)
if (perfFeatures.EnableCompression)
{
    app.UseResponseCompression();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Conditionally use cache middleware
if (perfFeatures.EnableOutputCaching)
{
    app.UseCacheLogging();
    app.UseOutputCache();
}

app.MapControllers();

// Conditional cache warm-up
if (perfFeatures.EnableOutputCaching)
{
    app.Lifetime.ApplicationStarted.Register(async () =>
    {
        try
        {
            await Task.Delay(500); // Give the server time to fully start
            using var client = new HttpClient { BaseAddress = new Uri("http://localhost:5206") };
            var response = await client.GetAsync("/users");
            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine("✅ Cache warmed up successfully");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Cache warm-up failed: {ex.Message}");
        }
    });
}

app.Run();
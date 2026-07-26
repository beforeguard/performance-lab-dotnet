using PerformanceLab.Api.Configuration;
using PerformanceLab.Api.Middleware;
using PerformanceLab.Application.Users;
using PerformanceLab.Application.Users.Abstractions;
using PerformanceLab.Infrastructure.Users;

var builder = WebApplication.CreateBuilder(args);

// Bind configuration
var perfFeatures = builder.Configuration
    .GetSection("PerformanceFeatures")
    .Get<PerformanceFeatures>() ?? new PerformanceFeatures();

builder.Services.AddControllers();

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

var app = builder.Build();

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
# Experiment 004 – Object Pooling for DTO Allocation Reduction

## Objective

Implement `ArrayPool<UserDto>` to reduce GC pressure from repeated DTO allocations without introducing the cache coordination overhead that degraded tail latency in Experiment 003.

**Primary Goal:** Reduce allocation rate and GC Gen 0 collection frequency by 60-70%  
**Secondary Goal:** Maintain or improve p95/p99 latency compared to Experiment 003 (target: <10ms)

**Context:** Experiment 003 achieved 99% GC reduction through output caching but increased p95 latency by 382% (3.36ms → 16.19ms) due to cache coordination overhead. This experiment explores an alternative approach: reducing allocations at the source through object pooling while avoiding cache synchronization costs.

---

## Hypothesis

Replacing `new UserDto()` allocations with `ArrayPool<UserDto>.Shared` will:
- Reduce allocation rate by -70% (from ~200 MB/s to ~60 MB/s)
- Reduce GC Gen 0 collections by -60%
- Maintain baseline latency characteristics (mean ~3ms, p95 <5ms)
- Avoid tail latency degradation observed with output caching

**Trade-off:** Pool rent/return overhead vs allocation/GC overhead. Hypothesis: rent/return is cheaper than allocation + GC collection for 10,000 DTOs per request.

---

## Environment

| Setting             | Value                |
| ------------------- | -------------------- |
| Build Configuration | Release              |
| Runtime             | .NET 10              |
| Endpoint            | `GET /users`         |
| Data Source         | In-memory repository |
| Dataset Size        | 10,000 users         |
| Caching             | **Disabled** (to isolate pooling impact) |

---

## Implementation Plan

### Phase 0: Configuration Refactoring (Prerequisite)

**Rationale:** Enable easy toggling of performance features (caching, pooling) via configuration instead of code changes. This improves experimental reproducibility and allows environment-specific feature control.

**1. Create Configuration Class** (`PerformanceFeatures.cs`)

New file: `src/PerformanceLab.Api/Configuration/PerformanceFeatures.cs`

```csharp
namespace PerformanceLab.Api.Configuration;

public class PerformanceFeatures
{
    public bool EnableOutputCaching { get; set; }
    public bool EnableObjectPooling { get; set; }
    public int CacheDurationSeconds { get; set; } = 60;
}
```

**2. Add Configuration Section** (`appsettings.json`)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "PerformanceFeatures": {
    "EnableOutputCaching": true,
    "EnableObjectPooling": false,
    "CacheDurationSeconds": 60
  }
}
```

**3. Update Program.cs** (Make caching conditional)

```csharp
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
            await Task.Delay(500);
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
```

**4. Update Controller** (Add feature visibility headers)

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using PerformanceLab.Api.Configuration;
using PerformanceLab.Application.Users;

namespace PerformanceLab.Api.Controllers;

[ApiController]
[Route("users")]
public class UsersController : ControllerBase
{
    private readonly UserService _userService;
    private readonly PerformanceFeatures _perfFeatures;

    public UsersController(
        UserService userService, 
        IConfiguration configuration)
    {
        _userService = userService;
        _perfFeatures = configuration
            .GetSection("PerformanceFeatures")
            .Get<PerformanceFeatures>() ?? new PerformanceFeatures();
    }

    [HttpGet]
    [OutputCache(PolicyName = "UsersCachePolicy")] // Only active when enabled
    public IActionResult GetUsers()
    {
        // Add headers to indicate which features are active
        Response.Headers["X-Caching-Enabled"] = _perfFeatures.EnableOutputCaching.ToString();
        Response.Headers["X-Pooling-Enabled"] = _perfFeatures.EnableObjectPooling.ToString();
        
        return Ok(_userService.GetUsers());
    }
}
```

**Benefits:**
- ✅ Toggle features via JSON configuration (no code changes)
- ✅ Self-documenting via response headers (`X-Caching-Enabled`, `X-Pooling-Enabled`)
- ✅ Environment-specific overrides via `appsettings.Development.json`
- ✅ Single source of truth for experiment configuration

**Verification:**
```powershell
# Start API
dotnet run --project src/PerformanceLab.Api

# Check feature status
curl http://localhost:5206/users -I
# Should see: X-Caching-Enabled: true (initially)
```

---

### Phase 1: Disable Output Caching (Establish Clean Baseline)

**Changes Required:**

**Update Configuration** (`appsettings.json`)
```json
{
  "PerformanceFeatures": {
    "EnableOutputCaching": false,  // ← Changed from true
    "EnableObjectPooling": false,
    "CacheDurationSeconds": 60
  }
}
```

**Verification:** 
```powershell
curl http://localhost:5206/users -I
# Should see: X-Caching-Enabled: false
# Should NOT see: Age header
```

---

### Phase 2: Implement ArrayPool

**1. Create Pooled Collection Wrapper** (`PooledUserDtoCollection.cs`)

New file: `src/PerformanceLab.Application/Users/Models/PooledUserDtoCollection.cs`

```csharp
using System.Buffers;
using System.Collections;

namespace PerformanceLab.Application.Users.Models;

public sealed class PooledUserDtoCollection : IReadOnlyList<UserDto>, IDisposable
{
    private readonly UserDto[] _rentedArray;
    private readonly int _count;
    private bool _disposed;

    public PooledUserDtoCollection(UserDto[] rentedArray, int count)
    {
        _rentedArray = rentedArray;
        _count = count;
    }

    public int Count => _count;

    public UserDto this[int index]
    {
        get
        {
            if (index < 0 || index >= _count)
                throw new IndexOutOfRangeException();
            return _rentedArray[index];
        }
    }

    public IEnumerator<UserDto> GetEnumerator()
    {
        for (int i = 0; i < _count; i++)
        {
            yield return _rentedArray[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void Dispose()
    {
        if (_disposed) return;
        
        // Clear DTO properties to avoid stale data in pool
        for (int i = 0; i < _count; i++)
        {
            _rentedArray[i].Id = 0;
            _rentedArray[i].Name = string.Empty;
        }
        
        ArrayPool<UserDto>.Shared.Return(_rentedArray);
        _disposed = true;
    }
}
```

**2. Modify UserService** (`UserService.cs`)

```csharp
using System.Buffers;
using PerformanceLab.Application.Users.Abstractions;
using PerformanceLab.Application.Users.Models;

namespace PerformanceLab.Application.Users;

public class UserService
{
    private readonly IUserRepository _repo;

    public UserService(IUserRepository repo)
    {
        _repo = repo;
    }

    public PooledUserDtoCollection GetUsers()
    {
        var users = _repo.GetAll();
        var count = users.Count;
        
        // Rent array from pool
        var dtoArray = ArrayPool<UserDto>.Shared.Rent(count);
        
        // Populate DTOs
        for (int i = 0; i < count; i++)
        {
            var user = users[i];
            dtoArray[i] = new UserDto
            {
                Id = user.Id,
                Name = user.Name
            };
        }
        
        // Wrap in disposable collection
        return new PooledUserDtoCollection(dtoArray, count);
    }
}
```

**3. Update Controller for Disposal** (`UsersController.cs`)

```csharp
[HttpGet]
public IActionResult GetUsers()
{
    using var users = _userService.GetUsers();
    return Ok(users);
}
```

**Key Design Decisions:**
- **ArrayPool over ObjectPool**: Automatic size management, zero configuration, thread-safe shared instance
- **IDisposable pattern**: ASP.NET Core automatically disposes after response completion, ensuring pool return
- **Property clearing**: Prevents stale data leaks when arrays are reused
- **Fixed size rent**: User count is constant (10,000), so we rent exactly that amount
- **Wrapper class**: Provides `IReadOnlyList<T>` interface for JSON serialization compatibility

---

### Phase 3: Add Observability (Optional)

**Pool Metrics Middleware** (`PoolMetricsMiddleware.cs`)

New file: `src/PerformanceLab.Api/Middleware/PoolMetricsMiddleware.cs`

```csharp
namespace PerformanceLab.Api.Middleware;

public class PoolMetricsMiddleware
{
    private static long _rentCount;
    private static long _returnCount;
    
    private readonly RequestDelegate _next;
    private readonly ILogger<PoolMetricsMiddleware> _logger;

    public PoolMetricsMiddleware(RequestDelegate next, ILogger<PoolMetricsMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        Interlocked.Increment(ref _rentCount);
        
        try
        {
            await _next(context);
        }
        finally
        {
            Interlocked.Increment(ref _returnCount);
            
            if (_rentCount % 1000 == 0)
            {
                _logger.LogInformation(
                    "Pool Metrics - Rents: {Rents}, Returns: {Returns}, Outstanding: {Outstanding}",
                    _rentCount, _returnCount, _rentCount - _returnCount);
            }
        }
    }
}

public static class PoolMetricsMiddlewareExtensions
{
    public static IApplicationBuilder UsePoolMetrics(this IApplicationBuilder app)
    {
        return app.UseMiddleware<PoolMetricsMiddleware>();
    }
}
```

Register in `Program.cs`:
```csharp
app.UsePoolMetrics(); // Add before MapControllers()
```

---

## Load Test Configuration

**Tool:** NBomber

### Scenarios

**Scenario 1: users_baseline**
- Duration: 60 seconds
- Injection Rate: 50 requests/second
- Total Requests: 3,000
- Purpose: Compare against Experiment 001 baseline

**Scenario 2: users_capacity_curve**
- Load Steps: 10 → 25 → 50 → 100 → 200 RPS
- Step Duration: 15 seconds each
- Total Duration: 75 seconds
- Total Requests: ~5,775
- Purpose: Verify pooling handles concurrent access without contention

---

## Execution Steps

### 1. Prepare Environment

```powershell
# Build in Release mode
dotnet build -c Release

# Verify build succeeded
$LASTEXITCODE -eq 0
```

### 2. Run Baseline Test (Pre-Pooling)

```powershell
# Terminal 1: Start API without pooling
dotnet run --project src/PerformanceLab.Api -c Release

# Terminal 2: Verify no caching
curl http://localhost:5206/users -I
# Should NOT see "Age" header

# Terminal 3: Run load test
dotnet run --project tools/PerformanceLab.LoadTests -c Release
```

### 3. Implement Pooling Changes

Apply all changes from Phase 2 implementation plan.

### 4. Run Pooling Test

```powershell
# Rebuild with pooling enabled
dotnet build -c Release

# Terminal 1: Start API
dotnet run --project src/PerformanceLab.Api -c Release

# Terminal 2: Capture GC metrics
dotnet-counters monitor --process-id <PID> System.Runtime

# Terminal 3: Run load test
dotnet run --project tools/PerformanceLab.LoadTests -c Release
```

### 5. Capture Diagnostics

```powershell
# Collect trace for 60 seconds during load test
dotnet-trace collect --process-id <PID> --duration 00:01:00 --providers System.Runtime
```

---

## Expected Results

### Allocation & GC Metrics

| Metric | Baseline (Exp 001) | Pooling (Expected) | Change |
|--------|-------------------:|-------------------:|-------:|
| Allocation Rate | 200+ MB/s | ~60 MB/s | -70% |
| Gen 0 Collections | 100+ | ~40 | -60% |
| Gen 1 Collections | ~10 | ~5 | -50% |
| Working Set | ~150 MB | ~180 MB | +20% (pool overhead) |

### Latency Distribution

| Metric | Baseline (Exp 001) | Pooling (Expected) | Change |
|--------|-------------------:|-------------------:|-------:|
| Mean Latency | 2.88 ms | ~2.5 ms | -13% |
| p50 Latency | 2.26 ms | ~2.0 ms | -11% |
| p95 Latency | 3.36 ms | ~3.5 ms | +4% (acceptable) |
| p99 Latency | 7.42 ms | ~6.0 ms | -19% |

**Expected Outcome:** Pooling reduces GC pauses without introducing cache coordination overhead, resulting in consistent low tail latency.

### Pool Efficiency

| Metric | Target |
|--------|-------:|
| Pool Rent Count | 3,000 (baseline) / 5,775 (curve) |
| Pool Return Count | 3,000 / 5,775 (should match rents) |
| Outstanding Leaks | 0 |
| Pool Hits (reuse) | >95% after warm-up |

---

## Measurements Checklist

### Pre-Implementation
- [ ] Disable output caching
- [ ] Run baseline test without caching
- [ ] Capture baseline allocation rate
- [ ] Capture baseline GC collection count

### Post-Implementation
- [ ] Verify build succeeds with pooling code
- [ ] Confirm no cache headers in response
- [ ] Run pooling test scenarios
- [ ] Capture allocation rate with pooling
- [ ] Capture GC collection count with pooling
- [ ] Verify pool rent count equals return count
- [ ] Check for memory leaks (outstanding pool rentals)
- [ ] Compare latency distributions

### Diagnostics
- [ ] `dotnet-counters` GC metrics during load test
- [ ] `dotnet-trace` allocation profile analysis
- [ ] NBomber latency percentile reports
- [ ] Pool metrics middleware logs

---

## Success Criteria

1. ✅ **Allocation Reduction:** -60% or better allocation rate reduction
2. ✅ **GC Reduction:** -50% or better Gen 0 collection reduction
3. ✅ **Latency Maintained:** p95 <5ms, p99 <10ms (better than Exp 003)
4. ✅ **No Leaks:** Pool rent count exactly matches return count
5. ✅ **100% Success Rate:** All requests succeed under load
6. ✅ **Scalability:** Handles 200 RPS without pool contention

---

## Comparison: Pooling vs Caching

| Aspect | Output Caching (Exp 003) | Object Pooling (Exp 004) |
|--------|--------------------------|--------------------------|
| **Allocation Reduction** | ~99% (cache hits) | ~70% (pool reuse) |
| **GC Reduction** | 99% | 60% (estimated) |
| **Mean Latency** | +29% (worse) | -13% (expected) |
| **p95 Latency** | +382% (worse) | ±5% (neutral) |
| **Approach** | Avoid work entirely | Reduce allocation overhead |
| **Trade-off** | Cache coordination cost | Pool rent/return overhead |
| **Best For** | Identical repeated requests | Variable requests, low tail latency SLAs |

**Hypothesis:** Pooling provides a "middle ground" – significant GC benefits without the extreme tail latency degradation of caching.

---

## Next Steps

### Experiment 004b: Combined Optimization (Future)
If pooling succeeds, test combining `ArrayPool` + `OutputCache` to get benefits of both:
- Cache handles identical requests (zero allocation)
- Pool handles cache misses (reduced allocation)
- Expected: Best of both worlds with minimal trade-offs

### Experiment 005: Streaming DTOs
If pooling overhead is still significant, explore streaming DTOs with `IAsyncEnumerable<UserDto>` to avoid materializing entire collection.

---

## Status

🔲 **Not Started** – Ready for implementation

**Date Planned:** 2026-07-19  
**Depends On:** Experiment 003 analysis complete  
**Blocks:** Experiment 004b (combined pooling + caching)

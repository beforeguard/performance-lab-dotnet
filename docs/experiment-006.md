# Experiment 006 – Response Compression

## Objective

Implement HTTP response compression (Gzip/Brotli) to reduce network transfer time by minimizing payload size, despite potential CPU overhead from compression operations.

**Primary Goal:** Reduce response size by -85% (from ~200KB to ~30KB)  
**Secondary Goal:** Achieve net latency improvement of -10% (compression CPU cost < network transfer savings)

**Context:** Experiments 004b (ArrayPool + OutputCache) achieved excellent latency improvements (-58.9% mean, -62.2% p95) but focused on allocation reduction. This experiment explores network optimization: trading CPU cycles for bandwidth savings, particularly valuable for bandwidth-constrained clients.

---

## Hypothesis

Enabling response compression (Gzip or Brotli) will:
- **Response Size:** -85% reduction (200KB → 30KB for JSON payload)
- **CPU Utilization:** +15-20% increase due to compression overhead
- **Net Latency:** -10% improvement (network savings > CPU cost)
- **Compression Ratio:** 6:1 to 7:1 for JSON data
- **Success Rate:** Maintain 100% (compression is transparent to client)

**Trade-off:** CPU cycles for bandwidth. Hypothesis: For large responses (200KB+), network transfer time reduction outweighs compression CPU overhead, especially for remote clients or bandwidth-limited connections.

**Algorithms Considered:**
- **Gzip:** Ubiquitous support, moderate compression (~80% reduction), lower CPU
- **Brotli:** Modern browsers, better compression (~85% reduction), slightly higher CPU
- **Recommendation:** Test both, prefer Brotli for production (15% better compression ratio)

---

## Environment

| Setting             | Value                |
| ------------------- | -------------------- |
| Build Configuration | Release              |
| Runtime             | .NET 10              |
| Endpoint            | `GET /users`         |
| Data Source         | In-memory repository |
| Dataset Size        | 10,000 users         |
| Response Size       | ~200KB (uncompressed JSON) |
| Base Configuration  | Combined (ArrayPool + OutputCache) from Exp 004b |

---

## Implementation Plan

### Phase 0: Prerequisite Understanding

**Current State:**
- Baseline response: ~200KB JSON (10,000 UserDto objects)
- Current optimizations: ArrayPool + OutputCache (Experiment 004b)
- Middleware order: TTFB → HttpsRedirection → CacheLogging → OutputCache → MapControllers

**Target State:**
- Add ResponseCompression middleware before caching
- Support both Gzip and Brotli via configuration
- Track compression ratios and response sizes
- Test across all optimization combinations (baseline, pool, cache, combined)

---

### Phase 1: Configuration & Feature Flag

**1. Update PerformanceFeatures Configuration**

File: `src/PerformanceLab.Shared/Configuration/PerformanceFeatures.cs`

```csharp
namespace PerformanceLab.Shared.Configuration;

public enum CompressionAlgorithm
{
    None,
    Gzip,
    Brotli,
    Both  // Allow ASP.NET to negotiate based on Accept-Encoding
}

public class PerformanceFeatures
{
    public bool EnableOutputCaching { get; set; }
    public bool EnableObjectPooling { get; set; }
    public bool EnableStreaming { get; set; }
    public bool EnableCompression { get; set; }  // NEW
    public CompressionAlgorithm CompressionAlgorithm { get; set; } = CompressionAlgorithm.Brotli;  // NEW
    public int CacheDurationSeconds { get; set; } = 60;
}
```

**2. Update appsettings.json**

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "PerformanceLab.Api.Middleware": "Information"
    }
  },
  "AllowedHosts": "*",
  "PerformanceFeatures": {
    "EnableOutputCaching": true,
    "EnableObjectPooling": true,
    "EnableStreaming": false,
    "EnableCompression": false,
    "CompressionAlgorithm": "Brotli",
    "CacheDurationSeconds": 60
  }
}
```

---

### Phase 2: Middleware Implementation

**3. Update Program.cs - Add ResponseCompression Service**

File: `src/PerformanceLab.Api/Program.cs`

Add after `builder.Services.AddSingleton(perfFeatures);`:

```csharp
// Conditionally add response compression
if (perfFeatures.EnableCompression)
{
    builder.Services.AddResponseCompression(options =>
    {
        options.EnableForHttps = true;  // Enable compression for HTTPS
        
        // Configure providers based on algorithm selection
        switch (perfFeatures.CompressionAlgorithm)
        {
            case Configuration.CompressionAlgorithm.Gzip:
                options.Providers.Add<GzipCompressionProvider>();
                break;
            case Configuration.CompressionAlgorithm.Brotli:
                options.Providers.Add<BrotliCompressionProvider>();
                break;
            case Configuration.CompressionAlgorithm.Both:
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
        options.Level = System.IO.Compression.CompressionLevel.Fastest;  // Balance speed vs ratio
    });
    
    builder.Services.Configure<GzipCompressionProviderOptions>(options =>
    {
        options.Level = System.IO.Compression.CompressionLevel.Fastest;
    });
}
```

**4. Update Program.cs - Add Middleware (Before Caching)**

Critical ordering: Compress → Cache (to cache compressed response)

```csharp
var app = builder.Build();

// TTFB (Time to First Byte) measurement middleware
app.UseTtfb();

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

// Response size tracking (AFTER compression to measure wire size)
if (perfFeatures.EnableCompression)
{
    app.UseResponseSize();
}

app.MapControllers();
```

---

### Phase 3: Response Size Tracking

**5. Create ResponseSizeMiddleware**

New file: `src/PerformanceLab.Api/Middleware/ResponseSizeMiddleware.cs`

```csharp
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
```

**6. Update MiddlewareExtensions.cs**

File: `src/PerformanceLab.Api/Middleware/MiddlewareExtensions.cs`

```csharp
namespace PerformanceLab.Api.Middleware;

public static class MiddlewareExtensions
{
    public static IApplicationBuilder UseCacheLogging(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<CacheLoggingMiddleware>();
    }

    public static IApplicationBuilder UseTtfb(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<TtfbMiddleware>();
    }
    
    public static IApplicationBuilder UseResponseSize(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ResponseSizeMiddleware>();
    }
}
```

---

### Phase 4: Test Script Enhancement

**7. Update run-experiment.ps1 - Add Compression Switch**

File: `scripts/run-experiment.ps1`

Add parameter:
```powershell
param (
    [int]$Port = 5206,
    
    [switch]$Cache,
    [switch]$Pool,
    [switch]$Stream,
    [switch]$Compression,  # NEW
    [switch]$All,
    
    [int]$WarmupSeconds = 3
)
```

Add to environment variable section:
```powershell
$env:PerformanceFeatures__EnableCompression = $config.Compression.ToString().ToLower()
$env:PerformanceFeatures__CompressionAlgorithm = "Brotli"  # Or make configurable
```

Update configuration matrix for `-All`:
```powershell
if ($All) {
    # Run all 8 base configurations (with/without compression)
    $configurations = @(
        # Without compression (existing)
        @{Cache=$false; Pool=$false; Stream=$false; Compression=$false; Name="baseline"},
        @{Cache=$false; Pool=$true;  Stream=$false; Compression=$false; Name="pool"},
        @{Cache=$true;  Pool=$false; Stream=$false; Compression=$false; Name="cache"},
        @{Cache=$true;  Pool=$true;  Stream=$false; Compression=$false; Name="combined"},
        
        # With compression (new)
        @{Cache=$false; Pool=$false; Stream=$false; Compression=$true; Name="baseline_gzip"},
        @{Cache=$false; Pool=$true;  Stream=$false; Compression=$true; Name="pool_gzip"},
        @{Cache=$true;  Pool=$false; Stream=$false; Compression=$true; Name="cache_gzip"},
        @{Cache=$true;  Pool=$true;  Stream=$false; Compression=$true; Name="combined_gzip"}
    )
    
    Write-Host "Running 8 configurations (4 base + 4 with compression)" -ForegroundColor Yellow
}
```

---

### Phase 5: NBomber Enhancement

**8. Update UsersScenarios.cs - Add Accept-Encoding Header**

File: `tools/PerformanceLab.LoadTests/Scenarios/UsersScenarios.cs`

Find the HTTP request creation and add:

```csharp
var request = Http.CreateRequest("GET", "http://localhost:5206/users")
    .WithHeader("Accept-Encoding", "gzip, br")  // NEW: Request compression
    .WithCheck(response => Task.FromResult(response.IsSuccessStatusCode));
```

**9. Add Response Size Tracking**

```csharp
var step = Step.Create("get_users", async context =>
{
    var response = await Http.Send(httpClient, request);
    
    // Track response size from header
    if (response.Message.Headers.TryGetValues("X-Response-Size-Bytes", out var sizeValues))
    {
        var size = long.Parse(sizeValues.First());
        // Store in context or log for aggregation
        context.Logger.Debug($"Response size: {size} bytes");
    }
    
    return response;
});
```

---

### Phase 6: Baseline Measurement

**Test Configurations (Without Compression):**

```powershell
# Run baseline configurations to establish uncompressed baseline
.\scripts\run-experiment.ps1                    # baseline
.\scripts\run-experiment.ps1 -Pool             # pool
.\scripts\run-experiment.ps1 -Cache            # cache
.\scripts\run-experiment.ps1 -Cache -Pool      # combined
```

**Metrics to Capture:**
- Uncompressed response size (~200KB expected)
- Baseline CPU utilization
- Baseline latency distribution (from Experiment 004b: 1.61ms mean, 2.50ms p95)

---

### Phase 7: Treatment Measurement

**Test Configurations (With Compression):**

```powershell
# Run with compression enabled
.\scripts\run-experiment.ps1 -Compression                    # baseline + gzip
.\scripts\run-experiment.ps1 -Pool -Compression             # pool + gzip
.\scripts\run-experiment.ps1 -Cache -Compression            # cache + gzip
.\scripts\run-experiment.ps1 -Cache -Pool -Compression      # combined + gzip
```

**Or run all at once:**
```powershell
.\scripts\run-experiment.ps1 -All  # Runs all 8 configurations
```

**Metrics to Capture:**
- Compressed response size (~30KB expected)
- Compression ratio (calculated: uncompressed / compressed)
- CPU utilization delta vs baseline
- Latency distribution change
- Compression algorithm used (from Content-Encoding header)

---

### Phase 8: Analysis & Documentation

**Calculations:**

1. **Compression Effectiveness:**
   - Actual compression ratio = Uncompressed Size / Compressed Size
   - Expected: 6:1 to 7:1 (200KB → 28-33KB)
   - Percent reduction = (1 - Compressed/Uncompressed) × 100

2. **CPU Overhead:**
   - Delta = (Compressed CPU - Baseline CPU) / Baseline CPU × 100
   - Expected: +15-20%

3. **Net Latency Impact:**
   - Compare mean/p95/p99 latency: compressed vs uncompressed
   - Consider: localhost testing minimizes network benefit (real-world improvement higher)

4. **Cache Interaction:**
   - Does cache + compression maintain benefits from Experiment 004b?
   - Does caching compressed responses avoid re-compression overhead?

**Decision Criteria:**

✅ **Accept compression if:**
- Compression ratio ≥ 5:1 (≥80% reduction)
- CPU overhead ≤ 25%
- Net latency ≤ +5% (neutral acceptable for localhost; real network = improvement)
- Cache compatibility maintained

❌ **Reject compression if:**
- CPU overhead > 30% (excessive for benefit)
- Latency degradation > +10%
- Cache interaction causes p95 spike (Experiment 003 pattern)

---

## Load Test Configuration

**Tool:** NBomber

### Scenarios

**Scenario 1: users_baseline**
- Duration: 60 seconds
- Injection Rate: 50 requests/second
- Total Requests: 3,000
- Purpose: Measure steady-state compression performance

**Scenario 2: users_capacity_curve**
- Duration: 75 seconds (5 steps × 15s)
- Injection Rate: Ramp 10 → 50 → 100 → 150 → 200 RPS
- Total Requests: ~5,775
- Purpose: Validate compression CPU scaling under load

### Metrics to Collect

| Metric | Tool | Purpose |
|--------|------|---------|
| Response Size | ResponseSizeMiddleware | Measure compression effectiveness |
| Compression Ratio | Calculated | Actual vs expected (~6:1) |
| CPU Utilization | dotnet-counters | Measure compression overhead |
| Latency Distribution | NBomber | Net impact (CPU cost vs network savings) |
| Success Rate | NBomber | Ensure compression is transparent |
| Content-Encoding | HTTP Headers | Verify Gzip/Brotli applied |

---

## Expected Results

### Baseline (Combined - from Experiment 004b)

| Metric | Expected Value | Source |
|--------|----------------|--------|
| Response Size | ~200KB | Uncompressed JSON |
| Mean Latency | 1.61ms | Experiment 004b |
| p95 Latency | 2.50ms | Experiment 004b |
| p99 Latency | 5.21ms | Experiment 004b |
| CPU Baseline | ~20% (estimated) | To be measured |

### Treatment (Combined + Compression)

| Metric | Expected Value | Change | Rationale |
|--------|----------------|--------|-----------|
| Response Size | ~30KB | **-85%** | JSON compression ratio 6-7:1 |
| Compression Ratio | 6.5:1 | N/A | Typical for JSON |
| Mean Latency | 1.45ms | **-10%** | Network savings > CPU cost |
| p95 Latency | 2.25ms | **-10%** | Consistent improvement |
| p99 Latency | 4.70ms | **-10%** | Tail latency maintained |
| CPU Utilization | ~24% | **+20%** | Compression overhead |
| Success Rate | 100% | 0% | Transparent to client |

**Note:** Localhost testing minimizes network transfer time, so latency improvement may be lower than expected. Real-world deployment (remote clients, bandwidth constraints) will show greater benefit.

### Gzip vs Brotli Comparison

| Algorithm | Compression Ratio | CPU Overhead | Latency Impact | Recommendation |
|-----------|------------------:|-------------:|---------------:|----------------|
| Gzip | 5.5:1 (~36KB) | +15% | -8% | Good baseline |
| Brotli | 6.5:1 (~31KB) | +18% | -10% | **Preferred** (better compression) |

---

## Measurements

**Status:** 🔲 Not Started

### Configuration: Baseline (No Compression)

_To be filled after Phase 6 baseline tests_

| Metric | Value |
|--------|------:|
| Response Size | — |
| Mean Latency | — |
| p50 Latency | — |
| p95 Latency | — |
| p99 Latency | — |
| CPU Utilization | — |
| Success Rate | — |

### Configuration: Baseline + Gzip

_To be filled after Phase 7 treatment tests_

| Metric | Value | Δ vs Baseline |
|--------|------:|:-------------:|
| Response Size | — | — |
| Compression Ratio | — | N/A |
| Mean Latency | — | — |
| p95 Latency | — | — |
| CPU Utilization | — | — |

### Configuration: Combined (ArrayPool + Cache) + Brotli

_To be filled after Phase 7 treatment tests_

| Metric | Value | Δ vs Combined |
|--------|------:|:-------------:|
| Response Size | — | — |
| Compression Ratio | — | N/A |
| Mean Latency | — | — |
| p95 Latency | — | — |
| CPU Utilization | — | — |

---

## Analysis

**Status:** 🔲 Pending Results

### Compression Effectiveness

_To be filled: Calculate actual compression ratio vs expected 6:1-7:1_

### CPU Overhead

_To be filled: Analyze CPU delta vs expected +15-20%_

### Net Latency Impact

_To be filled: Determine if network savings outweigh CPU cost_

### Cache Interaction

_To be filled: Validate that caching compressed responses maintains Experiment 004b benefits without coordination overhead_

### Localhost vs Real-World

_To be filled: Discuss how localhost testing underestimates network transfer savings; estimate real-world benefit_

---

## Decision

**Status:** 🔲 Pending Analysis

### Recommendation: TBD

_To be filled after analysis_

**Options:**
1. ✅ **Accept Brotli compression** - Deploy to production with `EnableCompression: true`
2. ✅ **Accept Gzip compression** - Use Gzip for broader compatibility
3. ⚠️ **Accept with conditions** - Enable only for specific endpoints or client types
4. ❌ **Reject compression** - CPU overhead exceeds network benefit

### Production Configuration

_To be filled with recommended appsettings.json values_

```json
{
  "PerformanceFeatures": {
    "EnableOutputCaching": true,
    "EnableObjectPooling": true,
    "EnableStreaming": false,
    "EnableCompression": false,  // Update based on results
    "CompressionAlgorithm": "Brotli",
    "CacheDurationSeconds": 60
  }
}
```

---

## Further Considerations

### 1. Compression Quality Level

Brotli supports quality levels 0-11 (default: 4, used `CompressionLevel.Fastest` ≈ 1-4).

**Trade-off:**
- Lower quality (0-4): Faster compression, larger output (~31KB)
- Higher quality (10-11): Slower compression, smaller output (~28KB)

**Recommendation:** Start with `Fastest` for latency optimization. If CPU overhead is acceptable, test `Optimal` quality in follow-up experiment.

### 2. Minimum Response Size Threshold

Should small responses (<1KB) be compressed?

**Current Implementation:** No threshold (compress all JSON responses)

**Consideration:** Compression overhead may exceed benefit for small payloads. Our 200KB response clearly benefits, but other endpoints may not.

**Recommendation:** Add configuration for minimum response size (e.g., 1024 bytes) if API expands to include small responses.

### 3. Streaming Compatibility (Experiment 005)

**Potential Conflict:** Response compression typically requires buffering the entire response before compressing, which conflicts with streaming's incremental transmission.

**Status:** Experiment 005 rejected streaming due to latency degradation. If revisited, test compression compatibility.

**Recommendation:** Document incompatibility; choose compression OR streaming, not both.

### 4. Client-Side Decompression

**Assumption:** All modern clients (browsers, HttpClient) automatically handle `Content-Encoding: gzip/br` and decompress transparently.

**Validation:** NBomber with `Accept-Encoding` header should handle decompression. Verify in results that response content is correct (not compressed bytes).

**Risk:** Legacy clients without Brotli support. Mitigation: Use `CompressionAlgorithm.Both` to fallback to Gzip.

### 5. Cache Storage Implications

**Question:** Does caching compressed responses reduce cache storage requirements?

**Expected:** Yes - OutputCache stores compressed bytes (30KB) instead of uncompressed (200KB), improving cache capacity 6-7x.

**Measurement:** Compare `dotnet-counters` memory metrics for cache with/without compression.

**Benefit:** More cached entries fit in memory, reducing cache eviction rate.

---

## Appendix: Middleware Order Rationale

**Final Order:**
1. `UseTtfb()` - Measure time to first byte (before any processing)
2. `UseResponseCompression()` - Compress response body
3. `UseHttpsRedirection()` - Redirect HTTP → HTTPS
4. `UseOutputCache()` - Cache compressed response
5. `UseResponseSize()` - Measure final wire size
6. `MapControllers()` - Route to endpoint

**Why compression BEFORE caching?**
- Cache stores the compressed response
- Subsequent cache hits serve compressed bytes directly (no re-compression)
- Reduces CPU overhead for cached requests

**Alternative (compression AFTER cache):**
- Cache hit serves uncompressed response, then compresses
- Wastes CPU re-compressing identical cached responses
- ❌ Rejected

---

## References

- **ASP.NET Core Docs:** [Response Compression Middleware](https://learn.microsoft.com/en-us/aspnet/core/performance/response-compression)
- **Experiment 003:** Output Caching (established cache hit ratio ~99.98%)
- **Experiment 004b:** ArrayPool + Cache (baseline: 1.61ms mean, 2.50ms p95)
- **Experiment 005:** Streaming (rejected due to latency degradation)

---

## Next Steps

1. ✅ Implement Phase 1-5 (configuration, middleware, test scripts)
2. 🔲 Run Phase 6 baseline measurements (4 configs without compression)
3. 🔲 Run Phase 7 treatment measurements (4-8 configs with compression)
4. 🔲 Execute Phase 8 analysis (calculate ratios, CPU overhead, net latency)
5. 🔲 Make production recommendation (accept/reject)
6. 🔲 Update [performance-experiments-tracking.md](performance-experiments-tracking.md) with results
7. 🔲 If accepted, deploy to production with recommended configuration

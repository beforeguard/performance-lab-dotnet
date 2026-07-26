# Experiment 005 – Response Streaming (IAsyncEnumerable)

## Objective

Implement streaming serialization using `IEnumerable<UserDto>` to reduce time-to-first-byte (TTFB) and peak memory footprint compared to the current `.ToList()` materialization approach.

**Primary Goal:** Reduce time-to-first-byte by -40% and peak memory footprint by -30%  
**Secondary Goal:** Maintain or improve total request latency

**Context:** Experiments 004b achieved excellent overall latency (1.61ms mean, 2.50ms p95) through combined ArrayPool + OutputCache optimization. However, the endpoint still materializes the entire 10,000-item collection before serialization begins. This experiment explores whether lazy enumeration can improve initial response time and reduce memory pressure, especially beneficial for large result sets or when caching is not feasible.

---

## Hypothesis

Removing `.ToList()` materialization and returning `IEnumerable<UserDto>` will allow ASP.NET Core's JSON serializer to stream DTOs incrementally:

- **Time to First Byte:** -40% (serializer begins writing before full collection is materialized)
- **Peak Memory Footprint:** -30% (no need to allocate and hold full List<T> in memory)
- **Total Request Time:** Neutral to +5% (streaming overhead may slightly increase total time)
- **Allocation Rate:** Neutral (ArrayPool already handles DTO allocations)

**Trade-off:** Streaming introduces serializer overhead vs materialized list. Hypothesis: TTFB improvement outweighs potential total latency increase, especially valuable for real-world scenarios with network latency.

---

## Environment

| Setting             | Value                |
| ------------------- | -------------------- |
| Build Configuration | Release              |
| Runtime             | .NET 10              |
| Endpoint            | `GET /users`         |
| Data Source         | In-memory repository |
| Dataset Size        | 10,000 users         |
| Baseline Config     | Combined (ArrayPool + Cache) from Experiment 004b |
| Feature Flags       | `EnableStreaming` toggle added for experiment control |

---

## Implementation Plan

### Phase 1: Baseline Measurement

**Configuration:**
- `EnableOutputCaching: false`
- `EnableObjectPooling: true`
- `EnableStreaming: false` (baseline - materialized lists)

**Steps:**
1. Run `.\scripts\run-experiment.ps1 -Pool` to establish ArrayPool-only baseline
2. Capture TTFB metrics (requires instrumentation - see Phase 2)
3. Record peak memory allocation from dotnet-counters

**Expected Baseline (from Experiment 004):**
- Mean latency: ~2.79ms
- p95 latency: ~5.01ms
- Peak memory: TBD (not previously measured)

---

### Phase 2: Add TTFB Instrumentation

**Rationale:** Current test harness measures only total request latency. TTFB requires measuring time until first response byte arrives.

**Implementation Options:**

**Option A: NBomber ClientFactory with HttpCompletionOption**
```csharp
var httpClient = new HttpClient();
var step = Step.Create("users_streaming", async context =>
{
    var sw = Stopwatch.StartNew();
    var response = await httpClient.GetAsync(
        "http://localhost:5206/users",
        HttpCompletionOption.ResponseHeadersRead  // Don't buffer response
    );
    var ttfb = sw.Elapsed;
    
    await response.Content.ReadAsStringAsync();  // Total time
    var totalTime = sw.Elapsed;
    
    // Log both metrics for analysis
    return Response.Ok(statusCode: (int)response.StatusCode);
});
```

**Option B: Custom Middleware**
```csharp
app.Use(async (context, next) =>
{
    var sw = Stopwatch.StartNew();
    context.Response.OnStarting(() =>
    {
        var ttfb = sw.Elapsed;
        context.Response.Headers["X-TTFB-Ms"] = ttfb.TotalMilliseconds.ToString("F2");
        return Task.CompletedTask;
    });
    await next();
});
```

**Recommendation:** Use Option B (middleware) - easier to implement, no NBomber changes needed, TTFB visible in response headers.

---

### Phase 3: Implement Streaming

**0. Add EnableStreaming to Configuration**

Update `src/PerformanceLab.Api/Configuration/PerformanceFeatures.cs`:
```csharp
public class PerformanceFeatures
{
    public bool EnableOutputCaching { get; set; }
    public bool EnableObjectPooling { get; set; }
    public bool EnableStreaming { get; set; }  // ← Add this
    public int CacheDurationSeconds { get; set; } = 60;
}
```

Update `appsettings.json`:
```json
{
  "PerformanceFeatures": {
    "EnableOutputCaching": true,
    "EnableObjectPooling": false,
    "EnableStreaming": false,
    "CacheDurationSeconds": 60
  }
}
```

**1. Update UserService.GetUsers()** (`src/PerformanceLab.Application/Users/UserService.cs`)

**Before:**
```csharp
public List<UserDto> GetUsers()
{
    if (!_perfFeatures.EnableObjectPooling)
    {
        return _repository.GetAll()
            .Select(u => new UserDto { ... })
            .ToList();  // ❌ Materializes entire collection
    }
    
    // ArrayPool implementation...
}
```

**After:**
```csharp
public IEnumerable<UserDto> GetUsers()
{
    IEnumerable<UserDto> users;
    
    if (!_perfFeatures.EnableObjectPooling)
    {
        // LINQ approach (baseline)
        users = _repository.GetAll()
            .Select(u => new UserDto { ... });
    }
    else
    {
        // ArrayPool: PooledUserDtoCollection implements IEnumerable
        users = GetUsersWithPooling();
    }
    
    // Conditionally materialize based on EnableStreaming flag
    return _perfFeatures.EnableStreaming 
        ? users                    // ✅ Stream as IEnumerable
        : users.ToList();          // ❌ Materialize for baseline comparison
}
```

**Key Design Decision:**
- Always return `IEnumerable<UserDto>` (simpler than conditional return types)
- Use `.ToList()` when streaming disabled to match original baseline behavior
- This allows clean A/B testing: same code path, different materialization strategy

**2. Update Controller Return Type** (`src/PerformanceLab.Api/Controllers/UsersController.cs`)

**Before:**
```csharp
[HttpGet]
[OutputCache(PolicyName = "UsersCachePolicy")]
public IActionResult GetUsers()
{
    var users = _userService.GetUsers();
    return Ok(users);  // users is List<UserDto>
}
```

**After:**
```csharp
[HttpGet]
[OutputCache(PolicyName = "UsersCachePolicy")]
public IActionResult GetUsers()
{
    var users = _userService.GetUsers();  // Now IEnumerable<UserDto>
    return Ok(users);  // ASP.NET Core serializer will stream
}
```

**Note:** No other changes needed - `System.Text.Json` automatically supports streaming `IEnumerable<T>`.

---

### Phase 4: Verification Test

**Test Streaming Behavior:**
1. Add debug logging in UserService to verify flag behavior:
```csharp
public IEnumerable<UserDto> GetUsers()
{
    Console.WriteLine($"GetUsers called - EnableStreaming: {_perfFeatures.EnableStreaming}");
    
    IEnumerable<UserDto> users = _perfFeatures.EnableObjectPooling 
        ? GetUsersWithPooling() 
        : GetUsersWithLinq();
    
    if (_perfFeatures.EnableStreaming)
    {
        Console.WriteLine("Returning lazy IEnumerable (streaming)");
        return users;  // Should see incremental enumeration in serializer
    }
    else
    {
        Console.WriteLine("Materializing to List (baseline)");
        return users.ToList();
    }
}
```

2. Test both configurations:
   - Set `EnableStreaming: false` → should see "Materializing to List"
   - Set `EnableStreaming: true` → should see "Returning lazy IEnumerable"
3. Verify streaming actually happens (optional): Monitor when DTOs are created during serialization
4. Remove debug logging before performance test

---

### Phase 5: Performance Measurement

**Run Baseline (No Streaming):**
```powershell
# Manually set EnableStreaming: false in appsettings.json
.\scripts\run-experiment.ps1 -Pool
```

**Run Streaming Configuration:**
```powershell
# Manually set EnableStreaming: true in appsettings.json
.\scripts\run-experiment.ps1 -Pool  # ArrayPool + Streaming (no cache)
```

**Future Enhancement:** Add `-Stream` switch to `run-experiment.ps1` to automate configuration:
```powershell
# Proposed (not yet implemented)
.\scripts\run-experiment.ps1 -Pool -Stream
```

**Measurements:**
1. **TTFB:** Capture from `X-TTFB-Ms` response header
2. **Total Latency:** NBomber standard metrics
3. **Peak Memory:** From dotnet-counters `gc-heap-size` metric
4. **Allocation Rate:** From dotnet-counters `alloc-rate` metric

---

### Phase 6: Combined Test (Optional)

**Test All Configurations:**
```powershell
# Manual: Update appsettings.json for each combination
# EnableStreaming: true/false
# EnableObjectPooling: true/false
# EnableOutputCaching: true/false
```

**Configurations to Compare:**
| Config | Cache | Pool | Stream | Purpose |
|--------|:-----:|:----:|:------:|---------|
| Current 004b | ✅ | ✅ | ❌ | Production baseline |
| Streaming Only | ❌ | ❌ | ✅ | Isolate streaming impact |
| Pool + Stream | ❌ | ✅ | ✅ | **Primary experiment target** |
| All Three | ✅ | ✅ | ✅ | Best combined configuration? |

**Question:** Does caching negate streaming benefits? (Cache serves fully materialized response from memory)

**Optional Future Enhancement:**  
Add `-Stream` switch to `run-experiment.ps1` to enable testing all 8 configurations with `-All` flag:
```powershell
.\scripts\run-experiment.ps1 -All -Stream  # Would test all combinations
```

---

## Load Test Configuration

**Tool:** NBomber

### Scenarios

**Scenario 1: users_baseline**
- Duration: 60 seconds
- Injection Rate: 50 requests/second
- Total Requests: 3,000
- Purpose: Measure TTFB and total latency under controlled load

**Scenario 2: users_capacity_curve**
- Duration: 75 seconds (5 steps × 15s)
- Injection Rate: Ramp 10 → 50 → 100 → 150 → 200 RPS
- Total Requests: ~5,775
- Purpose: Validate streaming behavior under variable load

### Metrics to Collect

| Metric | Tool | Purpose |
|--------|------|---------|
| TTFB (p50, p95, p99) | Custom middleware | Measure initial response time |
| Total Latency | NBomber | End-to-end request time |
| Peak Memory | dotnet-counters | Maximum heap size during test |
| Allocation Rate | dotnet-counters | Memory allocation per second |
| GC Collections | dotnet-counters | Gen 0/1/2 collection counts |

---

## Expected Results

### Baseline (ArrayPool Only - from Exp 004)
| Metric | Expected Value |
|--------|----------------|
| Mean Latency | 2.79ms |
| p95 Latency | 5.01ms |
| Peak Memory | ~80 MB (estimated) |
| TTFB (p50) | ~2.0ms (estimated) |

### Treatment (ArrayPool + Streaming)
| Metric | Expected Value | Change |
|--------|----------------|--------|
| Mean Latency | 2.79-2.93ms | 0% to +5% |
| p95 Latency | 5.01-5.26ms | 0% to +5% |
| Peak Memory | ~56 MB | **-30%** ✅ |
| TTFB (p50) | ~1.2ms | **-40%** ✅ |

**Hypothesis:**
- ✅ **TTFB improves significantly** - Serializer begins writing immediately without waiting for materialization
- ✅ **Memory footprint reduced** - No intermediate List<T> allocation
- ⚠️ **Total latency slightly worse** - Streaming overhead may add 0-5% to total time

---

## Measurement Checklist

- [ ] **Memory Allocation Watermark** - Peak `gc-heap-size` during load test
- [ ] **Time to First Byte** - Custom middleware captures response start time
- [ ] **Total Latency** - NBomber standard p50/p95/p99 metrics
- [ ] **Serialization Behavior** - Verify lazy enumeration via debug logging
- [ ] **GC Pressure** - Gen 0 collection count comparison
- [ ] **Cache Interaction** - Test if caching negates streaming benefits

---

## Success Criteria

### Accept Streaming If:
1. **TTFB reduction ≥ 30%** (target: -40%)
2. **Peak memory reduction ≥ 20%** (target: -30%)
3. **Total latency increase ≤ 10%** (acceptable trade-off)
4. **Success rate remains 100%** (no errors introduced)

### Reject Streaming If:
1. Total latency degrades by >10%
2. Memory reduction <10% (not worth the complexity)
3. Cache compatibility issues arise

---

## Risks & Mitigation

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Serializer buffers anyway | Medium | High | Verify via debug logging before perf test |
| TTFB measurement inaccurate | Low | Medium | Validate middleware timing with external tool |
| Streaming breaks caching | Low | High | Test cache hit behavior explicitly |
| ArrayPool + Streaming conflict | Low | Medium | Verify PooledUserDtoCollection.GetEnumerator() |
| `.ToList()` overhead affects baseline | Low | Low | Overhead negligible (~microseconds), matches original behavior |
| Feature flag misconfiguration | Low | Medium | Verification test in Phase 4 confirms flag behavior |

---

## Analysis Plan

### Feature Flag Strategy

**Why Add `EnableStreaming` Toggle:**
1. ✅ **Experimental rigor** - Clean A/B testing with same codebase
2. ✅ **Consistency** - Matches existing `EnableOutputCaching` and `EnableObjectPooling` pattern
3. ✅ **Production safety** - Can disable streaming if issues arise without redeployment
4. ✅ **Future automation** - Enables `-Stream` switch in `run-experiment.ps1`

**Implementation approach:**
- Always return `IEnumerable<UserDto>` from UserService
- Conditionally call `.ToList()` when `EnableStreaming: false`
- Minimal overhead (~microseconds) for materialization when disabled
- Avoids complex conditional return types

**Trade-off accepted:** Slight code complexity for significantly better experimental control

### Comparison Matrix

| Configuration | Cache | Pool | Stream | Mean | p95 | TTFB | Memory | Use Case |
|--------------|:-----:|:----:|:------:|------|-----|------|--------|----------|
| Baseline (004b) | ✅ | ✅ | ❌ | 1.61ms | 2.50ms | ? | ? | Current production |
| ArrayPool Only | ❌ | ✅ | ❌ | 2.79ms | 5.01ms | ? | ? | No caching scenario |
| **Pool + Streaming** | ❌ | ✅ | ✅ | **?** | **?** | **?** | **?** | **This experiment** |
| All Features | ✅ | ✅ | ✅ | **?** | **?** | **?** | **?** | **Best combined?** |

**Note:** `EnableStreaming: false` in configurations above represents materialized `.ToList()` behavior

### Decision Matrix

**Adopt Streaming if:**
- TTFB critical (API Gateway, microservices with cascading calls)
- Memory constrained environments
- Large result sets (future scenarios with >10k items)

**Reject Streaming if:**
- Total latency more important than TTFB
- Caching already handles performance (004b)
- Memory not a constraint

---

## Implementation Complexity

**Estimated Effort:** 2.5-4.5 hours

**Changes Required:**
- [ ] Add `EnableStreaming` to PerformanceFeatures class (~5 min)
- [ ] Add TTFB middleware (~30 min)
- [ ] Update UserService with conditional streaming logic (~20 min)
- [ ] Update controller signature (~5 min)
- [ ] Add verification logging (~15 min)
- [ ] Run baseline test (EnableStreaming: false) (~15 min)
- [ ] Run streaming test (EnableStreaming: true) (~15 min)
- [ ] Analyze results (~1 hour)
- [ ] Document findings (~1 hour)
- [ ] Optional: Add `-Stream` switch to run-experiment.ps1 (~30 min)

**Risk Level:** Low - changes are minimal, easily reversible via feature flag

---

## Next Steps

### Pre-Experiment
1. Implement TTFB middleware
2. Run baseline measurement (ArrayPool only)
3. Verify streaming behavior with debug logging

### Experiment Execution
1. Implement streaming changes
2. Run performance tests (baseline + capacity curve)
3. Collect TTFB, memory, and latency metrics

### Post-Experiment
1. Analyze results vs hypothesis
2. Update performance-experiments-tracking.md
3. Decide: Keep streaming, combine with cache, or revert

### Optional Follow-up
- **Experiment 005b:** Test `IAsyncEnumerable<UserDto>` for true async streaming
- **Experiment 006:** Response compression (may interact with streaming)

---

## Questions to Answer

1. **Does ASP.NET Core's System.Text.Json actually stream IEnumerable, or does it buffer?**
   - Answer via verification test with debug logging

2. **What's the actual TTFB difference between materialized vs streamed responses?**
   - Answer via custom middleware measurement

3. **Does streaming provide any benefit when output caching is enabled?**
   - Answer via combined configuration test

4. **Is the memory reduction significant enough to justify streaming?**
   - Answer via dotnet-counters heap size comparison

---

## References

- **Baseline:** Experiment 004b (Combined ArrayPool + Cache) - 1.61ms mean, 2.50ms p95
- **Related:** Experiment 004 (ArrayPool Only) - 2.79ms mean, 5.01ms p95
- **Context:** Experiment 003 (Cache Only) - p95 degradation due to coordination overhead
- **Alternative:** Experiment 006 (Response Compression) - different approach to reduce bandwidth

---

## Notes

- This experiment is marked as **optional** in the tracking document
- Primary value is TTFB improvement for cascading API calls
- May be more valuable in future database-backed scenarios (Experiment 009)
- Consider combining with `IAsyncEnumerable` for true async streaming if synchronous streaming shows promise
- **Feature flag approach:** `EnableStreaming` toggle added for experimental control, following existing pattern of `EnableOutputCaching` and `EnableObjectPooling`
- Implementation always returns `IEnumerable<UserDto>`, conditionally materializes via `.ToList()` when streaming disabled

---

**Status:** 🔲 Not Started  
**Priority:** Low (Combined optimization from 004b already production-ready)  
**Estimated Execution Date:** TBD

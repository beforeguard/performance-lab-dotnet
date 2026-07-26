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
- Service layer returns `IEnumerable<UserDto>` always
- Conditional `.ToList()` materialization based on flag
- Allows clean A/B testing with identical code paths

---

## Results

**Date:** 2026-07-26  
**Status:** ✅ COMPLETE - Partial hypothesis validation, production decision: reject streaming

### Test Configuration

**Complete 8-Configuration Matrix:**
- All combinations tested: Cache × Pool × Stream (2³ = 8 configs)
- Load: 50 RPS sustained for 60 seconds (3,000 requests per test)
- TTFB middleware: Custom `TtfbMiddleware.cs` using `Response.OnStarting()` callback
- Memory tracking: dotnet-counters `gc.last_collection.memory.committed_size` metric

### Performance Results

#### TTFB Analysis (Primary Goal: -40%)

| Configuration | TTFB Mean | TTFB p95 | vs Non-Streaming | Result |
|---------------|----------:|---------:|-----------------:|:------:|
| **baseline** | 0.97ms | 2.38ms | - | - |
| **baseline_stream** | **0.42ms** | **0.62ms** | **-57% / -74%** | ✅ |
| **pool** | 1.81ms | 3.64ms | - | - |
| **pool_stream** | **1.14ms** | **1.50ms** | **-37% / -59%** | ✅ |
| **cache** | 0.10ms | 0.14ms | - | - |
| **cache_stream** | 0.10ms | 0.15ms | +0% / +7% | ⚠️ |
| **combined** | 0.10ms | 0.15ms | - | - |
| **combined_stream** | 0.10ms | 0.15ms | +0% / +0% | ⚠️ |

**Key Finding:** Streaming achieves **-57% TTFB reduction** without caching (exceeds -40% target), but provides **no benefit** when caching is enabled (already 0.10ms).

#### Total Latency Impact (Acceptable: ≤+10%)

| Configuration | p50 | p95 | vs Non-Streaming | Result |
|---------------|----:|----:|-----------------:|:------:|
| **baseline** | 3.73ms | 8.00ms | - | - |
| **baseline_stream** | 4.39ms | 11.02ms | **+18% / +38%** | ❌ |
| **pool** | 3.74ms | 8.81ms | - | - |
| **pool_stream** | 4.20ms | 12.11ms | **+12% / +37%** | ❌ |
| **cache** | 1.55ms | 2.57ms | - | - |
| **cache_stream** | 1.59ms | 2.57ms | +3% / +0% | ✅ |
| **combined** | 1.52ms | 2.64ms | - | - |
| **combined_stream** | 1.87ms | 15.91ms | **+23% / +503%** | ❌ |

**Key Finding:** Streaming causes significant latency degradation (+12-38% p50/p95) without caching, catastrophic degradation with combined optimization (+503% p95). Only acceptable with cache-only configuration.

#### Memory Footprint (Goal: -30%)

| Configuration | Committed Memory | vs Non-Streaming | Result |
|---------------|------------------:|-----------------:|:------:|
| **pool** | 66,662,400 B (63.6 MB) | - | - |
| **pool_stream** | 68,153,344 B (65.0 MB) | **+2.2%** | ❌ |
| **combined** | 66,887,680 B (63.8 MB) | - | - |
| **combined_stream** | 67,186,688 B (64.1 MB) | **+0.4%** | ❌ |

**Key Finding:** Memory **increased** by 2-3%, contradicting the -30% reduction hypothesis. Streaming does not reduce peak memory footprint in this implementation.

### Hypothesis Validation

| Hypothesis | Expected | Actual | Result |
|------------|----------|--------|:------:|
| **TTFB Reduction** | -40% | **-57% (baseline), -37% (pool)** | ✅ VALIDATED (without cache) |
| **Memory Reduction** | -30% | **+2-3%** | ❌ REJECTED (increased) |
| **Latency Impact** | 0% to +5% | **+12% to +503%** | ❌ REJECTED (severe degradation) |
| **Cache Compatibility** | N/A | **No TTFB benefit, catastrophic p95 with combined** | ⚠️ INCOMPATIBLE |

### Analysis

#### Why TTFB Improved (Without Caching)
- Serializer begins writing JSON immediately upon enumeration
- No blocking wait for `.ToList()` materialization
- First bytes sent before full collection processed
- Effect most visible at baseline (-57%) and with pooling (-37%)

#### Why Memory Increased (Not Decreased)
**Incorrect Hypothesis:** Expected that removing `List<UserDto>` allocation would reduce memory footprint.

**Actual Behavior:**
- List<T> materialization is transient (ephemeral Gen 0 allocation)
- ArrayPool already handles DTO allocations
- Streaming adds serializer overhead (buffering, state machine)
- Peak memory measured AFTER serialization completes
- Streaming's incremental enumeration extends allocation lifetime

**Conclusion:** Memory reduction hypothesis assumed List<T> was the primary footprint contributor. ArrayPool + streaming combination actually increases memory slightly due to extended allocation lifetime.

#### Why Latency Degraded
**Without Caching:**
- Streaming overhead: state machine, incremental enumeration, smaller write buffers
- p50 degradation: +12-18% across configurations
- p95 degradation: +37-38% (tail latency more sensitive to overhead)

**With Combined (Cache+Pool+Stream):**
- Catastrophic +503% p95 degradation (2.64ms → 15.91ms)
- Root cause: Cache coordination + streaming serializer overhead
- Streaming's incremental writes conflict with cache buffering strategy
- Same issue observed in Experiment 003 (caching alone had +382% p95)

**Conclusion:** Streaming serialization overhead outweighs TTFB benefits when measuring total request time. Trade-off only acceptable if TTFB is critical metric.

#### Cache Interaction
- **TTFB:** No benefit when caching enabled (already 0.10ms from memory)
- **Latency:** Combined config shows worst results (+503% p95)
- **Conclusion:** Streaming provides zero value when response is cached

### Success Criteria Evaluation

| Criterion | Target | Actual | Pass |
|-----------|--------|--------|:----:|
| TTFB reduction | ≥30% | 57% (baseline), 37% (pool) | ✅ |
| Memory reduction | ≥20% | **+2-3%** | ❌ |
| Latency increase | ≤10% | **+12% to +503%** | ❌ |
| Success rate | 100% | 100% | ✅ |

**Overall:** ❌ **FAIL** - Memory and latency criteria not met

### Recommendations

#### Production Decision: ❌ REJECT STREAMING

**Rationale:**
1. ❌ **Memory hypothesis invalidated** - No reduction, slight increase observed
2. ❌ **Unacceptable latency penalty** - +12-503% degradation across configs
3. ⚠️ **TTFB improvement limited** - Only valuable without caching
4. ✅ **Current optimization superior** - Combined (004b) delivers 1.52ms p50, 2.64ms p95

**When Streaming MIGHT Be Valuable:**
- Large result sets (>100K items) where List<T> allocation is significant
- Network-bound scenarios where TTFB matters more than total latency
- Real-time/progressive rendering use cases
- Scenarios where caching is not feasible

**For This Project:**
- Dataset: 10,000 items (not large enough for streaming benefits)
- Optimization: ArrayPool + OutputCache already optimal (004b)
- Metric priority: Total latency > TTFB
- **Conclusion:** Streaming adds complexity without measurable benefit

#### Keep Existing Production Config (Experiment 004b)
- `EnableOutputCaching: true`
- `EnableObjectPooling: true`
- `EnableStreaming: false` ← Explicitly disable

**Results from 004b (confirmed superior):**
- Mean latency: 1.61ms (-58.9% vs baseline)
- p95 latency: 2.50ms (-62.2% vs baseline)
- p99 latency: 5.21ms (-65.7% vs baseline)
- Cache hit ratio: ~99.98%

### Implementation Notes

**What Was Built:**
1. ✅ `TtfbMiddleware.cs` - Response.OnStarting() for TTFB measurement
2. ✅ `EnableStreaming` flag - Added to PerformanceFeatures configuration
3. ✅ Service layer refactor - Returns IEnumerable<UserDto>, conditional materialization
4. ✅ Controller disposal pattern - Response.OnCompleted() for PooledUserDtoCollection
5. ✅ NBomber TTFB tracking - TtfbTracker.cs with percentile reporting
6. ✅ run-experiment.ps1 enhancement - `-Stream` switch, 8-config `-All` matrix

**Artifacts:**
- Code: Fully implemented and tested
- Results: 8 complete test runs in `results/2026-07-26_*` folders
- Documentation: This experiment report

### Lessons Learned

1. **Peak memory ≠ allocation pressure** - Transient vs sustained allocations behave differently
2. **TTFB optimization != total latency optimization** - Different metrics, different trade-offs
3. **Caching dominates streaming** - Cache hit (0.10ms TTFB) makes streaming irrelevant
4. **ArrayPool already optimal** - Further allocation optimization provides diminishing returns
5. **Measure assumptions** - "Obvious" memory savings require validation, not assumption

### Future Work

**Scenarios Where Streaming Might Win:**
- Test with 100K+ item collections
- Implement true `IAsyncEnumerable<T>` with async I/O
- Network latency simulation (TTFB matters more over WAN)
- Progressive rendering client (consumes partial responses)

**Not Recommended for This Project:**
- Dataset too small (10K items)
- Caching already optimal
- TTFB not critical metric
- Complexity not justified by results
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

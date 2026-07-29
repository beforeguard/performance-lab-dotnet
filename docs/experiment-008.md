# Experiment 008: JSON Source Generators for Zero-Reflection Serialization

**Date:** 2026-07-28  
**Status:** 🔲 Planned  
**Branch:** `experiment/008-json-source-generators`

---

## Objective

Implement compile-time JSON serialization using System.Text.Json source generators to eliminate reflection overhead and reduce allocations during serialization. Measure the performance impact on latency, memory allocation, and CPU usage.

**Goal:** Quantify the cost of reflection-based serialization and validate that source generators provide measurable improvement.

## Hypothesis

**Primary Hypothesis:** Compile-time code generation for JSON serialization eliminates reflection overhead, reducing serialization latency by 10-20% and allocation rate by 20-40%.

**Secondary Hypotheses:**
- Serialization becomes more consistent (lower variance in latency)
- Startup time improves (no reflection cache warmup needed)
- Better CPU cache utilization (direct property access vs reflection)
- AOT-ready (Native AOT compatible, trimming-friendly)

**Expected Findings:**
- Mean latency reduction: 10-20% (1.40ms → 1.20-1.30ms for 100-user paginated responses)
- p95 latency reduction: 10-20% (1.76ms → 1.50-1.60ms)
- Allocation rate reduction: 20-40% during serialization phase
- No change to response size or correctness (same JSON output)
- Immediate full-speed performance (no warmup period)

---

## Background: The Reflection Problem

### Current Implementation (Reflection-Based)

When you call `JsonSerializer.Serialize(userDto)` in ASP.NET Core, the default System.Text.Json serializer:

1. **Uses reflection** to inspect the `UserDto` type at runtime
2. **Discovers properties** (Id, Name, Email, CreatedAt) dynamically
3. **Builds serialization metadata** (cached, but still has overhead)
4. **Allocates temporary objects** for serialization state
5. **Boxes value types** during property access
6. **Performs virtual calls** for property getters

This happens on **every request** (though metadata is cached after first use).

### Source Generator Approach

With JSON Source Generators, the compiler generates specialized serialization code at **compile time**:

```csharp
// Instead of this (runtime reflection):
var json = JsonSerializer.Serialize(userDto); // ❌ Reflection

// You get this (compile-time generated):
var json = JsonSerializer.Serialize(userDto, AppJsonContext.Default.UserDto); // ✅ Direct
```

The generated code:
- ✅ **No reflection** - direct property access
- ✅ **Fewer allocations** - optimized state management
- ✅ **Better inlining** - JIT can optimize more aggressively
- ✅ **Trimming-safe** - unused code can be removed
- ✅ **AOT-ready** - works with Native AOT compilation

---

## Design Decisions

### 1. Source Generator Context Design

**Decision:** Create a single `AppJsonSerializerContext` that covers all API response types.

**Rationale:**
- Centralized serialization configuration
- Single source of truth for JSON options (camelCase, null handling, etc.)
- Easy to extend when new DTOs are added
- Follows Microsoft's recommended pattern

**Implementation:**
```csharp
[JsonSerializable(typeof(UserDto))]
[JsonSerializable(typeof(UserDto[]))]
[JsonSerializable(typeof(List<UserDto>))]
[JsonSerializable(typeof(IReadOnlyList<UserDto>))]
[JsonSerializable(typeof(PagedResult<UserDto>))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
public partial class AppJsonSerializerContext : JsonSerializerContext
{
}
```

**Alternative Considered:** Multiple context classes per feature area
- **Rejected:** Adds complexity without benefit for this small API

---

### 2. Integration Strategy

**Decision:** Use `TypeInfoResolver` to integrate with ASP.NET Core's JSON configuration.

**Rationale:**
- Seamless integration with existing `ConfigureHttpJsonOptions`
- Maintains compatibility with existing feature flags
- No changes to controller code required
- Falls back to reflection for types not in the context (future-proof)

**Implementation in Program.cs:**
```csharp
builder.Services.ConfigureHttpJsonOptions(options =>
{
    // Add source generator context to resolver chain
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
    
    // Existing options still work
    // options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase; // Handled by context
});
```

**Alternative Considered:** Replace all serialization call sites
- **Rejected:** Invasive changes, error-prone, breaks abstraction

---

### 3. Feature Flag Strategy

**Decision:** Add `EnableJsonSourceGenerators` flag to `PerformanceFeatures` for A/B testing.

**Rationale:**
- Consistent with existing feature flag pattern (Pool, Cache, Streaming, Compression)
- Allows baseline vs treatment comparison
- Easy to disable if issues arise
- Documents that source generators are a toggleable optimization

**Configuration:**
```csharp
public class PerformanceFeatures
{
    public bool EnableObjectPooling { get; set; }
    public bool EnableOutputCaching { get; set; }
    public bool EnableStreaming { get; set; }
    public bool EnableCompression { get; set; }
    public string CompressionAlgorithm { get; set; } = "Brotli";
    public bool EnableJsonSourceGenerators { get; set; } = true; // New flag
    public int CacheDurationSeconds { get; set; } = 60;
}
```

---

## Implementation Plan

### Steps

1. **Create AppJsonSerializerContext** _(new source generator context)_
   - Create new file: `src/PerformanceLab.Shared/Serialization/AppJsonSerializerContext.cs`
   - Add `[JsonSerializable]` attributes for all response types:
     - `UserDto`
     - `UserDto[]`
     - `List<UserDto>`
     - `IReadOnlyList<UserDto>`
     - `PagedResult<UserDto>`
   - Configure `[JsonSourceGenerationOptions]`:
     - `PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase`
     - `WriteIndented = false`
     - `DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull`

2. **Add feature flag** _(PerformanceFeatures configuration)_
   - Add `EnableJsonSourceGenerators` property to `PerformanceFeatures.cs`
   - Default to `true` (enabled)
   - Add to `appsettings.json` configuration section

3. **Update Program.cs** _(conditional source generator registration)_
   - Read `EnableJsonSourceGenerators` from configuration
   - If enabled: register `AppJsonSerializerContext` with `ConfigureHttpJsonOptions`
   - If disabled: use default reflection-based serialization
   - Add configuration logging to startup

4. **Verify generated code** _(build and inspect)_
   - Build project in Release mode
   - Inspect generated files in `obj/Release/net10.0/generated/`
   - Verify serialization code was generated for all registered types
   - Check for compiler warnings related to source generators

5. **Update load test scenarios** _(measure with/without source generators)_
   - No changes required - existing scenarios work as-is
   - Tests will automatically use source generators when enabled
   - Allocation tracking will capture reduced allocations

6. **Run baseline measurements** _(reflection-based serialization)_
   - Disable `EnableJsonSourceGenerators` in appsettings.json
   - Run all scenarios (baseline, paginated 10/50/100/500/1000, capacity curve)
   - Capture: latency (p50, p95, p99), allocation rate, GC counts
   - Establish baseline for comparison

7. **Run treatment measurements** _(source generator serialization)_
   - Enable `EnableJsonSourceGenerators` in appsettings.json
   - Run identical scenarios
   - Capture same metrics
   - Compare latency, allocations, GC behavior

8. **Measure startup time** _(warmup period comparison)_
   - Baseline: measure first-request latency (reflection cache warmup)
   - Treatment: measure first-request latency (source generator - immediate speed)
   - Compare cold-start performance

9. **Validate correctness** _(JSON output equivalence)_
   - Compare JSON output from baseline vs treatment
   - Verify byte-for-byte equivalence (or semantic equivalence with whitespace differences)
   - Test edge cases: null values, empty collections, large datasets

10. **Document findings** _(analyze results, make recommendations)_
    - Calculate percentage improvements in latency and allocations
    - Analyze GC pressure reduction
    - Measure consistency (variance in latency)
    - Document startup time improvements
    - Recommend whether to enable by default in production
    - Update `performance-experiments-tracking.md` with results

---

## Files to Modify

| File | Changes |
|------|---------|
| [src/PerformanceLab.Shared/Serialization/AppJsonSerializerContext.cs](../src/PerformanceLab.Shared/Serialization/AppJsonSerializerContext.cs) | **NEW FILE** - Source generator context with all response types |
| [src/PerformanceLab.Shared/PerformanceFeatures.cs](../src/PerformanceLab.Shared/PerformanceFeatures.cs) | Add `EnableJsonSourceGenerators` property |
| [src/PerformanceLab.Api/appsettings.json](../src/PerformanceLab.Api/appsettings.json) | Add `EnableJsonSourceGenerators` to PerformanceFeatures section |
| [src/PerformanceLab.Api/Program.cs](../src/PerformanceLab.Api/Program.cs) | Conditionally register source generator context with `ConfigureHttpJsonOptions` |

---

## Test Scenarios

All scenarios use existing NBomber load tests with feature flag toggled.

### Baseline (Reflection-Based)

**Configuration:** `EnableJsonSourceGenerators = false`

| Scenario | Endpoint | Expected Latency | Purpose |
|----------|----------|------------------|---------|
| Paginated 100 | `/users?offset=0&limit=100` | ~1.40ms mean | Standard page size benchmark |
| Paginated 1000 | `/users?offset=0&limit=1000` | ~1.50ms mean | Large page benchmark |
| Full Dataset | `/users` | ~1.59ms mean | Maximum serialization load |
| Capacity Curve | Various | Measure throughput | Server capacity under load |

### Treatment (Source Generators)

**Configuration:** `EnableJsonSourceGenerators = true`

Same scenarios, expected improvements:
- Mean latency: 10-20% faster
- p95 latency: 10-20% faster
- Allocation rate: 20-40% reduction
- GC collections: Fewer Gen 0/1 collections

### Startup Performance Test

**Test:** Measure first-request latency (cold start)

1. Start API (no warmup requests)
2. Send single request to `/users?offset=0&limit=100`
3. Measure time to first byte (TTFB) and total latency
4. Compare baseline vs treatment

**Expected Result:**
- Baseline: Higher first-request latency (reflection cache warmup: ~10-50ms overhead)
- Treatment: Immediate full speed (source generator - no warmup needed)

### Allocation Profiling

**Test:** Measure allocations during serialization phase

```powershell
# Run with dotnet-counters monitoring
dotnet-counters monitor --process-id <pid> --counters System.Runtime
```

**Metrics to capture:**
- `alloc-rate` (bytes/sec)
- `gen-0-gc-count`, `gen-1-gc-count`, `gen-2-gc-count`
- `gc-heap-size` (working set)

**Expected Result:**
- 20-40% lower allocation rate with source generators
- Fewer Gen 0 collections (less GC pressure)

---

## Verification Checklist

### Build Verification
- [ ] Project builds successfully with source generator context
- [ ] Generated code appears in `obj/Release/net10.0/generated/` directory
- [ ] No compiler warnings related to JSON serialization
- [ ] All registered types have generated serialization code

### Functional Verification
- [ ] `/users` returns correct JSON (all 10,000 users)
- [ ] `/users?offset=0&limit=100` returns correct JSON (100 users with PagedResult wrapper)
- [ ] Response format matches baseline (camelCase, null handling)
- [ ] Content-Type header is `application/json`
- [ ] Response compression still works (Brotli/Gzip)
- [ ] OutputCache still works (cache headers present)

### Performance Verification
- [ ] Latency improved vs baseline (10-20% reduction)
- [ ] Allocation rate reduced vs baseline (20-40% reduction)
- [ ] First-request latency improved (no warmup period)
- [ ] p95/p99 latency improved (better consistency)
- [ ] GC collections reduced (fewer Gen 0/1 collections)
- [ ] Success rate maintained at 100%

### Integration Verification
- [ ] Feature flag works (can toggle source generators on/off)
- [ ] ArrayPool still works with source generators
- [ ] OutputCache still works with source generators
- [ ] Response compression still works with source generators
- [ ] Pagination still works with source generators
- [ ] All existing experiments' results are reproducible

---

## Expected Results

### Latency Improvements

| Configuration | Baseline (Reflection) | Treatment (Source Gen) | Improvement |
|---------------|----------------------|------------------------|-------------|
| Paginated 100 | 1.40ms mean | 1.20-1.30ms mean | 7-14% faster |
| Paginated 1000 | 1.50ms mean | 1.30-1.40ms mean | 7-13% faster |
| Full Dataset | 1.59ms mean | 1.40-1.50ms mean | 6-12% faster |
| p95 (100 users) | 1.76ms | 1.50-1.60ms | 9-15% faster |

### Allocation Improvements

| Metric | Baseline | Treatment | Improvement |
|--------|----------|-----------|-------------|
| Allocation rate | ~50 MB/sec | ~30-40 MB/sec | 20-40% reduction |
| Gen 0 collections | ~10/min | ~6-8/min | 20-40% reduction |
| Gen 1 collections | ~2/min | ~1-2/min | 0-50% reduction |

### Startup Performance

| Scenario | Baseline | Treatment | Improvement |
|----------|----------|-----------|-------------|
| First request TTFB | ~10-50ms overhead | No overhead | Immediate speed |
| Cold start latency | ~15-60ms | ~1-2ms | 85-98% faster |

---

## Success Criteria

**Minimum Acceptable Results:**
- ✅ 5% or greater mean latency reduction
- ✅ 10% or greater allocation rate reduction
- ✅ No increase in p99 latency (tail latency maintained or improved)
- ✅ 100% success rate maintained
- ✅ JSON output correctness verified
- ✅ No breaking changes to existing functionality

**Ideal Results:**
- 🎯 10-20% mean latency reduction
- 🎯 20-40% allocation rate reduction
- 🎯 Improved startup time (no warmup period)
- 🎯 Better latency consistency (lower variance)

**Decision Criteria:**

| Outcome | Decision |
|---------|----------|
| ✅ Meets minimum criteria | **ACCEPT** - Enable by default in production |
| ⚠️ Marginal improvement (3-5%) | **ACCEPT WITH CAVEATS** - Enable, but document minimal gain |
| ❌ No improvement or regression | **REJECT** - Keep reflection-based serialization |

---

## Implementation Complexity Assessment

### Effort Required

**Low Complexity:**
- ✅ Single new file (AppJsonSerializerContext.cs)
- ✅ Minimal changes to existing code (~20 lines in Program.cs)
- ✅ Feature flag integration follows existing pattern
- ✅ No changes to controllers or services

**Time Estimate:**
- Implementation: 30-45 minutes
- Testing: 30 minutes (run existing test suite)
- Analysis: 15-30 minutes
- **Total: ~1.5-2 hours**

### Risk Assessment

**Low Risk:**
- ✅ Additive change (doesn't remove existing functionality)
- ✅ Feature flag allows easy rollback
- ✅ Generated code is deterministic and testable
- ✅ No runtime behavior changes (same JSON output)
- ✅ Compiler enforces correctness (missing types = compile error)

**Potential Issues:**
- ⚠️ Must remember to add `[JsonSerializable]` for new DTOs in future
- ⚠️ Slight build time increase (code generation overhead)
- ⚠️ Generated files in source control? (usually no - in obj/ folder)

---

## Technical Deep Dive

### How Source Generators Work

**Compile Time:**
1. Roslyn compiler discovers `AppJsonSerializerContext` class
2. JSON source generator plugin executes
3. Generator inspects `[JsonSerializable]` attributes
4. Generator emits C# code for serialization logic
5. Generated code is compiled into assembly

**Generated Code Example (simplified):**

```csharp
// Generated by compiler
partial class AppJsonSerializerContext
{
    private JsonTypeInfo<UserDto>? _UserDto;
    
    public JsonTypeInfo<UserDto> UserDto => 
        _UserDto ??= CreateUserDtoInfo();
    
    private JsonTypeInfo<UserDto> CreateUserDtoInfo()
    {
        return new JsonTypeInfo<UserDto>
        {
            CreateObject = () => new UserDto(),
            SerializeHandler = (writer, value) =>
            {
                writer.WriteStartObject();
                writer.WriteNumber("id", value.Id);
                writer.WriteString("name", value.Name);
                writer.WriteString("email", value.Email);
                writer.WriteString("createdAt", value.CreatedAt);
                writer.WriteEndObject();
            }
        };
    }
}
```

**Runtime:**
- No reflection needed
- Direct property access: `value.Id`, `value.Name`
- Optimized `Utf8JsonWriter` calls
- JIT can inline aggressively

---

## Comparison to Other Serializers

### Newtonsoft.Json (Json.NET)
- ❌ Reflection-based by default
- ⚠️ Slower than System.Text.Json
- ❌ Higher allocations
- ✅ More flexible (dynamic objects, etc.)

### System.Text.Json (Reflection)
- ⚠️ Uses reflection but caches metadata
- ✅ Faster than Newtonsoft.Json
- ✅ Lower allocations than Newtonsoft.Json
- ✅ Native to .NET Core

### System.Text.Json (Source Generators) ⭐
- ✅ Zero reflection
- ✅ Fastest option
- ✅ Lowest allocations
- ✅ AOT-ready
- ⚠️ Requires compile-time type knowledge

---

## Production Considerations

### When to Use Source Generators

**✅ Great Fit:**
- High-throughput APIs (every millisecond matters)
- Microservices with tight latency budgets
- Native AOT deployments (required for trimming)
- CPU-constrained environments (cloud cost optimization)
- Startup time matters (serverless, Kubernetes pods)

**⚠️ Less Important:**
- Low-traffic APIs (reflection overhead negligible)
- I/O-bound workloads (database latency dominates)
- Dynamic JSON handling (unknown schemas at compile time)

### Maintenance Burden

**Minimal:**
- ✅ Compiler enforces correctness (can't forget types)
- ✅ Build-time errors for serialization issues
- ✅ Generated code is deterministic
- ⚠️ Must update context when adding new DTOs

**Best Practice:**
```csharp
// Put this comment in AppJsonSerializerContext.cs
// IMPORTANT: When adding new DTOs, add [JsonSerializable(typeof(NewDto))] here
```

---

## References

### Microsoft Documentation
- [System.Text.Json source generation modes](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation-modes)
- [How to use source generation in System.Text.Json](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation)
- [JsonSerializerOptions.TypeInfoResolver](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsonserializeroptions.typeinforesolver)

### Performance Benchmarks
- [ASP.NET Core Performance Best Practices](https://learn.microsoft.com/en-us/aspnet/core/performance/performance-best-practices)
- [System.Text.Json performance improvements](https://devblogs.microsoft.com/dotnet/system-text-json-in-dotnet-6/)

### Related Experiments
- [Experiment 004: ArrayPool Optimization](experiment-004.md) - Memory allocation optimization
- [Experiment 007: Pagination](experiment-007.md) - Response size optimization
- [Performance Experiments Tracking](performance-experiments-tracking.md) - All experiments overview

---

## Next Steps After Experiment 008

If source generators show positive results (expected):

1. **Enable by default** in `appsettings.json`
2. **Update README.md** with new best configuration
3. **Document** in production deployment guide
4. **Consider** wrapping up the lab with a final summary document

If exploring further experiments:
- **Experiment 009:** HTTP/2 vs HTTP/3 performance comparison
- **Experiment 010:** Database integration (EF Core with optimizations)
- **Or:** Conclude lab with comprehensive learnings document

**Recommendation:** After Experiment 008, the lab will have covered all major runtime optimization categories. Consider writing a comprehensive conclusion document synthesizing all findings.

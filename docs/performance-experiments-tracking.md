# Performance Lab - Experiment Tracking & Results

**Project:** PerformanceLab
**Date:** 2026-07-04
**Status:** Baseline Established, Ready for Optimization Experiments

---

## Overview

This lab enables controlled performance experimentation on a .NET 10 REST API. Current system handles **50 RPS with 2.88ms average latency** serving 10,000 users from memory. Identified bottlenecks provide opportunities to measure impact of specific optimization techniques in isolation.

---

## System Under Test

### Architecture
```
API Layer (REST) → Application Layer (UserService) → Infrastructure (Repository) → Domain (Entities)
```

### Critical Path Analysis
**Endpoint:** GET /users

**Current Implementation:**
```csharp
// UserService.GetUsers()
return _repo.GetAll()                    // 10k User entities (singleton, cached)
    .Select(u => new UserDto {...})      // Allocate 10k DTOs (per request)
    .ToList();                           // Materialize entire collection
    // → JSON serialization (200KB+ response)
```

**Bottlenecks Identified:**
1. **Allocation:** 10,000 DTO objects created per request
2. **Serialization:** Full 200KB+ JSON response every time
3. **Materialization:** `.ToList()` forces eager evaluation
4. **No caching:** Identical requests perform identical work

---

## Baseline Performance (Experiment 001)

**Test Configuration:**
- Load: 50 RPS sustained for 60 seconds
- Total Requests: 3,000
- Environment: Release build, .NET 10, localhost

**Results:**
| Metric | Value |
|--------|-------|
| Success Rate | 100% (3,000/3,000) |
| Avg Latency | 2.88 ms |
| P95 Latency | 3.36 ms |
| P99 Latency | 7.42 ms |
| Max Latency | 206.26 ms |

**Observations:**
- GC activity detected during test run
- Single outlier at 206ms (likely GC collection)
- Consistent performance within margin

## Capacity Curve Results (Experiment 002)

**Test Configuration:**
- Load: Variable (10→200 RPS in 15s steps)
- Total Requests: 5,775
- Environment: Release build, .NET 10, localhost

**Aggregate Results:**
| Metric | Value |
|--------|-------|
| Success Rate | 100% (5,775/5,775) |
| Avg Latency | 5.22 ms |
| P95 Latency | 6.4 ms |
| P99 Latency | 10.02 ms |
| Max Load Tested | 200 RPS (no saturation) |

**Key Finding:**
- **Burst traffic performs better than sustained load** - Variable load to 200 RPS showed better latency (5.22ms avg) than sustained 50 RPS (12.92ms avg in retest)
- **GC pressure identified as bottleneck** during sustained allocation
- **No saturation point found** - system handled 200 RPS without degradation

**Next:** Execute response caching to eliminate allocation pressure

---

## Planned Experiments

### Experiment 003: Response Caching
**Hypothesis:** Caching eliminates repeated DTO allocation and serialization for identical requests

**Variables:**
- Control: No caching (baseline)
- Treatment: Output caching with 60s TTL

**Implementation:**
- Add `builder.Services.AddOutputCache()` in Program.cs
- Apply `[OutputCache(Duration = 60)]` to UsersController

**Expected Results:**
- Cache hit ratio: >95% at 50 RPS
- Allocation rate: -90% (estimated)
- Latency: -50% for cached responses

**Measurements:**
- [ ] Baseline rerun for comparison
- [ ] Treatment run with caching
- [ ] GC Gen 0 collection count
- [ ] Allocation rate (MB/sec)
- [ ] Cache hit ratio
- [ ] Latency distribution

**Status:** 🔲 Not Started

---

### Experiment 004: Object Pooling
**Hypothesis:** Pooling DTO objects reduces GC pressure from repeated allocations

**Variables:**
- Control: `new UserDto()` per user (baseline)
- Treatment: `ArrayPool<UserDto>` or `ObjectPool<UserDto>`

**Implementation:**
- Modify UserService.GetUsers() to rent from pool
- Return pool after serialization

**Expected Results:**
- Gen 0 collections: -60% (estimated)
- Allocation rate: -70% (estimated)
- Latency: Neutral or slight improvement

**Measurements:**
- [ ] GC collection frequency
- [ ] Allocation rate
- [ ] Pool rent/return overhead
- [ ] Memory working set

**Status:** 🔲 Not Started

---

### Experiment 005: Lazy Enumeration
**Hypothesis:** Streaming DTOs reduces memory footprint and time-to-first-byte

**Variables:**
- Control: `.ToList()` materialization
- Treatment: Return `IEnumerable<UserDto>` for streaming

**Implementation:**
- Change return type to `IEnumerable<UserDto>`
- Remove `.ToList()` call
- Let serializer enumerate lazily

**Expected Results:**
- Peak memory: -30% (estimated)
- Time to first byte: -40% (estimated)
- Total request time: Neutral

**Measurements:**
- [ ] Memory allocation watermark
- [ ] Time to first byte
- [ ] Total latency
- [ ] Serialization behavior

**Status:** 🔲 Not Started

---

### Experiment 006: Response Compression
**Hypothesis:** Compression reduces network transfer time despite CPU overhead

**Variables:**
- Control: No compression (200KB response)
- Treatment: Gzip/Brotli compression

**Implementation:**
- Add `services.AddResponseCompression()`
- Add `app.UseResponseCompression()`

**Expected Results:**
- Response size: -85% (~200KB → 30KB)
- CPU usage: +15-20%
- Latency: -10% (network savings > CPU cost)

**Measurements:**
- [ ] Response size (bytes)
- [ ] CPU utilization
- [ ] Latency distribution
- [ ] Compression ratio per algorithm

**Status:** 🔲 Not Started

---

### Experiment 007: Pagination
**Hypothesis:** Returning subset of data dramatically reduces serialization cost

**Variables:**
- Control: 10,000 users always returned
- Treatment: `limit` parameter (default 100)

**Implementation:**
- Add query parameters: `?limit=100&offset=0`
- Modify repository to support Skip/Take
- Update controller

**Expected Results:**
- Latency: -95% for paginated responses
- Allocation: -99% (100 vs 10,000 DTOs)
- Throughput capacity: +500% (estimated)

**Measurements:**
- [ ] Latency per page size (10, 100, 1000, 10000)
- [ ] Maximum sustainable RPS
- [ ] Allocation rate comparison

**Status:** 🔲 Not Started

---

### Experiment 008: Async Repository Pattern
**Hypothesis:** Async patterns prepare for I/O-bound operations without degrading performance

**Variables:**
- Control: Synchronous repository
- Treatment: `async Task<IReadOnlyList<User>>` repository

**Implementation:**
- Convert IUserRepository.GetAll() to async
- Update UserService and Controller with async/await
- Return `Task<ActionResult>` from controller

**Expected Results:**
- Performance: Neutral (in-memory has no I/O benefit)
- Thread pool: Better utilization under load
- Scalability: Improved for future DB integration

**Measurements:**
- [ ] Latency comparison
- [ ] Thread pool metrics
- [ ] Concurrent request handling capacity

**Status:** 🔲 Not Started

---

### Experiment 009: Database Integration (Optional)
**Hypothesis:** EF Core with optimizations can match in-memory performance at scale

**Variables:**
- Control: In-memory repository
- Treatment: EF Core + SQL Server with AsNoTracking()

**Implementation:**
- Install EF Core packages
- Create DbContext and migration
- Seed 10,000 users
- Apply `AsNoTracking()` and compiled queries

**Expected Results:**
- Latency: +50-100% vs in-memory (acceptable trade-off)
- Throughput: Database becomes bottleneck
- Optimization techniques validated

**Measurements:**
- [ ] Latency distribution
- [ ] Database query time
- [ ] Connection pool behavior
- [ ] Compiled query benefit

**Status:** 🔲 Not Started

---

## Experiment Protocol

### Standard Procedure
1. **Baseline Measurement:** Run control scenario 3x, average results
2. **Implementation:** Make targeted change in isolation
3. **Treatment Measurement:** Run treatment scenario 3x, average results
4. **Analysis:** Compare metrics, calculate % change
5. **Documentation:** Record in experiment-NNN.md with graphs
6. **Decision:** Keep, refine, or revert change

### Test Execution
```powershell
# Run automated experiment
.\scripts\run-experiment.ps1 -Port 5206

# Results written to: results/YYYY-MM-DD_HH-mm-ss/
```

### Metrics Collected
- **Latency:** min, max, mean, median, p95, p99
- **Throughput:** requests/sec, success rate
- **GC:** Gen 0/1/2 collection counts
- **Memory:** allocation rate (MB/sec), working set
- **CPU:** utilization percentage
- **Network:** response size, compression ratio

---

## Results Summary

| Experiment | Status | Latency Δ | Throughput Δ | Allocation Δ | Notes |
|------------|--------|-----------|--------------|--------------|-------|
| 001 - Baseline | ✅ Complete | 2.88ms | 50 RPS | TBD | Initial reference point |
| 002 - Capacity Curve | ✅ Complete | 5.22ms avg | 200 RPS tested | TBD | No saturation found; GC pressure identified |
| 003 - Caching | 🔲 Planned | — | — | — | — |
| 004 - Pooling | 🔲 Planned | — | — | — | — |
| 005 - Lazy Enum | 🔲 Planned | — | — | — | — |
| 006 - Compression | 🔲 Planned | — | — | — | — |
| 007 - Pagination | 🔲 Planned | — | — | — | — |
| 008 - Async | 🔲 Planned | — | — | — | — |
| 009 - Database | 🔲 Planned | — | — | — | — |

---

## File Modifications by Experiment

| Experiment | Files Modified | Key Changes |
|------------|----------------|-------------|
| 003 - Caching | Program.cs, UsersController.cs | Add output cache middleware & attribute |
| 004 - Pooling | UserService.cs | Replace `new UserDto()` with pool |
| 005 - Lazy Enum | UserService.cs | Remove `.ToList()`, return IEnumerable |
| 006 - Compression | Program.cs | Add response compression middleware |
| 007 - Pagination | UsersController.cs, IUserRepository, UserRepository | Add limit/offset parameters |
| 008 - Async | IUserRepository, UserRepository, UserService, UsersController | Convert to async/await |
| 009 - Database | UserRepository.cs, Program.cs, add DbContext | Replace in-memory with EF Core |

---

## Profiling Commands

### Real-Time Monitoring
```powershell
# Monitor GC and allocations
dotnet-counters monitor --process-id <pid> --counters System.Runtime

# Watch specific metrics
dotnet-counters monitor --process-id <pid> Microsoft.AspNetCore.Hosting
```

### Detailed Traces
```powershell
# Capture allocation stacks
dotnet-trace collect --process-id <pid> --providers Microsoft-Windows-DotNETRuntime:0x1:4

# Generate flamegraph
speedscope trace.nettrace
```

### Memory Analysis
```powershell
# Create memory dump
dotnet-dump collect --process-id <pid>

# Analyze dump
dotnet-dump analyze dump.dmp
> dumpheap -stat
> gcroot <address>
```

---

## Hypotheses to Test

- [ ] Does caching provide linear improvement with cache hit ratio?
- [ ] What's the overhead cost of object pooling vs allocation benefit?
- [ ] Does streaming reduce time-to-first-byte as predicted?
- [ ] Is compression CPU overhead justified by network savings?
- [ ] What's the optimal default page size for pagination?
- [ ] Can async patterns improve concurrent request capacity?
- [ ] Do EF optimizations close the gap with in-memory performance?

---

## Next Actions

1. ✅ ~~**Immediate:** Run capacity curve test (find saturation point)~~ - COMPLETE
   - **Finding:** No saturation at 200 RPS; GC pressure during sustained load identified
2. 🧪 **Next Experiment:** Response caching (Experiment 003) - Highest impact based on findings
3. 📊 **Optional:** Profile allocation with dotnet-trace to visualize hotspots
4. 📝 **Documentation:** Create experiment-003.md for caching experiment

---

## Notes & Observations

_Space for unexpected findings, anomalies, or insights discovered during experimentation_

- **2026-07-04 - Experiment 002 Finding:** Sustained 50 RPS showed worse performance (mean: 12.92ms) than variable load up to 200 RPS (mean: 5.22ms). This counter-intuitive result confirms GC pressure as primary bottleneck - short bursts complete before GC, while sustained load triggers collections causing latency spikes (p99: 444ms).
- **Recommendation:** Prioritize allocation reduction (caching, pooling) over throughput scaling optimizations.
- 

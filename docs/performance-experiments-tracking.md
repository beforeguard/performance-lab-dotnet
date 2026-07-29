# Performance Lab - Quick Reference

**Project:** PerformanceLab  
**Date Started:** 2026-07-04  
**Last Updated:** 2026-07-28  
**Status:** 6 Experiments Complete, 3 Planned

**Production Recommendation:** ✅ ArrayPool + OutputCache + Brotli Compression (Experiments 004b + 006)

> **Note:** This is a quick reference index. See individual `experiment-NNN.md` files and [README.md](../README.md) for complete details.

---

## Experiment Roadmap

### ✅ Completed Experiments

| # | Name | Status | Outcome | Documentation |
|---|------|--------|---------|---------------|
| 001 | Baseline Measurement | ✅ Complete | 2.88ms mean, 3.36ms p95 - GC bottleneck identified | [experiment-001.md](experiment-001.md) |
| 002 | Capacity Curve | ✅ Complete | 200 RPS capacity, burst > sustained load | [experiment-002.md](experiment-002.md) |
| 003 | Output Caching | ✅ Complete | 99% GC reduction, +382% p95 tail latency | [experiment-003.md](experiment-003.md) |
| 004 | ArrayPool Optimization | ✅ Complete | -48% mean, -39% p95 - Excellent tail latency | [experiment-004.md](experiment-004.md) |
| 004b | Combined (Pool+Cache) | ✅ Complete | **-58.9% mean, -62.2% p95** - Best base 🏆 | [experiment-004.md](experiment-004.md#phase-3) |
| 005 | Response Streaming | ✅ Complete | -57% TTFB but +12-503% latency - **REJECTED** ❌ | [experiment-005.md](experiment-005.md) |
| 006 | Response Compression | ✅ Complete | **-89.5% bandwidth (Brotli), +12.5% CPU** - ACCEPTED 🏆 | [experiment-006.md](experiment-006.md) |
| 007 | Pagination | ✅ Complete | **6-12% faster (optimized), 60-66% faster (baseline)** - ACCEPTED 🏆 | [experiment-007.md](experiment-007.md) |

### 🔲 Planned Experiments

| # | Name | Status | Hypothesis | Expected Impact |
|---|------|--------|------------|-----------------|
| 008 | Async Repository | 🔲 Planned | Async patterns prepare for I/O without performance penalty | Neutral latency, improved thread pool utilization |
| 009 | Database Integration | 🔲 Planned | EF Core + optimizations can approach in-memory performance | +50-100% latency (acceptable for persistence) |

---

## Results Comparison

| Experiment | Mean Latency | p95 Latency | Response Size | Success Rate | Key Metric |
|------------|-------------:|------------:|---------------:|:------------:|------------|
| 001 - Baseline | 2.88ms | 3.36ms | 307 KB | 100% | Reference point |
| 002 - Capacity (200 RPS) | 5.22ms | 6.4ms | 307 KB | 100% | No saturation |
| 003 - OutputCache | 3.72ms (+29%) | 16.19ms (+382%) | 307 KB | 100% | -99% GC collections ✅ |
| 004 - ArrayPool | 2.79ms (-48%) | 5.01ms (-39%) | 190 KB | 100% | -67% variance ✅ |
| **004b - Pool+Cache** | **1.61ms (-58.9%)** 🏆 | **2.50ms (-62.2%)** 🏆 | **190 KB** | **100%** | **99.98% cache hit** 🏆 |
| 005 - Streaming | 1.87ms | 15.91ms (+503%) | 190 KB | 100% | TTFB -57%, rejected ❌ |
| **006 - Compression (Brotli)** | **1.80ms (-54%)** 🏆 | **2.28ms (-66%)** 🏆 | **32 KB (-89.5%)** 🏆 | **100%** | **79 bytes cached** 🚀 |
| **007 - Pagination (100 users)** | **1.40ms (-51%)** 🏆 | **1.76ms (-48%)** 🏆 | **~1 KB (compressed)** 🏆 | **100%** | **Linear scaling** ✅ |

**Production Recommendation:** Enable ArrayPool + OutputCache + Brotli compression for optimal latency and bandwidth efficiency.

---

## File Modifications by Experiment

| Experiment | Files Modified | Key Changes |
|------------|----------------|-------------|
| 003 | Program.cs, UsersController.cs, CacheLoggingMiddleware.cs (new) | OutputCache middleware, cache policy, warm-up, logging |
| 004 | PerformanceFeatures.cs (new), appsettings.json, Program.cs, UserService.cs, UsersController.cs, PooledUserDtoCollection.cs (new) | Feature flags, ArrayPool implementation, for-loop vs LINQ |
| 004b | appsettings.json | Set EnableOutputCaching + EnableObjectPooling to true |
| 005 | PerformanceFeatures.cs, Program.cs, UserService.cs, UsersController.cs, TtfbMiddleware.cs (new) | EnableStreaming flag, IEnumerable return type, TTFB tracking |
| 006 | PerformanceFeatures.cs, Program.cs, HttpClientFactory.cs, UserScenarios.cs, ResponseSizeTracker.cs (new), run-experiment.ps1 | ResponseCompression middleware, response size measurement, compression algorithms |
| 007 | IUserRepository.cs, UserRepository.cs, UserService.cs, UsersController.cs, PagedResult.cs (new), UserScenarios.cs, Program.cs | Pagination (offset/limit), PagedResult wrapper, parameterized scenarios |
| 008 | IUserRepository, UserRepository, UserService, UsersController | Convert to async/await (planned) |
| 009 | UserRepository.cs, Program.cs, DbContext (new) | Replace in-memory with EF Core (planned) |

---

## Experiment Protocol

### Standard Procedure
1. **Baseline Measurement:** Run control scenario, capture metrics
2. **Implementation:** Make targeted change in isolation
3. **Treatment Measurement:** Run treatment scenario, capture metrics
4. **Analysis:** Compare results, calculate percentage changes
5. **Documentation:** Record findings in `experiment-NNN.md`
6. **Decision:** Accept, refine, or reject change

### Test Execution
```powershell
# Run automated experiment with script
.\scripts\run-experiment.ps1 -Port 5206 [-Cache] [-Pool] [-Stream] [-Compression] [-All]

# Results saved to: results/{timestamp}_{config}/
```

### Standard Metrics
- **Latency:** min, max, mean, median, p95, p99
- **Throughput:** requests/sec, success rate
- **GC:** Gen 0/1/2 collection counts
- **Memory:** allocation rate (MB/sec), working set
- **Network:** response size (bytes), compression ratio (if applicable)
- **TTFB:** time-to-first-byte (ms)

---

## Profiling Commands Reference

### Real-Time Monitoring
```powershell
# Monitor GC and allocations
dotnet-counters monitor --process-id <pid> --counters System.Runtime

# Monitor ASP.NET Core metrics
dotnet-counters monitor --process-id <pid> --counters Microsoft.AspNetCore.Hosting
```

### Detailed Traces
```powershell
# Capture allocation stacks
dotnet-trace collect --process-id <pid> --providers Microsoft-Windows-DotNETRuntime:0x1:4

# Open trace in visualizer
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

## Quick Links

- **README:** [../README.md](../README.md) - Project overview and getting started
- **Experiments:** Individual `experiment-NNN.md` files for detailed analysis
- **Test Script:** [../scripts/run-experiment.ps1](../scripts/run-experiment.ps1) - Automated runner
- **Results:** `../results/` - Generated test output (gitignored)

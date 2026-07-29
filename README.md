# performance-lab-dotnet

An ASP.NET Core performance and profiling lab for measuring runtime behavior, GC activity, allocation pressure, and throughput under controlled load.

## Overview

This project demonstrates systematic performance optimization through controlled experiments on a .NET 10 API. Each optimization is implemented as a toggleable feature, measured independently, and documented with before/after metrics.

**Key Achievements:**
- 🏆 **78% faster latency** - 2.88ms → 0.62ms mean (Full optimization stack)
- 🏆 **75% faster p95** - 3.36ms → 0.84ms p95 (Full optimization stack)
- 🏆 **99.7% bandwidth reduction** - 307KB → 1KB per request (100-user pagination + Brotli)
- 🏆 **Zero-reflection serialization** - Compile-time JSON generation (55% faster than reflection)
- 🏆 **100% success rate** - All optimizations maintain reliability at scale

## Current State

The solution is structured as a small layered .NET 10 app:

- `src/PerformanceLab.Api` - HTTP API with Swagger, middleware, and controllers
- `src/PerformanceLab.Application` - User service, DTO mapping, and business logic
- `src/PerformanceLab.Domain` - Core `User` entity
- `src/PerformanceLab.Infrastructure` - In-memory user repository
- `src/PerformanceLab.Shared` - Shared configuration (`PerformanceFeatures`)
- `tools/PerformanceLab.LoadTests` - NBomber load test harness with TTFB and response size tracking

**Endpoint:**
- `GET /users` - Returns 10,000 in-memory users as `UserDto` (Id + Name)

**Performance Features** (toggleable via `appsettings.json`):
- **EnableObjectPooling** - ArrayPool-based DTO allocation (`PooledUserDtoCollection`)
- **EnableOutputCaching** - ASP.NET Core output caching with 60s TTL
- **EnableStreaming** - IEnumerable streaming serialization (reduces TTFB)
- **EnableCompression** - HTTP response compression (Gzip/Brotli)
- **CompressionAlgorithm** - Compression algorithm selection: `Gzip`, `Brotli`, or `Both`
- **EnableJsonSourceGenerators** - Compile-time JSON serialization (zero reflection)
- **Pagination** - Optional offset/limit query parameters for subset responses

## Project Layout

```
├── PerformanceLab.slnx          # Solution file
├── dotnet-tools.json            # dotnet-counters, dotnet-trace
├── docs/
│   ├── experiment-001.md        # Baseline measurement
│   ├── experiment-002.md        # ArrayPool optimization
│   ├── experiment-003.md        # OutputCache optimization
│   ├── experiment-004.md        # Combined optimizations
│   ├── experiment-005.md        # Response streaming (TTFB)
│   ├── experiment-006.md        # Response compression (Gzip/Brotli)
│   ├── experiment-007.md        # Pagination (scalability curve)
│   ├── experiment-008.md        # JSON Source Generators (zero reflection)
│   └── performance-experiments-tracking.md
├── results/                     # Experiment outputs (gitignored)
├── reports/                     # NBomber HTML reports (gitignored)
├── scripts/
│   └── run-experiment.ps1       # Automated experiment runner
└── src/                         # Application code
```

## Running The API

The API targets `net10.0` and uses standard development ports:

- HTTP: `http://localhost:5206`
- HTTPS: `https://localhost:7262`

**Development mode:**
```powershell
dotnet run --project src/PerformanceLab.Api
```

**Release mode (for experiments):**
```powershell
dotnet run --project src/PerformanceLab.Api -c Release --urls http://localhost:5206
```

Swagger is available at `http://localhost:5206/swagger` in Development.

## Running Experiments

The automated experiment runner handles API startup, performance monitoring, load testing, and cleanup.

**Basic usage:**
```powershell
# Baseline (no optimizations)
.\scripts\run-experiment.ps1

# ArrayPool only
.\scripts\run-experiment.ps1 -Pool

# OutputCache only
.\scripts\run-experiment.ps1 -Cache

# Combined (ArrayPool + Cache)
.\scripts\run-experiment.ps1 -Cache -Pool

# Streaming (ArrayPool + Streaming)
.\scripts\run-experiment.ps1 -Pool -Stream

# Compression (Brotli)
.\scripts\run-experiment.ps1 -Compression

# Combined + Compression (recommended for production)
.\scripts\run-experiment.ps1 -Cache -Pool -Compression

# Run all configurations including compression variants
.\scripts\run-experiment.ps1 -All
```

**What it does:**
1. Builds API in Release mode
2. Starts API with specified feature flags (via environment variables)
3. Starts `dotnet-counters` for GC/memory metrics
4. Runs NBomber load tests (baseline + capacity curve scenarios)
5. Generates TTFB and response size reports
6. Saves results to timestamped folder in `results/`
7. Cleans up processes

**Results location:**
- `results/{timestamp}_{config}/experiment.md` - Configuration and summary
- `results/{timestamp}_{config}/nbomber.txt` - Latency metrics
- `results/{timestamp}_{config}/counters.csv` - GC and allocation data
- `reports/ttfb_report_{timestamp}.md` - Time-to-first-byte analysis
- `reports/response_size_report_{timestamp}.md` - Response size and compression metrics

## Manual Load Testing

If running tests manually without the script:

```powershell
# Start API first
dotnet run --project src/PerformanceLab.Api -c Release --urls http://localhost:5206

# In another terminal
dotnet run --project tools/PerformanceLab.LoadTests -c Release
```

## Configuration

Performance features are controlled via `appsettings.json` or environment variables:

**appsettings.json:**
```json
{
  "PerformanceFeatures": {
    "EnableOutputCaching": true,
    "EnableObjectPooling": true,
    "EnableStreaming": false,
    "EnableCompression": true,
    "CompressionAlgorithm": "Brotli",
    "EnableJsonSourceGenerators": true,
    "CacheDurationSeconds": 60
  }
}
```

**Environment variables (used by run-experiment.ps1):**
```powershell
$env:PerformanceFeatures__EnableOutputCaching = "true"
$env:PerformanceFeatures__EnableObjectPooling = "true"
$env:PerformanceFeatures__EnableStreaming = "false"
$env:PerformanceFeatures__EnableCompression = "true"
$env:PerformanceFeatures__CompressionAlgorithm = "Brotli"
$env:PerformanceFeatures__EnableJsonSourceGenerators = "true"
```

**Response headers** indicate active features:
- `X-Caching-Enabled: True/False`
- `X-Pooling-Enabled: True/False`
- `X-Streaming-Enabled: True/False`
- `X-TTFB-Ms: {milliseconds}` - Time to first byte
- `Content-Encoding: gzip|br` - Active compression algorithm

## Diagnostics

The repo includes local tool definitions for:

- **dotnet-counters** - Real-time GC, heap size, and allocation metrics
- **dotnet-trace** - Detailed runtime event traces

**Monitoring during manual tests:**
```powershell
# Monitor GC and allocations (replace PID)
dotnet counters monitor --process-id {PID} --counters System.Runtime,Microsoft.AspNetCore.Hosting

# Capture trace
dotnet trace collect --process-id {PID} --providers Microsoft-DotNETCore-SampleProfiler
```

## Completed Experiments

| Experiment | Description | Key Result | Status |
|------------|-------------|------------|--------|
| 001 | Baseline measurement | 5.44ms p50, 9.45ms p95 | ✅ Complete |
| 002 | ArrayPool optimization | 2.79ms p50 (-49%), 5.01ms p95 (-47%) | ✅ Complete |
| 003 | OutputCache optimization | Variable (coordination overhead) | ✅ Complete |
| 004 | Combined (Pool + Cache) | 1.61ms p50 (-70%), 2.50ms p95 (-74%) | ✅ Complete |
| 005 | Response streaming | -57% TTFB but +12-503% latency (rejected) | ✅ Complete |
| 006 | Response compression | 89.5% bandwidth reduction (Brotli) | ✅ Complete |
| 007 | Pagination | 6-12% faster (optimized), 60-66% faster (baseline) | ✅ Complete |
| 008 | JSON Source Generators | 55% faster serialization (zero reflection) | ✅ Complete |

**Best configuration (Experiment 008):**
- ArrayPool + OutputCache + Brotli Compression + Pagination + JSON Source Generators (100 users)
- **Mean latency:** 0.62ms (78% improvement vs baseline)
- **p95 latency:** 0.84ms (75% improvement vs baseline)
- **Response size:** ~1KB per page (99.7% reduction vs full dataset)
- **Success rate:** 100%
- **Scalability:** Sub-1ms p95 across all page sizes (10-1,000 users)
- **Serialization:** Zero reflection overhead (compile-time code generation)

See `docs/performance-experiments-tracking.md` for detailed tracking.

## Metrics Captured

**Latency (NBomber):**
- p50, p75, p95, p99 request latency
- RPS (requests per second)
- Success rate

**Memory (dotnet-counters):**
- `gc-heap-size` - Peak memory allocation
- `alloc-rate` - Allocation rate per second
- `gen-0-gc-count`, `gen-1-gc-count`, `gen-2-gc-count` - GC pressure

**TTFB (custom tracking):**
- Time from request received to response start
- Critical for streaming evaluation

**Response Size (compression tracking):**
- Wire bytes transferred (compressed size)
- Compression ratio and algorithm distribution
- Bandwidth savings per configuration

## Next Steps

- [x] Complete Experiment 005 (streaming evaluation - rejected)
- [x] Complete Experiment 006 (response compression - Brotli recommended)
- [x] Complete Experiment 007 (pagination - linear scaling confirmed)
- [x] Complete Experiment 008 (JSON source generators - 55% serialization improvement)
- [ ] Experiment 009: Async repository (prepare for database I/O)
- [ ] Experiment 010: Database integration (replace in-memory repo)

## Documentation

All experiments are documented in `docs/` with:
- Hypothesis and success criteria
- Implementation details
- Before/after metrics
- Analysis and conclusions

See `docs/performance-experiments-tracking.md` for the master experiment log.

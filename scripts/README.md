# Performance Experiment Scripts

## run-experiment.ps1

Automated performance testing script for running load tests with different optimization configurations.

### Usage

```powershell
# Run baseline (default - no optimizations)
.\scripts\run-experiment.ps1

# Run with specific optimizations
.\scripts\run-experiment.ps1 -Pool                # ArrayPool only
.\scripts\run-experiment.ps1 -Cache               # OutputCache only
.\scripts\run-experiment.ps1 -Cache -Pool         # Both optimizations (combined)

# Run ALL configurations in sequence (recommended for full experiment)
.\scripts\run-experiment.ps1 -All

# With custom port and warmup time
.\scripts\run-experiment.ps1 -Cache -Pool -Port 5000 -WarmupSeconds 5
```

### Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `-Port` | int | 5206 | Port for API to run on |
| `-Cache` | switch | false | Enable OutputCache optimization |
| `-Pool` | switch | false | Enable ArrayPool optimization |
| `-All` | switch | false | Run all 4 configurations in sequence |
| `-WarmupSeconds` | int | 3 | Seconds to wait after API start before running tests |

### Configuration Combinations

| Flags | Cache | Pool | Config Name | Description |
|-------|-------|------|-------------|-------------|
| *(none)* | ❌ | ❌ | `baseline` | No optimizations - baseline performance |
| `-Pool` | ❌ | ✅ | `pool` | ArrayPool optimization only |
| `-Cache` | ✅ | ❌ | `cache` | Output caching only |
| `-Cache -Pool` | ✅ | ✅ | `combined` | Both optimizations enabled |
| `-All` | - | - | *(all 4)* | Runs all 4 configurations in sequence |

### What It Does

For each configuration:

1. **Updates `appsettings.json`** - Sets `EnableOutputCaching` and `EnableObjectPooling` based on flags
2. **Builds API** - Compiles in Release mode
3. **Starts API** - Runs on specified port
4. **Starts dotnet-counters** - Collects GC and allocation metrics
5. **Runs NBomber** - Executes load test scenarios
6. **Saves results** - Creates timestamped folder with configuration name
7. **Cleans up** - Stops API and counters

### Results

Results are saved to `results/{timestamp}_{configName}/`:

```
results/
  2026-07-25_14-30-00_baseline/
    api.log
    counters.csv
    nbomber.txt
    experiment.md
  2026-07-25_14-32-00_pool/
    ...
  2026-07-25_14-34-00_cache/
    ...
  2026-07-25_14-36-00_combined/
    ...
```

### Example: Full Experiment Run

```powershell
# Run all configurations for Experiment 004
.\scripts\run-experiment.ps1 -All
```

This will:
- Run baseline, pool, cache, and combined configurations
- Create 4 results folders
- Take approximately 10-12 minutes total
- Show summary at the end with all result paths

### Example: Quick Iteration

```powershell
# Test just the combined optimization
.\scripts\run-experiment.ps1 -Cache -Pool

# Compare pool vs baseline
.\scripts\run-experiment.ps1        # baseline (run 1)
.\scripts\run-experiment.ps1 -Pool  # pool (run 2)
```

### Tips

- Use `-All` for initial experiments or regression testing
- Use specific flags (`-Pool`, `-Cache`, `-Cache -Pool`) for quick iteration
- Default (no flags) runs baseline configuration
- Review `nbomber.txt` for latency metrics
- Review `counters.csv` for GC/allocation metrics
- Results folder name includes configuration for easy identification

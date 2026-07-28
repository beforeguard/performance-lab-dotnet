param (
    [int]$Port = 5206,
    
    [switch]$Cache,
    [switch]$Pool,
    [switch]$Stream,
    [switch]$Compression,
    [switch]$All,
    
    [int]$WarmupSeconds = 3
)

$ErrorActionPreference = "Stop"

# -----------------------------
# Setup repo root
# -----------------------------
$RepoRoot = git rev-parse --show-toplevel
Set-Location $RepoRoot

# -----------------------------
# Define configurations
# -----------------------------
$configurations = @()

if ($All) {
    # Run all 12 configurations (4 base + 4 Gzip + 4 Brotli)
    $configurations = @(
        # Without compression
        @{Cache=$false; Pool=$false; Stream=$false; Compression=$false; Algorithm="None"; Name="baseline"},
        @{Cache=$false; Pool=$true;  Stream=$false; Compression=$false; Algorithm="None"; Name="pool"},
        @{Cache=$true;  Pool=$false; Stream=$false; Compression=$false; Algorithm="None"; Name="cache"},
        @{Cache=$true;  Pool=$true;  Stream=$false; Compression=$false; Algorithm="None"; Name="combined"},
        
        # With Gzip compression
        @{Cache=$false; Pool=$false; Stream=$false; Compression=$true; Algorithm="Gzip"; Name="baseline_gzip"},
        @{Cache=$false; Pool=$true;  Stream=$false; Compression=$true; Algorithm="Gzip"; Name="pool_gzip"},
        @{Cache=$true;  Pool=$false; Stream=$false; Compression=$true; Algorithm="Gzip"; Name="cache_gzip"},
        @{Cache=$true;  Pool=$true;  Stream=$false; Compression=$true; Algorithm="Gzip"; Name="combined_gzip"},
        
        # With Brotli compression
        @{Cache=$false; Pool=$false; Stream=$false; Compression=$true; Algorithm="Brotli"; Name="baseline_brotli"},
        @{Cache=$false; Pool=$true;  Stream=$false; Compression=$true; Algorithm="Brotli"; Name="pool_brotli"},
        @{Cache=$true;  Pool=$false; Stream=$false; Compression=$true; Algorithm="Brotli"; Name="cache_brotli"},
        @{Cache=$true;  Pool=$true;  Stream=$false; Compression=$true; Algorithm="Brotli"; Name="combined_brotli"}
    )
    
    Write-Host "Running ALL configurations (12 total - compression algorithm comparison)" -ForegroundColor Yellow
    Write-Host "  Without Compression:" -ForegroundColor Cyan
    Write-Host "    1. Baseline (no optimizations)" -ForegroundColor Gray
    Write-Host "    2. ArrayPool only" -ForegroundColor Gray
    Write-Host "    3. OutputCache only" -ForegroundColor Gray
    Write-Host "    4. Combined (ArrayPool + Cache)" -ForegroundColor Gray
    Write-Host "  With Gzip Compression:" -ForegroundColor Cyan
    Write-Host "    5. Baseline + Gzip" -ForegroundColor Gray
    Write-Host "    6. ArrayPool + Gzip" -ForegroundColor Gray
    Write-Host "    7. OutputCache + Gzip" -ForegroundColor Gray
    Write-Host "    8. Combined + Gzip" -ForegroundColor Gray
    Write-Host "  With Brotli Compression:" -ForegroundColor Cyan
    Write-Host "    9. Baseline + Brotli" -ForegroundColor Gray
    Write-Host "    10. ArrayPool + Brotli" -ForegroundColor Gray
    Write-Host "    11. OutputCache + Brotli" -ForegroundColor Gray
    Write-Host "    12. Combined + Brotli" -ForegroundColor Gray
    Write-Host ""
} else {
    # Run single configuration based on flags
    $enableCache = $Cache.IsPresent
    $enablePool = $Pool.IsPresent
    $enableStream = $Stream.IsPresent
    $enableCompression = $Compression.IsPresent
    
    # Determine compression algorithm (default to Brotli if compression enabled)
    $algorithm = if ($enableCompression) { "Brotli" } else { "None" }
    
    # Determine configuration name
    $configName = if (!$enableCache -and !$enablePool) {
        "baseline"
    } elseif (!$enableCache -and $enablePool) {
        "pool"
    } elseif ($enableCache -and !$enablePool) {
        "cache"
    } else {
        "combined"
    }
    
    # Add stream suffix if enabled
    if ($enableStream) {
        $configName += "_stream"
    }
    
    # Add compression suffix if enabled (default to brotli)
    if ($enableCompression) {
        $configName += "_brotli"
    }
    
    $configurations = @(@{Cache=$enableCache; Pool=$enablePool; Stream=$enableStream; Compression=$enableCompression; Algorithm=$algorithm; Name=$configName})
    
    if ($configName -eq "baseline") {
        Write-Host "Running BASELINE configuration (no optimizations)" -ForegroundColor Yellow
    } elseif ($configName -eq "pool") {
        Write-Host "Running ARRAYPOOL configuration" -ForegroundColor Yellow
    } elseif ($configName -eq "pool_stream") {
        Write-Host "Running ARRAYPOOL + STREAMING configuration" -ForegroundColor Yellow
    } elseif ($configName -eq "cache") {
        Write-Host "Running CACHE configuration" -ForegroundColor Yellow
    } elseif ($configName -eq "combined") {
        Write-Host "Running COMBINED configuration (ArrayPool + Cache)" -ForegroundColor Yellow
    } elseif ($configName -eq "combined_stream") {
        Write-Host "Running COMBINED + STREAMING configuration (ArrayPool + Cache + Streaming)" -ForegroundColor Yellow
    } else {
        Write-Host "Running $configName configuration" -ForegroundColor Yellow
    }
}

$allResultsFolders = @()

# -----------------------------
# Run each configuration
# -----------------------------
foreach ($config in $configurations) {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "Configuration: $($config.Name.ToUpper())" -ForegroundColor Cyan
    Write-Host "  EnableOutputCaching: $($config.Cache)" -ForegroundColor Gray
    Write-Host "  EnableObjectPooling: $($config.Pool)" -ForegroundColor Gray
    Write-Host "  EnableStreaming: $($config.Stream)" -ForegroundColor Gray
    Write-Host "  EnableCompression: $($config.Compression)" -ForegroundColor Gray
    Write-Host "  CompressionAlgorithm: $($config.Algorithm)" -ForegroundColor Gray
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
    
    # -----------------------------
    # Set environment variables for configuration override
    # -----------------------------
    Write-Host "Setting configuration via environment variables..." -ForegroundColor Cyan
    $env:PerformanceFeatures__EnableOutputCaching = $config.Cache.ToString().ToLower()
    $env:PerformanceFeatures__EnableObjectPooling = $config.Pool.ToString().ToLower()
    $env:PerformanceFeatures__EnableStreaming = $config.Stream.ToString().ToLower()
    $env:PerformanceFeatures__EnableCompression = $config.Compression.ToString().ToLower()
    $env:PerformanceFeatures__CompressionAlgorithm = $config.Algorithm
    
    Write-Host "  PerformanceFeatures__EnableOutputCaching=$($env:PerformanceFeatures__EnableOutputCaching)" -ForegroundColor Gray
    Write-Host "  PerformanceFeatures__EnableObjectPooling=$($env:PerformanceFeatures__EnableObjectPooling)" -ForegroundColor Gray
    Write-Host "  PerformanceFeatures__EnableStreaming=$($env:PerformanceFeatures__EnableStreaming)" -ForegroundColor Gray
    Write-Host "  PerformanceFeatures__EnableCompression=$($env:PerformanceFeatures__EnableCompression)" -ForegroundColor Gray
    Write-Host "  PerformanceFeatures__CompressionAlgorithm=$($env:PerformanceFeatures__CompressionAlgorithm)" -ForegroundColor Gray
    
    # -----------------------------
    # Create result folder
    # -----------------------------
    $timestamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
    $resultsDir = Join-Path $RepoRoot "results\${timestamp}_$($config.Name)"
    
    New-Item -ItemType Directory -Path $resultsDir | Out-Null
    
    Write-Host "Results folder: $resultsDir" -ForegroundColor Cyan
    $allResultsFolders += $resultsDir
    
    # -----------------------------
    # Build API
    # -----------------------------
    Write-Host "Building API..." -ForegroundColor Cyan
    dotnet build src/PerformanceLab.Api -c Release
    
    # -----------------------------
    # Start API
    # -----------------------------
    Write-Host "Starting API on port $Port..." -ForegroundColor Cyan
    
    $apiProcess = Start-Process `
        dotnet `
        -ArgumentList "run --project src/PerformanceLab.Api -c Release --urls http://localhost:$Port" `
        -WindowStyle Hidden `
        -PassThru `
        -RedirectStandardOutput "$resultsDir\api.log" `
        -RedirectStandardError "$resultsDir\api-errors.log"
    
    Write-Host "Waiting $WarmupSeconds seconds for API warmup..." -ForegroundColor Gray
    Start-Sleep -Seconds $WarmupSeconds
    
    # -----------------------------
    # Run dotnet-counters
    # -----------------------------
    $apiPid = $apiProcess.Id
    
    Write-Host "Starting performance counters (PID: $apiPid)..." -ForegroundColor Cyan
    
    $countersFile = Join-Path $resultsDir "counters.json"
    
    $counterProcess = Start-Process `
        -FilePath "dotnet" `
        -ArgumentList "tool run dotnet-counters collect --process-id $apiPid --format csv --output `"$resultsDir\counters.csv`" --providers System.Runtime,Microsoft.AspNetCore.Hosting" `
        -PassThru `
        -WindowStyle Hidden `
        -RedirectStandardOutput "$resultsDir\counters-log.txt" `
        -RedirectStandardError "$resultsDir\counters-error.txt"
    
    Write-Host "Counters saving to: $resultsDir\counters.csv" -ForegroundColor Gray
    
    # -----------------------------
    # Run NBomber
    # -----------------------------
    Write-Host "Running load test..." -ForegroundColor Cyan
    
    $nbomberFile = Join-Path $resultsDir "nbomber.txt"
    
    Start-Process `
        -FilePath "dotnet" `
        -ArgumentList "run --project tools/PerformanceLab.LoadTests -c Release" `
        -NoNewWindow `
        -RedirectStandardOutput $nbomberFile `
        -RedirectStandardError "$resultsDir\nbomber-error.txt" `
        -Wait
    
    # -----------------------------
    # Generate experiment report
    # -----------------------------
    $reportFile = Join-Path $resultsDir "experiment.md"
    
    @"
# Experiment Report

## Timestamp
$timestamp

## Configuration
- **Name:** $($config.Name)
- **EnableOutputCaching:** $($config.Cache)
- **EnableObjectPooling:** $($config.Pool)
- **EnableStreaming:** $($config.Stream)
- **EnableCompression:** $($config.Compression)
- **CompressionAlgorithm:** $($config.Algorithm)

## Endpoint
GET /users

## Load Test Output
See nbomber.txt

## Performance Counters
See counters.csv

## Notes
- API run on port $Port
- In-memory dataset (10,000 users)
- .NET Release build

## Next Step
Compare against previous experiment folders in /results
"@ | Out-File -Encoding utf8 $reportFile
    
    # -----------------------------
    # Stop Processes
    # -----------------------------
    Write-Host "Stopping dotnet-counters..." -ForegroundColor Cyan
    Stop-Process -Id $counterProcess.Id -Force -ErrorAction SilentlyContinue
    
    Write-Host "Stopping API..." -ForegroundColor Cyan
    $apiProcess.CloseMainWindow()
    $apiProcess.WaitForExit(5000)  # Wait 5 seconds for graceful exit
    if (!$apiProcess.HasExited) {
        Stop-Process -Id $apiProcess.Id -Force
    }
    
    # -----------------------------
    # Clean up environment variables
    # -----------------------------
    Remove-Item Env:\PerformanceFeatures__EnableOutputCaching -ErrorAction SilentlyContinue
    Remove-Item Env:\PerformanceFeatures__EnableObjectPooling -ErrorAction SilentlyContinue
    Remove-Item Env:\PerformanceFeatures__EnableStreaming -ErrorAction SilentlyContinue
    Remove-Item Env:\PerformanceFeatures__EnableCompression -ErrorAction SilentlyContinue
    Remove-Item Env:\PerformanceFeatures__CompressionAlgorithm -ErrorAction SilentlyContinue
    
    Write-Host "Configuration '$($config.Name)' complete!" -ForegroundColor Green
    
    # Small delay between configurations
    if ($configurations.Count -gt 1) {
        Write-Host "Waiting 2 seconds before next configuration..." -ForegroundColor Gray
        Start-Sleep -Seconds 2
    }
}

# -----------------------------
# Summary
# -----------------------------
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "ALL EXPERIMENTS COMPLETE" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""

if ($configurations.Count -gt 1) {
    Write-Host "Completed $($configurations.Count) configurations:" -ForegroundColor Cyan
    foreach ($folder in $allResultsFolders) {
        $folderName = Split-Path $folder -Leaf
        Write-Host "  - $folderName" -ForegroundColor Gray
    }
    Write-Host ""
    Write-Host "Results saved to:" -ForegroundColor Cyan
    Write-Host "  $($RepoRoot)\results\" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Cyan
    Write-Host "  1. Review nbomber.txt in each results folder for latency metrics" -ForegroundColor Gray
    Write-Host "  2. Review counters.csv for GC and allocation metrics" -ForegroundColor Gray
    Write-Host "  3. Update experiment documentation with results" -ForegroundColor Gray
} else {
    Write-Host "Configuration '$($configurations[0].Name)' completed successfully!" -ForegroundColor Cyan
    Write-Host "Results: $($allResultsFolders[0])" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
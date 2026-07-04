param (
    [int]$Port = 5206
)

$ErrorActionPreference = "Stop"

# -----------------------------
# Setup repo root
# -----------------------------
$RepoRoot = git rev-parse --show-toplevel
Set-Location $RepoRoot

# -----------------------------
# Create result folder
# -----------------------------
$timestamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
$resultsDir = Join-Path $RepoRoot "results\$timestamp"

New-Item -ItemType Directory -Path $resultsDir | Out-Null

Write-Host "Results folder: $resultsDir" -ForegroundColor Cyan

# -----------------------------
# Build API
# -----------------------------
Write-Host "Building API..." -ForegroundColor Cyan
dotnet build src/PerformanceLab.Api -c Release

# -----------------------------
# Start API
# -----------------------------
Write-Host "Starting API..." -ForegroundColor Cyan

$apiProcess = Start-Process `
    dotnet `
    -ArgumentList "run --project src/PerformanceLab.Api -c Release --urls http://localhost:$Port" `
    -PassThru

Start-Sleep -Seconds 3

# -----------------------------
# Run NBomber
# -----------------------------
Write-Host "Running load test..." -ForegroundColor Cyan

$nbomberFile = Join-Path $resultsDir "nbomber.txt"

Start-Process `
    -FilePath "dotnet" `
    -ArgumentList "run --project tools/PerformanceLab.LoadTests" `
    -NoNewWindow `
    -RedirectStandardOutput $nbomberFile `
    -RedirectStandardError "$resultsDir\nbomber-error.txt" `
    -Wait

# -----------------------------
# Generate simple experiment report
# -----------------------------
$reportFile = Join-Path $resultsDir "experiment.md"

@"
# Experiment Report

## Timestamp
$timestamp

## Endpoint
GET /users

## Load Test Output
See nbomber.txt

## Notes
- API run on port $Port
- In-memory dataset (10,000 users)
- .NET Release build

## Next Step
Compare against previous experiment folder in /results
"@ | Out-File -Encoding utf8 $reportFile

# -----------------------------
# Stop API
# -----------------------------
Write-Host "Stopping API..." -ForegroundColor Cyan
Stop-Process -Id $apiProcess.Id -Force

Write-Host "Done." -ForegroundColor Green
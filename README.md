# performance-lab-dotnet

An ASP.NET Core performance and profiling lab for measuring runtime behavior, GC activity, allocation pressure, and throughput under controlled load.

## Current State

The solution is structured as a small layered .NET 10 app:

- `src/PerformanceLab.Api` hosts the HTTP API and Swagger in Development.
- `src/PerformanceLab.Application` contains the user service and DTO mapping.
- `src/PerformanceLab.Domain` contains the core `User` entity.
- `src/PerformanceLab.Infrastructure` provides the in-memory user repository.
- `src/PerformanceLab.Shared` is currently available for shared types and utilities.
- `tools/PerformanceLab.LoadTests` contains the NBomber load test harness.

The API currently exposes a single endpoint:

- `GET /users` returns 10,000 in-memory users projected to `Id` and `Name`.

## Project Layout

- `PerformanceLab.slnx` - solution file
- `dotnet-tools.json` - local tool manifest for `dotnet-counters` and `dotnet-trace`
- `docs/baseline-v1.md` - initial steady-state baseline for `GET /users`
- `reports/` - load-test outputs and captured reports

## Running The API

The API project targets `net10.0` and uses the standard development ports from `launchSettings.json`:

- HTTP: `http://localhost:5206`
- HTTPS: `https://localhost:7262`

Swagger is enabled in Development.

```powershell
dotnet run --project src/PerformanceLab.Api/PerformanceLab.Api.csproj
```

## Running The Load Test

The load-test project uses NBomber and targets the local API at `http://localhost:5206/users`.

```powershell
dotnet run --project tools/PerformanceLab.LoadTests/PerformanceLab.LoadTests.csproj
```

The baseline scenario currently runs at 50 requests per second for 1 minute.

## Diagnostics

The repo includes local tool definitions for:

- `dotnet-counters`
- `dotnet-trace`

These are used alongside the load tests to inspect GC and runtime behavior while the endpoint is under pressure.

## Baseline Notes

The initial performance baseline for the current implementation is documented in `docs/baseline-v1.md`.

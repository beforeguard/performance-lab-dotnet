# Experiment 002 – Capacity Curve (Load Scaling Test)

## Objective

Identify the performance limits of the `GET /users` endpoint by gradually increasing load and observing:

- Throughput scaling behavior
- Latency degradation points
- Garbage collection pressure
- CPU saturation behavior

This experiment moves beyond steady-state measurement and explores **system capacity and bottlenecks**.

---

## Hypothesis

The application will sustain linear throughput growth up to a certain load threshold, after which:

- Latency (especially p95/p99) will increase non-linearly
- GC activity will increase due to allocation pressure from DTO mapping and JSON serialization
- CPU will become the primary limiting factor

---

## Environment

| Setting             | Value                |
|---------------------|----------------------|
| Build Configuration | Release              |
| Runtime             | .NET 10              |
| Endpoint            | `GET /users`         |
| Data Source         | In-memory repository |
| Dataset Size        | 10,000 users         |
| Baseline Load Test  | 50 RPS steady-state  |

---

## Load Test Design

Unlike the baseline experiment, this test uses **increasing load levels** to identify saturation points.

### Load Profile Options

#### Option A – Step Load (Recommended for clarity)

- 10 RPS (warm-up)
- 25 RPS
- 50 RPS (baseline reference point)
- 100 RPS
- 200 RPS
- 400 RPS (stress region)

Each step is observed independently for:

- Requests/sec stability
- Latency percentiles
- Error rate
- GC behavior

---

#### Option B – Concurrency-Based Load (Advanced)

- 50 concurrent users
- 100 concurrent users
- 200 concurrent users
- 400 concurrent users

This better simulates real-world traffic contention patterns.

---

## Metrics Captured

### Primary Metrics (NBomber)

- Requests per second (RPS)
- Response latency:
  - p50
  - p95
  - p99
- Failure rate

### Runtime Metrics (`dotnet-counters`)

- CPU utilization
- GC Gen 0 / Gen 1 / Gen 2 collections
- Allocation rate (MB/s)
- Working set memory

### Optional Deep Analysis (`dotnet-trace`)

- Hot methods
- Allocation call stacks
- Serialization cost (`System.Text.Json`)
- LINQ overhead (`Select`, `ToList`)

---

## Expected Behavior

### Phase 1 – Linear Scaling (Low Load)

- Near-constant latency (~2–5 ms)
- Minimal GC activity
- CPU underutilized

---

### Phase 2 – Transition Zone

- p95 latency begins increasing
- GC Gen 0 collections increase
- CPU usage rises steadily
- Slight variance in response time

---

### Phase 3 – Saturation Point

- Latency increases sharply (non-linear growth)
- GC pressure becomes significant
- CPU approaches 90–100%
- Throughput plateaus

---

### Phase 4 – Degradation

- Increased tail latency (p99 spikes)
- Potential request queuing
- Possible memory pressure effects
- System no longer scales linearly

---

## Observations (to be filled during execution)

### Throughput Curve

| Load Level | RPS Achieved | Notes |
|------------|-------------|------|
| 10 RPS     |             |      |
| 25 RPS     |             |      |
| 50 RPS     |             | Baseline reference |
| 100 RPS    |             |      |
| 200 RPS    |             |      |
| 400 RPS    |             |      |

---

### Latency Behavior

| Load Level | p50 | p95 | p99 | Notes |
|------------|-----|-----|-----|------|
| 10 RPS     |     |     |     |      |
| 50 RPS     |     |     |     | Baseline |
| 100 RPS    |     |     |     |      |
| 200 RPS    |     |     |     |      |

---

### Runtime Behavior Notes

- GC behavior changes observed:
- CPU saturation point:
- Allocation rate trends:
- Any unexpected spikes or anomalies:

---

## Key Questions to Answer

1. At what load does latency stop scaling linearly?
2. Is GC the first limiting factor or CPU?
3. Does DTO allocation dominate runtime cost?
4. Does serialization become a bottleneck at higher payload pressure?
5. What is the maximum sustainable throughput?

---

## Conclusion (to be completed after test)

- Identified system saturation point at: ___ RPS
- Primary bottleneck: (CPU / GC / Allocation / Serialization)
- Latency degradation onset: ___ RPS
- Notes on scalability characteristics:

---

## Next Experiment

Based on findings:

- Optimize DTO mapping (manual vs LINQ)
- Reduce allocations in hot path
- Investigate serialization improvements
- Re-run baseline comparison (Experiment 003)
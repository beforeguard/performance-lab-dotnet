# Experiment 001 – Steady-State Performance

## Objective

Establish an initial performance baseline for the `GET /users` endpoint before any optimization work.

This benchmark serves as the reference point for future performance experiments.

---

## Environment

| Setting             | Value                |
| ------------------- | -------------------- |
| Build Configuration | Release              |
| Runtime             | .NET 10              |
| Endpoint            | `GET /users`         |
| Data Source         | In-memory repository |
| Dataset Size        | 10,000 users         |

---

## Load Test Configuration

**Tool:** NBomber

### Scenario

* Duration: 60 seconds
* Injection Rate: 50 requests/second
* Total Requests: 3,000

This benchmark measures steady-state performance at a fixed request rate. It is not intended to determine the maximum throughput of the application.

---

## Results

### Request Statistics

| Metric              | Value |
| ------------------- | ----: |
| Total Requests      | 3,000 |
| Successful Requests | 3,000 |
| Failed Requests     |     0 |
| Requests/Second     |    50 |

### Latency

| Percentile |     Value |
| ---------- | --------: |
| Minimum    |   1.72 ms |
| Mean       |   2.88 ms |
| p50        |   2.26 ms |
| p75        |   2.48 ms |
| p95        |   3.36 ms |
| p99        |   7.42 ms |
| Maximum    | 206.26 ms |

---

## Observations

* All requests completed successfully.
* Average latency remained below 3 ms.
* 99% of requests completed within 7.42 ms.
* A single high-latency outlier (206.26 ms) was observed. This may be attributable to runtime initialization, garbage collection, or operating system scheduling and will be investigated if it persists in future benchmarks.

---

## Runtime Diagnostics

Runtime diagnostics were captured using:

* `dotnet-counters`
* `dotnet-trace`

Initial inspection indicated garbage collection activity during execution. This is expected given the endpoint currently allocates a new DTO for each user and serializes the complete collection on every request.

Further investigation will be performed in later milestones using trace analysis.

---

## Conclusions

The application successfully sustains a steady load of **50 requests per second** with very low latency and no request failures.

This benchmark establishes the initial performance baseline for the project. Future experiments will compare their results against this baseline while changing only a single implementation detail at a time.

---

## Next Experiments

* Measure maximum sustainable throughput using concurrent virtual users.
* Profile allocation pressure during DTO mapping and JSON serialization.
* Compare LINQ projection against manual mapping.
* Measure the effect of reducing allocations on latency and throughput.

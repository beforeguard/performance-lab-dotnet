# Experiment 007: Add Pagination to Reduce Serialization Overhead

**Date:** 2026-07-28  
**Status:** 🔲 Planned  
**Branch:** `experiment/007-pagination`

---

## Objective

Implement optional pagination (offset/limit query parameters) and establish the **relationship between response size and latency**. Determine optimal page sizes for different use cases by measuring:

1. **Scalability curve** - How latency scales with response size (linear, exponential, fixed overhead)
2. **Per-user cost** - Cost per user returned across different page sizes
3. **Throughput impact** - Server capacity with small vs large responses
4. **Cache efficiency** - Cache fragmentation vs hit rate tradeoffs

## Hypothesis

**Primary Hypothesis:** Latency scales **linearly** with the number of users returned (serialization is O(n)), with minimal fixed overhead from pagination logic.

**Secondary Hypotheses:**
- Smaller page sizes enable **higher server throughput** (reduced GC pressure, faster serialization per request)
- Cache fragmentation from multiple page entries has **minimal impact** on cache hit rates for realistic access patterns
- **Optimal page size** exists that balances latency, bandwidth, and cache efficiency

**Expected Findings:**
- Response latency is proportional to user count (~0.0018ms per user)
- 100-200 user pages optimal for interactive UIs (balance latency and round-trips)
- 500-1000 user pages optimal for batch operations
- Pagination overhead < 0.01ms (negligible compared to serialization)
- Maintains backward compatibility (no params = all results)

---

## Design Decisions

### 1. Pagination Layer: Repository ✓

**Decision:** Implement pagination at the **repository layer** using `IUserRepository.GetPage(offset, limit)`

**Rationale:**
- **Separation of concerns:** Repository is responsible for data access; pagination is fundamentally data filtering
- **Future-proofing:** Experiment 009 (Database Integration) will translate `GetPage()` to SQL `LIMIT/OFFSET` - avoiding loading 10K rows just to Skip/Take in memory
- **Testability:** Repository pagination is independently testable without service logic
- **Performance:** Skip/Take at repository avoids passing large collections through layers

**Alternative Considered:** Service-layer pagination (GetAll() then Skip/Take)
- **Rejected:** Inefficient for database integration, violates single responsibility principle

---

### 2. Total Count Return: Wrapped Object vs Header

**Decision:** TBD - Two viable options with different tradeoffs

#### Option A: X-Total-Count Header (Simpler)
```http
GET /users?offset=0&limit=100
X-Total-Count: 10000

[{ "id": 1, "name": "..." }, ...]  // Response body unchanged
```

**Pros:**
- ✅ Backward compatible (response body unchanged)
- ✅ Simple implementation
- ✅ Used by GitHub API, Stripe API
- ✅ Client can ignore header if not needed

**Cons:**
- ❌ Not discoverable in OpenAPI/Swagger schemas
- ❌ Not part of JSON response (harder to work with in some client frameworks)
- ❌ Inconsistent with modern REST API trends

#### Option B: Wrapped Object (Industry Standard)
```json
{
  "items": [{ "id": 1, "name": "..." }, ...],
  "total": 10000,
  "offset": 0,
  "limit": 100
}
```

**Pros:**
- ✅ Self-describing response
- ✅ Used by Twitter API, Google APIs, Microsoft Graph API
- ✅ All pagination metadata in one place
- ✅ Shows up in OpenAPI schema
- ✅ Can add more metadata later (hasMore, nextOffset, prevPage, etc.)
- ✅ Follows JSON:API and OData patterns

**Cons:**
- ❌ **BREAKING CHANGE** - response shape changes from `UserDto[]` to wrapped object
- ❌ More complex to implement (need `PagedResult<T>` type)

#### Option C: Conditional Wrapping (Clever Compromise)
```csharp
// No pagination params - backward compatible
GET /users → UserDto[]

// With pagination params - wrapped response
GET /users?offset=0&limit=100 → { items: UserDto[], total: 10000, ... }
```

**Pros:**
- ✅ Maintains backward compatibility
- ✅ Modern wrapped response when paginating

**Cons:**
- ❌ Two different response shapes (violates HTTP content negotiation principles)
- ❌ More complex to implement and document

---

**Recommendation:** Use **Option B (Wrapped Object)** for these reasons:
1. **Industry Standard:** Modern REST APIs overwhelmingly use wrapped objects
2. **Future-Proof:** Easier to add pagination metadata later
3. **Developer Experience:** Self-documenting, shows in Swagger UI
4. **Experiment Context:** This IS an experiment - breaking changes are acceptable before production

**Implementation:**
```csharp
// Add new DTO
public class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; }
    public int Total { get; init; }
    public int Offset { get; init; }
    public int Limit { get; init; }
}

// Controller returns PagedResult when params present
public IActionResult GetUsers([FromQuery] int? offset, [FromQuery] int? limit)
{
    if (limit.HasValue)
    {
        var items = _userService.GetUsers(offset ?? 0, limit.Value);
        var total = _userService.GetCount();
        return Ok(new PagedResult<UserDto> 
        { 
            Items = items, 
            Total = total, 
            Offset = offset ?? 0, 
            Limit = limit.Value 
        });
    }
    
    // Backward compatible - no params returns array
    return Ok(_userService.GetUsers());
}
```

---

### 3. Other Design Decisions

**Backward Compatibility:**
- **No default limit** - omitting parameters returns all 10,000 users (existing behavior preserved)
- Clients must explicitly opt-in to pagination

**Validation:**
- `offset >= 0` (default: 0 if limit specified without offset)
- `limit > 0` (no maximum enforced for experiment)
- Out-of-bounds offset returns empty list (consistent with Skip/Take behavior)

**Caching Strategy:**
- OutputCache will create separate cache entries for each `(offset, limit)` pair
- With 10K users and page size 100 = 100 potential cache entries
- **Accept cache fragmentation** for experiment simplicity (real-world LRU eviction handles this)
- Document cache behavior in findings

---

## Implementation Plan

### Steps

1. **Update IUserRepository interface** _(new GetPage and GetCount methods)_
   - Add `IReadOnlyList<User> GetPage(int offset, int limit)` method signature
   - Add `int GetCount()` method signature for total user count

2. **Implement pagination in UserRepository** _(Skip/Take on in-memory collection)_
   - Implement `GetPage(offset, limit)` using LINQ Skip/Take on `_users` list
   - Implement `GetCount()` returning `_users.Count`
   - Add validation: offset >= 0, limit > 0

3. **Update UserService to support pagination** _(conditional GetPage vs GetAll)_
   - Add optional `offset` and `limit` parameters to `GetUsers()` method signature
   - When limit is specified: call `_repository.GetPage(offset, limit)`
   - When limit is null: call `_repository.GetAll()` (backward compatible)
   - Maintain existing pooling/streaming feature flag logic for both paths
   - Add `int GetCount()` method that returns `_repository.GetCount()`

4. **Create PagedResult<T> DTO** _(if using wrapped object approach)_
   - Add `PagedResult<T>` class in `src/PerformanceLab.Shared/DTOs/`
   - Properties: `Items`, `Total`, `Offset`, `Limit`

5. **Update UsersController with query parameters** _(offset, limit, conditional response)_
   - Add `[FromQuery] int? offset = null, [FromQuery] int? limit = null` to GetUsers action
   - If `limit.HasValue`: return `PagedResult<UserDto>` with metadata
   - If `limit == null`: return `UserDto[]` (backward compatible)
   - Validate: if limit specified, offset defaults to 0

6. **Update load test scenarios** _(6 test cases for scalability curve + throughput tests)_
   - Add scalability curve scenarios:
     - `Paginated10()`: `/users?offset=0&limit=10`
     - `Paginated50()`: `/users?offset=0&limit=50`
     - `Paginated100()`: `/users?offset=0&limit=100`
     - `Paginated500()`: `/users?offset=0&limit=500`
     - `Paginated1000()`: `/users?offset=0&limit=1000`
   - Keep existing `Baseline()`: `/users` (no params, all 10,000 users)
   - Standard load: 50 RPS for 60 seconds per scenario
   - Add throughput test: Capacity curve (increasing load) for 100-user vs 10K-user responses
   - Update response parsing to handle both `UserDto[]` and `PagedResult<UserDto>`

7. **Update run-experiment.ps1** _(add -PageSize parameter)_
   - Add `-PageSize` parameter accepting: "10", "50", "100", "500", "1000", "all", "curve", "throughput"
   - Map PageSize to scenario selection in load test execution
   - "curve" mode: run all 6 scenarios sequentially to generate latency curve data
   - "throughput" mode: run capacity tests comparing 100-user vs 10K-user responses
   - Update result folder naming: `results/{timestamp}_pagination_{pagesize}/`

8. **Validate implementation** _(verify behavior and compatibility)_
   - Test backward compatibility: `/users` returns all 10,000 users as `UserDto[]`
   - Test pagination: `/users?offset=0&limit=100` returns `PagedResult` with 100 users
   - Test validation: negative offset/limit returns 400 Bad Request
   - Test out-of-bounds: offset >= 10000 returns empty items array
   - Test OutputCache generates separate cache entries per (offset, limit) combo
   - Test ArrayPool works with paginated results (smaller allocations)

9. **Run baseline and treatment measurements** _(3 measurement types)_
   
   **A. Scalability Curve (Primary):**
   - Run 6 scenarios: 10, 50, 100, 500, 1000, 10000 users
   - Capture: latency, response size, latency per user
   - Plot: latency vs user count (verify linear relationship)
   
   **B. Throughput Test (Secondary):**
   - Run capacity curve tests (Experiment 002 pattern)
   - Compare: 100-user responses vs 10K-user responses
   - Measure: max sustainable RPS before errors/latency spike
   
   **C. Cache Efficiency (Tertiary):**
   - Monitor cache hit rates across different page sizes
   - Measure: cache memory usage, hit rate, entry count
   
   All tests run with ArrayPool + OutputCache + Brotli enabled (Experiment 006 baseline)

10. **Document findings** _(analyze results, make recommendations)_
    - Plot latency vs user count curve (verify linear scaling, calculate per-user cost)
    - Calculate pagination overhead (10-user response vs theoretical minimum)
    - Compare throughput capacity (max RPS for small vs large responses)
    - Analyze cache hit rates and memory usage across page sizes
    - Recommend optimal page sizes by use case:
      - Interactive UIs (minimize latency + round-trips)
      - Batch operations (maximize throughput)
      - Export operations (minimize total requests)
    - Document that fetching ALL data without pagination is faster (but rarely needed)
    - Update `performance-experiments-tracking.md` with results summary

---

## Files to Modify

| File | Changes |
|------|---------|
| [src/PerformanceLab.Application/Users/Abstractions/IUserRepository.cs](../src/PerformanceLab.Application/Users/Abstractions/IUserRepository.cs) | Add `GetPage(offset, limit)` and `GetCount()` signatures |
| [src/PerformanceLab.Infrastructure/Users/UserRepository.cs](../src/PerformanceLab.Infrastructure/Users/UserRepository.cs) | Implement pagination with Skip/Take on in-memory `_users` list |
| [src/PerformanceLab.Application/Users/UserService.cs](../src/PerformanceLab.Application/Users/UserService.cs) | Add offset/limit parameters to `GetUsers()`, call `GetPage` conditionally, add `GetCount()` method |
| [src/PerformanceLab.Shared/DTOs/PagedResult.cs](../src/PerformanceLab.Shared/DTOs/PagedResult.cs) | **NEW FILE** - Generic wrapper for paginated responses |
| [src/PerformanceLab.Api/Controllers/UsersController.cs](../src/PerformanceLab.Api/Controllers/UsersController.cs) | Add query parameters `[FromQuery] offset/limit`, return `PagedResult` or `UserDto[]` conditionally |
| [tools/PerformanceLab.LoadTests/Scenarios/UserScenarios.cs](../tools/PerformanceLab.LoadTests/Scenarios/UserScenarios.cs) | Add 3 new pagination scenarios (50, 100, 500 page sizes), update response parsing |
| [scripts/run-experiment.ps1](../scripts/run-experiment.ps1) | Add `-PageSize` parameter for scenario selection, update result folder naming |

---

## Test Scenarios

### A. Scalability Curve Scenarios (Primary)

**Purpose:** Measure how latency scales with response size to find optimal page sizes.

#### Scenario 1: Minimal (10 users)
```http
GET /users?offset=0&limit=10
Response: PagedResult<UserDto> { Items: 10, Total: 10000, ... }
```
- **Purpose:** Measure pagination overhead (fixed costs)
- **Expected:** ~0.02ms (minimal serialization + fixed overhead)

#### Scenario 2: Small (50 users)
```http
GET /users?offset=0&limit=50
Response: PagedResult<UserDto> { Items: 50, Total: 10000, ... }
```
- **Purpose:** Dashboard/UI use case
- **Expected:** ~0.09ms

#### Scenario 3: Medium (100 users)
```http
GET /users?offset=0&limit=100
Response: PagedResult<UserDto> { Items: 100, Total: 10000, ... }
```
- **Purpose:** Standard page size for interactive UIs
- **Expected:** ~0.18ms

#### Scenario 4: Large (500 users)
```http
GET /users?offset=0&limit=500
Response: PagedResult<UserDto> { Items: 500, Total: 10000, ... }
```
- **Purpose:** Batch operations
- **Expected:** ~0.9ms

#### Scenario 5: Extra Large (1000 users)
```http
GET /users?offset=0&limit=1000
Response: PagedResult<UserDto> { Items: 1000, Total: 10000, ... }
```
- **Purpose:** Large batch operations
- **Expected:** ~1.8ms

#### Scenario 6: Baseline (All 10,000 users)
```http
GET /users
Response: UserDto[] (10,000 items)
```
- **Purpose:** Full dataset, no pagination
- **Expected:** ~18ms (but faster than 100 paginated requests)

**Load Pattern:**
- **Rate:** 50 requests/sec
- **Duration:** 60 seconds
- **Warmup:** 5 seconds

### B. Throughput Capacity Test (Secondary)

**Purpose:** Measure server capacity with different response sizes.

#### Scenario 7: Capacity - Small Responses
```http
GET /users?offset=0&limit=100
```
- **Load Pattern:** Capacity curve (50, 100, 150, 200, 250 RPS)
- **Expected:** Higher max RPS than large responses (less GC, faster serialization)

#### Scenario 8: Capacity - Large Responses
```http
GET /users
```
- **Load Pattern:** Capacity curve (50, 100, 150, 200, 250 RPS)
- **Expected:** Lower max RPS (GC pressure, slower serialization)

### C. Cache Efficiency Test (Tertiary)

**Purpose:** Measure cache fragmentation vs hit rate.

#### Scenario 9: Cache Behavior - Paginated
- **Pattern:** Random page requests (offset 0-9900 in increments of 100, limit=100)
- **Measure:** Cache hit rate, entry count, memory usage
- **Expected:** ~80% hit rate (Zipf distribution), 100 cache entries, ~3MB memory

#### Scenario 10: Cache Behavior - Baseline
- **Pattern:** All requests to `/users` (no params)
- **Measure:** Cache hit rate, entry count, memory usage
- **Expected:** ~99.98% hit rate, 1 cache entry, ~300KB memory

### Load Configuration (All Tests)
- **Features Enabled:** ArrayPool + OutputCache + Brotli (Exp 006 baseline)
- **Warmup:** 5 seconds to populate cache
- **Duration:** 60 seconds measurement window

---

## Verification Checklist

### Unit Tests
- [ ] Repository `GetPage(100, 50)` returns users 100-149
- [ ] Repository `GetPage(0, 50)` returns users 0-49
- [ ] Repository `GetPage(9999, 10)` returns 1 user
- [ ] Repository `GetPage(10000, 10)` returns empty list
- [ ] Repository `GetCount()` returns 10000

### Integration Tests
- [ ] GET `/users?offset=0&limit=100` returns `PagedResult` with exactly 100 items
- [ ] GET `/users?offset=0&limit=100` includes `Total: 10000`
- [ ] GET `/users?offset=9950&limit=100` returns 50 items (partial page)
- [ ] GET `/users?offset=10000&limit=100` returns 0 items
- [ ] GET `/users` (no params) returns `UserDto[]` with 10,000 items (backward compatible)
- [ ] GET `/users?offset=-1&limit=100` returns 400 Bad Request
- [ ] GET `/users?offset=0&limit=0` returns 400 Bad Request

### Feature Integration Tests
- [ ] OutputCache creates separate cache entries for different `(offset, limit)` combinations
- [ ] OutputCache hits for repeated requests with same `(offset, limit)`
- [ ] ArrayPool works with paginated results (smaller array rentals logged)
- [ ] Brotli compression applies to `PagedResult` response
- [ ] Response headers include `X-Pooling-Enabled`, `X-Caching-Enabled`

### Load Test Validation
- [ ] Run all 4 scenarios successfully
- [ ] NBomber reports show latency reduction for paginated scenarios
- [ ] Response size metrics show bandwidth reduction
- [ ] No errors or timeouts at 50 RPS
- [ ] GC collection counts remain low (OutputCache working)

---

## Expected Results

### A. Scalability Curve (Primary Finding)

**Hypothesis:** Latency scales linearly with user count (serialization is O(n))

| Users | Mean Latency | Latency/User | Response Size | Brotli Compressed | Use Case |
|------:|-------------:|-------------:|--------------:|------------------:|-----------|
| 10 | ~0.02ms | 0.002ms | ~0.3 KB | ~0.16 KB | Minimal |
| 50 | ~0.09ms | 0.0018ms | ~1.5 KB | ~0.8 KB | Dashboard |
| 100 | ~0.18ms | 0.0018ms | ~3.1 KB | ~1.6 KB | ⭐ UI Optimal |
| 500 | ~0.9ms | 0.0018ms | ~15.4 KB | ~8 KB | Batch |
| 1000 | ~1.8ms | 0.0018ms | ~30.7 KB | ~16 KB | Large Batch |
| 10000 | ~18ms | 0.0018ms | ~307 KB | ~160 KB | Full Export |

**Key Findings:**
- **Linear scaling:** Latency ≈ 0.0018ms × user_count (serialization dominates)
- **Fixed overhead:** ~0.002ms (pagination logic, HTTP overhead)
- **Pagination overhead:** < 0.01ms (negligible)
- **Per-request comparison:** 1 request for 10K users (18ms) is **10x faster** than 100 requests for 100 users each (18ms + network overhead)
- **Appropriate sizing:** Clients should request only what they need, not "all data then paginate client-side"

### B. Throughput Capacity (Secondary Finding)

**Hypothesis:** Smaller responses enable higher throughput

| Scenario | Response Size | Max Sustainable RPS | Saturation Point | GC Pressure |
|----------|---------------|--------------------:|-----------------:|-------------|
| 10K users | ~160 KB | ~200 RPS | GC Gen 2 | High |
| 100 users | ~1.6 KB | ~500+ RPS | CPU bound | Low |

**Key Findings:**
- Smaller pages reduce GC pressure → higher throughput
- Server can handle **2.5x more requests/sec** with 100-user pages vs 10K-user responses
- Saturation shifts from GC bottleneck to CPU-bound serialization

### C. Cache Efficiency (Tertiary Finding)

**Hypothesis:** Cache fragmentation has minimal impact on hit rates

| Scenario | Cache Entries | Memory Usage | Hit Rate | Benefit |
|----------|---------------:|-------------:|---------:|---------|
| No pagination | 1 | ~300 KB | 99.98% | Single entry, perfect hits |
| Paginated (100/page) | ~100 | ~3 MB | ~80% | Zipf distribution, acceptable |

**Key Findings:**
- Cache fragmentation creates ~100 entries instead of 1
- Memory usage increases ~10x (3MB vs 300KB) but still trivial
- Hit rate drops to ~80% due to Zipf distribution (popular pages cached, cold pages evicted)
- **Trade-off acceptable:** Cache efficiency slightly lower, but enables appropriate sizing

### D. Use-Case Recommendations

| Use Case | Recommended Page Size | Rationale |
|----------|-----------------------|-----------|
| **Interactive UI** | 100-200 users | Balance latency (0.18-0.36ms) and round-trips |
| **Dashboard** | 50 users | Minimize initial load time (~0.09ms) |
| **Batch Operations** | 500-1000 users | Reduce round-trips while keeping latency < 2ms |
| **Full Export** | No pagination | Fastest total time for fetching all data |
| **Infinite Scroll** | 50-100 users | Progressive loading, low memory footprint |

### E. Important Clarifications

**What This Experiment DOES NOT Show:**
- ✗ "Pagination makes the API faster" (misleading - depends on use case)
- ✗ "Always use pagination" (fetching all data without pagination is faster if you need all data)

**What This Experiment DOES Show:**
- ✓ Latency scales linearly with data size (0.0018ms per user)
- ✓ Pagination overhead is negligible (< 0.01ms)
- ✓ Appropriate sizing (requesting only what's needed) is the real optimization
- ✓ Smaller responses enable higher server throughput
- ✓ Optimal page size: 100-200 users for most interactive use cases

*(Note: These are estimates - actual results will be measured and documented)*

---

## Success Criteria

1. **Linear Scaling Verified:** Latency vs user count shows linear relationship (R² > 0.95)
2. **Per-User Cost Measured:** Calculate cost per user (expected: ~0.0018ms per user)
3. **Pagination Overhead Measured:** Fixed overhead < 0.01ms (negligible)
4. **Throughput Improvement:** Smaller pages (100 users) sustain ≥2x RPS vs large pages (10K users)
5. **Backward Compatibility:** `/users` (no params) continues to return all 10,000 users as `UserDto[]`
6. **No Errors:** 100% success rate across all scenarios at 50 RPS
7. **Cache Compatibility:** OutputCache creates separate entries per `(offset, limit)`, all cached responses valid
8. **Pool Compatibility:** ArrayPool allocations scale proportionally with page size
9. **Optimal Page Size Identified:** Recommend specific page sizes for UI, batch, and export use cases
10. **Honest Documentation:** Clearly state that fetching all data without pagination is faster (but rarely the right solution)

---

## Future Considerations

### 1. Cache Key Strategy
OutputCache creates separate entries for each `(offset, limit)` pair. With 10K users and page size 100 = 100 potential cache entries.

**Options:**
- **Accept fragmentation** (current plan) - Real-world LRU eviction handles this naturally
- **Tag-based invalidation** (future experiment) - Invalidate all pages when data changes
- **Single cache entry + slice** (complex) - Cache full dataset, slice in middleware

**Recommendation for Exp 007:** Accept fragmentation, document behavior in findings.

### 2. Page Size Limits
Should we enforce maximum page size (e.g., 1000) to prevent abuse?

**Recommendation for Exp 007:** No limits (trust model). Document recommended max based on latency thresholds in findings.

### 3. Out-of-Bounds Handling
If `offset >= total count`, return empty list or 416 Range Not Satisfiable?

**Recommendation for Exp 007:** Return empty list (consistent with Skip/Take behavior). Simpler for clients.

### 4. Additional Metadata
Future enhancements to `PagedResult<T>`:
- `HasMore: bool` - convenience flag
- `NextOffset: int?` - pre-calculated next page offset
- `PrevOffset: int?` - pre-calculated previous page offset
- `PageCount: int` - total number of pages

**Recommendation for Exp 007:** Keep simple. Add metadata in future experiment if needed.

### 5. Cursor-Based Pagination
For large datasets with concurrent modifications, cursor-based pagination is more reliable:
```http
GET /users?cursor=abc123&limit=100
Response: { items: [], nextCursor: "def456" }
```

**Recommendation for Exp 007:** Stick with offset/limit. Cursor-based adds complexity and is better suited for Experiment 009 (Database Integration).

---

## Notes

- This experiment builds on Experiment 006 (ArrayPool + OutputCache + Brotli)
- Pagination is a prerequisite for Experiment 008 (Async Repository) and 009 (Database Integration)
- The wrapped object approach aligns with future API versioning strategies
- Cache fragmentation behavior will inform cache invalidation strategies in future experiments

---

## Status Log

- **2026-07-28:** Experiment planned, branch created, design decisions documented

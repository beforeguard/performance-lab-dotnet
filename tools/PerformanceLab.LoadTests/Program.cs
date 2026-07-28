using NBomber.CSharp;
using PerformanceLab.LoadTests.Scenarios;
using PerformanceLab.LoadTests.Metrics;

// Note: TTFB values are captured in scenario responses via X-TTFB-Ms header
// and tracked as payload. NBomber will report standard latency metrics.
// For TTFB analysis, inspect the X-TTFB-Ms header values or add custom reporting.

// Experiment 007: Pagination Scalability Curve
// Runs all page sizes sequentially to measure latency vs response size relationship
// Results will show how latency scales with the number of users returned

var stats = NBomberRunner
    .RegisterScenarios(
        UsersScenarios.Baseline(),               // All 10,000 users (no pagination)
        UsersScenarios.Paginated(10),            // 10 users per page
        UsersScenarios.Paginated(50),            // 50 users per page
        UsersScenarios.Paginated(100),           // 100 users per page
        UsersScenarios.Paginated(500),           // 500 users per page
        UsersScenarios.Paginated(1000),          // 1,000 users per page
        UsersScenarios.CapacityCurve(),          // Throughput test - all users
        UsersScenarios.CapacityCurvePaginated()  // Throughput test - 100 users/page (default)
    )
    .Run();

// Write TTFB report
var reportDir = Path.Combine(Directory.GetCurrentDirectory(), "reports");
Directory.CreateDirectory(reportDir);

var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
var ttfbReportPath = Path.Combine(reportDir, $"ttfb_report_{timestamp}.md");
var sizeReportPath = Path.Combine(reportDir, $"response_size_report_{timestamp}.md");

TtfbTracker.WriteReport(ttfbReportPath);
ResponseSizeTracker.WriteReport(sizeReportPath);

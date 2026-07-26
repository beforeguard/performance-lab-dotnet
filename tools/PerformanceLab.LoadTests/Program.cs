using NBomber.CSharp;
using PerformanceLab.LoadTests.Scenarios;
using PerformanceLab.LoadTests.Metrics;

// Note: TTFB values are captured in scenario responses via X-TTFB-Ms header
// and tracked as payload. NBomber will report standard latency metrics.
// For TTFB analysis, inspect the X-TTFB-Ms header values or add custom reporting.

var stats = NBomberRunner
    .RegisterScenarios(
        UsersScenarios.Baseline(),
        UsersScenarios.CapacityCurve()
    )
    .Run();

// Write TTFB report
var reportDir = Path.Combine(Directory.GetCurrentDirectory(), "reports");
Directory.CreateDirectory(reportDir);

var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
var ttfbReportPath = Path.Combine(reportDir, $"ttfb_report_{timestamp}.md");

TtfbTracker.WriteReport(ttfbReportPath);

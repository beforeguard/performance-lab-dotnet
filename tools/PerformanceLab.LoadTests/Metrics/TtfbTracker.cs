using System.Collections.Concurrent;

namespace PerformanceLab.LoadTests.Metrics;

public static class TtfbTracker
{
    private static readonly ConcurrentBag<double> _ttfbValues = new();
    
    public static void Record(double ttfbMs)
    {
        _ttfbValues.Add(ttfbMs);
    }
    
    public static void WriteReport(string outputPath)
    {
        var values = _ttfbValues.ToArray();
        
        if (values.Length == 0)
        {
            Console.WriteLine("⚠️  No TTFB values recorded");
            return;
        }
        
        Array.Sort(values);
        
        var count = values.Length;
        var min = values[0];
        var max = values[count - 1];
        var mean = values.Average();
        var p50 = GetPercentile(values, 50);
        var p75 = GetPercentile(values, 75);
        var p95 = GetPercentile(values, 95);
        var p99 = GetPercentile(values, 99);
        
        var report = $@"# TTFB Metrics Report

## Summary
- **Count**: {count:N0} requests
- **Min**: {min:F2} ms
- **Mean**: {mean:F2} ms
- **Max**: {max:F2} ms

## Percentiles
- **p50**: {p50:F2} ms
- **p75**: {p75:F2} ms
- **p95**: {p95:F2} ms
- **p99**: {p99:F2} ms

## Raw Data (sample of first 100)
{string.Join(", ", values.Take(100).Select(v => $"{v:F2}"))}
";
        
        File.WriteAllText(outputPath, report);
        
        Console.WriteLine($"\n✅ TTFB Report written to: {outputPath}");
        Console.WriteLine($"   Mean TTFB: {mean:F2} ms | p95: {p95:F2} ms | p99: {p99:F2} ms\n");
    }
    
    private static double GetPercentile(double[] sortedValues, int percentile)
    {
        var index = (int)Math.Ceiling(sortedValues.Length * percentile / 100.0) - 1;
        return sortedValues[Math.Max(0, Math.Min(index, sortedValues.Length - 1))];
    }
    
    public static void Clear()
    {
        _ttfbValues.Clear();
    }
}

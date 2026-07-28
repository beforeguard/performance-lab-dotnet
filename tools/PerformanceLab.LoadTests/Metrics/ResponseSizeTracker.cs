using System.Collections.Concurrent;

namespace PerformanceLab.LoadTests.Metrics;

public static class ResponseSizeTracker
{
    private static readonly ConcurrentBag<long> _responseSizes = new();
    private static readonly ConcurrentBag<string> _compressionTypes = new();
    
    public static void Record(long sizeBytes, string compressionType)
    {
        _responseSizes.Add(sizeBytes);
        _compressionTypes.Add(compressionType);
    }
    
    public static void WriteReport(string outputPath)
    {
        var sizes = _responseSizes.ToArray();
        var types = _compressionTypes.ToArray();
        
        if (sizes.Length == 0)
        {
            Console.WriteLine("⚠️  No response size values recorded");
            return;
        }
        
        Array.Sort(sizes);
        
        var count = sizes.Length;
        var min = sizes[0];
        var max = sizes[count - 1];
        var mean = sizes.Average();
        var total = sizes.Sum();
        
        // Count compression types
        var compressionCounts = types
            .GroupBy(t => t)
            .OrderByDescending(g => g.Count())
            .Select(g => $"{g.Key}: {g.Count():N0} ({(g.Count() * 100.0 / count):F2}%)")
            .ToList();
        
        var report = $@"# Response Size Metrics Report

## Summary
- **Count**: {count:N0} responses
- **Min Size**: {min:N0} bytes ({FormatBytes(min)})
- **Mean Size**: {mean:F0} bytes ({FormatBytes((long)mean)})
- **Max Size**: {max:N0} bytes ({FormatBytes(max)})
- **Total Data**: {total:N0} bytes ({FormatBytes(total)})

## Compression Distribution
{string.Join("\n", compressionCounts)}

## Size Distribution
- **< 10 KB**: {sizes.Count(s => s < 10 * 1024):N0}
- **10-50 KB**: {sizes.Count(s => s >= 10 * 1024 && s < 50 * 1024):N0}
- **50-100 KB**: {sizes.Count(s => s >= 50 * 1024 && s < 100 * 1024):N0}
- **100-200 KB**: {sizes.Count(s => s >= 100 * 1024 && s < 200 * 1024):N0}
- **> 200 KB**: {sizes.Count(s => s >= 200 * 1024):N0}
";
        
        File.WriteAllText(outputPath, report);
        
        Console.WriteLine($"\n✅ Response Size Report written to: {outputPath}");
        Console.WriteLine($"   Mean Size: {FormatBytes((long)mean)} | Total: {FormatBytes(total)}\n");
    }
    
    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        else if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:F2} KB";
        else
            return $"{bytes / (1024.0 * 1024.0):F2} MB";
    }
    
    public static void Clear()
    {
        _responseSizes.Clear();
        _compressionTypes.Clear();
    }
}

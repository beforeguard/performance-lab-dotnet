namespace PerformanceLab.Shared.Configuration;

public enum CompressionAlgorithm
{
    None,
    Gzip,
    Brotli,
    Both  // Allow ASP.NET to negotiate based on Accept-Encoding
}

public class PerformanceFeatures
{
    public bool EnableOutputCaching { get; set; }
    public bool EnableObjectPooling { get; set; }
    public bool EnableStreaming { get; set; }
    public bool EnableCompression { get; set; }
    public CompressionAlgorithm CompressionAlgorithm { get; set; } = CompressionAlgorithm.Brotli;
    public int CacheDurationSeconds { get; set; } = 60;
}

namespace PerformanceLab.Api.Configuration;

public class PerformanceFeatures
{
    public bool EnableOutputCaching { get; set; }
    public bool EnableObjectPooling { get; set; }
    public int CacheDurationSeconds { get; set; } = 60;
}

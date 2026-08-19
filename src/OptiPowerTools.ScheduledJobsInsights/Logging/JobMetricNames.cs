namespace OptiPowerTools.ScheduledJobsInsights.Logging;

/// <summary>
/// Well-known names for the metrics <see cref="LoggedScheduledJobBase"/> records automatically for every execution.
/// </summary>
internal static class JobMetricNames
{
    public const string DurationMs = "DurationMs";
    public const string AllocatedBytes = "AllocatedBytes";
    public const string CpuTimeMs = "CpuTimeMs";
    public const string GcGen0Collections = "GcGen0Collections";
    public const string GcGen1Collections = "GcGen1Collections";
    public const string GcGen2Collections = "GcGen2Collections";
}

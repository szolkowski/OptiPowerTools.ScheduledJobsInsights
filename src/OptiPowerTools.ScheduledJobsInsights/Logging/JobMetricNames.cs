namespace OptiPowerTools.ScheduledJobsInsights.Logging;

/// <summary>
/// Well-known names for the metrics <see cref="LoggedScheduledJobBase"/> records automatically for every execution.
/// </summary>
internal static class JobMetricNames
{
    public const string DurationMs = "DurationMs";

    /// <summary>
    /// Bytes allocated on the job's own thread while it ran.
    /// </summary>
    /// <remarks>
    /// Named for its scope on purpose. <c>GC.GetAllocatedBytesForCurrentThread()</c> counts one
    /// thread, so a job that fans work out to the thread pool or awaits its way onto another thread
    /// under-reports — and the delta can even come out negative if the job resumes on a different
    /// thread than it started on. Called <c>AllocatedBytes</c>, a reader would take it for the run's
    /// total; called this, the number means what it says.
    /// </remarks>
    public const string ThreadAllocatedBytes = "ThreadAllocatedBytes";

    /// <summary>
    /// CPU time consumed by the whole process during the job's wall-clock window.
    /// </summary>
    /// <remarks>
    /// <c>Process.TotalProcessorTime</c> is process-wide, so on a CMS serving requests this includes
    /// everything else the application did while the job ran, and on a multi-core host it can exceed
    /// the job's own duration. It was called <c>CpuTimeMs</c>, which an administrator would reasonably
    /// read as the job's cost and act on. Per-job CPU is not something this package can measure, so
    /// the honest fix is to say whose CPU it is rather than to imply one it cannot.
    /// </remarks>
    public const string ProcessCpuTimeMs = "ProcessCpuTimeMs";
    public const string GcGen0Collections = "GcGen0Collections";
    public const string GcGen1Collections = "GcGen1Collections";
    public const string GcGen2Collections = "GcGen2Collections";

    /// <summary>Recorded by the cleanup job for the number of executions it removed.</summary>
    public const string ExecutionsDeleted = "ExecutionsDeleted";

    /// <summary>Recorded by the cleanup job for unfinished executions it gave up on.</summary>
    public const string ExecutionsMarkedInterrupted = "ExecutionsMarkedInterrupted";
}

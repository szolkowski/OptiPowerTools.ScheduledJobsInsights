namespace OptiPowerTools.ScheduledJobsInsights.Logging;

/// <summary>
/// Well-known names for the metrics <see cref="LoggedScheduledJobBase"/> records automatically for
/// every execution, and for those the cleanup job records for itself.
/// </summary>
/// <remarks>
/// Public because these names are a data contract, not an implementation detail: they are written to
/// the <c>Name</c> column of the metrics table and rendered on the execution detail page, so anything
/// querying that table or building an alert on it has to match them. Naming them here is what stops
/// that being a hard-coded string on the consumer's side.
/// </remarks>
public static class JobMetricNames
{
    /// <summary>Wall-clock duration of the run, in milliseconds.</summary>
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
    /// <summary>Gen 0 garbage collections that occurred during the run, process-wide.</summary>
    /// <remarks>
    /// A count of collections, not of the job's own garbage: the GC is per process, so anything else
    /// the application was doing is included. Useful as a relative signal between runs of the same
    /// job on the same host, not as an absolute measure of what the job allocated.
    /// </remarks>
    public const string GcGen0Collections = "GcGen0Collections";
    /// <summary>Gen 1 garbage collections during the run, process-wide. See <see cref="GcGen0Collections"/>.</summary>
    public const string GcGen1Collections = "GcGen1Collections";
    /// <summary>Gen 2 garbage collections during the run, process-wide. See <see cref="GcGen0Collections"/>.</summary>
    public const string GcGen2Collections = "GcGen2Collections";

    /// <summary>Recorded by the cleanup job for the number of executions it removed.</summary>
    public const string ExecutionsDeleted = "ExecutionsDeleted";

    /// <summary>Recorded by the cleanup job for unfinished executions it gave up on.</summary>
    public const string ExecutionsMarkedInterrupted = "ExecutionsMarkedInterrupted";
}

using System.Diagnostics;

namespace OptiPowerTools.ScheduledJobsInsights.Logging;

/// <summary>
/// The counter readings taken just before a job runs, differenced afterwards to produce the
/// automatic metrics.
/// </summary>
/// <remarks>
/// Capturing is deliberately infallible. Reading process CPU time goes to <c>/proc/self/stat</c> on
/// Linux and throws outright where that is masked or restricted — and this runs *before*
/// <c>ExecuteJob()</c>, so an exception here would stop the job from running at all. A package that
/// only observes executions must never be the reason one does not happen; a counter it cannot read
/// is simply a metric it does not record.
/// </remarks>
internal readonly record struct ExecutionBaseline(
    long Timestamp,
    long AllocatedBytes,
    TimeSpan? CpuTime,
    int Gen0,
    int Gen1,
    int Gen2)
{
    /// <summary>Takes the baseline readings. Never throws.</summary>
    public static ExecutionBaseline Capture(TimeProvider timeProvider)
    {
        var timestamp = timeProvider.GetTimestamp();

        long allocated = 0;
        int gen0 = 0, gen1 = 0, gen2 = 0;

        try
        {
            allocated = GC.GetAllocatedBytesForCurrentThread();
            gen0 = GC.CollectionCount(0);
            gen1 = GC.CollectionCount(1);
            gen2 = GC.CollectionCount(2);
        }
        catch
        {
            // Same reasoning as the CPU read below: a metric, not the run.
        }

        return new ExecutionBaseline(
            timestamp,
            allocated,
            TryReadCpuTime(out var cpu) ? cpu : null,
            gen0,
            gen1,
            gen2);
    }

    /// <summary>
    /// Reads total processor time for this process, reporting failure rather than throwing.
    /// </summary>
    public static bool TryReadCpuTime(out TimeSpan cpuTime)
    {
        try
        {
            // Disposed rather than left to the finalizer: this runs twice per execution, and each
            // undisposed Process holds a native handle until GC gets to it.
            using var process = Process.GetCurrentProcess();
            cpuTime = process.TotalProcessorTime;
            return true;
        }
        catch
        {
            cpuTime = default;
            return false;
        }
    }
}

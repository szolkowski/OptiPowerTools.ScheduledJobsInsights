using System.Diagnostics;
using System.Text.Json;
using EPiServer.DataAbstraction;
using EPiServer.Scheduler;
using OptiPowerTools.ScheduledJobsInsights.Configuration;

namespace OptiPowerTools.ScheduledJobsInsights.Logging;

/// <summary>
/// Base class for Optimizely scheduled jobs that persists execution history: every
/// <c>OnStatusChanged</c> message, the job's final return value, unhandled exceptions,
/// automatic execution metrics, and anything logged via <see cref="Log"/>/<see cref="LogInputData"/>/<see cref="RecordMetric"/>.
/// </summary>
/// <remarks>
/// Derive from this instead of <see cref="ScheduledJobBase"/> directly and implement
/// <see cref="ExecuteJob"/> instead of <c>Execute()</c> — <c>Execute()</c> is sealed here so the
/// capture wrapper always runs. Optimizely constructs a fresh instance per execution (via
/// <c>ActivatorUtilities.GetServiceOrCreateInstance</c>), so correlating logs/metrics to the
/// current run via a plain instance field (<c>_executionId</c>) is safe — there is no cross-run
/// leakage to guard against, and no <see cref="AsyncLocal{T}"/> is needed.
/// </remarks>
public abstract class LoggedScheduledJobBase : ScheduledJobBase
{
    private readonly IJobExecutionWriter _writer;
    private readonly IScheduledJobRepository _scheduledJobRepository;
    private long _executionId;
    private int _logSequence;

    /// <summary>
    /// Initializes the base class. Derived jobs must forward both parameters to this constructor —
    /// they're normally supplied by DI, since Optimizely constructs job instances via
    /// <c>ActivatorUtilities.GetServiceOrCreateInstance</c>.
    /// </summary>
    protected LoggedScheduledJobBase(IJobExecutionWriter writer, IScheduledJobRepository scheduledJobRepository)
    {
        _writer = writer;
        _scheduledJobRepository = scheduledJobRepository;
    }

    /// <summary>
    /// Implement the job's actual work here instead of overriding <c>Execute()</c>. The returned
    /// string is both the CMS admin's "last execution message" and the persisted <c>ResultMessage</c>.
    /// </summary>
    protected abstract string ExecuteJob();

    /// <summary>
    /// Sealed so the capture wrapper always runs — implement <see cref="ExecuteJob"/> instead.
    /// Wraps the run with automatic metrics and execution persistence, and always rethrows on
    /// failure so Optimizely's own success/failure tracking is unaffected.
    /// </summary>
    public sealed override string Execute()
    {
        var jobName = TryResolveJobName(ScheduledJobId);
        _executionId = _writer.BeginExecution(ScheduledJobId, jobName, GetType().FullName ?? GetType().Name);
        _logSequence = 0;

        var stopwatch = Stopwatch.StartNew();
        var allocatedStart = GC.GetAllocatedBytesForCurrentThread();
        var cpuStart = Process.GetCurrentProcess().TotalProcessorTime;
        var gcStart = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));

        try
        {
            var result = ExecuteJob();
            RecordAutomaticMetrics(stopwatch, allocatedStart, cpuStart, gcStart);
            _writer.Complete(_executionId, succeeded: true, resultMessage: result, exception: null);
            return result;
        }
        catch (Exception ex)
        {
            RecordAutomaticMetrics(stopwatch, allocatedStart, cpuStart, gcStart);
            _writer.Complete(_executionId, succeeded: false, resultMessage: null, exception: ex);
            throw; // Never swallow — Optimizely's own executor sets HasLastExecutionFailed/LastExecutionMessage from this.
        }
    }

    /// <summary>Sealed so every <c>OnStatusChanged</c> call is captured; also raises the native <c>StatusChanged</c> event as usual.</summary>
    protected sealed override void OnStatusChanged(string statusMessage)
    {
        base.OnStatusChanged(statusMessage); // preserves the native StatusChanged event / live CMS admin status
        _writer.Log(_executionId, NextSequence(), LogSeverity.Info, statusMessage, LogEntrySource.StatusChanged);
    }

    /// <summary>Records an explicit log line for the current execution.</summary>
    protected void Log(string message, LogSeverity severity = LogSeverity.Default) =>
        _writer.Log(_executionId, NextSequence(), severity, message, LogEntrySource.DevLog);

    /// <summary>
    /// Captures the input/parameters this run started with, serialized as JSON. Call once near the
    /// start of <see cref="ExecuteJob"/>.
    /// </summary>
    protected void LogInputData(object inputData) =>
        _writer.SetInputData(_executionId, JsonSerializer.Serialize(inputData));

    /// <summary>Records a custom numeric metric for the current execution.</summary>
    protected void RecordMetric(string name, double value, string? unit = null) =>
        _writer.RecordMetric(_executionId, name, value, unit);

    private void RecordAutomaticMetrics(
        Stopwatch stopwatch,
        long allocatedStart,
        TimeSpan cpuStart,
        (int Gen0, int Gen1, int Gen2) gcStart)
    {
        stopwatch.Stop();
        RecordMetric(JobMetricNames.DurationMs, stopwatch.Elapsed.TotalMilliseconds, "ms");
        RecordMetric(JobMetricNames.AllocatedBytes, GC.GetAllocatedBytesForCurrentThread() - allocatedStart, "bytes");
        RecordMetric(JobMetricNames.CpuTimeMs, (Process.GetCurrentProcess().TotalProcessorTime - cpuStart).TotalMilliseconds, "ms");
        RecordMetric(JobMetricNames.GcGen0Collections, GC.CollectionCount(0) - gcStart.Gen0);
        RecordMetric(JobMetricNames.GcGen1Collections, GC.CollectionCount(1) - gcStart.Gen1);
        RecordMetric(JobMetricNames.GcGen2Collections, GC.CollectionCount(2) - gcStart.Gen2);
    }

    private string TryResolveJobName(Guid scheduledJobId)
    {
        try
        {
            var job = _scheduledJobRepository.Get(scheduledJobId);
            if (job is not null && !string.IsNullOrEmpty(job.Name))
                return job.Name;
        }
        catch
        {
            // Repository lookup is best-effort — fall back to the type name (also covers unit tests
            // that construct a job directly without a registered ScheduledJob definition).
        }

        return GetType().Name;
    }

    private int NextSequence() => Interlocked.Increment(ref _logSequence);
}

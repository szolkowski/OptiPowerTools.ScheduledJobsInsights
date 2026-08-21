using System.Diagnostics;
using System.Text.Json;
using EPiServer.DataAbstraction;
using EPiServer.Scheduler;
using OptiPowerTools.ScheduledJobsInsights.Configuration;

namespace OptiPowerTools.ScheduledJobsInsights.Logging;

/// <summary>
/// Base class for Optimizely scheduled jobs that persists execution history: every
/// <c>OnStatusChanged</c> message, the job's final return value, unhandled exceptions,
/// automatic execution metrics, and anything recorded via <see cref="Log"/>/<see cref="LogInputData"/>/
/// <see cref="RecordMetric"/>/<see cref="Summary"/>.
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
    private readonly JobLoggingContext _context;
    private readonly IJobExecutionWriter _writer;
    private long? _executionId;
    private int _logSequence;
    private JobResultSummary? _summary;
    private volatile bool _stopRequested;

    /// <summary>
    /// Initializes the base class. Derived jobs declare <see cref="JobLoggingContext"/> as a
    /// constructor parameter and forward it here; DI supplies it, since Optimizely constructs job
    /// instances via <c>ActivatorUtilities.GetServiceOrCreateInstance</c>.
    /// </summary>
    /// <param name="context">Collaborators this base class records with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
    protected LoggedScheduledJobBase(JobLoggingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _writer = context.Writer;
    }

    /// <summary>
    /// Implement the job's actual work here instead of overriding <c>Execute()</c>. The returned
    /// string is both the CMS admin's "last execution message" and the persisted <c>ResultMessage</c>.
    /// </summary>
    protected abstract string ExecuteJob();

    /// <summary>
    /// Whether an administrator has pressed <em>Stop</em> in the CMS since this run began. Long
    /// jobs should check it between units of work and return early when it becomes <c>true</c>; a
    /// run that ends this way is recorded as <see cref="ExecutionStatus.Stopped"/> rather than as a
    /// success, because its work was cut short.
    /// </summary>
    protected bool IsStopRequested => _stopRequested;

    /// <summary>
    /// Records the stop request and raises the base implementation. Override to add your own
    /// cancellation, and call <c>base.Stop()</c> so the outcome is still recorded correctly.
    /// </summary>
    public override void Stop()
    {
        _stopRequested = true;
        base.Stop();
    }

    /// <summary>
    /// A run that finished after a stop request completed early, whatever it returned — so it is
    /// recorded as stopped rather than as a clean outcome.
    /// </summary>
    private ExecutionStatus Outcome(ExecutionStatus natural) =>
        _stopRequested ? ExecutionStatus.Stopped : natural;

    /// <summary>
    /// Sealed so the capture wrapper always runs — implement <see cref="ExecuteJob"/> instead.
    /// Wraps the run with automatic metrics and execution persistence, and always rethrows on
    /// failure so Optimizely's own success/failure tracking is unaffected.
    /// </summary>
    /// <remarks>
    /// If the execution cannot be recorded at all — an unreachable insights database, most likely —
    /// the job still runs, unrecorded. Recording is dropped rather than the run: a package whose
    /// purpose is to observe jobs must not be able to stop them, and an installation whose reporting
    /// database goes down should lose its history, not its nightly imports.
    /// </remarks>
    public sealed override string Execute()
    {
        var jobName = TryResolveJobName(ScheduledJobId);

        // Null means this run goes unrecorded; every write below is a no-op from here on.
        _executionId = _writer.BeginExecution(ScheduledJobId, jobName, GetType().FullName ?? GetType().Name);
        _logSequence = 0;
        _summary = null;

        // Captured before the run, but never at the cost of the run: reading process CPU time hits
        // /proc on Linux and throws outright on a hardened container. A baseline we cannot take is
        // a metric we do not record, not a job we refuse to start.
        var baseline = ExecutionBaseline.Capture(_context.TimeProvider);

        try
        {
            var result = ExecuteJob();

            // Inside the try, but its own failure must not reach the catch below — a metrics error
            // after a clean run would otherwise record the run as failed and rethrow.
            SafelyRecordAutomaticMetrics(baseline);
            CompleteExecution(Outcome(ExecutionStatus.Succeeded), resultMessage: result, exception: null);
            return result;
        }
        catch (Exception ex)
        {
            SafelyRecordAutomaticMetrics(baseline);

            // Guarded so the job's own exception always wins. An escape here would both replace it
            // and leave the row stranded at Running, with nothing to ever finish it.
            try
            {
                CompleteExecution(Outcome(ExecutionStatus.Failed), resultMessage: null, exception: ex);
            }
            catch
            {
                // Nothing useful to do: the recording is already lost and the run's own failure is
                // the more important of the two.
            }

            throw; // Never swallow — Optimizely's own executor sets HasLastExecutionFailed/LastExecutionMessage from this.
        }
    }

    /// <summary>
    /// Sealed so every <c>OnStatusChanged</c> call is captured; also raises the native
    /// <c>StatusChanged</c> event as usual.
    /// </summary>
    /// <remarks>
    /// The base call happens unconditionally, before and regardless of any recording. The CMS admin's
    /// live status column depends on it, and that must keep working even when this package cannot
    /// persist a thing.
    /// </remarks>
    protected sealed override void OnStatusChanged(string statusMessage)
    {
        base.OnStatusChanged(statusMessage); // preserves the native StatusChanged event / live CMS admin status

        if (_executionId is { } executionId)
            _writer.Log(executionId, NextSequence(), LogSeverity.Info, statusMessage, LogEntrySource.StatusChanged);
    }

    /// <summary>Records an explicit log line for the current execution. A no-op if the run is unrecorded.</summary>
    protected void Log(string message, LogSeverity severity = LogSeverity.Default)
    {
        if (_executionId is { } executionId)
            _writer.Log(executionId, NextSequence(), severity, message, LogEntrySource.DevLog);
    }

    /// <summary>
    /// Captures the input/parameters this run started with, serialized as JSON. Call once near the
    /// start of <see cref="ExecuteJob"/>. A no-op if the run is unrecorded.
    /// </summary>
    protected void LogInputData(object inputData)
    {
        // Serialized only when there is somewhere to put it — the payload can be large, and an
        // unrecorded run should not pay to build a string nobody will read.
        if (_executionId is not { } executionId)
            return;

        string json;

        try
        {
            json = JsonSerializer.Serialize(inputData, InputDataJsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // Reachable with any ordinary domain object: an EF navigation or an IContent is a
            // reference cycle, and cycles, depth limits and unsupported types all throw. Recording
            // the failure is right; letting it out of here would fail a job that merely described
            // its own input.
            json = JsonSerializer.Serialize(new { InputDataUnavailable = ex.Message });
        }

        _writer.SetInputData(executionId, json);
    }

    /// <summary>Records a custom numeric metric for the current execution. A no-op if the run is unrecorded.</summary>
    protected void RecordMetric(string name, double value, string? unit = null)
    {
        if (_executionId is { } executionId)
            _writer.RecordMetric(executionId, name, value, unit);
    }

    /// <summary>
    /// Optional multi-line report for this run, rendered as the <em>Result summary</em> section of
    /// the execution detail view. Append to it as the job works; nothing is written unless something
    /// was appended.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use this for the readable account of what the run did — counts, skipped items, per-step
    /// outcomes — and keep the string returned from <see cref="ExecuteJob"/> to one line, since that
    /// value is Optimizely's "last execution message" and shows in a single admin grid cell.
    /// </para>
    /// <para>
    /// Persisted once, just before the execution is completed, on both the success and failure
    /// paths. A job that runs for a long time and wants the summary visible while it is still
    /// running can checkpoint it with <see cref="FlushSummary"/>.
    /// </para>
    /// </remarks>
    protected JobResultSummary Summary => _summary ??= new JobResultSummary(_context.MaxResultSummaryLength);

    /// <summary>
    /// Replaces the whole summary with <paramref name="summary"/>, for jobs that already hold the
    /// finished text rather than building it up.
    /// </summary>
    /// <param name="summary">The summary text. Newlines are preserved.</param>
    protected void SetSummary(string summary)
    {
        Summary.Clear();
        Summary.Append(summary);
    }

    /// <summary>
    /// Persists the summary as it currently stands. Called automatically when the job finishes;
    /// call it directly only to make a partial summary visible part-way through a long run.
    /// </summary>
    protected void FlushSummary()
    {
        if (_executionId is not { } executionId || _summary is null || _summary.IsEmpty)
            return;

        _writer.SetResultSummary(executionId, _summary.ToString());
    }

    /// <summary>
    /// Persists the summary and marks the execution finished. Skipped entirely when the run is
    /// unrecorded.
    /// </summary>
    private void CompleteExecution(ExecutionStatus outcome, string? resultMessage, Exception? exception)
    {
        if (_executionId is not { } executionId)
            return;

        // Flushed on the failure path too: whatever the job managed to summarise before throwing is
        // usually the most useful thing on the page when diagnosing that failure.
        FlushSummary();
        _writer.Complete(executionId, outcome, resultMessage, exception);
    }

    /// <summary>Options used for <see cref="LogInputData"/>, tolerant of ordinary domain objects.</summary>
    private static readonly JsonSerializerOptions InputDataJsonOptions = new()
    {
        ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
        MaxDepth = 16
    };

    /// <summary>
    /// Records the automatic metrics, absorbing anything that goes wrong. Called from both the
    /// success and failure paths, where a throw would corrupt the outcome of the run itself.
    /// </summary>
    private void SafelyRecordAutomaticMetrics(ExecutionBaseline baseline)
    {
        try
        {
            var elapsed = _context.TimeProvider.GetElapsedTime(baseline.Timestamp);
            RecordMetric(JobMetricNames.DurationMs, elapsed.TotalMilliseconds, "ms");
            RecordMetric(JobMetricNames.AllocatedBytes, GC.GetAllocatedBytesForCurrentThread() - baseline.AllocatedBytes, "bytes");

            if (ExecutionBaseline.TryReadCpuTime(out var cpuNow) && baseline.CpuTime is { } cpuStart)
                RecordMetric(JobMetricNames.CpuTimeMs, (cpuNow - cpuStart).TotalMilliseconds, "ms");

            RecordMetric(JobMetricNames.GcGen0Collections, GC.CollectionCount(0) - baseline.Gen0);
            RecordMetric(JobMetricNames.GcGen1Collections, GC.CollectionCount(1) - baseline.Gen1);
            RecordMetric(JobMetricNames.GcGen2Collections, GC.CollectionCount(2) - baseline.Gen2);
        }
        catch
        {
            // Metrics are the least important thing this class does, and the only one whose failure
            // could otherwise change what the CMS reports about the run.
        }
    }

    private string TryResolveJobName(Guid scheduledJobId)
    {
        try
        {
            var job = _context.ScheduledJobRepository.Get(scheduledJobId);
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

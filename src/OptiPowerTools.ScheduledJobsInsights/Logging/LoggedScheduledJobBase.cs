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
    /// Cancelled when an administrator stops the run. Created per execution and disposed with it,
    /// so a job type that runs nightly for a year does not accumulate one of these per run.
    /// </summary>
    private CancellationTokenSource? _stopping;

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
    /// Cancelled when an administrator stops this run — hand it to any async or cancellable work the
    /// job does. Outside a run it is <see cref="CancellationToken.None"/>.
    /// </summary>
    protected CancellationToken StopToken => _stopping?.Token ?? CancellationToken.None;

    /// <summary>
    /// Records the stop request, cancels <see cref="StopToken"/> and raises the base implementation.
    /// Sealed — override <see cref="OnStopRequested"/> to add cancellation of your own.
    /// </summary>
    /// <remarks>
    /// Sealed for the same reason as <see cref="Execute"/> and <see cref="OnStatusChanged"/>: the
    /// bookkeeping here has to happen. It used to be overridable with a doc comment asking derived
    /// jobs to call <c>base.Stop()</c>, and forgetting that lost <see cref="IsStopRequested"/> and
    /// <see cref="StopToken"/> silently — so a run cut short was recorded as
    /// <see cref="ExecutionStatus.Succeeded"/>, which is the one outcome that must never be wrong.
    /// </remarks>
    public sealed override void Stop()
    {
        _stopRequested = true;

        try
        {
            _stopping?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Stop arrived just as the run finished. Nothing to cancel, and certainly nothing worth
            // throwing over — this is called by the CMS, not by the job.
        }

        try
        {
            OnStopRequested();
        }
        catch (Exception ex)
        {
            // A job's own stop handling must not prevent the stop from being recorded, nor throw into
            // the CMS thread that pressed the button. The request is already registered above, so the
            // run still ends as Stopped either way.
            Log($"The job's OnStopRequested handler threw: {ex.Message}", LogSeverity.Warning);
        }

        base.Stop();
    }

    /// <summary>
    /// Called when an administrator stops the run, after the stop has been recorded and
    /// <see cref="StopToken"/> cancelled. Override to cancel work of your own; the default does
    /// nothing.
    /// </summary>
    /// <remarks>
    /// Most jobs need nothing here — checking <see cref="IsStopRequested"/> between units of work, or
    /// passing <see cref="StopToken"/> to whatever they call, is the ordinary way to honour a stop.
    /// This exists for the job that holds something the token cannot reach. It is called on the CMS's
    /// thread, not the job's, so keep it short and do not block.
    /// </remarks>
    protected virtual void OnStopRequested()
    {
    }

    /// <summary>
    /// A run that finished after a stop request completed early, whatever it returned — so it is
    /// recorded as stopped rather than as a clean outcome.
    /// </summary>
    /// <remarks>
    /// A stop does <em>not</em> mask a failure. A run that was stopped and then threw for some
    /// unrelated reason is recorded as <see cref="ExecutionStatus.Failed"/>, with its exception, so the
    /// history agrees with the CMS admin's own <c>HasLastExecutionFailed</c> — which is set from the
    /// rethrown exception and knows nothing about the stop. The exception being present is the
    /// discriminator; a job that honours <c>StopToken</c> by throwing
    /// <see cref="OperationCanceledException"/> is still recorded as stopped, because that is the stop
    /// working rather than the job breaking.
    /// </remarks>
    private ExecutionStatus Outcome(ExecutionStatus natural, Exception? exception = null)
    {
        if (!_stopRequested)
            return natural;

        return exception is null or OperationCanceledException
            ? ExecutionStatus.Stopped
            : ExecutionStatus.Failed;
    }

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

        _stopping = new CancellationTokenSource();

        try
        {
            var result = ExecuteJob();

            // Both of these are inside the try, and neither may let its own failure reach the catch
            // below: a recording error after a clean run would otherwise record that run as *failed*,
            // rethrow a recording exception as though the job had thrown it, and duplicate the metrics
            // on the way. The metrics call has always been guarded; this one was not, which left the
            // package able to report a successful job as failed — the exact inversion it exists to
            // prevent. Unreachable with the shipped writer, which swallows everything, and reachable
            // by any host that substitutes its own IJobExecutionWriter.
            SafelyRecordAutomaticMetrics(baseline);

            try
            {
                CompleteExecution(Outcome(ExecutionStatus.Succeeded), resultMessage: result, exception: null);
            }
            catch
            {
                // The run succeeded and that is what it returns. Losing the record of it is the lesser
                // failure, and there is nowhere left to report this that would not corrupt the outcome.
            }

            return result;
        }
        catch (Exception ex)
        {
            SafelyRecordAutomaticMetrics(baseline);

            // Guarded so the job's own exception always wins. An escape here would both replace it
            // and leave the row stranded at Running, with nothing to ever finish it.
            try
            {
                CompleteExecution(Outcome(ExecutionStatus.Failed, ex), resultMessage: null, exception: ex);
            }
            catch
            {
                // Nothing useful to do: the recording is already lost and the run's own failure is
                // the more important of the two.
            }

            throw; // Never swallow — Optimizely's own executor sets HasLastExecutionFailed/LastExecutionMessage from this.
        }
        finally
        {
            // Cleared before disposing so a concurrent Stop() sees null rather than a disposed source.
            var stopping = _stopping;
            _stopping = null;
            stopping.Dispose();
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
        catch (Exception ex)
        {
            // Deliberately unfiltered. Cycles, depth limits and unsupported types arrive as
            // JsonException/NotSupportedException — but System.Text.Json does not wrap what a
            // property getter throws, it propagates it unchanged. A disposed lazy-loading proxy
            // raises ObjectDisposedException, an IContent's computed property raises whatever it
            // likes, and a filtered catch would let those out of here and fail a job that merely
            // described its own input. Recording why is the whole point; nothing serialized here is
            // worth a failed run.
            json = SerializeUnavailable(ex);
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

    /// <summary>
    /// Renders "the input could not be captured, and here is why" as JSON, without trusting the
    /// exception either.
    /// </summary>
    /// <remarks>
    /// <see cref="Exception.Message"/> is itself overridable, so a hostile or merely careless
    /// exception type can throw from the very property this reads. That would put a throw back on the
    /// path this whole method exists to keep clear, so the type name — which cannot throw — is the
    /// fallback.
    /// </remarks>
    private static string SerializeUnavailable(Exception ex)
    {
        try
        {
            return JsonSerializer.Serialize(new { InputDataUnavailable = ex.Message });
        }
        catch
        {
            return JsonSerializer.Serialize(new { InputDataUnavailable = ex.GetType().FullName });
        }
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
            RecordMetric(JobMetricNames.ThreadAllocatedBytes, GC.GetAllocatedBytesForCurrentThread() - baseline.AllocatedBytes, "bytes");

            if (ExecutionBaseline.TryReadCpuTime(out var cpuNow) && baseline.CpuTime is { } cpuStart)
                RecordMetric(JobMetricNames.ProcessCpuTimeMs, (cpuNow - cpuStart).TotalMilliseconds, "ms");

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
            // Null outside a CMS — a context built by JobLoggingContext.ForWriter for a unit test.
            // Checked rather than left to the catch below, which is there for a lookup that fails,
            // not for a collaborator that was never supplied.
            var job = _context.ScheduledJobRepository?.Get(scheduledJobId);
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

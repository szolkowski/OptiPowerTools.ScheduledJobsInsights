using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Data;
using OptiPowerTools.ScheduledJobsInsights.Data.Entities;

namespace OptiPowerTools.ScheduledJobsInsights.Logging;

/// <summary>
/// Default <see cref="IJobExecutionWriter"/>. Begin/Complete/SetInputData use short-lived
/// <see cref="IDbContextFactory{TContext}"/> instances for immediate, synchronous writes.
/// Log/RecordMetric try a non-blocking channel write first, falling back to a synchronous
/// single-row insert only if the channel is momentarily full — this guarantees no log loss
/// while keeping the common case cheap.
/// </summary>
/// <remarks>
/// <b>No member throws.</b> Every one of them is called from inside a running job, and this package's
/// contract is that it only *observes* an execution: a failure to record must never become a failure
/// of the run. Immediate writes funnel through <see cref="Write"/>, which logs and swallows;
/// <see cref="BeginExecution"/> reports failure by returning <c>null</c>, which disables recording
/// for the rest of that run.
/// </remarks>
internal sealed class JobExecutionWriter : IJobExecutionWriter
{
    private readonly IDbContextFactory<ScheduledJobsInsightsDbContext> _dbContextFactory;
    private readonly ChannelWriter<JobRecord> _channelWriter;
    private readonly ILogger<JobExecutionWriter> _logger;
    private readonly int _maxResultSummaryLength;

    /// <summary>Set once the first channel-full fallback has been reported, to keep the log readable.</summary>
    private int _backpressureReported;

    public JobExecutionWriter(
        IDbContextFactory<ScheduledJobsInsightsDbContext> dbContextFactory,
        Channel<JobRecord> channel,
        IOptions<OptiPowerToolScheduledJobsInsightsOptions> options,
        ILogger<JobExecutionWriter> logger)
    {
        _dbContextFactory = dbContextFactory;
        _channelWriter = channel.Writer;
        _logger = logger;

        // A misconfigured zero would make every summary unstorable, which is a worse outcome than
        // quietly using the default bound.
        var configured = options.Value.MaxResultSummaryLength;
        _maxResultSummaryLength = configured > 0 ? configured : JobResultSummary.DefaultMaxLength;
    }

    public long? BeginExecution(Guid scheduledJobId, string jobName, string jobTypeName)
    {
        try
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            var execution = new JobExecution
            {
                ScheduledJobId = scheduledJobId,
                JobName = jobName,
                JobTypeName = jobTypeName,
                StartedAt = DateTimeOffset.UtcNow,
                Status = ExecutionStatus.Running,
                MachineName = Environment.MachineName
            };
            dbContext.JobExecutions.Add(execution);
            dbContext.SaveChanges();
            return execution.Id;
        }
        catch (Exception ex)
        {
            // Warning, not error: the job is about to run perfectly well, it just will not appear in
            // the history. Escalating this to an exception would take a working job down with an
            // unavailable reporting database, which is precisely backwards.
            _logger.LogWarning(
                ex,
                "ScheduledJobsInsights could not begin recording an execution of '{JobName}'. The job will run, but this run will not appear in the execution history.",
                jobName);
            return null;
        }
    }

    public void Complete(long executionId, ExecutionStatus outcome, string? resultMessage, Exception? exception)
    {
        // Running is not a completion. Recording it would leave the row looking unfinished forever,
        // so an out-of-range caller is treated as a failure rather than silently stranding the run.
        var status = outcome is ExecutionStatus.Succeeded or ExecutionStatus.Failed or ExecutionStatus.Stopped
            ? outcome
            : ExecutionStatus.Failed;

        Write(executionId, nameof(Complete), dbContext => dbContext.JobExecutions
            .Where(e => e.Id == executionId)
            .ExecuteUpdate(setters => setters
                .SetProperty(e => e.Status, status)
                .SetProperty(e => e.CompletedAt, DateTimeOffset.UtcNow)
                .SetProperty(e => e.ResultMessage, resultMessage)
                .SetProperty(e => e.ExceptionMessage, exception != null ? exception.Message : null)
                .SetProperty(e => e.ExceptionStackTrace, exception != null ? exception.StackTrace : null)));
    }

    public void SetInputData(long executionId, string inputDataJson) =>
        Write(executionId, nameof(SetInputData), dbContext => dbContext.JobExecutions
            .Where(e => e.Id == executionId)
            .ExecuteUpdate(setters => setters.SetProperty(e => e.InputDataJson, inputDataJson)));

    public void SetResultSummary(long executionId, string summary)
    {
        // Written immediately rather than through the channel: Complete() follows straight after, and
        // a buffered summary could otherwise land after the execution is already marked finished.
        var bounded = Truncate(summary, _maxResultSummaryLength);

        Write(executionId, nameof(SetResultSummary), dbContext => dbContext.JobExecutions
            .Where(e => e.Id == executionId)
            .ExecuteUpdate(setters => setters.SetProperty(e => e.ResultSummary, bounded)));
    }

    /// <summary>
    /// Bounds a summary written directly through <see cref="SetResultSummary"/>, which
    /// <see cref="JobResultSummary"/>'s own bound does not cover. Ends with the same notice the
    /// summary type uses, so a truncated value never looks merely short, and never splits a
    /// surrogate pair — half a character would render as a replacement glyph.
    /// </summary>
    private static string Truncate(string summary, int maxLength)
    {
        if (summary.Length <= maxLength)
            return summary;

        var budget = Math.Max(1, maxLength - (Environment.NewLine.Length + JobResultSummary.TruncationNotice.Length));

        if (budget < summary.Length && char.IsHighSurrogate(summary[budget - 1]))
            budget--;

        return summary[..budget] + Environment.NewLine + JobResultSummary.TruncationNotice;
    }

    /// <summary>
    /// Runs an immediate write, swallowing and logging any failure. Every one of these happens while
    /// a job is executing, so none of them may throw into it.
    /// </summary>
    private void Write(long executionId, string operation, Action<ScheduledJobsInsightsDbContext> write)
    {
        try
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            write(dbContext);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "ScheduledJobsInsights failed to record {Operation} for execution {ExecutionId}. The execution history for this run is incomplete; the job itself is unaffected.",
                operation,
                executionId);
        }
    }

    public void Log(long executionId, int sequence, LogSeverity severity, string message, LogEntrySource source)
    {
        var record = new LogRecordItem(executionId, sequence, severity, message, source, DateTimeOffset.UtcNow);
        if (_channelWriter.TryWrite(record))
            return;

        ReportBackpressure();
        FlushSynchronously(record, dbContext => dbContext.JobLogEntries.Add(new JobLogEntry
        {
            JobExecutionId = record.ExecutionId,
            Sequence = record.Sequence,
            Timestamp = record.Timestamp,
            Severity = record.Severity,
            Source = record.Source,
            Message = record.Message
        }));
    }

    public void RecordMetric(long executionId, string name, double value, string? unit)
    {
        var record = new MetricRecordItem(executionId, name, value, unit, DateTimeOffset.UtcNow);
        if (_channelWriter.TryWrite(record))
            return;

        ReportBackpressure();
        FlushSynchronously(record, dbContext => dbContext.JobMetrics.Add(new JobMetric
        {
            JobExecutionId = record.ExecutionId,
            Name = record.Name,
            Value = record.Value,
            Unit = record.Unit,
            RecordedAt = record.RecordedAt
        }));
    }

    /// <summary>
    /// Writes a single record immediately, because the buffer was full. Never throws: this runs on the
    /// job's own thread, and a job must not fail because recording a log line did.
    /// </summary>
    private void FlushSynchronously(JobRecord record, Action<ScheduledJobsInsightsDbContext> add)
    {
        try
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            add(dbContext);
            dbContext.SaveChanges();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "ScheduledJobsInsights could not record a log/metric entry for execution {ExecutionId}. The entry is lost; the job itself is unaffected.",
                record.ExecutionId);
        }
    }

    /// <summary>
    /// Notes that the buffer overflowed. Warned about once per process, then dropped to Debug: under
    /// sustained backpressure this fires on every single write, and a warning per log line would
    /// bury the one message that matters in the noise it is describing.
    /// </summary>
    private void ReportBackpressure()
    {
        if (Interlocked.Exchange(ref _backpressureReported, 1) == 0)
        {
            _logger.LogWarning(
                "ScheduledJobsInsights log buffer is full; writes are falling back to synchronous inserts, which will slow down chatty jobs. Consider raising LogChannelCapacity or LogBatchSize. Further occurrences are logged at Debug level.");
            return;
        }

        _logger.LogDebug("ScheduledJobsInsights log buffer full; wrote one record synchronously.");
    }
}

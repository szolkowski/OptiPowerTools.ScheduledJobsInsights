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
/// Log/RecordMetric never throw. They are called from inside a running job, and this package's
/// contract is that it only *observes* an execution: a failure to record a log line must not become
/// a failure of the job that logged it. Begin/Complete/SetInputData/SetResultSummary do still
/// propagate — <c>BeginExecution</c> has to, since the execution id it returns is what everything
/// else is keyed on.
/// </remarks>
internal sealed class JobExecutionWriter : IJobExecutionWriter
{
    private readonly IDbContextFactory<ScheduledJobsInsightsDbContext> _dbContextFactory;
    private readonly ChannelWriter<JobRecord> _channelWriter;
    private readonly ILogger<JobExecutionWriter> _logger;

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
        MaxResultSummaryLength = configured > 0 ? configured : JobResultSummary.DefaultMaxLength;
    }

    public int MaxResultSummaryLength { get; }

    public long BeginExecution(Guid scheduledJobId, string jobName, string jobTypeName)
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

    public void Complete(long executionId, bool succeeded, string? resultMessage, Exception? exception)
    {
        using var dbContext = _dbContextFactory.CreateDbContext();
        dbContext.JobExecutions
            .Where(e => e.Id == executionId)
            .ExecuteUpdate(setters => setters
                .SetProperty(e => e.Status, succeeded ? ExecutionStatus.Succeeded : ExecutionStatus.Failed)
                .SetProperty(e => e.CompletedAt, DateTimeOffset.UtcNow)
                .SetProperty(e => e.ResultMessage, resultMessage)
                .SetProperty(e => e.ExceptionMessage, exception != null ? exception.Message : null)
                .SetProperty(e => e.ExceptionStackTrace, exception != null ? exception.StackTrace : null));
    }

    public void SetInputData(long executionId, string inputDataJson)
    {
        using var dbContext = _dbContextFactory.CreateDbContext();
        dbContext.JobExecutions
            .Where(e => e.Id == executionId)
            .ExecuteUpdate(setters => setters.SetProperty(e => e.InputDataJson, inputDataJson));
    }

    public void SetResultSummary(long executionId, string summary)
    {
        // Written immediately rather than through the channel: Complete() follows straight after, and
        // a buffered summary could otherwise land after the execution is already marked finished.
        var bounded = summary.Length > MaxResultSummaryLength
            ? summary[..MaxResultSummaryLength]
            : summary;

        using var dbContext = _dbContextFactory.CreateDbContext();
        dbContext.JobExecutions
            .Where(e => e.Id == executionId)
            .ExecuteUpdate(setters => setters.SetProperty(e => e.ResultSummary, bounded));
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

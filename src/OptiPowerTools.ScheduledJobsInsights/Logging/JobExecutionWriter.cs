using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
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
internal sealed class JobExecutionWriter : IJobExecutionWriter
{
    private readonly IDbContextFactory<ScheduledJobsInsightsDbContext> _dbContextFactory;
    private readonly ChannelWriter<JobRecord> _channelWriter;

    public JobExecutionWriter(IDbContextFactory<ScheduledJobsInsightsDbContext> dbContextFactory, Channel<JobRecord> channel)
    {
        _dbContextFactory = dbContextFactory;
        _channelWriter = channel.Writer;
    }

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

    public void Log(long executionId, int sequence, LogSeverity severity, string message, LogEntrySource source)
    {
        var record = new LogRecordItem(executionId, sequence, severity, message, source, DateTimeOffset.UtcNow);
        if (!_channelWriter.TryWrite(record))
            FlushLogSynchronously(record);
    }

    public void RecordMetric(long executionId, string name, double value, string? unit)
    {
        var record = new MetricRecordItem(executionId, name, value, unit, DateTimeOffset.UtcNow);
        if (!_channelWriter.TryWrite(record))
            FlushMetricSynchronously(record);
    }

    private void FlushLogSynchronously(LogRecordItem record)
    {
        using var dbContext = _dbContextFactory.CreateDbContext();
        dbContext.JobLogEntries.Add(new JobLogEntry
        {
            JobExecutionId = record.ExecutionId,
            Sequence = record.Sequence,
            Timestamp = record.Timestamp,
            Severity = record.Severity,
            Source = record.Source,
            Message = record.Message
        });
        dbContext.SaveChanges();
    }

    private void FlushMetricSynchronously(MetricRecordItem record)
    {
        using var dbContext = _dbContextFactory.CreateDbContext();
        dbContext.JobMetrics.Add(new JobMetric
        {
            JobExecutionId = record.ExecutionId,
            Name = record.Name,
            Value = record.Value,
            Unit = record.Unit,
            RecordedAt = record.RecordedAt
        });
        dbContext.SaveChanges();
    }
}

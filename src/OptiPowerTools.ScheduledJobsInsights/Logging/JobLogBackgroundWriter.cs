using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Data;
using OptiPowerTools.ScheduledJobsInsights.Data.Entities;

namespace OptiPowerTools.ScheduledJobsInsights.Logging;

/// <summary>
/// Drains the channel that <see cref="JobExecutionWriter"/> buffers log lines and metrics into,
/// flushing them to the database in batches — either when <see cref="OptiPowerToolScheduledJobsInsightsOptions.LogBatchSize"/>
/// records have accumulated or <see cref="OptiPowerToolScheduledJobsInsightsOptions.LogFlushInterval"/> has elapsed,
/// whichever comes first. On shutdown, remaining buffered records are drained and flushed one final time.
/// </summary>
/// <remarks>
/// Nothing in here may throw out of <see cref="ExecuteAsync"/>. Since .NET 6 the default
/// <c>BackgroundServiceExceptionBehavior</c> is <c>StopHost</c>, so an unhandled exception from a
/// hosted service shuts the whole application down — which for this package would mean a transient
/// SQL error while writing *log lines* taking the CMS offline. Diagnostics are worth strictly less
/// than the thing they are diagnosing, so every failure here is caught, logged and survived.
/// </remarks>
internal sealed class JobLogBackgroundWriter : BackgroundService
{
    /// <summary>Attempts per batch. Covers a transient blip without holding the drain loop for long.</summary>
    private const int MaxFlushAttempts = 3;

    private static readonly TimeSpan RetryBackoff = TimeSpan.FromMilliseconds(200);

    private readonly Channel<JobRecord> _channel;
    private readonly IDbContextFactory<ScheduledJobsInsightsDbContext> _dbContextFactory;
    private readonly OptiPowerToolScheduledJobsInsightsOptions _options;
    private readonly ILogger<JobLogBackgroundWriter> _logger;

    public JobLogBackgroundWriter(
        Channel<JobRecord> channel,
        IDbContextFactory<ScheduledJobsInsightsDbContext> dbContextFactory,
        IOptions<OptiPowerToolScheduledJobsInsightsOptions> options,
        ILogger<JobLogBackgroundWriter> logger)
    {
        _channel = channel;
        _dbContextFactory = dbContextFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = _channel.Reader;

        try
        {
            while (await reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
            {
                var batch = await CollectBatchAsync(reader, stoppingToken).ConfigureAwait(false);
                if (batch.Count > 0)
                    await FlushAsync(batch, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected on shutdown — fall through to the final drain below.
        }
        catch (Exception ex)
        {
            // FlushAsync already handles its own failures, so reaching here means the channel or the
            // loop itself broke. Stop draining, but do not take the host down over it.
            _logger.LogError(ex, "ScheduledJobsInsights log writer stopped unexpectedly. Job logs and metrics will no longer be persisted until the application restarts.");
            return;
        }

        await DrainRemainingAsync().ConfigureAwait(false);
    }

    private async Task<List<JobRecord>> CollectBatchAsync(ChannelReader<JobRecord> reader, CancellationToken stoppingToken)
    {
        var batch = new List<JobRecord>(_options.LogBatchSize);
        var deadline = DateTime.UtcNow + _options.LogFlushInterval;

        while (batch.Count < _options.LogBatchSize)
        {
            if (reader.TryRead(out var item))
            {
                batch.Add(item);
                continue;
            }

            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                break;

            var readTask = reader.WaitToReadAsync(stoppingToken).AsTask();
            var completedTask = await Task.WhenAny(readTask, Task.Delay(remaining, stoppingToken)).ConfigureAwait(false);
            if (completedTask != readTask)
                break; // flush interval elapsed before more data arrived

            if (!await readTask.ConfigureAwait(false))
                break; // channel completed
        }

        return batch;
    }

    /// <summary>Reads whatever is currently buffered (without waiting) and flushes it — used on shutdown.</summary>
    private async Task DrainRemainingAsync()
    {
        var remaining = new List<JobRecord>();
        while (_channel.Reader.TryRead(out var item))
            remaining.Add(item);

        if (remaining.Count > 0)
            await FlushAsync(remaining, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes a batch, retrying a transient failure a couple of times and giving up rather than
    /// throwing. A dropped batch costs some log lines; an escaping exception costs the application.
    /// </summary>
    private async Task FlushAsync(List<JobRecord> batch, CancellationToken stoppingToken)
    {
        for (var attempt = 1; attempt <= MaxFlushAttempts; attempt++)
        {
            try
            {
                await WriteBatchAsync(batch, stoppingToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw; // Shutdown, not a failure — ExecuteAsync's handler drains what is left.
            }
            catch (Exception ex)
            {
                if (attempt == MaxFlushAttempts)
                {
                    _logger.LogError(
                        ex,
                        "ScheduledJobsInsights dropped {RecordCount} buffered log/metric record(s) after {AttemptCount} failed write attempts. Job execution history is incomplete for this period; the writer is still running.",
                        batch.Count,
                        MaxFlushAttempts);
                    return;
                }

                _logger.LogWarning(
                    ex,
                    "ScheduledJobsInsights failed to write {RecordCount} buffered log/metric record(s) on attempt {Attempt} of {AttemptCount}. Retrying.",
                    batch.Count,
                    attempt,
                    MaxFlushAttempts);

                // Each attempt builds a fresh DbContext, so there is no failed change tracker to reset.
                await Task.Delay(RetryBackoff * attempt, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private async Task WriteBatchAsync(List<JobRecord> batch, CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        foreach (var record in batch)
        {
            switch (record)
            {
                case LogRecordItem log:
                    dbContext.JobLogEntries.Add(new JobLogEntry
                    {
                        JobExecutionId = log.ExecutionId,
                        Sequence = log.Sequence,
                        Timestamp = log.Timestamp,
                        Severity = log.Severity,
                        Source = log.Source,
                        Message = log.Message
                    });
                    break;
                case MetricRecordItem metric:
                    dbContext.JobMetrics.Add(new JobMetric
                    {
                        JobExecutionId = metric.ExecutionId,
                        Name = metric.Name,
                        Value = metric.Value,
                        Unit = metric.Unit,
                        RecordedAt = metric.RecordedAt
                    });
                    break;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

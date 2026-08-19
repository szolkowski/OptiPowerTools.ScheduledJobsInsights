using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
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
internal sealed class JobLogBackgroundWriter : BackgroundService
{
    private readonly Channel<JobRecord> _channel;
    private readonly IDbContextFactory<ScheduledJobsInsightsDbContext> _dbContextFactory;
    private readonly OptiPowerToolScheduledJobsInsightsOptions _options;

    public JobLogBackgroundWriter(
        Channel<JobRecord> channel,
        IDbContextFactory<ScheduledJobsInsightsDbContext> dbContextFactory,
        IOptions<OptiPowerToolScheduledJobsInsightsOptions> options)
    {
        _channel = channel;
        _dbContextFactory = dbContextFactory;
        _options = options.Value;
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

    private async Task FlushAsync(List<JobRecord> batch, CancellationToken cancellationToken)
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

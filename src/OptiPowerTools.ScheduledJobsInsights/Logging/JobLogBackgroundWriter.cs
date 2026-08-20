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

    /// <summary>
    /// Records taken out of the channel but not yet written. A field rather than a local because
    /// both <see cref="ExecuteAsync"/> and <see cref="StopAsync"/> have to be able to flush it, and
    /// only one of them is guaranteed to run. Never touched concurrently: <see cref="StopAsync"/>
    /// only reaches it after <c>base.StopAsync</c> has awaited <see cref="ExecuteAsync"/> to a stop.
    /// </summary>
    private readonly List<JobRecord> _pending = [];

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
                // _pending rather than a local: collecting takes records *out* of the channel, so a
                // batch abandoned mid-collect is gone — a drain that only reads the channel finds
                // nothing. Shutdown cancels while the collector is typically parked waiting for more.
                await CollectBatchAsync(reader, _pending, stoppingToken).ConfigureAwait(false);
                if (_pending.Count > 0)
                    await FlushAsync(_pending, stoppingToken).ConfigureAwait(false);

                _pending.Clear();
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

    /// <summary>
    /// Flushes whatever is still buffered as the application stops.
    /// </summary>
    /// <remarks>
    /// The drain has to live here as well as at the end of <see cref="ExecuteAsync"/>, because
    /// <see cref="ExecuteAsync"/> is not guaranteed to run at all. <see cref="BackgroundService"/>
    /// starts it on the thread pool, so a host that stops promptly — or a pool busy at startup — can
    /// cancel the task before its body is ever entered, leaving it <c>Canceled</c> with no trace. A
    /// drain that only existed inside it would be skipped exactly then, silently dropping everything
    /// a job logged. Draining twice is harmless: the second pass finds nothing.
    /// </remarks>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        await DrainRemainingAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Fills <paramref name="batch"/> from the channel until it is full or the flush interval
    /// elapses. The caller supplies the list so that a cancellation part-way through does not strand
    /// the records already taken out of the channel.
    /// </summary>
    private async Task CollectBatchAsync(ChannelReader<JobRecord> reader, List<JobRecord> batch, CancellationToken stoppingToken)
    {
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
    }

    /// <summary>
    /// Final flush on shutdown: anything already taken from the channel but not yet written, plus
    /// whatever is still queued behind it.
    /// </summary>

    /// <remarks>
    /// Uses <see cref="CancellationToken.None"/> deliberately — the whole purpose of this call is to
    /// run after cancellation, so passing the stopping token would cancel the write it exists to
    /// perform. <c>StopAsync</c>'s own shutdown timeout still bounds it.
    /// </remarks>
    private async Task DrainRemainingAsync()
    {
        while (_channel.Reader.TryRead(out var item))
            _pending.Add(item);

        if (_pending.Count == 0)
            return;

        await FlushAsync(_pending, CancellationToken.None).ConfigureAwait(false);
        _pending.Clear();
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

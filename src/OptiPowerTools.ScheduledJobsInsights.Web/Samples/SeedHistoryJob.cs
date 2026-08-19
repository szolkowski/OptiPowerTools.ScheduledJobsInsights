using EPiServer.DataAbstraction;
using EPiServer.Scheduler;
using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Logging;

namespace OptiPowerTools.ScheduledJobsInsights.Web.Samples;

/// <summary>
/// Not part of the NuGet package — the worked example for <see cref="IJobExecutionWriter"/> used
/// directly, rather than through <see cref="LoggedScheduledJobBase"/>. This is the escape hatch for
/// code that wants to record an execution without being an Optimizely scheduled job at all.
/// </summary>
/// <remarks>
/// <para>
/// Its practical purpose is filling the execution list. One run writes
/// <see cref="SeededExecutions"/> synthetic executions across several job names, mixing succeeded
/// and failed outcomes, which is enough to push the list past the default <c>PageSize</c> of 50 and
/// make the Next/Previous keyset paging and the job/status filters testable without triggering the
/// other samples dozens of times.
/// </para>
/// <para>
/// Two things worth knowing. The writer stamps <c>StartedAt</c> itself, so every seeded row lands
/// within the same second — which incidentally exercises the cursor's <c>(StartedAt DESC, Id DESC)</c>
/// tie-break, the case ordinary runs rarely hit. And the last row is left deliberately incomplete:
/// <c>BeginExecution</c> without <c>Complete</c>, so it stays <c>Running</c> forever and gives the
/// Running badge, the Running filter and the "—" duration something stable to render.
/// <see cref="SlowMigrationJob"/> covers the genuinely-live version of that state.
/// </para>
/// </remarks>
[ScheduledJob(DisplayName = "Sample: Seed Execution History", IntervalType = ScheduledIntervalType.Days, DefaultEnabled = false)]
public sealed class SeedHistoryJob : LoggedScheduledJobBase
{
    private const int SeededExecutions = 60;

    private static readonly (string Name, string TypeName)[] FakeJobs =
    [
        ("Nightly Price Import", "Contoso.Jobs.NightlyPriceImportJob"),
        ("Catalog Reindex", "Contoso.Jobs.CatalogReindexJob"),
        ("Newsletter Dispatch", "Contoso.Jobs.NewsletterDispatchJob"),
        ("Orphaned Media Sweep", "Contoso.Jobs.OrphanedMediaSweepJob")
    ];

    private readonly IJobExecutionWriter _writer;

    public SeedHistoryJob(IJobExecutionWriter writer, IScheduledJobRepository scheduledJobRepository)
        : base(writer, scheduledJobRepository)
    {
        _writer = writer;
    }

    protected override string ExecuteJob()
    {
        LogInputData(new { SeededExecutions, JobNames = FakeJobs.Select(j => j.Name) });

        var failed = 0;
        for (var i = 0; i < SeededExecutions; i++)
        {
            var fake = FakeJobs[i % FakeJobs.Length];

            // Every fourth run fails, so the Failed filter and the exception panes have data.
            if (SeedExecution(fake, shouldFail: i % 4 == 3))
                failed++;

            if ((i + 1) % 20 == 0)
                OnStatusChanged($"Seeded {i + 1} of {SeededExecutions} executions");
        }

        SeedStuckExecution();

        Log($"Seeded {SeededExecutions} executions ({failed} failed) plus one left Running.", LogSeverity.Success);
        RecordMetric("SeededExecutions", SeededExecutions);

        return $"Seeded {SeededExecutions} executions across {FakeJobs.Length} job names, plus one stuck in Running.";
    }

    private bool SeedExecution((string Name, string TypeName) fake, bool shouldFail)
    {
        var executionId = _writer.BeginExecution(Guid.NewGuid(), fake.Name, fake.TypeName);
        var sequence = 0;

        _writer.SetInputData(executionId, $"{{\"source\":\"{fake.Name}\",\"dryRun\":false}}");
        _writer.Log(executionId, ++sequence, LogSeverity.Info, $"{fake.Name} starting.", LogEntrySource.StatusChanged);

        var itemCount = Random.Shared.Next(20, 400);
        _writer.Log(executionId, ++sequence, LogSeverity.Default, $"Fetched {itemCount} items.", LogEntrySource.DevLog);
        _writer.RecordMetric(executionId, "ItemsProcessed", itemCount, null);
        _writer.RecordMetric(executionId, "DurationMs", Random.Shared.Next(40, 9000), "ms");

        if (shouldFail)
        {
            _writer.Log(executionId, ++sequence, LogSeverity.Error, "Upstream returned HTTP 503.", LogEntrySource.DevLog);
            _writer.Complete(executionId, succeeded: false, resultMessage: null, exception: CaptureException(fake.Name));
            return true;
        }

        _writer.Log(executionId, ++sequence, LogSeverity.Success, $"Processed {itemCount} items.", LogEntrySource.DevLog);
        _writer.Complete(executionId, succeeded: true, resultMessage: $"Processed {itemCount} items.", exception: null);
        return false;
    }

    private void SeedStuckExecution()
    {
        var executionId = _writer.BeginExecution(Guid.NewGuid(), "Stalled Feed Import", "Contoso.Jobs.StalledFeedImportJob");
        _writer.Log(executionId, 1, LogSeverity.Info, "Connecting to feed…", LogEntrySource.StatusChanged);
        _writer.Log(executionId, 2, LogSeverity.Warning, "Left intentionally incomplete by SeedHistoryJob.", LogEntrySource.DevLog);
        // No Complete() — this row stays Running so the badge and filter always have a subject.
    }

    /// <summary>Throws and catches so the seeded exception carries a real stack trace, as a genuine failure would.</summary>
    private static Exception CaptureException(string jobName)
    {
        try
        {
            throw new HttpRequestException($"The remote endpoint for '{jobName}' is unavailable (503).");
        }
        catch (HttpRequestException ex)
        {
            return ex;
        }
    }
}

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
/// <para>
/// Roughly half the seeded rows also carry a result summary, written through
/// <see cref="IJobExecutionWriter.SetResultSummary"/>, so the list's "summary" marker and the detail
/// view's <em>Result summary</em> section both have data without running anything else.
/// <see cref="SummaryShowcaseJob"/> covers the large-summary case.
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

    public SeedHistoryJob(JobLoggingContext context, IJobExecutionWriter writer)
        : base(context)
    {
        // Injected, not taken off the context: the writer is public API precisely so callers like this
        // one can record executions that are not scheduled job runs at all, and DI is how they get it.
        // Optimizely builds jobs through ActivatorUtilities, so a second constructor parameter costs
        // nothing. Reaching through JobLoggingContext would bypass the base class's execution-id guard
        // and sequence counter for this job's *own* run, which is why the context does not expose it.
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
            // Every other run gets a summary, so both marked and unmarked rows appear in the list.
            if (SeedExecution(fake, shouldFail: i % 4 == 3, withSummary: i % 2 == 0))
                failed++;

            if ((i + 1) % 20 == 0)
                OnStatusChanged($"Seeded {i + 1} of {SeededExecutions} executions");
        }

        SeedStuckExecution();

        Log($"Seeded {SeededExecutions} executions ({failed} failed) plus one left Running.", LogSeverity.Success);
        RecordMetric("SeededExecutions", SeededExecutions);

        // The one-shot form, for a job that already holds the finished text rather than building it
        // up as it goes. ReportBuilderJob shows the incremental alternative.
        SetSummary($"""
            Seeded {SeededExecutions} executions across {FakeJobs.Length} job names.
              Failed:  {failed}
              Running: 1 (left deliberately incomplete)
            """);

        return $"Seeded {SeededExecutions} executions across {FakeJobs.Length} job names, plus one stuck in Running.";
    }

    private bool SeedExecution((string Name, string TypeName) fake, bool shouldFail, bool withSummary)
    {
        // BeginExecution returns null when the insights store is unreachable. Nothing to seed onto,
        // so this row is simply skipped — the same shape any direct writer caller should have.
        if (_writer.BeginExecution(Guid.NewGuid(), fake.Name, fake.TypeName) is not { } executionId)
            return false;

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

            if (withSummary)
                _writer.SetResultSummary(executionId, BuildSummary(fake.Name, itemCount, succeeded: false));

            _writer.Complete(executionId, ExecutionStatus.Failed, resultMessage: null, exception: CaptureException(fake.Name));
            return true;
        }

        _writer.Log(executionId, ++sequence, LogSeverity.Success, $"Processed {itemCount} items.", LogEntrySource.DevLog);

        if (withSummary)
            _writer.SetResultSummary(executionId, BuildSummary(fake.Name, itemCount, succeeded: true));

        _writer.Complete(executionId, ExecutionStatus.Succeeded, resultMessage: $"Processed {itemCount} items.", exception: null);
        return false;
    }

    /// <summary>
    /// Builds a small multi-line summary for a seeded row. A plain string here rather than
    /// <see cref="JobResultSummary"/>: that class belongs to a job's own run, and these rows are
    /// written on behalf of jobs that never actually executed.
    /// </summary>
    private static string BuildSummary(string jobName, int itemCount, bool succeeded)
    {
        var outcome = succeeded
            ? $"  Committed : {itemCount:N0}\n  Rejected  : 0"
            : $"  Committed : 0\n  Rejected  : {itemCount:N0} (upstream 503)";

        return $"{jobName}\n{new string('-', jobName.Length)}\n  Fetched   : {itemCount:N0}\n{outcome}";
    }

    private void SeedStuckExecution()
    {
        if (_writer.BeginExecution(Guid.NewGuid(), "Stalled Feed Import", "Contoso.Jobs.StalledFeedImportJob") is not { } executionId)
            return;

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

using EPiServer.DataAbstraction;
using EPiServer.Scheduler;
using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Logging;

namespace OptiPowerTools.ScheduledJobsInsights.Web.Samples;

/// <summary>
/// Not part of the NuGet package — the stress case for the <em>Result summary</em> section, the way
/// <see cref="SeedHistoryJob"/> is the stress case for the execution list.
/// </summary>
/// <remarks>
/// <para>
/// It deliberately writes more than <see cref="JobResultSummary.DefaultMaxLength"/> characters, so
/// one run exercises everything the section has to cope with: a summary long enough to scroll inside
/// its own pane, lines long enough to test wrapping (rather than pushing the CMS chrome sideways),
/// and the truncation notice that closes an over-long summary.
/// </para>
/// <para>
/// It also demonstrates <see cref="LoggedScheduledJobBase.FlushSummary"/>. The job sleeps between
/// batches and checkpoints as it goes, so opening its detail view while it is still Running shows
/// the summary filling in on the two-second poll — the ordinary case, where the summary is written
/// once at the end, would show nothing until the job finished.
/// </para>
/// </remarks>
[ScheduledJob(DisplayName = "Sample: Summary Showcase", IntervalType = ScheduledIntervalType.Days, DefaultEnabled = false)]
public sealed class SummaryShowcaseJob : LoggedScheduledJobBase
{
    private const int Batches = 12;
    private const int ItemsPerBatch = 120;

    public SummaryShowcaseJob(JobLoggingContext context)
        : base(context)
    {
    }

    protected override string ExecuteJob()
    {
        LogInputData(new { Batches, ItemsPerBatch, MaxSummaryLength = Summary.MaxLength });

        Summary.AppendLine($"Rewriting {Batches * ItemsPerBatch:N0} media references across the content tree.");
        Summary.AppendLine($"Summary limit for this installation: {Summary.MaxLength:N0} characters.");

        var processed = 0;

        for (var batch = 1; batch <= Batches; batch++)
        {
            OnStatusChanged($"Batch {batch} of {Batches}");
            Summary.AppendSection($"Batch {batch}");

            for (var item = 0; item < ItemsPerBatch; item++)
            {
                processed++;

                // Long, unbroken paths on purpose: this is what forces the summary pane to wrap
                // instead of widening the page.
                Summary.AppendLine(
                    $"  /globalassets/migrated/{batch:00}/legacy-media-library/{processed:0000}-original-asset-filename-with-no-word-breaks.png → /globalassets/media/{processed:0000}.png");
            }

            Thread.Sleep(250);

            // Checkpoint, so the detail view shows progress while the job is still Running.
            FlushSummary();
            Log($"Batch {batch} committed ({processed:N0} references so far).", LogSeverity.Success);
        }

        RecordMetric("ReferencesRewritten", processed);

        if (Summary.IsTruncated)
        {
            // The summary itself is full at this point, so the note goes to the log instead.
            Log($"Summary hit its {Summary.MaxLength:N0} character limit and was truncated.", LogSeverity.Warning);
        }

        return $"Rewrote {processed:N0} media references.";
    }
}

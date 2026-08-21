using EPiServer.DataAbstraction;
using EPiServer.Scheduler;
using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Logging;

namespace OptiPowerTools.ScheduledJobsInsights.Web.Samples;

/// <summary>
/// Not part of the NuGet package — a link-check report that summarises every one of the
/// <see cref="UrlsChecked"/> URLs it visited, thousands of lines in one summary.
/// </summary>
/// <remarks>
/// <para>
/// This is the volume case, and it is deliberately the opposite of
/// <see cref="SummaryShowcaseJob"/>'s. That job writes long lines and overruns the character limit,
/// so it exercises wrapping and the truncation notice. This one writes short lines and stays inside
/// the limit, so the whole report survives — which is what exercises the summary section's scrolling
/// and the auto-collapse that keeps a report this long from burying the log beneath it. Between them
/// they cover both ways a summary gets big.
/// </para>
/// <para>
/// Per-URL detail is the realistic reason to write a summary this long: it is exactly the record you
/// want when someone asks which link was broken last Tuesday, and it does not belong in the job's
/// one-line result message or in a few thousand log entries.
/// </para>
/// </remarks>
[ScheduledJob(DisplayName = "Sample: Bulk Summary", IntervalType = ScheduledIntervalType.Days, DefaultEnabled = false)]
public sealed class BulkSummaryJob : LoggedScheduledJobBase
{
    private const int UrlsChecked = 2_000;

    private static readonly string[] Sections = ["Products", "Campaigns", "Support articles", "Press releases"];

    public BulkSummaryJob(JobLoggingContext context)
        : base(context)
    {
    }

    protected override string ExecuteJob()
    {
        LogInputData(new { UrlsChecked, Sections });

        var perSection = UrlsChecked / Sections.Length;
        var broken = 0;
        var redirects = 0;

        Summary.AppendLine($"Link check over {UrlsChecked:N0} URLs in {Sections.Length} sections.");

        for (var s = 0; s < Sections.Length; s++)
        {
            OnStatusChanged($"Checking {Sections[s]} ({s + 1} of {Sections.Length})");
            Summary.AppendSection(Sections[s]);

            for (var i = 1; i <= perSection; i++)
            {
                var index = (s * perSection) + i;

                // Deterministic rather than random, so two runs of this job produce byte-identical
                // summaries — handy when comparing what the section renders before and after a change.
                var (status, latencyMs) = index % 47 == 0
                    ? (404, 12)
                    : index % 13 == 0
                        ? (301, 24)
                        : (200, 30 + (index % 90));

                if (status == 404)
                    broken++;
                else if (status == 301)
                    redirects++;

                // Kept short on purpose: the point of this job is line count, not line length.
                Summary.AppendLine($"  {index:0000}  {status}  {latencyMs,3}ms  /p/{index:0000}");
            }

            Log($"{Sections[s]}: {perSection:N0} URLs checked.", LogSeverity.Success);
        }

        Summary.AppendSection("Totals");
        Summary.AppendLine($"  Checked   : {UrlsChecked:N0}");
        Summary.AppendLine($"  Redirects : {redirects:N0}");
        Summary.AppendLine($"  Broken    : {broken:N0}");

        RecordMetric("UrlsChecked", UrlsChecked);
        RecordMetric("BrokenLinks", broken);
        RecordMetric("Redirects", redirects);
        RecordMetric("SummaryCharacters", Summary.Length, "characters");

        if (Summary.IsTruncated)
        {
            // Should not happen at these line lengths, but the job says so rather than leaving a
            // silently half-written report — the same thing any real job should do.
            Log($"Summary hit its {Summary.MaxLength:N0} character limit; the tail was dropped.", LogSeverity.Warning);
        }

        return $"Checked {UrlsChecked:N0} URLs — {broken} broken, {redirects} redirects.";
    }
}

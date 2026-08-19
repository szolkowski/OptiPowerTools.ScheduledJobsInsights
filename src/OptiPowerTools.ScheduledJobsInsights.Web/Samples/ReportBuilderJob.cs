using EPiServer.DataAbstraction;
using EPiServer.Scheduler;
using OptiPowerTools.ScheduledJobsInsights.Logging;

namespace OptiPowerTools.ScheduledJobsInsights.Web.Samples;

/// <summary>
/// Not part of the NuGet package — the worked example for <see cref="LoggedScheduledJobBase.Summary"/>,
/// alongside <see cref="LoggedScheduledJobBase.LogInputData"/> and custom
/// <see cref="LoggedScheduledJobBase.RecordMetric"/> calls.
/// </summary>
/// <remarks>
/// The point of the summary is the division of labour with the returned string. What
/// <see cref="ExecuteJob"/> returns is Optimizely's "last execution message", squeezed into one cell
/// of the CMS admin grid, so it stays a single sentence. The per-region breakdown, the warnings and
/// the totals go into the summary, which keeps its newlines and is read in the detail view. Building
/// it up as the work happens — rather than assembling one big string at the end — is what makes it
/// natural to record something in every branch.
/// </remarks>
[ScheduledJob(DisplayName = "Sample: Report Builder", IntervalType = ScheduledIntervalType.Days, DefaultEnabled = false)]
public sealed class ReportBuilderJob : LoggedScheduledJobBase
{
    private static readonly string[] Regions = ["EMEA", "AMER", "APAC", "LATAM"];

    public ReportBuilderJob(IJobExecutionWriter writer, IScheduledJobRepository scheduledJobRepository)
        : base(writer, scheduledJobRepository)
    {
    }

    protected override string ExecuteJob()
    {
        var from = DateTime.UtcNow.AddDays(-7).Date;
        var to = DateTime.UtcNow.Date;
        LogInputData(new { From = from, To = to, Regions });

        Log($"Building report for {from:yyyy-MM-dd} to {to:yyyy-MM-dd}.");

        Summary.AppendLine($"Weekly export for {from:yyyy-MM-dd} … {to:yyyy-MM-dd}");
        Summary.AppendSection("Rows by region");

        var totalRows = 0;
        var emptyRegions = new List<string>();

        foreach (var region in Regions)
        {
            var rows = Random.Shared.Next(0, 1500);
            totalRows += rows;

            // Chained appends read naturally when a line is built from parts.
            Summary.Append("  ")
                   .Append(region.PadRight(6))
                   .AppendLine($"{rows,6:N0} rows");

            if (rows == 0)
                emptyRegions.Add(region);
        }

        Thread.Sleep(300);

        if (emptyRegions.Count > 0)
        {
            Summary.AppendSection("Warnings");
            foreach (var region in emptyRegions)
                Summary.AppendLine($"  {region} returned no rows — check the upstream feed.");
        }

        var reportSizeBytes = totalRows * 128L;

        Summary.AppendSection("Totals");
        Summary.AppendLine($"  Rows exported : {totalRows:N0}");
        Summary.AppendLine($"  Report size   : {reportSizeBytes / 1024:N0} KB");
        Summary.AppendLine($"  Regions empty : {emptyRegions.Count}");

        RecordMetric("RowsExported", totalRows);
        RecordMetric("ReportSizeBytes", reportSizeBytes, "bytes");

        // One line, because Optimizely shows this one in a grid cell. Everything else is in Summary.
        return $"Report built with {totalRows:N0} rows ({reportSizeBytes / 1024:N0} KB).";
    }
}

using EPiServer.DataAbstraction;
using EPiServer.Scheduler;
using OptiPowerTools.ScheduledJobsInsights.Logging;

namespace OptiPowerTools.ScheduledJobsInsights.Web.Samples;

/// <summary>
/// Not part of the NuGet package — a manual-testing sample showing <see cref="LoggedScheduledJobBase.LogInputData"/>
/// and custom <see cref="LoggedScheduledJobBase.RecordMetric"/> calls.
/// </summary>
[ScheduledJob(DisplayName = "Sample: Report Builder", IntervalType = ScheduledIntervalType.Days, DefaultEnabled = false)]
public sealed class ReportBuilderJob : LoggedScheduledJobBase
{
    public ReportBuilderJob(IJobExecutionWriter writer, IScheduledJobRepository scheduledJobRepository)
        : base(writer, scheduledJobRepository)
    {
    }

    protected override string ExecuteJob()
    {
        var from = DateTime.UtcNow.AddDays(-7).Date;
        var to = DateTime.UtcNow.Date;
        LogInputData(new { From = from, To = to, Region = "EMEA" });

        Log($"Building report for {from:yyyy-MM-dd} to {to:yyyy-MM-dd}.");

        var rowsExported = Random.Shared.Next(500, 5000);
        var reportSizeBytes = rowsExported * 128L;
        Thread.Sleep(300);

        RecordMetric("RowsExported", rowsExported);
        RecordMetric("ReportSizeBytes", reportSizeBytes, "bytes");

        return $"Report built with {rowsExported} rows ({reportSizeBytes / 1024} KB).";
    }
}

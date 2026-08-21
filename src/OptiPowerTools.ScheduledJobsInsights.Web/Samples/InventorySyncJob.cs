using EPiServer.DataAbstraction;
using EPiServer.Scheduler;
using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Logging;

namespace OptiPowerTools.ScheduledJobsInsights.Web.Samples;

/// <summary>
/// Not part of the NuGet package — a manual-testing sample showing multi-phase logging at
/// different <see cref="LogSeverity"/> levels via <see cref="LoggedScheduledJobBase.Log"/>.
/// </summary>
[ScheduledJob(DisplayName = "Sample: Inventory Sync", IntervalType = ScheduledIntervalType.Days, DefaultEnabled = false)]
public sealed class InventorySyncJob : LoggedScheduledJobBase
{
    private static readonly string[] Warehouses = ["North", "South", "East", "West"];

    public InventorySyncJob(JobLoggingContext context)
        : base(context)
    {
    }

    protected override string ExecuteJob()
    {
        Log("Starting inventory sync across all warehouses.");

        var synced = 0;
        foreach (var warehouse in Warehouses)
        {
            Thread.Sleep(150);
            Log($"Synced warehouse '{warehouse}'.", LogSeverity.Success);
            synced++;
        }

        Log("One SKU had a stale timestamp and was re-queued for next run.", LogSeverity.Warning);

        return $"Synced {synced} warehouse(s).";
    }
}

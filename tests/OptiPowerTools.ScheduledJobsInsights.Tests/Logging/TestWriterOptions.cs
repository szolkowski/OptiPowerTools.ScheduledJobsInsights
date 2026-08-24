using Microsoft.Extensions.Options;
using OptiPowerTools.ScheduledJobsInsights.Configuration;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Logging;

/// <summary>Option snapshots for constructing a <c>JobExecutionWriter</c> under test.</summary>
internal static class TestWriterOptions
{
    /// <summary>Everything left at its default.</summary>
    public static IOptions<OptiPowerToolsScheduledJobsInsightsOptions> Default =>
        Options.Create(new OptiPowerToolsScheduledJobsInsightsOptions());

    /// <summary>Defaults except for a summary limit, for exercising truncation cheaply.</summary>
    public static IOptions<OptiPowerToolsScheduledJobsInsightsOptions> WithSummaryLimit(int maxResultSummaryLength) =>
        Options.Create(new OptiPowerToolsScheduledJobsInsightsOptions
        {
            MaxResultSummaryLength = maxResultSummaryLength
        });
}

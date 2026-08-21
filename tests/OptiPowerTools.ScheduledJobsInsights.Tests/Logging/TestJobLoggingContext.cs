using EPiServer.DataAbstraction;
using Microsoft.Extensions.Options;
using NSubstitute;
using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Logging;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Logging;

/// <summary>
/// Builds the <see cref="JobLoggingContext"/> a logged job needs, so tests name only the
/// collaborator they care about.
/// </summary>
internal static class TestJobLoggingContext
{
    /// <summary>
    /// A context around <paramref name="writer"/>.
    /// </summary>
    /// <param name="writer">The writer under observation.</param>
    /// <param name="scheduledJobRepository">Job-name lookup; substituted when not supplied.</param>
    /// <param name="maxResultSummaryLength">
    /// Summary bound. Zero means "not configured", which the context resolves to
    /// <see cref="JobResultSummary.DefaultMaxLength"/>.
    /// </param>
    /// <param name="timeProvider">Clock; the system clock when not supplied.</param>
    public static JobLoggingContext For(
        IJobExecutionWriter writer,
        IScheduledJobRepository? scheduledJobRepository = null,
        int maxResultSummaryLength = 0,
        TimeProvider? timeProvider = null) =>
        new(writer,
            scheduledJobRepository ?? Substitute.For<IScheduledJobRepository>(),
            Options.Create(new OptiPowerToolScheduledJobsInsightsOptions
            {
                MaxResultSummaryLength = maxResultSummaryLength
            }),
            timeProvider ?? TimeProvider.System);
}

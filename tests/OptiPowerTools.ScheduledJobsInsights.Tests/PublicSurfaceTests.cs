using OptiPowerTools.ScheduledJobsInsights.Logging;

namespace OptiPowerTools.ScheduledJobsInsights.Tests;

/// <summary>
/// Pins the exported surface of the package.
/// </summary>
/// <remarks>
/// <para>
/// 1.0 freezes this list: after the tag, adding to it is a commitment and removing from it is a
/// breaking change. The mechanism meant to enforce that is
/// <c>PackageValidationBaselineVersion</c>, which compares against the previously published package —
/// and it cannot help on the release that establishes the baseline, because there is nothing yet to
/// compare against. This test covers exactly that gap, and keeps covering it locally afterwards
/// without needing to download a baseline package.
/// </para>
/// <para>
/// It is deliberately a whole-list snapshot rather than a count. A count catches a type appearing but
/// not a type being swapped, and the failure message from a set difference tells you which type moved
/// — a count difference makes you go and find out. The Razor compiler is the usual accidental author
/// here: a stray <c>.razor</c> file adds a public type nobody wrote.
/// </para>
/// <para>
/// If a change to this list is intended, update it in the same commit and say why in the message.
/// </para>
/// </remarks>
public class PublicSurfaceTests
{
    /// <summary>
    /// Every type the package exports, as of 1.0. Sorted ordinally.
    /// </summary>
    private static readonly string[] Expected =
    [
        "OptiPowerTools.ScheduledJobsInsights.Cms.ScheduledJobsInsightsAuthorization",
        "OptiPowerTools.ScheduledJobsInsights.Cms.ScheduledJobsInsightsCmsController",
        "OptiPowerTools.ScheduledJobsInsights.Cms.ScheduledJobsInsightsMenuProvider",
        "OptiPowerTools.ScheduledJobsInsights.Components.Pages.Detail",
        "OptiPowerTools.ScheduledJobsInsights.Components.Pages.Index",
        "OptiPowerTools.ScheduledJobsInsights.Components.Pages.Retention",
        "OptiPowerTools.ScheduledJobsInsights.Components.Shared.AccordionSection",
        "OptiPowerTools.ScheduledJobsInsights.Configuration.CmsMenuPlacement",
        "OptiPowerTools.ScheduledJobsInsights.Configuration.ExecutionStatus",
        "OptiPowerTools.ScheduledJobsInsights.Configuration.LogEntrySource",
        "OptiPowerTools.ScheduledJobsInsights.Configuration.LogSeverity",
        "OptiPowerTools.ScheduledJobsInsights.Configuration.OptiPowerToolsScheduledJobsInsightsOptions",
        "OptiPowerTools.ScheduledJobsInsights.Extensions.ApplicationBuilderExtensions",
        "OptiPowerTools.ScheduledJobsInsights.Extensions.ServiceCollectionExtensions",
        "OptiPowerTools.ScheduledJobsInsights.Jobs.ScheduledJobsInsightsCleanupJob",
        "OptiPowerTools.ScheduledJobsInsights.Logging.IJobExecutionWriter",
        "OptiPowerTools.ScheduledJobsInsights.Logging.JobLoggingContext",
        "OptiPowerTools.ScheduledJobsInsights.Logging.JobMetricNames",
        "OptiPowerTools.ScheduledJobsInsights.Logging.JobResultSummary",
        "OptiPowerTools.ScheduledJobsInsights.Logging.LoggedScheduledJobBase",
        "OptiPowerTools.ScheduledJobsInsights.Retention.JobRetentionAttribute",
        "OptiPowerTools.ScheduledJobsInsights.Retention.RetentionPeriod"
    ];

    private static string[] Actual() =>
        [.. typeof(LoggedScheduledJobBase).Assembly
            .GetExportedTypes()
            .Select(type => type.FullName!)
            .OrderBy(name => name, StringComparer.Ordinal)];

    [Fact]
    public void TheExportedSurface_IsExactlyWhatWasFrozen()
    {
        var actual = Actual();

        // Reported as two directed differences rather than one equality failure: "which type appeared"
        // and "which type vanished" are different mistakes with different fixes, and a bare
        // collection-inequality message makes you diff two 22-line lists by eye.
        Assert.Empty(actual.Except(Expected, StringComparer.Ordinal));
        Assert.Empty(Expected.Except(actual, StringComparer.Ordinal));
    }

    [Fact]
    public void TheMigrationsStayOffTheExportedSurface()
    {
        // EF finds migrations through GetConstructableTypes(), which does not require public. Five
        // generically-named Migration classes on a frozen surface would be tracked for ever — and the
        // scaffolder emits them public by default, so this is a mistake that regenerates itself every
        // time somebody adds a migration.
        Assert.DoesNotContain(Actual(), name => name.Contains(".Migrations.", StringComparison.Ordinal));
    }
}

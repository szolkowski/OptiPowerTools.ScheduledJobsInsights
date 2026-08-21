using EPiServer;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.Scheduler;
using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Logging;

namespace OptiPowerTools.ScheduledJobsInsights.Web.Samples;

/// <summary>
/// Not part of the NuGet package — shows that a logged job is still an ordinary DI citizen. Beyond
/// the <see cref="JobLoggingContext"/> the base class needs, this one takes an
/// <see cref="IContentLoader"/> and uses it for real work. Optimizely builds a fresh job instance per
/// execution through <c>ActivatorUtilities.GetServiceOrCreateInstance</c>, so any registered service
/// can be injected; only the context has to be forwarded to <c>base</c>.
/// </summary>
[ScheduledJob(DisplayName = "Sample: Content Audit", IntervalType = ScheduledIntervalType.Days, DefaultEnabled = false)]
public sealed class ContentAuditJob : LoggedScheduledJobBase
{
    private readonly IContentLoader _contentLoader;

    public ContentAuditJob(JobLoggingContext context, IContentLoader contentLoader)
        : base(context)
    {
        _contentLoader = contentLoader;
    }

    protected override string ExecuteJob()
    {
        // Root rather than the start page: a scheduled job runs without site context.
        var root = ContentReference.RootPage;
        LogInputData(new { Root = root.ToString(), IncludeChildCounts = true });

        var topLevel = _contentLoader.GetChildren<IContent>(root).ToList();
        Log($"Loaded {topLevel.Count} top-level item(s) under root.", LogSeverity.Info);

        var totalChildren = 0;
        foreach (var item in topLevel)
        {
            var childCount = _contentLoader.GetChildren<IContent>(item.ContentLink).Count();
            totalChildren += childCount;

            if (childCount == 0)
                Log($"'{item.Name}' has no children.", LogSeverity.Debug);
            else
                Log($"'{item.Name}' has {childCount} child item(s).", LogSeverity.Success);
        }

        RecordMetric("TopLevelItems", topLevel.Count);
        RecordMetric("ChildItems", totalChildren);

        return $"Audited {topLevel.Count} top-level item(s) and {totalChildren} child item(s).";
    }
}

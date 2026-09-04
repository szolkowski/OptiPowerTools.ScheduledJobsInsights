using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using OptiPowerTools.ScheduledJobsInsights.Configuration;

namespace OptiPowerTools.ScheduledJobsInsights.Extensions;

/// <summary>
/// The things this package checks at startup and reports rather than fixes.
/// </summary>
/// <remarks>
/// <para>
/// Separated from <see cref="ApplicationBuilderExtensions"/> so each check is a function of its
/// inputs rather than of a live application: an endpoint list, a file provider, an options object.
/// That is what makes them testable — the wiring around them needs a real routing pipeline, which
/// is why the checks used to be untested despite being the part with the logic in it.
/// </para>
/// <para>
/// Everything here reports and returns. None of it throws, and none of it changes behaviour: a
/// misconfiguration this package can see is still the host's to fix, and a diagnostic that took the
/// application down would be worse than the fault it describes.
/// </para>
/// </remarks>
internal static partial class StartupDiagnostics
{
    /// <summary>The logger category every startup diagnostic writes under.</summary>
    public const string LoggerCategory = "OptiPowerTools.ScheduledJobsInsights";

    /// <summary>
    /// Reports this package's endpoints resolving more than once.
    /// </summary>
    /// <remarks>
    /// Duplicates surface at request time as <c>AmbiguousMatchException</c> from
    /// <c>DefaultEndpointSelector</c>, naming two identical-looking candidates and nothing else —
    /// and because endpoint matching precedes authentication, an anonymous request hits it too.
    /// Two wirings cause it on an Optimizely host, they are independent, and they need opposite
    /// fixes; the messages say which is which.
    /// </remarks>
    /// <param name="endpoints">The application's resolved endpoints.</param>
    /// <param name="options">Supplies the page path to look for.</param>
    /// <param name="logger">Where the findings go.</param>
    public static void ReportDuplicateEndpoints(
        IEnumerable<Endpoint> endpoints,
        OptiPowerToolsScheduledJobsInsightsOptions options,
        ILogger logger)
    {
        var routes = endpoints.OfType<RouteEndpoint>().ToArray();

        // Both of the package's pages, because a duplicated controller duplicates every action on it:
        // reporting only the list would have named one of the two symptoms and left the other to be
        // discovered by clicking Retention.
        foreach (var path in new[] { options.CmsShellPath, options.CmsRetentionPath })
        {
            var page = routes.Count(endpoint => IsSamePath(endpoint.RoutePattern.RawText, path));

            if (page > 1)
            {
                logger.LogError(
                    "ScheduledJobsInsights' page is registered {Count} times at {Path}, so every request to it fails with AmbiguousMatchException. The usual cause is calling both MapContent() and MapControllers(): on some Optimizely stacks MapContent() already maps attribute-routed controllers, and the second call duplicates all of them. Remove MapControllers() if your host is one of those — but check first, because on a plain CMS host it is required and removing it makes this page return 404 instead.",
                    page,
                    path);
            }
        }

        var hub = routes.Count(endpoint => IsSamePath(endpoint.RoutePattern.RawText, "/_blazor"));

        if (hub > 1)
        {
            logger.LogError(
                "The Blazor hub is registered {Count} times at /_blazor, so every Blazor request in this application fails with AmbiguousMatchException — not only this package's pages. Map the hub inside the host's own UseEndpoints(...) block with endpoints.MapOptiPowerToolsScheduledJobsInsights(), placed before MapContent(), and keep UseOptiPowerToolsScheduledJobsInsights() after that block for migrations. If the host maps its own hub, set MapBlazorHub to false.",
                hub);
        }
    }

    /// <summary>
    /// States the resolved installation-wide retention.
    /// </summary>
    /// <remarks>
    /// <c>RetentionDays</c> is the one sizing option the validator cannot reject, because zero or
    /// less is a documented, supported value meaning "keep indefinitely". That makes a typo
    /// indistinguishable from an intention: somebody writing <c>-30</c> for "thirty days" gets
    /// unbounded growth, silently, and the symptom appears months later as a table nothing trims.
    /// A negative value is called out separately from zero for that reason — zero is the documented
    /// way to ask for indefinite, a negative number is almost always a mistake.
    /// </remarks>
    /// <param name="options">Supplies the configured retention.</param>
    /// <param name="logger">Where the finding goes.</param>
    public static void ReportResolvedRetention(OptiPowerToolsScheduledJobsInsightsOptions options, ILogger logger)
    {
        if (options.RetentionDays > 0)
            LogRetentionInDays(logger, options.RetentionDays);
        else if (options.RetentionDays == 0)
            LogRetentionIndefinite(logger);
        else
            LogRetentionNegative(logger, options.RetentionDays);
    }

    /// <remarks>
    /// Source-generated, like the rest of the logging here: passing the day count to
    /// <c>LogInformation</c> boxes it into a <c>params object?[]</c> before the logger has decided
    /// whether the level is even enabled, which is what <c>CA1873</c> objects to.
    /// </remarks>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "ScheduledJobsInsights default retention: {RetentionDays} day(s). Jobs with a rule of their own are unaffected.")]
    private static partial void LogRetentionInDays(ILogger logger, int retentionDays);

    /// <remarks>Zero is the documented way to ask for indefinite retention, so this is not a warning.</remarks>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "ScheduledJobsInsights default retention is indefinite (RetentionDays = 0), so the default sweep trims nothing. Only jobs with a rule of their own will be trimmed.")]
    private static partial void LogRetentionIndefinite(ILogger logger);

    /// <remarks>
    /// Warning rather than Information: a negative value behaves identically to zero, but zero is a
    /// choice and a negative number is almost always a day count that lost its sign.
    /// </remarks>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "ScheduledJobsInsights default retention is indefinite because RetentionDays is {RetentionDays}, which is negative. Zero is the documented way to ask for indefinite retention; if you meant a number of days, set a positive value — as written, execution history will grow without bound.")]
    private static partial void LogRetentionNegative(ILogger logger, int retentionDays);

    /// <summary>
    /// Reports the host application missing the static web assets this package's UI needs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one hosting requirement a consumer owns and can silently get wrong.
    /// <c>blazor.server.js</c> comes from <c>Microsoft.AspNetCore.App.Internal.Assets</c>, which the
    /// Web SDK references only when the <em>application</em> contains <c>.razor</c> files. This
    /// package's live in the package, so a consumer whose own project has none never gets it, and
    /// the package cannot set it for them: NuGet resolves that implicit reference during restore,
    /// before package MSBuild assets are imported.
    /// </para>
    /// <para>
    /// Warning rather than Critical, and silent when there is no file provider to ask: this probes
    /// the host's asset pipeline, and a diagnostic that cried wolf on a correctly configured host
    /// would be worse than no diagnostic at all.
    /// </para>
    /// </remarks>
    /// <param name="webRoot">The host's web-root file provider, or <c>null</c> if unavailable.</param>
    /// <param name="logger">Where the finding goes.</param>
    public static void ReportMissingWebAssets(IFileProvider? webRoot, ILogger logger)
    {
        if (webRoot is null)
            return;

        if (webRoot.GetFileInfo("_framework/blazor.server.js").Exists)
            return;

        logger.LogWarning(
            "ScheduledJobsInsights could not find _framework/blazor.server.js among the application's static web assets. Its pages will render but will not become interactive: the Blazor circuit cannot start, and the log viewer will appear empty. Add <RequiresAspNetWebAssets>true</RequiresAspNetWebAssets> to the hosting application's project file and rebuild.");
    }

    /// <summary>
    /// Whether two route patterns address the same path, ignoring a leading slash and casing.
    /// </summary>
    /// <remarks>
    /// Route patterns are stored without a leading slash in some registrations and with one in
    /// others, so a raw string comparison misses the duplicate it is looking for.
    /// </remarks>
    private static bool IsSamePath(string? routePattern, string path) =>
        routePattern is not null
        && string.Equals(
            "/" + routePattern.TrimStart('/'),
            "/" + path.TrimStart('/'),
            StringComparison.OrdinalIgnoreCase);
}

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Extensions;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Extensions;

/// <summary>
/// The startup checks. Each one exists because its fault is otherwise silent, so the log line is the
/// behaviour and asserting on it is the point rather than a shortcut.
/// </summary>
public class StartupDiagnosticsTests
{
    private static RouteEndpoint Route(string pattern) =>
        new(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse(pattern),
            order: 0,
            EndpointMetadataCollection.Empty,
            displayName: pattern);

    private static (RecordingLogger<StartupDiagnosticsTests> Logger, OptiPowerToolsScheduledJobsInsightsOptions Options) Setup() =>
        (new RecordingLogger<StartupDiagnosticsTests>(), new OptiPowerToolsScheduledJobsInsightsOptions());

    // ---------- duplicate endpoints ----------

    [Fact]
    public void DuplicateEndpoints_ASinglyRegisteredApplication_ReportsNothing()
    {
        var (logger, options) = Setup();

        StartupDiagnostics.ReportDuplicateEndpoints(
            [Route(options.CmsShellPath), Route("/_blazor"), Route("/something/else")],
            options,
            logger);

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void DuplicateEndpoints_ThePageTwice_IsReportedWithTheCountAndThePath()
    {
        var (logger, options) = Setup();

        StartupDiagnostics.ReportDuplicateEndpoints(
            [Route(options.CmsShellPath), Route(options.CmsShellPath)],
            options,
            logger);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("registered 2 times", entry.Message, StringComparison.Ordinal);
        Assert.Contains(options.CmsShellPath, entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateEndpoints_TheRetentionPageTwice_IsReportedToo()
    {
        // A duplicated controller duplicates every action on it, so the retention route fails the same
        // way as the list. Reporting only the list would have named one symptom and left the other to
        // be found by clicking Retention.
        var (logger, options) = Setup();

        StartupDiagnostics.ReportDuplicateEndpoints(
            [Route(options.CmsShellPath), Route(options.CmsRetentionPath), Route(options.CmsRetentionPath)],
            options,
            logger);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("registered 2 times", entry.Message, StringComparison.Ordinal);
        Assert.Contains(options.CmsRetentionPath, entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateEndpoints_BothPagesTwice_ReportsEachOne()
    {
        var (logger, options) = Setup();

        StartupDiagnostics.ReportDuplicateEndpoints(
            [
                Route(options.CmsShellPath), Route(options.CmsShellPath),
                Route(options.CmsRetentionPath), Route(options.CmsRetentionPath)
            ],
            options,
            logger);

        Assert.Equal(2, logger.Entries.Count);
        Assert.Contains(logger.Entries, e => e.Message.Contains(options.CmsShellPath, StringComparison.Ordinal));
        Assert.Contains(logger.Entries, e => e.Message.Contains(options.CmsRetentionPath, StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateEndpoints_ThePageMessage_WarnsAgainstDeletingMapControllersBlindly()
    {
        // The fix inverts by stack: on a Commerce host MapContent() already maps attribute-routed
        // controllers so MapControllers() duplicates them, while on a plain CMS host removing it
        // makes this page 404 with nothing logged. A message that only said "remove MapControllers()"
        // would be actively harmful to half the consumers who read it.
        var (logger, options) = Setup();

        StartupDiagnostics.ReportDuplicateEndpoints(
            [Route(options.CmsShellPath), Route(options.CmsShellPath)], options, logger);

        Assert.Contains("404", Assert.Single(logger.Entries).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateEndpoints_TheHubTwice_IsReportedSeparately()
    {
        var (logger, options) = Setup();

        StartupDiagnostics.ReportDuplicateEndpoints([Route("/_blazor"), Route("/_blazor")], options, logger);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("_blazor", entry.Message, StringComparison.Ordinal);
        Assert.Contains("not only this package", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateEndpoints_BothAtOnce_ReportsBoth()
    {
        // They are independent causes with opposite fixes, so one must not mask the other.
        var (logger, options) = Setup();

        StartupDiagnostics.ReportDuplicateEndpoints(
            [Route(options.CmsShellPath), Route(options.CmsShellPath), Route("/_blazor"), Route("/_blazor")],
            options,
            logger);

        Assert.Equal(2, logger.Entries.Count);
        Assert.All(logger.Entries, entry => Assert.Equal(LogLevel.Error, entry.Level));
    }

    [Theory]
    [InlineData("/ScheduledJobsInsightsCms/Index", "ScheduledJobsInsightsCms/Index")]
    [InlineData("ScheduledJobsInsightsCms/Index", "/ScheduledJobsInsightsCms/Index")]
    [InlineData("/scheduledjobsinsightscms/index", "/ScheduledJobsInsightsCms/Index")]
    public void DuplicateEndpoints_MatchesRegardlessOfLeadingSlashOrCase(string registered, string configured)
    {
        // Route patterns are stored with a leading slash by some registrations and without by others.
        // Comparing raw strings would miss the very duplicate this exists to find.
        var logger = new RecordingLogger<StartupDiagnosticsTests>();
        var options = new OptiPowerToolsScheduledJobsInsightsOptions { CmsShellPath = configured };

        StartupDiagnostics.ReportDuplicateEndpoints([Route(registered), Route(registered)], options, logger);

        Assert.Single(logger.Entries);
    }

    [Fact]
    public void DuplicateEndpoints_NonRouteEndpoints_AreIgnoredRatherThanThrowingOn()
    {
        var (logger, options) = Setup();

        var plain = new Endpoint(_ => Task.CompletedTask, EndpointMetadataCollection.Empty, "not a route");

        StartupDiagnostics.ReportDuplicateEndpoints([plain, plain, Route(options.CmsShellPath)], options, logger);

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void DuplicateEndpoints_NoEndpointsAtAll_ReportsNothing() =>
        Assert.Empty(RunWithNoEndpoints().Entries);

    private static RecordingLogger<StartupDiagnosticsTests> RunWithNoEndpoints()
    {
        var (logger, options) = Setup();
        StartupDiagnostics.ReportDuplicateEndpoints([], options, logger);
        return logger;
    }

    // ---------- resolved retention ----------

    [Fact]
    public void Retention_APositiveValue_IsStatedAtInformation()
    {
        var logger = new RecordingLogger<StartupDiagnosticsTests>();

        StartupDiagnostics.ReportResolvedRetention(
            new OptiPowerToolsScheduledJobsInsightsOptions { RetentionDays = 30 }, logger);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("30 day(s)", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Retention_Zero_IsStatedAsIndefiniteWithoutAWarning()
    {
        // Zero is the documented way to ask for indefinite retention, so warning about it would be
        // crying wolf at somebody who configured exactly what they meant.
        var logger = new RecordingLogger<StartupDiagnosticsTests>();

        StartupDiagnostics.ReportResolvedRetention(
            new OptiPowerToolsScheduledJobsInsightsOptions { RetentionDays = 0 }, logger);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("indefinite", entry.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-30)]
    public void Retention_ANegativeValue_IsWarnedAbout(int days)
    {
        // The validator cannot reject it, because "0 or less" is documented as indefinite. So -30
        // meaning "thirty days" is accepted and silently disables cleanup for ever.
        var logger = new RecordingLogger<StartupDiagnosticsTests>();

        StartupDiagnostics.ReportResolvedRetention(
            new OptiPowerToolsScheduledJobsInsightsOptions { RetentionDays = days }, logger);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("negative", entry.Message, StringComparison.Ordinal);
    }

    // ---------- static web assets ----------

    private sealed class StubFileProvider(bool exists) : IFileProvider
    {
        public string? Requested { get; private set; }

        public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;

        public IFileInfo GetFileInfo(string subpath)
        {
            Requested = subpath;
            return exists ? new StubFileInfo() : new NotFoundFileInfo(subpath);
        }

        // Never signals; nothing under test watches for changes.
        public Microsoft.Extensions.Primitives.IChangeToken Watch(string filter) => new NeverChanges();

        private sealed class NeverChanges : Microsoft.Extensions.Primitives.IChangeToken
        {
            public bool HasChanged => false;
            public bool ActiveChangeCallbacks => false;
            public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) =>
                new NoopDisposable();

            private sealed class NoopDisposable : IDisposable
            {
                public void Dispose() { }
            }
        }

        private sealed class StubFileInfo : IFileInfo
        {
            public bool Exists => true;
            public long Length => 1;
            public string? PhysicalPath => null;
            public string Name => "blazor.server.js";
            public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
            public bool IsDirectory => false;
            public Stream CreateReadStream() => Stream.Null;
        }
    }

    [Fact]
    public void WebAssets_WhenTheScriptIsPresent_ReportsNothing()
    {
        var logger = new RecordingLogger<StartupDiagnosticsTests>();
        var provider = new StubFileProvider(exists: true);

        StartupDiagnostics.ReportMissingWebAssets(provider, logger);

        Assert.Empty(logger.Entries);
        Assert.Equal("_framework/blazor.server.js", provider.Requested);
    }

    [Fact]
    public void WebAssets_WhenTheScriptIsMissing_NamesTheCsprojPropertyToSet()
    {
        // The failure is otherwise silent: the page renders, the circuit never starts, and the log
        // viewer is an empty box. The property name is the whole value of the message.
        var logger = new RecordingLogger<StartupDiagnosticsTests>();

        StartupDiagnostics.ReportMissingWebAssets(new StubFileProvider(exists: false), logger);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("RequiresAspNetWebAssets", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WebAssets_WithNoFileProvider_StaysSilentRatherThanGuessing()
    {
        // A probe of the host's asset pipeline that cried wolf on a correctly configured host would
        // be worse than no probe at all.
        var logger = new RecordingLogger<StartupDiagnosticsTests>();

        StartupDiagnostics.ReportMissingWebAssets(webRoot: null, logger);

        Assert.Empty(logger.Entries);
    }
}

namespace OptiPowerTools.ScheduledJobsInsights.Configuration;

/// <summary>
/// Configuration options for the OptiPowerTools ScheduledJobsInsights Blazor integration.
/// </summary>
public class OptiPowerToolScheduledJobsInsightsOptions
{
    /// <summary>
    /// SQL Server connection string used to store job executions, logs, and metrics. Required — there is
    /// no fallback to the CMS's own "EPiServerDB" connection string.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Whether to automatically apply pending EF Core migrations at startup. The schema itself
    /// (<see cref="Data.ScheduledJobsInsightsDbContext.SchemaName"/>) is a fixed constant, not configurable.
    /// Defaults to true.
    /// </summary>
    public bool AutoMigrateDatabase { get; set; } = true;

    /// <summary>
    /// How many days of job execution history to retain. Enforced by <see cref="Jobs.ScheduledJobsInsightsCleanupJob"/>.
    /// Defaults to 30.
    /// </summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>
    /// Maximum number of executions deleted per batch by the cleanup job, to avoid large single-transaction deletes.
    /// Defaults to 500.
    /// </summary>
    public int CleanupBatchSize { get; set; } = 500;

    /// <summary>
    /// Capacity of the in-memory channel that buffers log/metric writes before they're flushed to the database.
    /// If the channel is momentarily full, writes fall back to a synchronous insert rather than blocking or dropping data.
    /// Defaults to 10,000.
    /// </summary>
    public int LogChannelCapacity { get; set; } = 10_000;

    /// <summary>
    /// Maximum number of buffered log/metric records flushed to the database in a single batch.
    /// Defaults to 100.
    /// </summary>
    public int LogBatchSize { get; set; } = 100;

    /// <summary>
    /// Maximum time buffered log/metric records wait before being flushed, even if <see cref="LogBatchSize"/> hasn't been reached.
    /// Defaults to 500ms.
    /// </summary>
    public TimeSpan LogFlushInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Maximum number of characters retained in an execution's result summary. Appends past this
    /// limit are discarded and the stored text ends with a truncation notice.
    /// Defaults to <see cref="Logging.JobResultSummary.DefaultMaxLength"/> (100,000).
    /// </summary>
    /// <remarks>
    /// The summary is stored in a single unbounded column, so this is the only thing standing
    /// between a job that appends a line per processed row and a multi-megabyte row. Values of zero
    /// or less are ignored in favour of the default.
    /// </remarks>
    public int MaxResultSummaryLength { get; set; } = Logging.JobResultSummary.DefaultMaxLength;

    /// <summary>
    /// Number of executions shown per page in the Blazor execution list.
    /// Defaults to 50.
    /// </summary>
    public int PageSize { get; set; } = 50;

    /// <summary>
    /// The title displayed in the CMS shell chrome and browser tab.
    /// Defaults to "Scheduled Jobs Insights".
    /// </summary>
    public string PageTitle { get; set; } = "Scheduled Jobs Insights";

    /// <summary>
    /// The Optimizely/EPiServer roles authorized to access the page and the retention screen.
    /// Defaults to Administrators, CmsAdmins, and WebAdmins.
    /// </summary>
    /// <remarks>
    /// Ignored when <see cref="AuthorizationPolicy"/> names a policy of your own, or when
    /// <see cref="AllowAnyAuthenticatedUser"/> is set.
    /// </remarks>
    public IList<string> AuthorizedRoles { get; set; } = ["Administrators", "CmsAdmins", "WebAdmins"];

    /// <summary>
    /// Name of an authorization policy registered by the host, used instead of the built-in role
    /// check. Leave <c>null</c> to authorize on <see cref="AuthorizedRoles"/>.
    /// </summary>
    /// <remarks>
    /// The named policy is applied as ordinary endpoint authorization metadata, so it composes with
    /// whatever else the application does. Startup fails with a named error if no such policy is
    /// registered — a silently unenforced policy would be the worst possible outcome here.
    /// </remarks>
    public string? AuthorizationPolicy { get; set; }

    /// <summary>
    /// Grants access to <em>every authenticated user</em>, with no role or policy check.
    /// Defaults to <c>false</c>.
    /// </summary>
    /// <remarks>
    /// Only appropriate when access is already restricted elsewhere — a reverse proxy, or a network
    /// boundary. On an Optimizely site with front-end membership, "authenticated" includes ordinary
    /// site visitors, who would then be able to read execution history and any captured input data.
    /// </remarks>
    public bool AllowAnyAuthenticatedUser { get; set; }

    /// <summary>
    /// Whether to add a menu item in the Optimizely CMS navigation.
    /// Defaults to true.
    /// </summary>
    public bool EnableCmsMenu { get; set; } = true;

    /// <summary>
    /// Controls where the menu item is placed in the CMS navigation.
    /// Defaults to <see cref="CmsMenuPlacement.CmsSection"/>.
    /// </summary>
    public CmsMenuPlacement MenuPlacement { get; set; } = CmsMenuPlacement.CmsSection;

    /// <summary>
    /// Overrides the full menu path. When null (the default), the path is derived from <see cref="MenuPlacement"/>.
    /// </summary>
    public string? MenuPath { get; set; }

    /// <summary>
    /// Overrides the sort index for the menu item. When null, a sensible default is chosen based on placement.
    /// </summary>
    public int? MenuSortIndex { get; set; }

    /// <summary>
    /// The display name for the section when <see cref="MenuPlacement"/> is
    /// <see cref="CmsMenuPlacement.TopLevel"/> or <see cref="CmsMenuPlacement.CustomSection"/>.
    /// Defaults to "OptiPowerTools".
    /// </summary>
    public string CustomSectionName { get; set; } = "OptiPowerTools";

    /// <summary>
    /// The display name for the menu item. When empty or null, falls back to <see cref="PageTitle"/>.
    /// </summary>
    public string CustomMenuItemName { get; set; } = string.Empty;

    /// <summary>
    /// Whether to also add a menu item under the CMS's own <em>Settings &gt; Data &amp; Sync
    /// Management</em> group, directly below the native <em>Scheduled Jobs</em> page. Defaults to
    /// <c>true</c>.
    /// </summary>
    /// <remarks>
    /// This is independent of <see cref="MenuPlacement"/>, which positions the primary entry: with
    /// both enabled the UI is reachable from two places, which is usually what you want since an
    /// administrator looking at Scheduled Jobs expects to find its history alongside it. Set to
    /// <c>false</c> to keep a single entry.
    /// </remarks>
    public bool ShowInDataSyncManagement { get; set; } = true;

    /// <summary>
    /// Whether to add a menu item for the <em>Job Retention</em> screen, beside the insights entry
    /// under <em>Settings &gt; Data &amp; Sync Management</em>. Defaults to <c>true</c>.
    /// </summary>
    /// <remarks>
    /// Set to <c>false</c> to keep retention governed purely by configuration and
    /// <see cref="Retention.JobRetentionAttribute"/>. The screen itself remains reachable at
    /// <c>?view=retention</c>; this only controls its discoverability in the CMS navigation.
    /// </remarks>
    public bool ShowRetentionMenuItem { get; set; } = true;

    /// <summary>
    /// The URL path where the UI is served. The CMS menu item links here, and a single execution is
    /// addressed with an "id" query string (for example "/ScheduledJobsInsightsCms/Index?id=42").
    /// The id stays out of the path deliberately — the CMS shell resolves which navigation to render
    /// by matching the request path against registered menu items.
    /// Defaults to "/ScheduledJobsInsightsCms/Index".
    /// </summary>
    /// <remarks>
    /// Validated at startup: it must be an absolute path with at least one segment and no query
    /// string or fragment, since it is used simultaneously as a route template, a menu URL and the
    /// base for the UI's own cross-links.
    /// </remarks>
    public string CmsShellPath { get; set; } = "/ScheduledJobsInsightsCms/Index";

    /// <summary>
    /// Whether <c>UseOptiPowerToolScheduledJobsInsights</c> maps the Blazor Server hub.
    /// <c>null</c> (the default) detects an existing <c>/_blazor</c> mapping and skips its own.
    /// </summary>
    /// <remarks>
    /// Mapping the hub twice registers two endpoints on the same route pattern, which fails every
    /// Blazor request in the application with <c>AmbiguousMatchException</c>. Detection handles the
    /// common case; set it explicitly when the host maps its hub after this call, or on a
    /// non-default path.
    /// </remarks>
    public bool? MapBlazorHub { get; set; }
}

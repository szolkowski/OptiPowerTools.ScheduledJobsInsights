namespace OptiPowerTools.ScheduledJobsInsights.Configuration;

/// <summary>
/// Configuration options for the OptiPowerTools ScheduledJobsInsights Blazor integration.
/// </summary>
public sealed class OptiPowerToolsScheduledJobsInsightsOptions
{
    /// <summary>
    /// The configuration section these options bind from — <c>"OptiPowerTools:ScheduledJobsInsights"</c>.
    /// </summary>
    /// <remarks>
    /// Exposed so a host that reads or rewrites the section itself does not have to repeat the string,
    /// and so a rename would be a compile error rather than a silently unbound configuration.
    /// </remarks>
    public const string ConfigurationSectionName = "OptiPowerTools:ScheduledJobsInsights";

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

    /// <summary>Default for <see cref="MaxLogMessageLength"/>.</summary>
    /// <remarks>
    /// <c>static readonly</c> rather than <c>const</c>: a <c>const</c> is inlined into every consumer
    /// assembly that reads it, so revising this later would reach only the consumers that recompiled.
    /// </remarks>
    public static readonly int DefaultMaxLogMessageLength = 4_000;

    /// <summary>
    /// Longest log message stored, in characters. Longer messages are truncated with an ellipsis.
    /// Defaults to 4,000.
    /// </summary>
    /// <remarks>
    /// The column itself is unbounded, which is the problem: a job logging a response body per
    /// iteration writes megabytes per row, and the execution list and detail page both have to carry
    /// that. Raise it if you genuinely log large payloads — but prefer
    /// <see cref="Logging.LoggedScheduledJobBase.Summary"/>, which is bounded and rendered for
    /// reading rather than one line per row.
    /// </remarks>
    public int MaxLogMessageLength { get; set; } = DefaultMaxLogMessageLength;

    /// <summary>Default for <see cref="MaxLogEntriesPerExecution"/>.</summary>
    /// <remarks><c>static readonly</c> for the reason given on <see cref="DefaultMaxLogMessageLength"/>.</remarks>
    public static readonly int DefaultMaxLogEntriesPerExecution = 20_000;

    /// <summary>
    /// Most log lines the detail page will read for one execution. Defaults to 20,000.
    /// </summary>
    /// <remarks>
    /// The reader is a Blazor Server circuit, which holds every line it is given for as long as the
    /// page stays open — so an unbounded read of a very chatty execution is an out-of-memory on the
    /// server, once per viewer. This bounds both halves of that: the query asks for no more than this
    /// many lines, and the page holds no more than this many across all the polls of a running
    /// execution. A run that exceeds it is displayed truncated, and says so above the log.
    /// </remarks>
    public int MaxLogEntriesPerExecution { get; set; } = DefaultMaxLogEntriesPerExecution;

    /// <summary>Default for <see cref="MaxLogCharactersPerExecution"/>: 4,000,000 (about 8 MB).</summary>
    /// <remarks><c>static readonly</c> for the reason given on <see cref="DefaultMaxLogMessageLength"/>.</remarks>
    public static readonly int DefaultMaxLogCharactersPerExecution = 4_000_000;

    /// <summary>
    /// Most log text the detail page will hold for one execution, in characters.
    /// Defaults to 4,000,000 — roughly 8 MB of UTF-16, per open page.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The companion bound to <see cref="MaxLogEntriesPerExecution"/>, and the one that actually
    /// describes the cost. A line count is only a proxy for memory: multiplied by
    /// <see cref="MaxLogMessageLength"/> it permits far more than it appears to, and the product is
    /// what a Blazor Server circuit holds for as long as the tab is open, once per viewer.
    /// </para>
    /// <para>
    /// Whichever bound is reached first stops the buffer, and the page renders the same truncation
    /// notice either way. Ordinary logs reach neither: a typical line is a couple of hundred
    /// characters, so this budget accommodates well beyond the line cap and only bites when lines are
    /// unusually long — which is exactly the case a line count fails to catch.
    /// </para>
    /// <para>
    /// One line is always held, however long it is, so a single oversized line cannot leave the log
    /// looking empty.
    /// </para>
    /// </remarks>
    public int MaxLogCharactersPerExecution { get; set; } = DefaultMaxLogCharactersPerExecution;

    /// <summary>
    /// How long an execution may sit unfinished before the cleanup job records it as
    /// <see cref="ExecutionStatus.Interrupted"/>. Defaults to 24 hours; <see cref="TimeSpan.Zero"/>
    /// disables the sweep.
    /// </summary>
    /// <remarks>
    /// A process recycled mid-run writes nothing further, so its row stays <c>Running</c> for ever
    /// and quietly distorts every count and filter that follows. The default is deliberately far
    /// longer than any reasonable job: marking a genuinely long-running job as interrupted while it
    /// is still working would be worse than leaving a stale row for a day. Raise it if you run jobs
    /// that legitimately take longer.
    /// </remarks>
    public TimeSpan InterruptedExecutionThreshold { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// The title displayed in the CMS shell chrome and browser tab.
    /// Defaults to "Scheduled Jobs Insights".
    /// </summary>
    public string PageTitle { get; set; } = "Scheduled Jobs Insights";

    /// <summary>
    /// The Optimizely/EPiServer roles authorized to access the page and the retention screen.
    /// Leave empty — the default — to authorize the built-in set: Administrators, CmsAdmins and
    /// WebAdmins.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Naming any role here <em>replaces</em> the built-in set rather than adding to it, from
    /// <c>appsettings.json</c> and from code alike.
    /// </para>
    /// <para>
    /// The property starts empty for that reason, rather than carrying the built-in roles as its
    /// default value: <c>ConfigurationBinder</c> adds into an existing collection instead of clearing
    /// it, so a non-empty default cannot be replaced from configuration at all. Written the other way,
    /// <c>"AuthorizedRoles": [ "SecOps" ]</c> authorized four roles rather than one — silently
    /// widening access for an administrator who was trying to narrow it.
    /// </para>
    /// <para>
    /// Ignored when <see cref="AuthorizationPolicy"/> names a policy of your own, or when
    /// <see cref="AllowAnyAuthenticatedUser"/> is set.
    /// </para>
    /// </remarks>
    public IList<string> AuthorizedRoles { get; set; } = [];

    /// <summary>
    /// The roles authorized when <see cref="AuthorizedRoles"/> is left empty. Not an option in its own
    /// right — it is the value <see cref="AuthorizedRoles"/> documents as its default, held separately
    /// so that configuration replaces it rather than appending to it.
    /// </summary>
    internal static readonly string[] DefaultAuthorizedRoles = ["Administrators", "CmsAdmins", "WebAdmins"];

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
    /// <see cref="CmsRetentionPath"/>; this only controls its discoverability in the CMS navigation.
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
    /// The URL path where the <em>Job Retention</em> screen is served, separately from
    /// <see cref="CmsShellPath"/>. Defaults to "/ScheduledJobsInsightsCms/Retention".
    /// </summary>
    /// <remarks>
    /// <para>
    /// A path of its own rather than a query string on <see cref="CmsShellPath"/>, and the reason is
    /// the CMS navigation: the shell decides which menu entry to highlight by comparing the request
    /// <em>path</em> against each registered item's URL, ignoring the query string entirely — server
    /// side in <c>MenuItem.IsSelected</c>, and again client side in the navigation bundle, which
    /// matches against <c>location.pathname</c> alone. A retention entry whose URL was
    /// <c>…/Index?view=retention</c> could therefore never match, while the execution list's entry
    /// matched every time, so opening retention highlighted the list.
    /// </para>
    /// <para>
    /// Deliberately a sibling of <see cref="CmsShellPath"/> rather than a segment beneath it. An
    /// unmapped extra segment leaves the shell unable to resolve a product at all (which is why an
    /// execution id is still a query string), and a nested one that <em>is</em> mapped puts the two
    /// URLs in a prefix relationship the navigation resolves through its "closest match" fallback
    /// rather than an exact match.
    /// </para>
    /// <para>
    /// Validated at startup by the same rule as <see cref="CmsShellPath"/>, and additionally must
    /// differ from it. Set it alongside <see cref="CmsShellPath"/> when customising where the UI
    /// lives: the two are independent route templates, so changing only one leaves the retention
    /// screen at this default.
    /// </para>
    /// </remarks>
    public string CmsRetentionPath { get; set; } = "/ScheduledJobsInsightsCms/Retention";

    /// <summary>
    /// Whether <c>UseOptiPowerToolsScheduledJobsInsights</c> maps the Blazor Server hub.
    /// <c>null</c> (the default) detects an existing <c>/_blazor</c> mapping and skips its own.
    /// </summary>
    /// <remarks>
    /// Mapping the hub twice registers two endpoints on the same route pattern, which fails every
    /// Blazor request in the application with <c>AmbiguousMatchException</c>. Detection handles the
    /// common case; set it explicitly when the host maps its hub after this call, or on a
    /// non-default path.
    /// </remarks>
    public bool? MapBlazorHub { get; set; }

    /// <summary>
    /// Whether <c>AddOptiPowerToolsScheduledJobsInsights</c> registers Blazor Server and cascading
    /// authentication state. Defaults to <c>true</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately <c>bool</c> where <see cref="MapBlazorHub"/> is <c>bool?</c>. The nullable third
    /// state there means "detect an existing mapping and skip my own", which is answerable at map
    /// time by inspecting the endpoints. There is nothing equivalent to detect at registration time —
    /// a host that has called <c>AddServerSideBlazor</c> leaves no signal this could read — so a
    /// third state here would be a value with no defined behaviour.
    /// </para>
    /// <para>
    /// The service-side counterpart to <see cref="MapBlazorHub"/>, and needed for the same reason:
    /// this package's UI is Blazor Server, but the host may already have made its own choices.
    /// <c>AddServerSideBlazor</c> grafts circuit services into an application that may be on the
    /// Blazor Web App model, and <c>AddCascadingAuthenticationState</c> registers a cascading value
    /// provider for the whole application, not just for these pages.
    /// </para>
    /// <para>
    /// Set it to <c>false</c> when the host registers Blazor itself. The insights pages then rely on
    /// the host's registrations, so they must be equivalent — Blazor Server circuits and a cascading
    /// authentication state — or the retention screen loses the authorization re-check it makes before
    /// a destructive write.
    /// </para>
    /// </remarks>
    public bool AddBlazorServices { get; set; } = true;

    /// <summary>
    /// How often the detail page re-reads an execution that is still running. Defaults to 2 seconds.
    /// </summary>
    /// <remarks>
    /// One query per open detail page per interval, so it is the one setting here that scales with the
    /// number of people watching rather than with the amount of history. Raise it on a busy
    /// installation; lower it when watching a job that reports progress in bursts. Each tick reads a
    /// narrow projection plus whatever log lines are new, not the whole execution row.
    /// </remarks>
    public TimeSpan DetailPollInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Applied to the insights <c>DbContext</c> after its connection string, for options this package
    /// does not decide — <c>EnableRetryOnFailure()</c>, a command timeout, a connection interceptor.
    /// </summary>
    /// <remarks>
    /// Runs last, so it can override anything set before it. Set through the code-based
    /// <c>setupAction</c>, not <c>appsettings.json</c> — it is a delegate, so configuration binding
    /// cannot supply it.
    /// </remarks>
    public Action<Microsoft.EntityFrameworkCore.DbContextOptionsBuilder>? ConfigureDbContext { get; set; }
}

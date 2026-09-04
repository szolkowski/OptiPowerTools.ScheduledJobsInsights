using EPiServer.Shell.Navigation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OptiPowerTools.ScheduledJobsInsights.Configuration;

namespace OptiPowerTools.ScheduledJobsInsights.Cms;

/// <summary>
/// Provides a menu item in the Optimizely CMS navigation for accessing the ScheduledJobsInsights Blazor page.
/// The menu item links to <see cref="ScheduledJobsInsightsCmsController"/>, which renders the Blazor page
/// embedded in the CMS shell chrome.
/// </summary>
[MenuProvider]
public sealed class ScheduledJobsInsightsMenuProvider : IMenuProvider
{
    private readonly OptiPowerToolsScheduledJobsInsightsOptions _options;
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>
    /// Initializes a new instance of <see cref="ScheduledJobsInsightsMenuProvider"/>.
    /// </summary>
    /// <param name="options">Package options.</param>
    /// <param name="httpContextAccessor">Supplies the current request, its user and its services.</param>
    /// <exception cref="ArgumentNullException">Either argument is <c>null</c>.</exception>
    /// <remarks>
    /// Deliberately does <em>not</em> take <see cref="IAuthorizationService"/>. Optimizely registers
    /// menu providers as singletons and that service is scoped, so holding one would be a captive
    /// dependency — the scoped service and everything it captured living for the life of the
    /// process. It is resolved per call from the request's own scope instead.
    /// </remarks>
    public ScheduledJobsInsightsMenuProvider(
        IOptions<OptiPowerToolsScheduledJobsInsightsOptions> options,
        IHttpContextAccessor httpContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpContextAccessor);

        _options = options.Value;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Menu path of the CMS's own <em>Settings &gt; Data &amp; Sync Management</em> group. Its first
    /// child is the native Scheduled Jobs page (<c>.../scheduledjobs/list</c>); adding a sibling here
    /// puts this package's history view directly alongside it.
    /// </summary>
    private const string DataSyncManagementPath = MenuPaths.Global + "/cms/admin/scheduledjobs";

    /// <summary>
    /// Separator between segments of an Optimizely menu path.
    /// </summary>
    private const char MenuPathSeparator = '/';

    /// <summary>Final segment of this package's menu path, whatever the placement resolves to.</summary>
    private const string LeafSegment = "/scheduledjobsinsights";

    /// <summary>Menu leaf for the retention screen, a sibling of the insights entry.</summary>
    private const string RetentionLeafSegment = "/scheduledjobsinsightsretention";

    /// <inheritdoc />
    /// <remarks>
    /// Contributes the insights entry in exactly one place, chosen by
    /// <see cref="OptiPowerToolsScheduledJobsInsightsOptions.MenuPlacement"/>, plus the retention
    /// entry beside it when enabled — and the "exactly one" is load-bearing rather than tidiness.
    /// The shell identifies an entry by its URL: it matches the request path against every
    /// registered item and never learns which one was clicked. Two entries for this one page were
    /// therefore resolved differently by different CMS UI versions — on 13.0.0 the chosen entry came
    /// out as a childless top-level leaf, so no sub-navigation was rendered and the admin tree
    /// vanished on an otherwise correct page. The retention entry is safe alongside it because it is
    /// a different page with a URL of its own.
    /// </remarks>
    public IEnumerable<MenuItem> GetMenuItems()
    {
        if (!_options.EnableCmsMenu)
            return Enumerable.Empty<MenuItem>();

        return _options.MenuPlacement switch
        {
            CmsMenuPlacement.CmsSection => BuildCmsSection(),
            CmsMenuPlacement.TopLevel => BuildTopLevel(),
            CmsMenuPlacement.CustomSection => BuildCustomSection(),
            _ => BuildDataSyncManagement()
        };
    }

    /// <summary>
    /// Builds the entries under the CMS's own <em>Settings &gt; Data &amp; Sync Management</em>
    /// group, below the native Scheduled Jobs page. The parent group is the CMS's own, so only the
    /// leaves are contributed here.
    /// </summary>
    /// <remarks>
    /// The default placement. It is also the only one that leaves the reader inside the admin
    /// navigation: these are leaves of the CMS's Settings branch, so the shell resolves that branch
    /// and its sub-navigation stays on screen.
    /// </remarks>
    private List<MenuItem> BuildDataSyncManagement() =>
        // Sorts after the native Scheduled Jobs entry, so this reads as a companion to it rather
        // than displacing it.
        Leaves(DataSyncManagementPath, _options.MenuSortIndex ?? SortIndex.Last - 10);

    private List<MenuItem> BuildCmsSection()
    {
        // MenuPath, for this placement, overrides the *item* path rather than its parent — so the
        // retention sibling is derived from whatever that resolves to, below.
        var itemPathSuffix = string.IsNullOrEmpty(_options.MenuPath) ? "/cms" + LeafSegment : NormalizePath(_options.MenuPath);
        return Leaves(
            ParentOf(MenuPaths.Global + itemPathSuffix),
            _options.MenuSortIndex ?? SortIndex.Last - 10,
            itemPath: MenuPaths.Global + itemPathSuffix);
    }

    private List<MenuItem> BuildTopLevel()
    {
        var sectionName = string.IsNullOrEmpty(_options.CustomSectionName) ? _options.PageTitle : _options.CustomSectionName;
        var sectionSortIndex = _options.MenuSortIndex ?? SortIndex.Last - 10;
        var sectionPath = string.IsNullOrEmpty(_options.MenuPath)
            ? MenuPathSeparator + ToSlug(sectionName)
            : NormalizePath(_options.MenuPath);

        var section = new SectionMenuItem(sectionName, MenuPaths.Global + sectionPath)
        {
            IsAvailable = _ => IsCurrentUserAuthorized(),
            SortIndex = sectionSortIndex
        };

        return [section, .. Leaves(MenuPaths.Global + sectionPath, sectionSortIndex)];
    }

    private List<MenuItem> BuildCustomSection()
    {
        var sectionName = string.IsNullOrEmpty(_options.CustomSectionName) ? _options.PageTitle : _options.CustomSectionName;
        var sectionPath = MenuPaths.Global + (string.IsNullOrEmpty(_options.MenuPath)
            ? MenuPathSeparator + ToSlug(sectionName)
            : NormalizePath(_options.MenuPath));

        var section = new SectionMenuItem(sectionName, sectionPath)
        {
            IsAvailable = _ => IsCurrentUserAuthorized(),
            SortIndex = _options.MenuSortIndex ?? SortIndex.Last - 10
        };

        return [section, .. Leaves(sectionPath, sortIndex: 100)];
    }

    /// <summary>
    /// The insights entry under <paramref name="parentPath"/>, plus the retention entry beside it
    /// when <see cref="OptiPowerToolsScheduledJobsInsightsOptions.ShowRetentionMenuItem"/> is set.
    /// </summary>
    /// <param name="parentPath">Menu path both leaves hang from.</param>
    /// <param name="sortIndex">Sort index of the insights entry; retention sorts immediately after.</param>
    /// <param name="itemPath">
    /// Full menu path of the insights entry, when the placement resolves it to something other than
    /// <paramref name="parentPath"/> plus the standard leaf segment — which <c>MenuPath</c> can do.
    /// </param>
    private List<MenuItem> Leaves(string parentPath, int sortIndex, string? itemPath = null)
    {
        var menuItemName = string.IsNullOrEmpty(_options.CustomMenuItemName) ? _options.PageTitle : _options.CustomMenuItemName;

        var items = new List<MenuItem>
        {
            new UrlMenuItem(menuItemName, itemPath ?? parentPath + LeafSegment, _options.CmsShellPath)
            {
                IsAvailable = _ => IsCurrentUserAuthorized(),
                SortIndex = sortIndex
            }
        };

        if (!_options.ShowRetentionMenuItem)
            return items;

        // A sibling of the insights entry, wherever that is — the two screens configure and read the
        // same data, and splitting them across the navigation would be arbitrary.
        //
        // Its URL is CmsRetentionPath, a path of its own, and it has to be. MenuItem.IsSelected
        // compares this URL with the request path and the shell's navigation script compares it with
        // location.pathname: both drop the query string, so while this pointed at
        // CmsShellPath?view=retention it could never match, and the execution list's entry — whose
        // URL *is* the request path — was highlighted instead whenever retention was open.
        items.Add(new UrlMenuItem(
            $"{menuItemName} - Retention",
            parentPath + RetentionLeafSegment,
            _options.CmsRetentionPath)
        {
            IsAvailable = _ => IsCurrentUserAuthorized(),
            SortIndex = sortIndex + 1
        });

        return items;
    }

    /// <summary>The menu path one level up, so a leaf's sibling can be derived from it.</summary>
    private static string ParentOf(string menuPath) =>
        menuPath[..menuPath.LastIndexOf(MenuPathSeparator)];

    /// <summary>
    /// Asks the same question the page does, through the same policy.
    /// </summary>
    /// <remarks>
    /// Previously this rolled its own role check, which meant the menu and the controller could
    /// disagree — with <c>EnableStandardAuthorization</c> off, the page was reachable by URL while
    /// its own menu entry stayed hidden from the very users allowed to open it.
    /// <see cref="MenuItem.IsAvailable"/> gives no async form to await from, so the policy has to be
    /// evaluated synchronously here.
    /// </remarks>
    /// <remarks>
    /// The result is cached for the request. The built-in policy is in-memory and answers instantly,
    /// but a host is invited to substitute its own through
    /// <see cref="OptiPowerToolsScheduledJobsInsightsOptions.AuthorizationPolicy"/>, and a policy that
    /// checks group membership against a directory or a database does I/O — blocking a request thread
    /// each time. This method is called once per contributed entry, so up to three times per back
    /// office page load; caching makes that one evaluation instead of three.
    /// </remarks>
    private bool IsCurrentUserAuthorized()
    {
        if (_httpContextAccessor.HttpContext is not { } httpContext)
            return false;

        if (httpContext.Items.TryGetValue(AuthorizationCacheKey, out var cached) && cached is bool decided)
            return decided;

        // From the request's scope, not a captured one — see the constructor.
        var authorizationService = httpContext.RequestServices.GetService<IAuthorizationService>();

        if (authorizationService is null || httpContext.User is not { } principal)
            return false;

        var authorized = authorizationService
            .AuthorizeAsync(principal, resource: null, ScheduledJobsInsightsAuthorization.PolicyName)
            .GetAwaiter()
            .GetResult()
            .Succeeded;

        httpContext.Items[AuthorizationCacheKey] = authorized;

        return authorized;
    }

    /// <summary>
    /// Key for the per-request authorization result. An object instance rather than a string so it
    /// cannot collide with anything else in <c>HttpContext.Items</c>.
    /// </summary>
    private static readonly object AuthorizationCacheKey = new();

    private static string NormalizePath(string path) =>
        path.StartsWith(MenuPathSeparator) ? path : MenuPathSeparator + path;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private static string ToSlug(string name)
    {
        var slug = name.ToLowerInvariant()
            .Replace(' ', '-')
            .Replace('_', '-')
            .Replace('.', '-');
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[^a-z0-9\-]", "", System.Text.RegularExpressions.RegexOptions.None, RegexTimeout);
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"-{2,}", "-", System.Text.RegularExpressions.RegexOptions.None, RegexTimeout);
        return slug.Trim('-');
    }
}

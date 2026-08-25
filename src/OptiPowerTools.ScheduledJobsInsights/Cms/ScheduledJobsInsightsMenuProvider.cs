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
    public IEnumerable<MenuItem> GetMenuItems()
    {
        if (!_options.EnableCmsMenu)
            return Enumerable.Empty<MenuItem>();

        var items = _options.MenuPlacement switch
        {
            CmsMenuPlacement.TopLevel => BuildTopLevel(),
            CmsMenuPlacement.CustomSection => BuildCustomSection(),
            _ => BuildCmsSection()
        };

        // Independent of MenuPlacement — these are additional entries, not alternative ones.
        if (_options.ShowInDataSyncManagement)
            items.Add(BuildDataSyncManagementItem());

        if (_options.ShowRetentionMenuItem)
            items.Add(BuildRetentionItem());

        return items;
    }

    /// <summary>
    /// Builds the entry that sits under <em>Data &amp; Sync Management</em>, below the native
    /// Scheduled Jobs page. The parent group is the CMS's own, so only the leaf is contributed here.
    /// </summary>
    private UrlMenuItem BuildDataSyncManagementItem()
    {
        var menuItemName = string.IsNullOrEmpty(_options.CustomMenuItemName) ? _options.PageTitle : _options.CustomMenuItemName;

        return new UrlMenuItem(menuItemName, DataSyncManagementPath + LeafSegment, _options.CmsShellPath)
        {
            IsAvailable = _ => IsCurrentUserAuthorized(),
            // Sorts after the native Scheduled Jobs entry, so this reads as a companion to it rather
            // than displacing it.
            SortIndex = SortIndex.Last - 10
        };
    }

    /// <summary>
    /// Builds the entry for the retention screen. Sits under <em>Data &amp; Sync Management</em>
    /// beside the insights entry, since it configures the same data.
    /// </summary>
    private UrlMenuItem BuildRetentionItem() =>
        new($"{(string.IsNullOrEmpty(_options.CustomMenuItemName) ? _options.PageTitle : _options.CustomMenuItemName)} - Retention",
            DataSyncManagementPath + RetentionLeafSegment,
            $"{_options.CmsShellPath}?view={ScheduledJobsInsightsCmsController.RetentionView}")
        {
            IsAvailable = _ => IsCurrentUserAuthorized(),
            SortIndex = SortIndex.Last - 9
        };

    private List<MenuItem> BuildCmsSection()
    {
        var defaultPathSuffix = string.IsNullOrEmpty(_options.MenuPath) ? "/cms" + LeafSegment : NormalizePath(_options.MenuPath);
        return [BuildUrlMenuItem(defaultPathSuffix)];
    }

    private List<MenuItem> BuildTopLevel()
    {
        var sectionName = string.IsNullOrEmpty(_options.CustomSectionName) ? _options.PageTitle : _options.CustomSectionName;
        var sectionSlug = ToSlug(sectionName);
        var sectionSortIndex = _options.MenuSortIndex ?? SortIndex.Last - 10;
        var sectionPath = string.IsNullOrEmpty(_options.MenuPath) ? MenuPathSeparator + sectionSlug : NormalizePath(_options.MenuPath);
        var itemPath = sectionPath + LeafSegment;

        var section = new SectionMenuItem(sectionName, MenuPaths.Global + sectionPath)
        {
            IsAvailable = _ => IsCurrentUserAuthorized(),
            SortIndex = sectionSortIndex
        };

        return [section, BuildUrlMenuItem(itemPath)];
    }

    private UrlMenuItem BuildUrlMenuItem(string defaultPathSuffix)
    {
        var path = MenuPaths.Global + defaultPathSuffix;
        var sortIndex = _options.MenuSortIndex ?? SortIndex.Last - 10;
        var menuItemName = string.IsNullOrEmpty(_options.CustomMenuItemName) ? _options.PageTitle : _options.CustomMenuItemName;

        return new UrlMenuItem(menuItemName, path, _options.CmsShellPath)
        {
            IsAvailable = _ => IsCurrentUserAuthorized(),
            SortIndex = sortIndex
        };
    }

    private List<MenuItem> BuildCustomSection()
    {
        var sectionName = string.IsNullOrEmpty(_options.CustomSectionName) ? _options.PageTitle : _options.CustomSectionName;
        var sectionSlug = ToSlug(sectionName);
        var sectionPath = MenuPaths.Global + (string.IsNullOrEmpty(_options.MenuPath) ? MenuPathSeparator + sectionSlug : NormalizePath(_options.MenuPath));
        var itemPath = sectionPath + LeafSegment;
        var menuItemName = string.IsNullOrEmpty(_options.CustomMenuItemName) ? _options.PageTitle : _options.CustomMenuItemName;
        var sectionSortIndex = _options.MenuSortIndex ?? SortIndex.Last - 10;

        var section = new SectionMenuItem(sectionName, sectionPath)
        {
            IsAvailable = _ => IsCurrentUserAuthorized(),
            SortIndex = sectionSortIndex
        };

        var item = new UrlMenuItem(menuItemName, itemPath, _options.CmsShellPath)
        {
            IsAvailable = _ => IsCurrentUserAuthorized(),
            SortIndex = 100
        };

        return [section, item];
    }

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

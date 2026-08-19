using EPiServer.Shell.Navigation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using OptiPowerTools.ScheduledJobsInsights.Configuration;

namespace OptiPowerTools.ScheduledJobsInsights.Cms;

/// <summary>
/// Provides a menu item in the Optimizely CMS navigation for accessing the ScheduledJobsInsights Blazor page.
/// The menu item links to <see cref="ScheduledJobsInsightsCmsController"/>, which renders the Blazor page
/// embedded in the CMS shell chrome.
/// </summary>
[MenuProvider]
public class ScheduledJobsInsightsMenuProvider : IMenuProvider
{
    private readonly OptiPowerToolScheduledJobsInsightsOptions _options;
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>
    /// Initializes a new instance of <see cref="ScheduledJobsInsightsMenuProvider"/>.
    /// </summary>
    public ScheduledJobsInsightsMenuProvider(IOptions<OptiPowerToolScheduledJobsInsightsOptions> options, IHttpContextAccessor httpContextAccessor)
    {
        _options = options.Value;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Menu path of the CMS's own <em>Settings &gt; Data &amp; Sync Management</em> group. Its first
    /// child is the native Scheduled Jobs page (<c>.../scheduledjobs/list</c>); adding a sibling here
    /// puts this package's history view directly alongside it.
    /// </summary>
    private const string DataSyncManagementPath = MenuPaths.Global + "/cms/admin/scheduledjobs";

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

        // Independent of MenuPlacement — this is an additional entry, not an alternative one.
        if (_options.ShowInDataSyncManagement)
            items.Add(BuildDataSyncManagementItem());

        return items;
    }

    /// <summary>
    /// Builds the entry that sits under <em>Data &amp; Sync Management</em>, below the native
    /// Scheduled Jobs page. The parent group is the CMS's own, so only the leaf is contributed here.
    /// </summary>
    private UrlMenuItem BuildDataSyncManagementItem()
    {
        var menuItemName = string.IsNullOrEmpty(_options.CustomMenuItemName) ? _options.PageTitle : _options.CustomMenuItemName;

        return new UrlMenuItem(menuItemName, DataSyncManagementPath + "/scheduledjobsinsights", _options.CmsShellPath)
        {
            IsAvailable = _ => IsCurrentUserAuthorized(),
            // Sorts after the native Scheduled Jobs entry, so this reads as a companion to it rather
            // than displacing it.
            SortIndex = SortIndex.Last - 10
        };
    }

    private List<MenuItem> BuildCmsSection()
    {
        var defaultPathSuffix = string.IsNullOrEmpty(_options.MenuPath) ? "/cms/scheduledjobsinsights" : NormalizePath(_options.MenuPath);
        return BuildUrlMenuItem(defaultPathSuffix);
    }

    private List<MenuItem> BuildTopLevel()
    {
        var sectionName = string.IsNullOrEmpty(_options.CustomSectionName) ? _options.PageTitle : _options.CustomSectionName;
        var sectionSlug = ToSlug(sectionName);
        var sectionSortIndex = _options.MenuSortIndex ?? SortIndex.Last - 10;
        var sectionPath = string.IsNullOrEmpty(_options.MenuPath) ? "/" + sectionSlug : NormalizePath(_options.MenuPath);
        var itemPath = sectionPath + "/scheduledjobsinsights";

        var section = new SectionMenuItem(sectionName, MenuPaths.Global + sectionPath)
        {
            IsAvailable = _ => IsCurrentUserAuthorized(),
            SortIndex = sectionSortIndex
        };

        var item = BuildUrlMenuItem(itemPath).First();

        return [section, item];
    }

    private List<MenuItem> BuildUrlMenuItem(string defaultPathSuffix)
    {
        var path = MenuPaths.Global + defaultPathSuffix;
        var sortIndex = _options.MenuSortIndex ?? SortIndex.Last - 10;
        var menuItemName = string.IsNullOrEmpty(_options.CustomMenuItemName) ? _options.PageTitle : _options.CustomMenuItemName;

        var item = new UrlMenuItem(menuItemName, path, _options.CmsShellPath)
        {
            IsAvailable = _ => IsCurrentUserAuthorized(),
            SortIndex = sortIndex
        };

        return [item];
    }

    private List<MenuItem> BuildCustomSection()
    {
        var sectionName = string.IsNullOrEmpty(_options.CustomSectionName) ? _options.PageTitle : _options.CustomSectionName;
        var sectionSlug = ToSlug(sectionName);
        var sectionPath = MenuPaths.Global + (string.IsNullOrEmpty(_options.MenuPath) ? "/" + sectionSlug : NormalizePath(_options.MenuPath));
        var itemPath = sectionPath + "/scheduledjobsinsights";
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

    private bool IsCurrentUserAuthorized()
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        return principal?.Identity?.IsAuthenticated == true
            && _options.AuthorizedRoles is { } roles
            && roles.Any(principal.IsInRole);
    }

    private static string NormalizePath(string path) =>
        path.StartsWith('/') ? path : "/" + path;

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

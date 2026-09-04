using EPiServer.Shell.Navigation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using OptiPowerTools.ScheduledJobsInsights.Cms;
using OptiPowerTools.ScheduledJobsInsights.Configuration;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Cms;

public class ScheduledJobsInsightsMenuProviderTests
{
    /// <summary>Path of the CMS's own "Data &amp; Sync Management" group, parent of the native Scheduled Jobs page.</summary>
    private const string DataSyncManagementItemPath = "/global/cms/admin/scheduledjobs/scheduledjobsinsights";

    private static ScheduledJobsInsightsMenuProvider CreateProvider(OptiPowerToolsScheduledJobsInsightsOptions options) =>
        new(Options.Create(options), Substitute.For<IHttpContextAccessor>());

    [Fact]
    public void GetMenuItems_ReturnsEmpty_WhenMenuDisabled()
    {
        var provider = CreateProvider(new OptiPowerToolsScheduledJobsInsightsOptions { EnableCmsMenu = false });

        Assert.Empty(provider.GetMenuItems());
    }

    [Fact]
    public void GetMenuItems_ByDefault_ContributesOneEntryPerPageAndNoMore()
    {
        // The bug this pins: the package used to contribute a second entry for the *same* page — its
        // own placement plus one under Data & Sync Management. The shell identifies an entry by its
        // URL, matching the request path against every registered item, and never learns which one
        // was clicked; two entries on one URL were resolved differently by different CMS UI versions.
        // On 13.0.0 the winner came out as a childless top-level leaf, so the shell rendered no
        // sub-navigation and the admin tree disappeared on a page that had rendered correctly.
        var items = CreateProvider(new OptiPowerToolsScheduledJobsInsightsOptions { EnableCmsMenu = true })
            .GetMenuItems()
            .ToList();

        Assert.Equal(
            [DataSyncManagementItemPath, "/global/cms/admin/scheduledjobs/scheduledjobsinsightsretention"],
            items.Select(i => i.Path).Order(StringComparer.Ordinal));

        // One URL each, which is what makes the resolution deterministic.
        Assert.Equal(items.Count, items.Select(i => i.Url).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData(CmsMenuPlacement.DataSyncManagement)]
    [InlineData(CmsMenuPlacement.CmsSection)]
    [InlineData(CmsMenuPlacement.TopLevel)]
    [InlineData(CmsMenuPlacement.CustomSection)]
    public void GetMenuItems_WhateverThePlacement_NoTwoEntriesShareAUrl(CmsMenuPlacement placement)
    {
        // The invariant, not just the default: a second entry for one page is exactly what broke the
        // admin navigation, so no placement may reintroduce one.
        var items = CreateProvider(new OptiPowerToolsScheduledJobsInsightsOptions
        {
            EnableCmsMenu = true,
            MenuPlacement = placement
        }).GetMenuItems().ToList();

        var urls = items.Select(i => i.Url).Where(u => !string.IsNullOrEmpty(u) && !u.StartsWith('#')).ToList();

        Assert.Equal(urls.Count, urls.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void GetMenuItems_DefaultPlacement_IsDataSyncManagement()
    {
        // Beside the native Scheduled Jobs page, and the only placement that keeps the reader inside
        // the admin tree: these are leaves of the CMS's Settings branch, so the shell resolves that
        // branch and its sub-navigation stays on screen.
        var provider = CreateProvider(new OptiPowerToolsScheduledJobsInsightsOptions
        {
            EnableCmsMenu = true,
            ShowRetentionMenuItem = false
        });

        Assert.Equal(DataSyncManagementItemPath, Assert.Single(provider.GetMenuItems()).Path);
    }

    [Fact]
    public void GetMenuItems_CmsSectionPlacement_ContributesOnlyThatEntry()
    {
        var provider = CreateProvider(new OptiPowerToolsScheduledJobsInsightsOptions
        {
            EnableCmsMenu = true,
            MenuPlacement = CmsMenuPlacement.CmsSection,
            ShowRetentionMenuItem = false
        });

        var item = Assert.Single(provider.GetMenuItems());
        Assert.Equal("/global/cms/scheduledjobsinsights", item.Path);
    }

    [Theory]
    [InlineData(CmsMenuPlacement.CmsSection)]
    [InlineData(CmsMenuPlacement.TopLevel)]
    [InlineData(CmsMenuPlacement.CustomSection)]
    public void GetMenuItems_ANonDefaultPlacement_DoesNotAlsoAppearUnderDataSyncManagement(CmsMenuPlacement placement)
    {
        var provider = CreateProvider(new OptiPowerToolsScheduledJobsInsightsOptions
        {
            EnableCmsMenu = true,
            MenuPlacement = placement
        });

        Assert.DoesNotContain(provider.GetMenuItems(), i => i.Path.StartsWith("/global/cms/admin/", StringComparison.Ordinal));
    }

    [Fact]
    public void GetMenuItems_DataSyncManagementEntry_UsesCustomMenuItemNameAndShellPath()
    {
        var provider = CreateProvider(new OptiPowerToolsScheduledJobsInsightsOptions
        {
            EnableCmsMenu = true,
            CustomMenuItemName = "Job History",
            CmsShellPath = "/custom/shell"
        });

        var item = Assert.Single(provider.GetMenuItems(), i => i.Path == DataSyncManagementItemPath);
        Assert.Equal("Job History", item.Text);
        Assert.Equal("/custom/shell", item.Url);
    }

    [Fact]
    public void GetMenuItems_TopLevel_NestsTheItemUnderItsOwnSection()
    {
        // Pins the literal paths. They are assembled by string concatenation from a separator, a slug
        // and a leaf, so a refactor of that assembly can quietly change them — and a menu path that
        // does not match what the shell expects simply fails to appear, with no error anywhere.
        var provider = CreateProvider(new OptiPowerToolsScheduledJobsInsightsOptions
        {
            EnableCmsMenu = true,
            MenuPlacement = CmsMenuPlacement.TopLevel,
            CustomSectionName = "OptiPowerTools",
            ShowRetentionMenuItem = false
        });

        var items = provider.GetMenuItems().ToList();

        Assert.Equal(2, items.Count);
        Assert.Equal("/global/optipowertools", items[0].Path);
        Assert.Equal("/global/optipowertools/scheduledjobsinsights", items[1].Path);
    }

    [Fact]
    public void GetMenuItems_CustomSection_NestsTheItemUnderItsOwnSection()
    {
        var provider = CreateProvider(new OptiPowerToolsScheduledJobsInsightsOptions
        {
            EnableCmsMenu = true,
            MenuPlacement = CmsMenuPlacement.CustomSection,
            CustomSectionName = "My Tools",
            ShowRetentionMenuItem = false
        });

        var items = provider.GetMenuItems().ToList();

        Assert.Equal(2, items.Count);
        Assert.Equal("/global/my-tools", items[0].Path);
        Assert.Equal("/global/my-tools/scheduledjobsinsights", items[1].Path);
    }

    [Theory]
    [InlineData("custom/place", "/global/custom/place")]   // MenuPath is normalised to a leading slash
    [InlineData("/custom/place", "/global/custom/place")]
    public void GetMenuItems_AnExplicitMenuPath_IsNormalised(string configured, string expected)
    {
        var provider = CreateProvider(new OptiPowerToolsScheduledJobsInsightsOptions
        {
            EnableCmsMenu = true,
            MenuPlacement = CmsMenuPlacement.CmsSection,
            MenuPath = configured,
            ShowRetentionMenuItem = false
        });

        Assert.Equal(expected, Assert.Single(provider.GetMenuItems()).Path);
    }

    [Fact]
    public void GetMenuItems_AddsTheRetentionEntry_ByDefault()
    {
        var provider = CreateProvider(new OptiPowerToolsScheduledJobsInsightsOptions { EnableCmsMenu = true });

        Assert.Contains(
            provider.GetMenuItems(),
            i => i.Path == "/global/cms/admin/scheduledjobs/scheduledjobsinsightsretention");
    }

    [Theory]
    [InlineData(CmsMenuPlacement.DataSyncManagement, "/global/cms/admin/scheduledjobs")]
    [InlineData(CmsMenuPlacement.CmsSection, "/global/cms")]
    [InlineData(CmsMenuPlacement.TopLevel, "/global/optipowertools")]
    [InlineData(CmsMenuPlacement.CustomSection, "/global/optipowertools")]
    public void GetMenuItems_TheRetentionEntry_IsASiblingOfTheInsightsEntry(
        CmsMenuPlacement placement, string expectedParent)
    {
        // The two screens read and configure the same data, so splitting them across the navigation
        // would be arbitrary — and under a section placement the retention entry used to be left
        // behind under Settings, in a different branch from the entry it belongs beside.
        var items = CreateProvider(new OptiPowerToolsScheduledJobsInsightsOptions
        {
            EnableCmsMenu = true,
            MenuPlacement = placement,
            CustomSectionName = "OptiPowerTools"
        }).GetMenuItems().ToList();

        Assert.Contains(items, i => i.Path == expectedParent + "/scheduledjobsinsights");
        Assert.Contains(items, i => i.Path == expectedParent + "/scheduledjobsinsightsretention");
    }

    [Fact]
    public void GetMenuItems_TheRetentionEntry_SortsImmediatelyAfterTheInsightsEntry()
    {
        var items = CreateProvider(new OptiPowerToolsScheduledJobsInsightsOptions { EnableCmsMenu = true })
            .GetMenuItems()
            .ToList();

        var insights = Assert.Single(items, i => i.Path == DataSyncManagementItemPath);
        var retention = Assert.Single(items, i => i.Path.EndsWith("retention", StringComparison.Ordinal));

        Assert.Equal(insights.SortIndex + 1, retention.SortIndex);
    }

    [Fact]
    public void GetMenuItems_OmitsTheRetentionEntry_WhenDisabled()
    {
        var provider = CreateProvider(new OptiPowerToolsScheduledJobsInsightsOptions
        {
            EnableCmsMenu = true,
            ShowRetentionMenuItem = false
        });

        Assert.DoesNotContain(provider.GetMenuItems(), i => i.Path.Contains("retention", StringComparison.Ordinal));
    }

    [Fact]
    public void GetMenuItems_RetentionEntry_PointsAtTheRetentionPath()
    {
        var provider = CreateProvider(new OptiPowerToolsScheduledJobsInsightsOptions
        {
            EnableCmsMenu = true,
            CmsShellPath = "/ScheduledJobsInsightsCms/Index",
            CmsRetentionPath = "/ScheduledJobsInsightsCms/Retention"
        });

        var item = Assert.IsType<UrlMenuItem>(
            Assert.Single(provider.GetMenuItems(), i => i.Path.EndsWith("scheduledjobsinsightsretention", StringComparison.Ordinal)));

        Assert.Equal("/ScheduledJobsInsightsCms/Retention", item.Url);
    }

    [Fact]
    public void GetMenuItems_RetentionEntry_IsSelectedByTheRetentionRequestPath()
    {
        // The bug this pins: the CMS shell decides which entry to highlight by comparing the item's
        // URL with the request *path*, dropping the query string (MenuItem.IsSelected server side, and
        // location.pathname client side). While the retention entry's URL was "…/Index?view=retention"
        // it could never match, and the execution list's entry matched instead — so opening retention
        // highlighted the list. Asserting through IsSelected rather than on the string, because that is
        // the comparison the shell actually makes.
        var options = new OptiPowerToolsScheduledJobsInsightsOptions
        {
            EnableCmsMenu = true,
            CmsShellPath = "/ScheduledJobsInsightsCms/Index",
            CmsRetentionPath = "/ScheduledJobsInsightsCms/Retention"
        };
        var provider = CreateProvider(options);
        var items = provider.GetMenuItems().ToList();

        var onRetention = new DefaultHttpContext();
        onRetention.Request.Path = options.CmsRetentionPath;

        var retentionItem = Assert.Single(
            items, i => i.Path.EndsWith("scheduledjobsinsightsretention", StringComparison.Ordinal));

        Assert.True(retentionItem.IsSelected(onRetention));
        Assert.All(
            items.Where(i => i.Path != retentionItem.Path),
            i => Assert.False(i.IsSelected(onRetention)));
    }

    [Fact]
    public void GetMenuItems_ExecutionEntries_AreSelectedByTheShellRequestPath()
    {
        // The other half of the same rule: the list's entries still match their own path, and the
        // retention entry does not claim it.
        var options = new OptiPowerToolsScheduledJobsInsightsOptions
        {
            EnableCmsMenu = true,
            CmsShellPath = "/ScheduledJobsInsightsCms/Index",
            CmsRetentionPath = "/ScheduledJobsInsightsCms/Retention"
        };
        var items = CreateProvider(options).GetMenuItems().ToList();

        var onList = new DefaultHttpContext();
        onList.Request.Path = options.CmsShellPath;

        Assert.Contains(items, i => i.IsSelected(onList));
        Assert.False(
            Assert.Single(items, i => i.Path.EndsWith("scheduledjobsinsightsretention", StringComparison.Ordinal))
                .IsSelected(onList));
    }

    /// <summary>
    /// A provider whose menu items evaluate authorization against a real HttpContext.
    /// </summary>
    private static (ScheduledJobsInsightsMenuProvider Provider, IAuthorizationService Authorization) WithAuthorization(bool succeeds)
    {
        var authorization = Substitute.For<IAuthorizationService>();
        authorization
            .AuthorizeAsync(Arg.Any<System.Security.Claims.ClaimsPrincipal>(), Arg.Any<object?>(), Arg.Any<string>())
            .Returns(succeeds ? AuthorizationResult.Success() : AuthorizationResult.Failed());

        var services = new ServiceCollection();
        services.AddSingleton(authorization);

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity("Test"))
        };

        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);

        var provider = new ScheduledJobsInsightsMenuProvider(
            Options.Create(new OptiPowerToolsScheduledJobsInsightsOptions { EnableCmsMenu = true }),
            accessor);

        return (provider, authorization);
    }

    [Fact]
    public async Task IsAvailable_EvaluatesThePolicyOncePerRequest_NotOncePerMenuItem()
    {
        // MenuItem.IsAvailable has no async form, so the policy is evaluated synchronously on the
        // request thread. The built-in policy answers from memory, but a host is invited to supply
        // its own, and one that checks group membership against a directory does I/O — three times
        // per back office page load without the cache.
        var (provider, authorization) = WithAuthorization(succeeds: true);

        var items = provider.GetMenuItems().ToList();
        Assert.True(items.Count > 1, "this test is only meaningful with several contributed entries");

        foreach (var item in items)
            Assert.True(item.IsAvailable(null));

        await authorization.Received(1).AuthorizeAsync(
            Arg.Any<System.Security.Claims.ClaimsPrincipal>(), Arg.Any<object?>(), Arg.Any<string>());
    }

    [Fact]
    public async Task IsAvailable_WhenThePolicyDenies_EveryEntryIsHidden_AndStillEvaluatedOnce()
    {
        // The cached value has to be the answer, not merely "something was cached": caching a denial
        // as an approval would show entries leading to a 403.
        var (provider, authorization) = WithAuthorization(succeeds: false);

        var items = provider.GetMenuItems().ToList();

        Assert.All(items, item => Assert.False(item.IsAvailable(null)));
        await authorization.Received(1).AuthorizeAsync(
            Arg.Any<System.Security.Claims.ClaimsPrincipal>(), Arg.Any<object?>(), Arg.Any<string>());
    }

    [Fact]
    public void IsAvailable_WithNoHttpContext_IsUnavailableRatherThanThrowing()
    {
        // Menu providers are also constructed outside a request, where there is nobody to authorize.
        var provider = CreateProvider(new OptiPowerToolsScheduledJobsInsightsOptions { EnableCmsMenu = true });

        Assert.All(provider.GetMenuItems(), item => Assert.False(item.IsAvailable(null)));
    }

    [Fact]
    public void IsAvailable_WithNoAuthorizationServiceRegistered_IsUnavailableRatherThanThrowing()
    {
        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().BuildServiceProvider(),
            User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity("Test"))
        };
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);

        var provider = new ScheduledJobsInsightsMenuProvider(
            Options.Create(new OptiPowerToolsScheduledJobsInsightsOptions { EnableCmsMenu = true }), accessor);

        Assert.All(provider.GetMenuItems(), item => Assert.False(item.IsAvailable(null)));
    }
}

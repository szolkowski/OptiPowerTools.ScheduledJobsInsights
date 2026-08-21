using EPiServer.Shell.Navigation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSubstitute;
using OptiPowerTools.ScheduledJobsInsights.Cms;
using OptiPowerTools.ScheduledJobsInsights.Configuration;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Cms;

public class ScheduledJobsInsightsMenuProviderTests
{
    /// <summary>Path of the CMS's own "Data &amp; Sync Management" group, parent of the native Scheduled Jobs page.</summary>
    private const string DataSyncManagementItemPath = "/global/cms/admin/scheduledjobs/scheduledjobsinsights";

    private static ScheduledJobsInsightsMenuProvider CreateProvider(OptiPowerToolScheduledJobsInsightsOptions options) =>
        new(Options.Create(options), Substitute.For<IHttpContextAccessor>(), Substitute.For<IAuthorizationService>());

    [Fact]
    public void GetMenuItems_ReturnsEmpty_WhenMenuDisabled()
    {
        var provider = CreateProvider(new OptiPowerToolScheduledJobsInsightsOptions { EnableCmsMenu = false });

        Assert.Empty(provider.GetMenuItems());
    }

    [Fact]
    public void GetMenuItems_ReturnsSingleItem_WhenPlacementIsCmsSectionAndDataSyncEntryDisabled()
    {
        var provider = CreateProvider(new OptiPowerToolScheduledJobsInsightsOptions
        {
            EnableCmsMenu = true,
            MenuPlacement = CmsMenuPlacement.CmsSection,
            ShowInDataSyncManagement = false,
            ShowRetentionMenuItem = false
        });

        var item = Assert.Single(provider.GetMenuItems());
        Assert.Equal("/global/cms/scheduledjobsinsights", item.Path);
    }

    [Fact]
    public void GetMenuItems_AddsDataSyncManagementEntry_ByDefault()
    {
        // Defaults deliberately surface the UI in two places: its own entry, plus one beside the
        // native Scheduled Jobs page where an administrator would look for a job's history.
        var provider = CreateProvider(new OptiPowerToolScheduledJobsInsightsOptions
        {
            EnableCmsMenu = true,
            ShowRetentionMenuItem = false
        });

        var items = provider.GetMenuItems().ToList();

        Assert.Equal(2, items.Count);
        Assert.Contains(items, i => i.Path == DataSyncManagementItemPath);
        Assert.Contains(items, i => i.Path == "/global/cms/scheduledjobsinsights");
    }

    [Fact]
    public void GetMenuItems_OmitsDataSyncManagementEntry_WhenDisabled()
    {
        var provider = CreateProvider(new OptiPowerToolScheduledJobsInsightsOptions
        {
            EnableCmsMenu = true,
            ShowInDataSyncManagement = false
        });

        Assert.DoesNotContain(provider.GetMenuItems(), i => i.Path == DataSyncManagementItemPath);
    }

    [Theory]
    [InlineData(CmsMenuPlacement.CmsSection)]
    [InlineData(CmsMenuPlacement.TopLevel)]
    [InlineData(CmsMenuPlacement.CustomSection)]
    public void GetMenuItems_AddsDataSyncManagementEntry_IndependentlyOfPlacement(CmsMenuPlacement placement)
    {
        // The Data & Sync entry is additional, not an alternative placement, so it appears whichever
        // way the primary entry is positioned.
        var provider = CreateProvider(new OptiPowerToolScheduledJobsInsightsOptions
        {
            EnableCmsMenu = true,
            MenuPlacement = placement
        });

        Assert.Contains(provider.GetMenuItems(), i => i.Path == DataSyncManagementItemPath);
    }

    [Fact]
    public void GetMenuItems_DataSyncManagementEntry_UsesCustomMenuItemNameAndShellPath()
    {
        var provider = CreateProvider(new OptiPowerToolScheduledJobsInsightsOptions
        {
            EnableCmsMenu = true,
            CustomMenuItemName = "Job History",
            CmsShellPath = "/custom/shell"
        });

        var item = Assert.Single(provider.GetMenuItems(), i => i.Path == DataSyncManagementItemPath);
        Assert.Equal("Job History", item.Text);
    }

    [Fact]
    public void GetMenuItems_TopLevel_NestsTheItemUnderItsOwnSection()
    {
        // Pins the literal paths. They are assembled by string concatenation from a separator, a slug
        // and a leaf, so a refactor of that assembly can quietly change them — and a menu path that
        // does not match what the shell expects simply fails to appear, with no error anywhere.
        var provider = CreateProvider(new OptiPowerToolScheduledJobsInsightsOptions
        {
            EnableCmsMenu = true,
            MenuPlacement = CmsMenuPlacement.TopLevel,
            CustomSectionName = "OptiPowerTools",
            ShowInDataSyncManagement = false,
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
        var provider = CreateProvider(new OptiPowerToolScheduledJobsInsightsOptions
        {
            EnableCmsMenu = true,
            MenuPlacement = CmsMenuPlacement.CustomSection,
            CustomSectionName = "My Tools",
            ShowInDataSyncManagement = false,
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
        var provider = CreateProvider(new OptiPowerToolScheduledJobsInsightsOptions
        {
            EnableCmsMenu = true,
            MenuPath = configured,
            ShowInDataSyncManagement = false,
            ShowRetentionMenuItem = false
        });

        Assert.Equal(expected, Assert.Single(provider.GetMenuItems()).Path);
    }

    [Fact]
    public void GetMenuItems_AddsTheRetentionEntry_ByDefault()
    {
        var provider = CreateProvider(new OptiPowerToolScheduledJobsInsightsOptions { EnableCmsMenu = true });

        Assert.Contains(
            provider.GetMenuItems(),
            i => i.Path == "/global/cms/admin/scheduledjobs/scheduledjobsinsightsretention");
    }

    [Fact]
    public void GetMenuItems_OmitsTheRetentionEntry_WhenDisabled()
    {
        var provider = CreateProvider(new OptiPowerToolScheduledJobsInsightsOptions
        {
            EnableCmsMenu = true,
            ShowRetentionMenuItem = false
        });

        Assert.DoesNotContain(provider.GetMenuItems(), i => i.Path.Contains("retention", StringComparison.Ordinal));
    }

    [Fact]
    public void GetMenuItems_RetentionEntry_PointsAtTheRetentionView()
    {
        // A query string, not a path segment: an extra segment would stop the CMS shell resolving
        // which product's navigation to render, and the left-hand menu would spin forever.
        var provider = CreateProvider(new OptiPowerToolScheduledJobsInsightsOptions
        {
            EnableCmsMenu = true,
            CmsShellPath = "/ScheduledJobsInsightsCms/Index"
        });

        var item = Assert.IsType<UrlMenuItem>(
            Assert.Single(provider.GetMenuItems(), i => i.Path.EndsWith("scheduledjobsinsightsretention", StringComparison.Ordinal)));

        Assert.Equal("/ScheduledJobsInsightsCms/Index?view=retention", item.Url);
    }
}

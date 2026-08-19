using OptiPowerTools.ScheduledJobsInsights.Cms;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Cms;

public class CmsAdminUrlsTests
{
    [Fact]
    public void ScheduledJobDetail_BuildsTheUrlTheCmsShellUses()
    {
        // Format taken from the anchors the CMS's own Scheduled Jobs list renders. The id is
        // lower-case and hyphenated, which is Guid.ToString()'s default — asserted so a change of
        // format here would fail rather than silently produce a link that lands nowhere.
        var url = CmsAdminUrls.ScheduledJobDetail(Guid.Parse("204B5F36-719B-4A02-8A5C-4C855907DAD9"));

        Assert.Equal(
            "/Optimizely/Settings/default#/ScheduledJobs/detailScheduledJob/204b5f36-719b-4a02-8a5c-4c855907dad9",
            url);
    }

    [Fact]
    public void ScheduledJobDetail_IsPrefixedByTheListUrl()
    {
        Assert.StartsWith(CmsAdminUrls.ScheduledJobsList, CmsAdminUrls.ScheduledJobDetail(Guid.NewGuid()));
    }
}

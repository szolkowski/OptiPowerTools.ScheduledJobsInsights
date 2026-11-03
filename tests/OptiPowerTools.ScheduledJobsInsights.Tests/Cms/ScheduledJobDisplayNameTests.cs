using OptiPowerTools.ScheduledJobsInsights.Cms;
using OptiPowerTools.ScheduledJobsInsights.Tests.Logging;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Cms;

public class ScheduledJobDisplayNameTests
{
    [Fact]
    public void Of_ReturnsTheDisplayNameTheTypeDeclares() =>
        Assert.Equal(DisplayNamedTestJob.DeclaredName, ScheduledJobDisplayName.Of(typeof(DisplayNamedTestJob)));

    [Fact]
    public void Of_IsNull_ForATypeWithoutTheAttribute() =>
        Assert.Null(ScheduledJobDisplayName.Of(typeof(TestLoggedJob)));

    [Fact]
    public void Of_IsNull_ForABlankDisplayName() =>
        Assert.Null(ScheduledJobDisplayName.Of(typeof(BlankNamedType)));

#if CMS12
    [EPiServer.PlugIn.ScheduledPlugIn(DisplayName = "   ")]
#else
    [EPiServer.Scheduler.ScheduledJob(DisplayName = "   ")]
#endif
    private sealed class BlankNamedType;
}

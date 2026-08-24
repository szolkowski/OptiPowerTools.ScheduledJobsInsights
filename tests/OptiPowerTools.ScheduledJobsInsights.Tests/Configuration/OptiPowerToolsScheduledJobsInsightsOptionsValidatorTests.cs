using Microsoft.Extensions.Options;
using OptiPowerTools.ScheduledJobsInsights.Configuration;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Configuration;

/// <summary>
/// Every case here used to start the application successfully and then misbehave silently — which is
/// why validation exists at all.
/// </summary>
public class OptiPowerToolsScheduledJobsInsightsOptionsValidatorTests
{
    private static readonly OptiPowerToolsScheduledJobsInsightsOptionsValidator Validator = new();

    private static OptiPowerToolsScheduledJobsInsightsOptions Valid() => new()
    {
        ConnectionString = "Server=.;Database=Insights;Trusted_Connection=True;"
    };

    private static ValidateOptionsResult Validate(Action<OptiPowerToolsScheduledJobsInsightsOptions> mutate)
    {
        var options = Valid();
        mutate(options);
        return Validator.Validate(name: null, options);
    }

    [Fact]
    public void ADefaultConfigurationWithAConnectionString_IsValid() =>
        Assert.True(Validator.Validate(null, Valid()).Succeeded);

    [Fact]
    public void AMissingConnectionString_Fails()
    {
        // The default. Without this the app starts, the menu appears, the page 500s, and every job
        // silently records nothing.
        var result = Validate(options => options.ConnectionString = "   ");

        Assert.True(result.Failed);
        Assert.Contains("ConnectionString", result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ANonPositiveLogBatchSize_Fails(int batchSize)
    {
        // Zero made the background writer spin a core while never writing a log line.
        var result = Validate(options => options.LogBatchSize = batchSize);

        Assert.True(result.Failed);
        Assert.Contains("LogBatchSize", result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ANonPositiveLogChannelCapacity_Fails(int capacity)
    {
        // Zero is legal for a bounded channel and turns every log call into its own round trip;
        // negative throws from inside a DI factory, during job construction.
        var result = Validate(options => options.LogChannelCapacity = capacity);

        Assert.True(result.Failed);
        Assert.Contains("LogChannelCapacity", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ANonPositivePageSize_Fails()
    {
        var result = Validate(options => options.PageSize = 0);

        Assert.True(result.Failed);
        Assert.Contains("PageSize", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ANonPositiveCleanupBatchSize_Fails()
    {
        var result = Validate(options => options.CleanupBatchSize = 0);

        Assert.True(result.Failed);
        Assert.Contains("CleanupBatchSize", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ANonPositiveFlushInterval_Fails()
    {
        var result = Validate(options => options.LogFlushInterval = TimeSpan.Zero);

        Assert.True(result.Failed);
        Assert.Contains("LogFlushInterval", result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ScheduledJobsInsightsCms/Index")]   // relative
    [InlineData("/")]                                 // no segment
    [InlineData("/Insights/")]                        // trailing slash
    [InlineData("/Insights?view=retention")]          // query string
    [InlineData("/Insights#top")]                     // fragment
    public void AnUnusableCmsShellPath_Fails(string path)
    {
        var result = Validate(options => options.CmsShellPath = path);

        Assert.True(result.Failed);
        Assert.Contains("CmsShellPath", result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/Insights")]
    [InlineData("/Custom/Segment/Path")]
    public void AUsableCmsShellPath_Passes(string path) =>
        Assert.True(Validate(options => options.CmsShellPath = path).Succeeded);

    [Fact]
    public void NoRolesAndNoPolicy_Fails()
    {
        // Nobody could open the page, which is a misconfiguration rather than a security posture.
        var result = Validate(options => options.AuthorizedRoles = []);

        Assert.True(result.Failed);
        Assert.Contains("AuthorizedRoles", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void NoRoles_IsFine_WhenAPolicyIsNamed() =>
        Assert.True(Validate(options =>
        {
            options.AuthorizedRoles = [];
            options.AuthorizationPolicy = "HostPolicy";
        }).Succeeded);

    [Fact]
    public void NoRoles_IsFine_WhenAnyAuthenticatedUserIsAllowed() =>
        Assert.True(Validate(options =>
        {
            options.AuthorizedRoles = [];
            options.AllowAnyAuthenticatedUser = true;
        }).Succeeded);

    [Fact]
    public void EveryFailure_IsReportedTogether()
    {
        // One restart per mistake is a poor way to configure a package.
        var result = Validate(options =>
        {
            options.ConnectionString = "";
            options.LogBatchSize = 0;
            options.PageSize = -5;
        });

        Assert.True(result.Failed);
        Assert.Equal(3, result.Failures.Count());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ANonPositiveDetailPollInterval_IsRejected(int seconds)
    {
        // Zero would spin the detail page's PeriodicTimer as fast as the database answers, one query
        // per tick per open page.
        var result = Validate(options => options.DetailPollInterval = TimeSpan.FromSeconds(seconds));

        Assert.Contains("DetailPollInterval", string.Join(" ", result.Failures ?? []), StringComparison.Ordinal);
    }
}

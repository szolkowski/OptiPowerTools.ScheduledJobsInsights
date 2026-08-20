using OptiPowerTools.ScheduledJobsInsights.Retention;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Retention;

/// <summary>
/// The precedence chain — override, then attribute, then the installation default — expressed in one
/// place and pinned here, because getting it wrong silently deletes or hoards data.
/// </summary>
public class JobRetentionPrecedenceTests
{
    private static JobRetention Job(RetentionPeriod? attribute, RetentionPeriod? overridden) =>
        new("Contoso.Jobs.Thing", "Thing", IsRegistered: true, ExistsInCode: true, attribute,
            AttributeDescription: null, HasInvalidAttribute: false, overridden,
            ModifiedBy: null, ModifiedAt: null, ExecutionCount: 0);

    private static readonly RetentionPeriod Default = RetentionPeriod.OfDays(30);

    [Fact]
    public void AnOverride_WinsOverEverything()
    {
        var (period, source) = Job(RetentionPeriod.OfDays(7), RetentionPeriod.OfDays(90)).Resolve(Default);

        Assert.Equal(RetentionPeriod.OfDays(90), period);
        Assert.Equal(RetentionSource.Override, source);
    }

    [Fact]
    public void AnIndefiniteOverride_BeatsAShorterAttribute()
    {
        // The case that matters most: an administrator deciding to keep a noisy job's history after
        // all must not be quietly undone by the job's own declaration.
        var (period, source) = Job(RetentionPeriod.OfDays(7), RetentionPeriod.Indefinite).Resolve(Default);

        Assert.True(period.IsIndefinite);
        Assert.Equal(RetentionSource.Override, source);
    }

    [Fact]
    public void TheAttribute_AppliesWhenThereIsNoOverride()
    {
        var (period, source) = Job(RetentionPeriod.OfDays(7), overridden: null).Resolve(Default);

        Assert.Equal(RetentionPeriod.OfDays(7), period);
        Assert.Equal(RetentionSource.Attribute, source);
    }

    [Fact]
    public void TheDefault_AppliesWhenNothingElseDoes()
    {
        var (period, source) = Job(attribute: null, overridden: null).Resolve(Default);

        Assert.Equal(Default, period);
        Assert.Equal(RetentionSource.Default, source);
    }

    [Fact]
    public void AnAttributeOfIndefinite_IsHonouredRatherThanTreatedAsUnset()
    {
        var (period, source) = Job(RetentionPeriod.Indefinite, overridden: null).Resolve(Default);

        Assert.True(period.IsIndefinite);
        Assert.Equal(RetentionSource.Attribute, source);
    }
}

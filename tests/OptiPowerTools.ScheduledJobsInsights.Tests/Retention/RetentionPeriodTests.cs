using OptiPowerTools.ScheduledJobsInsights.Retention;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Retention;

public class RetentionPeriodTests
{
    [Fact]
    public void OfDays_ProducesACutoffThatFarBack()
    {
        var now = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(now.AddDays(-30), RetentionPeriod.OfDays(30).CutoffFrom(now));
    }

    [Fact]
    public void Indefinite_HasNoCutoff_SoNothingIsEverEligible()
    {
        // The cleanup job keys off exactly this: no cutoff means skip the job entirely.
        Assert.Null(RetentionPeriod.Indefinite.CutoffFrom(DateTimeOffset.UtcNow));
        Assert.True(RetentionPeriod.Indefinite.IsIndefinite);
    }

    [Fact]
    public void FromAttribute_ReadsAPositiveDayCount() =>
        Assert.Equal(RetentionPeriod.OfDays(7), RetentionPeriod.FromAttribute(new JobRetentionAttribute(7)));

    [Fact]
    public void FromAttribute_ReadsIndefinite() =>
        Assert.Equal(
            RetentionPeriod.Indefinite,
            RetentionPeriod.FromAttribute(new JobRetentionAttribute(JobRetentionAttribute.Indefinite)));

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(int.MinValue)]
    public void FromAttribute_RejectsValuesItCannotActOn(int days)
    {
        // Returning null rather than throwing: an attribute cannot fail usefully at startup, so a bad
        // value falls back to the default and is flagged in the retention screen instead.
        Assert.Null(RetentionPeriod.FromAttribute(new JobRetentionAttribute(days)));
    }
}

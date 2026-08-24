using NSubstitute;
using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Logging;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Logging;

/// <summary>
/// The public factory a consumer uses to unit-test their own job, and the bound resolution behind it.
/// </summary>
public class JobLoggingContextTests
{
    [Fact]
    public void ForWriter_NeedsOnlyAWriter()
    {
        // The whole point of the factory: a consumer testing their job should not have to satisfy
        // collaborators they do not care about, nor be broken by one added in a later version.
        var context = JobLoggingContext.ForWriter(Substitute.For<IJobExecutionWriter>());

        Assert.Equal(JobResultSummary.DefaultMaxLength, context.MaxResultSummaryLength);
    }

    [Fact]
    public void ForWriter_WithNoRepository_LetsAJobRunAndFallBackToItsTypeName()
    {
        // A context built for a test has no CMS to ask for the display name.
        var writer = Substitute.For<IJobExecutionWriter>();
        writer.BeginExecution(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Returns(1L);
        var job = new TestLoggedJob(JobLoggingContext.ForWriter(writer));

        var result = job.Execute();

        Assert.Equal("done", result);
        writer.Received(1).BeginExecution(Arg.Any<Guid>(), nameof(TestLoggedJob), Arg.Any<string>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ANonPositiveBound_FallsBackToTheDefault(int configured)
    {
        var context = JobLoggingContext.ForWriter(
            Substitute.For<IJobExecutionWriter>(), maxResultSummaryLength: configured);

        Assert.Equal(JobResultSummary.DefaultMaxLength, context.MaxResultSummaryLength);
    }

    [Fact]
    public void AConfiguredBound_IsUsedAsGiven()
    {
        var context = JobLoggingContext.ForWriter(
            Substitute.For<IJobExecutionWriter>(), maxResultSummaryLength: 25);

        Assert.Equal(25, context.MaxResultSummaryLength);
    }

    [Fact]
    public void ForWriter_RejectsANullWriter() =>
        Assert.Throws<ArgumentNullException>(() => JobLoggingContext.ForWriter(null!));

    [Fact]
    public void TheConstructorIsNotPublic()
    {
        // Guards the decision, not the mechanics: a public constructor would freeze its own parameter
        // list at 1.0, which is the problem JobLoggingContext exists to solve.
        Assert.Empty(typeof(JobLoggingContext).GetConstructors());
    }

    [Fact]
    public void TheCollaboratorsAreNotPublic()
    {
        // A derived job reaching the writer could log against an execution id that does not exist,
        // bypassing the guard the never-throw design rests on.
        var publicNames = typeof(JobLoggingContext)
            .GetProperties()
            .Select(p => p.Name)
            .ToArray();

        Assert.Equal([nameof(JobLoggingContext.MaxResultSummaryLength)], publicNames);
    }
}

using EPiServer.DataAbstraction;
using Microsoft.Extensions.Options;
using NSubstitute;
using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Jobs;
using OptiPowerTools.ScheduledJobsInsights.Logging;
using OptiPowerTools.ScheduledJobsInsights.Repositories;
using OptiPowerTools.ScheduledJobsInsights.Retention;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Jobs;

public class ScheduledJobsInsightsCleanupJobTests
{
    private static ScheduledJobsInsightsCleanupJob CreateJob(
        ICleanupRepository repository,
        IJobRetentionPolicySource retention,
        int retentionDays = 30,
        int batchSize = 500)
    {
        var writer = Substitute.For<IJobExecutionWriter>();
        writer.BeginExecution(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Returns(1L);

        return new ScheduledJobsInsightsCleanupJob(
            writer,
            Substitute.For<IScheduledJobRepository>(),
            repository,
            retention,
            Options.Create(new OptiPowerToolScheduledJobsInsightsOptions
            {
                RetentionDays = retentionDays,
                CleanupBatchSize = batchSize
            }));
    }

    /// <summary>A policy source with the given per-job rules and a fixed default.</summary>
    private static IJobRetentionPolicySource RetentionOf(
        RetentionPeriod defaultPeriod,
        params (string JobTypeName, RetentionPeriod Period)[] perJob)
    {
        var source = Substitute.For<IJobRetentionPolicySource>();
        source.DefaultPeriod.Returns(defaultPeriod);
        source.GetEffectiveOverridesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<string, RetentionPeriod>>(
                perJob.ToDictionary(x => x.JobTypeName, x => x.Period)));
        return source;
    }

    [Fact]
    public void Execute_DeletesInBatchesUntilNothingRemains()
    {
        // The loop is convergent rather than counted: it keeps going until a batch comes back empty.
        var repository = Substitute.For<ICleanupRepository>();
        repository.DeleteExecutionsOlderThan(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<IReadOnlyCollection<string>>())
            .Returns(500, 500, 120, 0);

        var result = CreateJob(repository, RetentionOf(RetentionPeriod.OfDays(30))).Execute();

        Assert.Contains("1120", result);
        repository.Received(4).DeleteExecutionsOlderThan(
            Arg.Any<DateTimeOffset>(), 500, Arg.Any<IReadOnlyCollection<string>>());
    }

    [Fact]
    public void Execute_UsesTheDefaultRetentionForTheCutoff()
    {
        var repository = Substitute.For<ICleanupRepository>();
        repository.DeleteExecutionsOlderThan(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<IReadOnlyCollection<string>>())
            .Returns(0);
        var before = DateTimeOffset.UtcNow.AddDays(-7);

        CreateJob(repository, RetentionOf(RetentionPeriod.OfDays(7)), retentionDays: 7).Execute();

        repository.Received().DeleteExecutionsOlderThan(
            Arg.Is<DateTimeOffset>(cutoff => cutoff >= before.AddMinutes(-1) && cutoff <= before.AddMinutes(1)),
            Arg.Any<int>(),
            Arg.Any<IReadOnlyCollection<string>>());
    }

    [Fact]
    public void Execute_ExcludesJobsThatHaveTheirOwnRetention_FromTheDefaultSweep()
    {
        // Otherwise the default would delete history that a job explicitly asked to keep for longer.
        var repository = Substitute.For<ICleanupRepository>();
        repository.DeleteExecutionsOlderThan(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<IReadOnlyCollection<string>>())
            .Returns(0);
        repository.DeleteExecutionsOlderThan(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>())
            .Returns(0);

        var retention = RetentionOf(
            RetentionPeriod.OfDays(30),
            ("Contoso.Jobs.AuditJob", RetentionPeriod.OfDays(365)));

        CreateJob(repository, retention).Execute();

        repository.Received().DeleteExecutionsOlderThan(
            Arg.Any<DateTimeOffset>(),
            Arg.Any<int>(),
            Arg.Is<IReadOnlyCollection<string>>(excluded => excluded.Contains("Contoso.Jobs.AuditJob")));
    }

    [Fact]
    public void Execute_AppliesEachJobsOwnCutoff()
    {
        var repository = Substitute.For<ICleanupRepository>();
        repository.DeleteExecutionsOlderThan(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<IReadOnlyCollection<string>>())
            .Returns(0);
        repository.DeleteExecutionsOlderThan(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>())
            .Returns(3, 0);

        var retention = RetentionOf(
            RetentionPeriod.OfDays(30),
            ("Contoso.Jobs.ChattyJob", RetentionPeriod.OfDays(7)));
        var expected = DateTimeOffset.UtcNow.AddDays(-7);

        var result = CreateJob(repository, retention).Execute();

        repository.Received().DeleteExecutionsOlderThan(
            "Contoso.Jobs.ChattyJob",
            Arg.Is<DateTimeOffset>(cutoff => cutoff >= expected.AddMinutes(-1) && cutoff <= expected.AddMinutes(1)),
            Arg.Any<int>());
        Assert.Contains("3", result);
    }

    [Fact]
    public void Execute_SkipsJobsSetToIndefinite()
    {
        var repository = Substitute.For<ICleanupRepository>();
        repository.DeleteExecutionsOlderThan(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<IReadOnlyCollection<string>>())
            .Returns(0);

        var retention = RetentionOf(
            RetentionPeriod.OfDays(30),
            ("Contoso.Jobs.ForeverJob", RetentionPeriod.Indefinite));

        CreateJob(repository, retention).Execute();

        repository.DidNotReceive().DeleteExecutionsOlderThan(
            "Contoso.Jobs.ForeverJob", Arg.Any<DateTimeOffset>(), Arg.Any<int>());
    }

    [Fact]
    public void Execute_WithAnIndefiniteDefault_DeletesOnlyJobsThatOptedIntoARetention()
    {
        // An installation can choose to keep everything by default and trim only the noisy jobs.
        var repository = Substitute.For<ICleanupRepository>();
        repository.DeleteExecutionsOlderThan(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>())
            .Returns(2, 0);

        var retention = RetentionOf(
            RetentionPeriod.Indefinite,
            ("Contoso.Jobs.ChattyJob", RetentionPeriod.OfDays(7)));

        CreateJob(repository, retention).Execute();

        repository.DidNotReceive().DeleteExecutionsOlderThan(
            Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<IReadOnlyCollection<string>>());
        repository.Received().DeleteExecutionsOlderThan(
            "Contoso.Jobs.ChattyJob", Arg.Any<DateTimeOffset>(), Arg.Any<int>());
    }

    [Fact]
    public void Execute_WithNothingToDelete_ReportsZeroRatherThanFailing()
    {
        var repository = Substitute.For<ICleanupRepository>();
        repository.DeleteExecutionsOlderThan(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<IReadOnlyCollection<string>>())
            .Returns(0);

        var result = CreateJob(repository, RetentionOf(RetentionPeriod.OfDays(30))).Execute();

        Assert.Contains("0 job execution(s)", result);
    }
}

using EPiServer.DataAbstraction;
using Microsoft.Extensions.Options;
using NSubstitute;
using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Jobs;
using OptiPowerTools.ScheduledJobsInsights.Logging;
using OptiPowerTools.ScheduledJobsInsights.Tests.Logging;
using OptiPowerTools.ScheduledJobsInsights.Repositories;
using OptiPowerTools.ScheduledJobsInsights.Retention;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Jobs;

public class ScheduledJobsInsightsCleanupJobTests
{
    private static ScheduledJobsInsightsCleanupJob CreateJob(
        ICleanupRepository repository,
        IJobRetentionPolicySource retention,
        int retentionDays = 30,
        int batchSize = 500,
        TimeSpan? interruptedThreshold = null)
    {
        var writer = Substitute.For<IJobExecutionWriter>();
        writer.BeginExecution(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Returns(1L);

        return new ScheduledJobsInsightsCleanupJob(
            TestJobLoggingContext.For(writer),
            repository,
            retention,
            Options.Create(new OptiPowerToolsScheduledJobsInsightsOptions
            {
                RetentionDays = retentionDays,
                CleanupBatchSize = batchSize,
                InterruptedExecutionThreshold = interruptedThreshold ?? TimeSpan.FromHours(24)
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
        repository.DeleteExecutionsOlderThan(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(500, 500, 120, 0);

        var result = CreateJob(repository, RetentionOf(RetentionPeriod.OfDays(30))).Execute();

        Assert.Contains("1120", result);
        repository.Received(4).DeleteExecutionsOlderThan(
            Arg.Any<DateTimeOffset>(), 500, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Execute_UsesTheDefaultRetentionForTheCutoff()
    {
        var repository = Substitute.For<ICleanupRepository>();
        repository.DeleteExecutionsOlderThan(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(0);
        var before = DateTimeOffset.UtcNow.AddDays(-7);

        CreateJob(repository, RetentionOf(RetentionPeriod.OfDays(7)), retentionDays: 7).Execute();

        repository.Received().DeleteExecutionsOlderThan(
            Arg.Is<DateTimeOffset>(cutoff => cutoff >= before.AddMinutes(-1) && cutoff <= before.AddMinutes(1)),
            Arg.Any<int>(),
            Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Execute_ExcludesJobsThatHaveTheirOwnRetention_FromTheDefaultSweep()
    {
        // Otherwise the default would delete history that a job explicitly asked to keep for longer.
        var repository = Substitute.For<ICleanupRepository>();
        repository.DeleteExecutionsOlderThan(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(0);
        repository.DeleteExecutionsOlderThan(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(0);

        var retention = RetentionOf(
            RetentionPeriod.OfDays(30),
            ("Contoso.Jobs.AuditJob", RetentionPeriod.OfDays(365)));

        CreateJob(repository, retention).Execute();

        repository.Received().DeleteExecutionsOlderThan(
            Arg.Any<DateTimeOffset>(),
            Arg.Any<int>(),
            Arg.Is<IReadOnlyCollection<string>>(excluded => excluded.Contains("Contoso.Jobs.AuditJob")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Execute_AppliesEachJobsOwnCutoff()
    {
        var repository = Substitute.For<ICleanupRepository>();
        repository.DeleteExecutionsOlderThan(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(0);
        repository.DeleteExecutionsOlderThan(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(3, 0);

        var retention = RetentionOf(
            RetentionPeriod.OfDays(30),
            ("Contoso.Jobs.ChattyJob", RetentionPeriod.OfDays(7)));
        var expected = DateTimeOffset.UtcNow.AddDays(-7);

        var result = CreateJob(repository, retention).Execute();

        repository.Received().DeleteExecutionsOlderThan(
            "Contoso.Jobs.ChattyJob",
            Arg.Is<DateTimeOffset>(cutoff => cutoff >= expected.AddMinutes(-1) && cutoff <= expected.AddMinutes(1)),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
        // The whole message, not a substring: "3" also matches 13, 30 and 300.
        Assert.Equal("Deleted 3 job execution(s).", result);
    }

    [Fact]
    public void Execute_SkipsJobsSetToIndefinite()
    {
        var repository = Substitute.For<ICleanupRepository>();
        repository.DeleteExecutionsOlderThan(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(0);

        var retention = RetentionOf(
            RetentionPeriod.OfDays(30),
            ("Contoso.Jobs.ForeverJob", RetentionPeriod.Indefinite));

        CreateJob(repository, retention).Execute();

        repository.DidNotReceive().DeleteExecutionsOlderThan(
            "Contoso.Jobs.ForeverJob", Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Execute_WithAnIndefiniteDefault_DeletesOnlyJobsThatOptedIntoARetention()
    {
        // An installation can choose to keep everything by default and trim only the noisy jobs.
        var repository = Substitute.For<ICleanupRepository>();
        repository.DeleteExecutionsOlderThan(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(2, 0);

        var retention = RetentionOf(
            RetentionPeriod.Indefinite,
            ("Contoso.Jobs.ChattyJob", RetentionPeriod.OfDays(7)));

        CreateJob(repository, retention).Execute();

        repository.DidNotReceive().DeleteExecutionsOlderThan(
            Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
        repository.Received().DeleteExecutionsOlderThan(
            "Contoso.Jobs.ChattyJob", Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Execute_WhenStopped_HaltsBetweenBatchesAndSaysSo()
    {
        // A first sweep over years of history can run for a long time; an administrator watching it
        // has to be able to call it off, and the run must then not report a clean completion.
        var repository = Substitute.For<ICleanupRepository>();
        ScheduledJobsInsightsCleanupJob? job = null;
        var batches = 0;
        repository.DeleteExecutionsOlderThan(
                Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                job!.Stop();   // pressed during the first batch
                // Only the first batch reports work. Without this a regression that ignores the stop
                // request would loop for ever, and a hanging test is a far worse signal than a
                // failing one.
                return ++batches == 1 ? 500 : 0;
            });

        job = CreateJob(repository, RetentionOf(RetentionPeriod.OfDays(30)));
        var result = job.Execute();

        // The batch in flight finishes; the next one never starts.
        repository.Received(1).DeleteExecutionsOlderThan(
            Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
        Assert.Contains("Stopped", result, StringComparison.Ordinal);
        Assert.Contains("500", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_PassesACancellableTokenToTheRetentionLookup()
    {
        // CancellationToken.None would opt the lookup out of stopping entirely; the base class's
        // per-run token is what makes Stop reach it.
        var repository = Substitute.For<ICleanupRepository>();
        repository.DeleteExecutionsOlderThan(
                Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(0);
        var retention = RetentionOf(RetentionPeriod.OfDays(30));

        CreateJob(repository, retention).Execute();

        retention.Received(1).GetEffectiveOverridesAsync(Arg.Is<CancellationToken>(token => token.CanBeCanceled));
    }

    [Fact]
    public void Execute_GivesUpOnExecutionsLeftHangingByADeadProcess()
    {
        var repository = Substitute.For<ICleanupRepository>();
        repository.DeleteExecutionsOlderThan(
                Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(0);
        repository.MarkInterruptedExecutions(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(3);
        var expectedCutoff = DateTimeOffset.UtcNow.AddHours(-24);

        var result = CreateJob(repository, RetentionOf(RetentionPeriod.OfDays(30))).Execute();

        repository.Received(1).MarkInterruptedExecutions(
            Arg.Is<DateTimeOffset>(cutoff => cutoff >= expectedCutoff.AddMinutes(-1) && cutoff <= expectedCutoff.AddMinutes(1)),
            Arg.Any<CancellationToken>());
        Assert.Contains("0 job execution(s)", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_WithTheThresholdDisabled_LeavesUnfinishedExecutionsAlone()
    {
        // An installation running jobs that legitimately take days can switch the sweep off rather
        // than have a working job reported as interrupted underneath it.
        var repository = Substitute.For<ICleanupRepository>();
        repository.DeleteExecutionsOlderThan(
                Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(0);

        CreateJob(repository, RetentionOf(RetentionPeriod.OfDays(30)), interruptedThreshold: TimeSpan.Zero).Execute();

        repository.DidNotReceive().MarkInterruptedExecutions(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Execute_WithNothingToDelete_ReportsZeroRatherThanFailing()
    {
        var repository = Substitute.For<ICleanupRepository>();
        repository.DeleteExecutionsOlderThan(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(0);

        var result = CreateJob(repository, RetentionOf(RetentionPeriod.OfDays(30))).Execute();

        Assert.Contains("0 job execution(s)", result);
    }
}

using EPiServer.DataAbstraction;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Data;
using OptiPowerTools.ScheduledJobsInsights.Data.Entities;
using OptiPowerTools.ScheduledJobsInsights.Retention;
using OptiPowerTools.ScheduledJobsInsights.Tests.Data;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Retention;

public class JobRetentionServiceTests
{
    private static readonly string ChattyType = typeof(ChattyTestJob).FullName!;
    private static readonly string ForeverType = typeof(ForeverTestJob).FullName!;
    private static readonly string InvalidType = typeof(InvalidRetentionTestJob).FullName!;
    private static readonly string PlainType = typeof(PlainTestJob).FullName!;

    private static JobRetentionService CreateService(
        SqliteDbContextFactory factory,
        int defaultDays = 30,
        params (string TypeName, string DisplayName)[] registeredJobs)
    {
        var repository = Substitute.For<IScheduledJobRepository>();
        repository.List().Returns(registeredJobs
            .Select(j => new ScheduledJob { TypeName = j.TypeName, Name = j.DisplayName })
            .ToList());

        return CreateService(factory, repository, defaultDays);
    }

    /// <summary>The service over a given database and job registry, with its collaborators real.</summary>
    private static JobRetentionService CreateService(
        IDbContextFactory<ScheduledJobsInsightsDbContext> factory,
        IScheduledJobRepository repository,
        int defaultDays = 30,
        TimeProvider? timeProvider = null,
        ILogger<JobRetentionService>? logger = null) =>
        new(factory,
            new JobRetentionPolicyStore(factory, NullLogger<JobRetentionPolicyStore>.Instance),
            new RegisteredJobNames(ScopeFactoryFor(repository), NullLogger<RegisteredJobNames>.Instance),
            new LoggedJobTypeIndex(),
            Options.Create(new OptiPowerToolsScheduledJobsInsightsOptions { RetentionDays = defaultDays }),
            logger ?? NullLogger<JobRetentionService>.Instance,
            timeProvider ?? TimeProvider.System);

    private static void SeedExecutions(SqliteDbContextFactory factory, string jobTypeName, int count)
    {
        using var dbContext = factory.CreateDbContext();
        for (var i = 0; i < count; i++)
        {
            dbContext.JobExecutions.Add(new JobExecution
            {
                ScheduledJobId = Guid.NewGuid(),
                JobName = jobTypeName,
                JobTypeName = jobTypeName,
                StartedAt = DateTimeOffset.UtcNow,
                Status = ExecutionStatus.Succeeded,
                MachineName = "test"
            });
        }
        dbContext.SaveChanges();
    }

    [Fact]
    public async Task GetAllAsync_ListsLoggedJobsAndHistory()
    {
        using var factory = new SqliteDbContextFactory();
        SeedExecutions(factory, "Contoso.Jobs.DeletedLongAgo", 3);
        var service = CreateService(factory);

        var byType = (await service.GetAllAsync()).ToDictionary(j => j.JobTypeName);

        // A logged job in this assembly, never run — configurable before its first execution.
        Assert.True(byType[PlainType].ExistsInCode);
        Assert.Equal(0, byType[PlainType].ExecutionCount);

        // History whose code is gone — still worth managing, and flagged as such.
        Assert.False(byType["Contoso.Jobs.DeletedLongAgo"].ExistsInCode);
        Assert.Equal(3, byType["Contoso.Jobs.DeletedLongAgo"].ExecutionCount);
    }

    [Fact]
    public async Task GetAllAsync_ExcludesJobsOnOptimizelysOwnBaseClass()
    {
        // A job deriving from ScheduledJobBase never writes a row here, so it has no history to
        // retain. Listing the CMS's two dozen built-ins would bury the handful that matter.
        using var factory = new SqliteDbContextFactory();
        var service = CreateService(factory, registeredJobs: ("EPiServer.Cms.Jobs.NativeThing", "Automatic Emptying of Trash"));

        var jobs = await service.GetAllAsync();

        Assert.DoesNotContain(jobs, j => j.JobTypeName == "EPiServer.Cms.Jobs.NativeThing");
    }

    [Fact]
    public async Task GetAllAsync_StillListsANativeJobThatHasSomehowLeftHistory()
    {
        // Belt and braces: history is what retention acts on, so anything with rows stays manageable
        // regardless of what wrote them.
        using var factory = new SqliteDbContextFactory();
        SeedExecutions(factory, "EPiServer.Cms.Jobs.NativeThing", 2);
        var service = CreateService(factory, registeredJobs: ("EPiServer.Cms.Jobs.NativeThing", "Native Thing"));

        Assert.Contains(await service.GetAllAsync(), j => j.JobTypeName == "EPiServer.Cms.Jobs.NativeThing");
    }

    [Fact]
    public async Task GetAllAsync_ReadsTheAttributeAndItsDescription()
    {
        using var factory = new SqliteDbContextFactory();

        var job = Assert.Single(await CreateService(factory).GetAllAsync(), j => j.JobTypeName == ChattyType);

        Assert.Equal(RetentionPeriod.OfDays(7), job.Attribute);
        Assert.Equal("Logs one line per row; a week is plenty.", job.AttributeDescription);
        Assert.False(job.HasInvalidAttribute);
    }

    [Fact]
    public async Task GetAllAsync_FlagsAnUnusableAttributeRatherThanIgnoringItSilently()
    {
        // Silently dropping it would leave the job's author believing retention was configured.
        using var factory = new SqliteDbContextFactory();

        var job = Assert.Single(await CreateService(factory).GetAllAsync(), j => j.JobTypeName == InvalidType);

        Assert.True(job.HasInvalidAttribute);
        Assert.Null(job.Attribute);
        Assert.Equal(RetentionSource.Default, job.Resolve(RetentionPeriod.OfDays(30)).Source);
    }

    [Fact]
    public async Task GetAllAsync_PrefersTheRegisteredDisplayName_OverTheTypeName()
    {
        // Registered against a real logged job, since only those are listed now.
        using var factory = new SqliteDbContextFactory();
        var service = CreateService(factory, registeredJobs: (PlainType, "Nightly Import"));

        var job = Assert.Single(await service.GetAllAsync(), j => j.JobTypeName == PlainType);

        Assert.Equal("Nightly Import", job.DisplayName);
        Assert.True(job.IsRegistered);
    }

    [Fact]
    public async Task GetAllAsync_ListsALoggedJobTheCmsHasNotRegistered()
    {
        // Exists in code but not registered — not the same as history-only, and not flagged as such.
        using var factory = new SqliteDbContextFactory();

        var job = Assert.Single(await CreateService(factory).GetAllAsync(), j => j.JobTypeName == PlainType);

        Assert.True(job.ExistsInCode);
        Assert.False(job.IsRegistered);
        Assert.Equal("PlainTestJob", job.DisplayName);
    }

    [Fact]
    public async Task GetAllAsync_FallsBackToTheShortTypeName_ForJobsTheCmsNoLongerKnows()
    {
        using var factory = new SqliteDbContextFactory();
        SeedExecutions(factory, "Contoso.Jobs.OrphanedSweep", 1);

        var job = Assert.Single(await CreateService(factory).GetAllAsync(), j => j.JobTypeName == "Contoso.Jobs.OrphanedSweep");

        Assert.Equal("OrphanedSweep", job.DisplayName);
        Assert.False(job.ExistsInCode);
    }

    [Fact]
    public async Task SetOverrideAsync_StoresTheValueAndWhoSetIt()
    {
        using var factory = new SqliteDbContextFactory();
        var service = CreateService(factory);

        await service.SetOverrideAsync("Contoso.Jobs.Thing", RetentionPeriod.OfDays(90), "alice");

        var job = Assert.Single(await service.GetAllAsync(), j => j.JobTypeName == "Contoso.Jobs.Thing");
        Assert.Equal(RetentionPeriod.OfDays(90), job.Override);
        Assert.Equal("alice", job.ModifiedBy);
        Assert.NotNull(job.ModifiedAt);
    }

    [Fact]
    public async Task SetOverrideAsync_StoresIndefinite()
    {
        using var factory = new SqliteDbContextFactory();
        var service = CreateService(factory);

        await service.SetOverrideAsync("Contoso.Jobs.Thing", RetentionPeriod.Indefinite, "alice");

        var job = Assert.Single(await service.GetAllAsync(), j => j.JobTypeName == "Contoso.Jobs.Thing");
        Assert.True(job.Override!.Value.IsIndefinite);
    }

    [Fact]
    public async Task SetOverrideAsync_UpdatesAnExistingOverride_RatherThanAddingASecond()
    {
        using var factory = new SqliteDbContextFactory();
        var service = CreateService(factory);

        await service.SetOverrideAsync("Contoso.Jobs.Thing", RetentionPeriod.OfDays(90), "alice");
        await service.SetOverrideAsync("Contoso.Jobs.Thing", RetentionPeriod.OfDays(7), "bob");

        var job = Assert.Single(await service.GetAllAsync(), j => j.JobTypeName == "Contoso.Jobs.Thing");
        Assert.Equal(RetentionPeriod.OfDays(7), job.Override);
        Assert.Equal("bob", job.ModifiedBy);

        using var dbContext = factory.CreateDbContext();
        Assert.Single(dbContext.JobRetentionPolicies);
    }

    [Fact]
    public async Task SetOverrideAsync_WithNull_ClearsTheOverrideSoTheAttributeAppliesAgain()
    {
        using var factory = new SqliteDbContextFactory();
        var service = CreateService(factory);
        await service.SetOverrideAsync(ChattyType, RetentionPeriod.Indefinite, "alice");

        await service.SetOverrideAsync(ChattyType, period: null, "alice");

        var job = Assert.Single(await service.GetAllAsync(), j => j.JobTypeName == ChattyType);
        Assert.Null(job.Override);
        Assert.Equal(RetentionSource.Attribute, job.Resolve(RetentionPeriod.OfDays(30)).Source);
    }

    [Fact]
    public async Task AnUnusableStoredOverride_IsIgnoredAndFlagged_RatherThanDeletingEverything()
    {
        // A zero reaches CutoffFrom as "now", which would delete the job's whole history including
        // the run in progress. Nothing in the UI can write one, but a hand-edited row or a restored
        // backup can.
        using var factory = new SqliteDbContextFactory();
        using (var dbContext = factory.CreateDbContext())
        {
            dbContext.JobRetentionPolicies.Add(new JobRetentionPolicy
            {
                JobTypeName = ChattyType,
                RetentionDays = 0,
                ModifiedBy = "someone",
                ModifiedAt = DateTimeOffset.UtcNow
            });
            dbContext.SaveChanges();
        }
        var logger = new RecordingLogger<JobRetentionService>();
        var service = CreateService(factory, Substitute.For<IScheduledJobRepository>(), logger: logger);

        var job = Assert.Single(await service.GetAllAsync(), j => j.JobTypeName == ChattyType);

        Assert.Null(job.Override);
        Assert.True(job.HasInvalidOverride);
        // Reported, not merely flagged in a screen the administrator may never open.
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning && entry.Message.Contains(ChattyType, StringComparison.Ordinal));
        // Falls back to the attribute, exactly as though the row were absent.
        Assert.Equal(RetentionSource.Attribute, job.Resolve(RetentionPeriod.OfDays(30)).Source);
    }

    [Fact]
    public async Task AnUnusableStoredOverride_IsNotHandedToTheCleanupJob()
    {
        using var factory = new SqliteDbContextFactory();
        using (var dbContext = factory.CreateDbContext())
        {
            dbContext.JobRetentionPolicies.Add(new JobRetentionPolicy
            {
                JobTypeName = "Contoso.Jobs.Tampered",
                RetentionDays = -5,
                ModifiedBy = "someone",
                ModifiedAt = DateTimeOffset.UtcNow
            });
            dbContext.SaveChanges();
        }

        var effective = await CreateService(factory).GetEffectiveOverridesAsync();

        Assert.DoesNotContain("Contoso.Jobs.Tampered", effective.Keys);
    }

    [Fact]
    public async Task SetOverrideAsync_RecoversFromALostRaceOnTheUniqueIndex()
    {
        // Read-then-write against a unique index: two administrators saving at once both see no
        // existing row, both insert, and the loser hits the constraint — which surfaced as a red
        // banner on the retention screen. Re-reading and retrying once turns the loser into an
        // update. The conflict is injected rather than raced for: against Sqlite on one connection
        // two real callers simply serialise, so a "both at once" test passes with or without the fix.
        using var sqlite = new SqliteDbContextFactory();
        var factory = new ConflictOnFirstSaveDbContextFactory(sqlite);
        var service = CreateService(factory, Substitute.For<IScheduledJobRepository>());

        await service.SetOverrideAsync("Contoso.Jobs.Contended", RetentionPeriod.OfDays(90), "bob");

        Assert.Equal(2, factory.Attempts);
        using var dbContext = sqlite.CreateDbContext();
        var stored = Assert.Single(dbContext.JobRetentionPolicies.Where(p => p.JobTypeName == "Contoso.Jobs.Contended"));
        Assert.Equal(90, stored.RetentionDays);
        Assert.Equal("bob", stored.ModifiedBy);
    }

    [Fact]
    public async Task GetEffectiveOverridesAsync_ResolvesAttributesAndOverridesForTheCleanupJob()
    {
        using var factory = new SqliteDbContextFactory();
        var service = CreateService(factory);
        await service.SetOverrideAsync(ChattyType, RetentionPeriod.OfDays(90), "alice");

        var effective = await service.GetEffectiveOverridesAsync();

        // Override wins over the attribute's 7 days...
        Assert.Equal(RetentionPeriod.OfDays(90), effective[ChattyType]);
        // ...and an attribute with no override still appears.
        Assert.True(effective[ForeverType].IsIndefinite);
        // An unusable attribute contributes nothing, so the job falls to the default.
        Assert.DoesNotContain(InvalidType, effective.Keys);
    }

    [Fact]
    public async Task TheExecutionCount_IsCachedAcrossTheRenderAndTheCircuit()
    {
        // The screen loads twice per view — once prerendered, once when the circuit connects — and
        // this GROUP BY over every execution row is the only query here that scales with history.
        // Uncached it was the most expensive query in the UI, paid twice for every visit.
        using var sqlite = new SqliteDbContextFactory();
        SeedExecutions(sqlite, "Contoso.Jobs.Thing", 3);
        var clock = new AdjustableTimeProvider();
        var service = CreateService(sqlite, Substitute.For<IScheduledJobRepository>(), timeProvider: clock);

        var first = Assert.Single(await service.GetAllAsync(), j => j.JobTypeName == "Contoso.Jobs.Thing");

        // Another execution lands, but within the cache window the screen keeps the count it had.
        SeedExecutions(sqlite, "Contoso.Jobs.Thing", 1);
        var cached = Assert.Single(await service.GetAllAsync(), j => j.JobTypeName == "Contoso.Jobs.Thing");

        clock.Advance(TimeSpan.FromSeconds(61));
        var refreshed = Assert.Single(await service.GetAllAsync(), j => j.JobTypeName == "Contoso.Jobs.Thing");

        Assert.Equal(3, first.ExecutionCount);
        Assert.Equal(3, cached.ExecutionCount);
        Assert.Equal(4, refreshed.ExecutionCount);
    }

    [Fact]
    public async Task GetEffectiveOverridesAsync_AgreesWithTheScreen_ForEverySource()
    {
        // The cleanup job acts on this; the screen shows Resolve. They used to be two independent
        // expressions of the same precedence order, agreeing only by inspection. This asserts they
        // agree by construction, across all four sources at once.
        using var factory = new SqliteDbContextFactory();
        SeedExecutions(factory, "Contoso.Jobs.HistoryOnly", 2);
        var service = CreateService(factory, defaultDays: 30);
        await service.SetOverrideAsync(PlainType, RetentionPeriod.OfDays(90), "alice");

        var effective = await service.GetEffectiveOverridesAsync();
        var onScreen = await service.GetAllAsync();

        foreach (var job in onScreen)
        {
            var (period, source) = job.Resolve(service.DefaultPeriod);

            if (source is RetentionSource.Default)
            {
                // Governed only by the default sweep, so it must not appear as an exclusion.
                Assert.DoesNotContain(job.JobTypeName, effective.Keys);
            }
            else
            {
                Assert.Equal(period, effective[job.JobTypeName]);
            }
        }

        // Sanity: the fixture really does exercise more than one source.
        Assert.Equal(RetentionPeriod.OfDays(90), effective[PlainType]);       // override
        Assert.Equal(RetentionPeriod.OfDays(7), effective[ChattyType]);       // attribute
        Assert.DoesNotContain("Contoso.Jobs.HistoryOnly", effective.Keys);    // default
    }

    [Fact]
    public async Task DefaultPeriod_ReadsTheConfiguredRetention()
    {
        using var factory = new SqliteDbContextFactory();

        Assert.Equal(RetentionPeriod.OfDays(45), CreateService(factory, defaultDays: 45).DefaultPeriod);
    }

    [Fact]
    public async Task DefaultPeriod_TreatsANonPositiveConfiguredValueAsIndefinite()
    {
        // Rather than computing a cutoff in the future and deleting everything.
        using var factory = new SqliteDbContextFactory();

        Assert.True(CreateService(factory, defaultDays: 0).DefaultPeriod.IsIndefinite);
    }

    [Fact]
    public async Task GetAllAsync_SurvivesTheScheduledJobRepositoryFailing()
    {
        // Same best-effort treatment as LoggedScheduledJobBase's name lookup: losing the registry
        // costs display names, not the screen.
        using var factory = new SqliteDbContextFactory();
        SeedExecutions(factory, "Contoso.Jobs.Thing", 1);
        var repository = Substitute.For<IScheduledJobRepository>();
        repository.List().Returns(_ => throw new InvalidOperationException("registry unavailable"));

        var service = CreateService(factory, repository);

        var job = Assert.Single(await service.GetAllAsync(), j => j.JobTypeName == "Contoso.Jobs.Thing");
        Assert.Equal("Thing", job.DisplayName);
        Assert.False(job.IsRegistered);
    }

    /// <summary>
    /// A scope factory handing out <paramref name="repository"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="RegisteredJobNames"/> resolves the repository per call from a fresh scope rather than
    /// holding it — it is a singleton, and Optimizely registers that repository imperatively, so
    /// holding one would be a captive dependency. Building a real one-service provider here is simpler
    /// and more faithful than substituting the three scope interfaces by hand.
    /// </remarks>
    private static IServiceScopeFactory ScopeFactoryFor(IScheduledJobRepository repository)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => repository);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }
}

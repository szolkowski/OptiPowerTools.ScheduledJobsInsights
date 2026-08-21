using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Data.Entities;
using OptiPowerTools.ScheduledJobsInsights.Repositories;
using OptiPowerTools.ScheduledJobsInsights.Tests.Data;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Repositories;

public class JobExecutionQueryServiceTests
{
    /// <summary>Seeds executions all sharing one <c>StartedAt</c>, and returns their ids in insert order.</summary>
    private static async Task<List<long>> SeedSimultaneousAsync(SqliteDbContextFactory factory, DateTimeOffset startedAt, int count)
    {
        await using var dbContext = factory.CreateDbContext();
        var executions = new List<JobExecution>();

        for (var i = 0; i < count; i++)
        {
            var execution = new JobExecution
            {
                ScheduledJobId = Guid.NewGuid(),
                JobName = $"Job {i}",
                JobTypeName = "Test.Job",
                StartedAt = startedAt,
                Status = ExecutionStatus.Succeeded,
                MachineName = "test"
            };
            dbContext.JobExecutions.Add(execution);
            executions.Add(execution);
        }

        await dbContext.SaveChangesAsync();
        return [.. executions.Select(e => e.Id)];
    }

    [Fact]
    public async Task GetExecutionsAsync_PagesCleanlyWhenEveryRowSharesAStartTime()
    {
        // Routine, not exotic: BeginExecution stamps UtcNow and the CMS scheduler fires several jobs
        // on one tick. Without the Id half of the cursor a page boundary landing inside a group of
        // equal timestamps either repeats rows for ever or skips them — and every existing test seeds
        // distinct timestamps, so the tie-break was unverified.
        using var factory = new SqliteDbContextFactory();
        var sameInstant = new DateTimeOffset(DateTime.UtcNow.Date) + TimeSpan.FromHours(12);
        var ids = await SeedSimultaneousAsync(factory, sameInstant, 5);
        var queryService = new JobExecutionQueryService(factory, TimeProvider.System);

        var seen = new List<long>();
        ExecutionCursor? cursor = null;

        for (var page = 0; page < 5; page++)
        {
            var result = await queryService.GetExecutionsAsync(new ExecutionFilter(), cursor, pageSize: 2);
            seen.AddRange(result.Items.Select(item => item.Id));

            if (!result.HasMore)
                break;

            cursor = result.NextCursor;
        }

        // Every row exactly once, newest id first — the only stable order available when the
        // timestamps are identical.
        //
        // What this pins is the cursor *predicate*: deleting the `e.Id < after.Id` half fails it.
        // It does not pin the matching `ThenByDescending(e => e.Id)`, because Sqlite returns tied
        // rows in rowid order regardless, so removing that clause still passes here. The descending
        // index keys that make the real ordering work on SQL Server are asserted separately, by
        // .github/sql/verify-schema.sql against a real server.
        Assert.Equal(ids.OrderByDescending(id => id), seen);
    }

    [Fact]
    public async Task GetExecutionsAsync_WithAFullFinalPage_DoesNotClaimThereIsMore()
    {
        // The boundary an off-by-one hides in: the last page is exactly pageSize, so "did we read
        // one extra?" is the only thing distinguishing a full page from a full page plus more.
        using var factory = new SqliteDbContextFactory();
        var sameInstant = new DateTimeOffset(DateTime.UtcNow.Date) + TimeSpan.FromHours(12);
        await SeedSimultaneousAsync(factory, sameInstant, 4);
        var queryService = new JobExecutionQueryService(factory, TimeProvider.System);

        var first = await queryService.GetExecutionsAsync(new ExecutionFilter(), after: null, pageSize: 2);
        var second = await queryService.GetExecutionsAsync(new ExecutionFilter(), first.NextCursor, pageSize: 2);

        Assert.Equal(2, second.Items.Count);
        Assert.False(second.HasMore);
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task GetExecutionsAsync_PagesByKeyset_NewestFirst()
    {
        using var factory = new SqliteDbContextFactory();
        // Truncated to whole seconds: Sqlite storage (via ScheduledJobsInsightsDbContext's
        // DateTimeOffsetToBinaryConverter workaround) round-trips DateTimeOffset with reduced
        // sub-millisecond precision, which would otherwise make an exact-equality assertion flaky.
        var baseline = new DateTimeOffset(DateTime.UtcNow.Date) + TimeSpan.FromHours(12);

        await using (var dbContext = factory.CreateDbContext())
        {
            for (var i = 0; i < 5; i++)
            {
                dbContext.JobExecutions.Add(new JobExecution
                {
                    ScheduledJobId = Guid.NewGuid(),
                    JobName = "Job A",
                    JobTypeName = "Test.JobA",
                    StartedAt = baseline.AddMinutes(i),
                    Status = ExecutionStatus.Succeeded,
                    MachineName = "test"
                });
            }
            await dbContext.SaveChangesAsync();
        }

        var queryService = new JobExecutionQueryService(factory, TimeProvider.System);

        var firstPage = await queryService.GetExecutionsAsync(new ExecutionFilter(), after: null, pageSize: 2);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.True(firstPage.HasMore);
        Assert.Equal(baseline.AddMinutes(4), firstPage.Items[0].StartedAt);
        Assert.Equal(baseline.AddMinutes(3), firstPage.Items[1].StartedAt);

        var secondPage = await queryService.GetExecutionsAsync(new ExecutionFilter(), firstPage.NextCursor, pageSize: 2);
        Assert.Equal(2, secondPage.Items.Count);
        Assert.Equal(baseline.AddMinutes(2), secondPage.Items[0].StartedAt);
        Assert.Equal(baseline.AddMinutes(1), secondPage.Items[1].StartedAt);

        var thirdPage = await queryService.GetExecutionsAsync(new ExecutionFilter(), secondPage.NextCursor, pageSize: 2);
        Assert.Single(thirdPage.Items);
        Assert.False(thirdPage.HasMore);
        Assert.Null(thirdPage.NextCursor);
    }

    [Fact]
    public async Task GetExecutionsAsync_FiltersByJobNameAndStatus()
    {
        using var factory = new SqliteDbContextFactory();

        await using (var dbContext = factory.CreateDbContext())
        {
            dbContext.JobExecutions.AddRange(
                new JobExecution { ScheduledJobId = Guid.NewGuid(), JobName = "Job A", JobTypeName = "A", StartedAt = DateTimeOffset.UtcNow, Status = ExecutionStatus.Succeeded, MachineName = "m" },
                new JobExecution { ScheduledJobId = Guid.NewGuid(), JobName = "Job A", JobTypeName = "A", StartedAt = DateTimeOffset.UtcNow, Status = ExecutionStatus.Failed, MachineName = "m" },
                new JobExecution { ScheduledJobId = Guid.NewGuid(), JobName = "Job B", JobTypeName = "B", StartedAt = DateTimeOffset.UtcNow, Status = ExecutionStatus.Succeeded, MachineName = "m" });
            await dbContext.SaveChangesAsync();
        }

        var queryService = new JobExecutionQueryService(factory, TimeProvider.System);

        var filtered = await queryService.GetExecutionsAsync(
            new ExecutionFilter(JobName: "Job A", Status: ExecutionStatus.Succeeded), after: null, pageSize: 10);

        Assert.Single(filtered.Items);
        Assert.Equal("Job A", filtered.Items[0].JobName);
        Assert.Equal(ExecutionStatus.Succeeded, filtered.Items[0].Status);
    }

    [Fact]
    public async Task GetLogEntriesAsync_ReturnsEntries_OrderedBySequence()
    {
        using var factory = new SqliteDbContextFactory();
        long executionId;

        await using (var dbContext = factory.CreateDbContext())
        {
            var execution = new JobExecution { ScheduledJobId = Guid.NewGuid(), JobName = "Job", JobTypeName = "Job", StartedAt = DateTimeOffset.UtcNow, Status = ExecutionStatus.Running, MachineName = "m" };
            dbContext.JobExecutions.Add(execution);
            await dbContext.SaveChangesAsync();
            executionId = execution.Id;

            dbContext.JobLogEntries.AddRange(
                new JobLogEntry { JobExecutionId = executionId, Sequence = 3, Timestamp = DateTimeOffset.UtcNow, Message = "third" },
                new JobLogEntry { JobExecutionId = executionId, Sequence = 1, Timestamp = DateTimeOffset.UtcNow, Message = "first" },
                new JobLogEntry { JobExecutionId = executionId, Sequence = 2, Timestamp = DateTimeOffset.UtcNow, Message = "second" });
            await dbContext.SaveChangesAsync();
        }

        var queryService = new JobExecutionQueryService(factory, TimeProvider.System);
        var entries = await queryService.GetLogEntriesAsync(executionId);

        Assert.Equal(["first", "second", "third"], entries.Select(e => e.Message));
    }

    [Theory]
    [InlineData(0, new[] { "first", "second", "third" })]
    [InlineData(1, new[] { "second", "third" })]
    [InlineData(2, new[] { "third" })]
    [InlineData(3, new string[0])]
    public async Task GetLogEntriesAsync_AfterSequence_ReturnsOnlyNewerEntries(int afterSequence, string[] expected)
    {
        // Backs the detail view's polling: each tick asks only for lines past the highest sequence
        // it already holds, so a long-running chatty execution does not re-read its whole log.
        using var factory = new SqliteDbContextFactory();
        long executionId;

        await using (var dbContext = factory.CreateDbContext())
        {
            var execution = new JobExecution { ScheduledJobId = Guid.NewGuid(), JobName = "Job", JobTypeName = "Job", StartedAt = DateTimeOffset.UtcNow, Status = ExecutionStatus.Running, MachineName = "m" };
            dbContext.JobExecutions.Add(execution);
            await dbContext.SaveChangesAsync();
            executionId = execution.Id;

            dbContext.JobLogEntries.AddRange(
                new JobLogEntry { JobExecutionId = executionId, Sequence = 1, Timestamp = DateTimeOffset.UtcNow, Message = "first" },
                new JobLogEntry { JobExecutionId = executionId, Sequence = 2, Timestamp = DateTimeOffset.UtcNow, Message = "second" },
                new JobLogEntry { JobExecutionId = executionId, Sequence = 3, Timestamp = DateTimeOffset.UtcNow, Message = "third" });
            await dbContext.SaveChangesAsync();
        }

        var queryService = new JobExecutionQueryService(factory, TimeProvider.System);
        var entries = await queryService.GetLogEntriesAsync(executionId, afterSequence);

        Assert.Equal(expected, entries.Select(e => e.Message));
    }

    [Fact]
    public async Task GetLogEntriesAsync_AfterSequence_IsScopedToTheRequestedExecution()
    {
        using var factory = new SqliteDbContextFactory();
        long firstId, secondId;

        await using (var dbContext = factory.CreateDbContext())
        {
            var first = new JobExecution { ScheduledJobId = Guid.NewGuid(), JobName = "A", JobTypeName = "A", StartedAt = DateTimeOffset.UtcNow, Status = ExecutionStatus.Running, MachineName = "m" };
            var second = new JobExecution { ScheduledJobId = Guid.NewGuid(), JobName = "B", JobTypeName = "B", StartedAt = DateTimeOffset.UtcNow, Status = ExecutionStatus.Running, MachineName = "m" };
            dbContext.JobExecutions.AddRange(first, second);
            await dbContext.SaveChangesAsync();
            firstId = first.Id;
            secondId = second.Id;

            dbContext.JobLogEntries.AddRange(
                new JobLogEntry { JobExecutionId = firstId, Sequence = 1, Timestamp = DateTimeOffset.UtcNow, Message = "a1" },
                new JobLogEntry { JobExecutionId = firstId, Sequence = 2, Timestamp = DateTimeOffset.UtcNow, Message = "a2" },
                new JobLogEntry { JobExecutionId = secondId, Sequence = 1, Timestamp = DateTimeOffset.UtcNow, Message = "b1" },
                new JobLogEntry { JobExecutionId = secondId, Sequence = 2, Timestamp = DateTimeOffset.UtcNow, Message = "b2" });
            await dbContext.SaveChangesAsync();
        }

        var queryService = new JobExecutionQueryService(factory, TimeProvider.System);
        var entries = await queryService.GetLogEntriesAsync(secondId, afterSequence: 1);

        Assert.Equal(["b2"], entries.Select(e => e.Message));
    }

    [Fact]
    public async Task GetExecutionsAsync_FlagsRowsWithASummary_WithoutSelectingTheText()
    {
        // The list projection deliberately leaves ResultSummary out — the grid only needs to know
        // whether one exists, and the column is unbounded.
        using var factory = new SqliteDbContextFactory();
        var baseline = new DateTimeOffset(DateTime.UtcNow.Date) + TimeSpan.FromHours(12);

        await using (var dbContext = factory.CreateDbContext())
        {
            dbContext.JobExecutions.Add(new JobExecution
            {
                ScheduledJobId = Guid.NewGuid(),
                JobName = "With summary",
                JobTypeName = "Test.WithSummary",
                StartedAt = baseline.AddMinutes(1),
                Status = ExecutionStatus.Succeeded,
                MachineName = "test",
                ResultSummary = "Totals\n------\n  Rows: 12"
            });
            dbContext.JobExecutions.Add(new JobExecution
            {
                ScheduledJobId = Guid.NewGuid(),
                JobName = "Without summary",
                JobTypeName = "Test.WithoutSummary",
                StartedAt = baseline,
                Status = ExecutionStatus.Succeeded,
                MachineName = "test"
            });
            await dbContext.SaveChangesAsync();
        }

        var page = await new JobExecutionQueryService(factory, TimeProvider.System)
            .GetExecutionsAsync(new ExecutionFilter(), after: null, pageSize: 10);

        Assert.True(Assert.Single(page.Items, i => i.JobName == "With summary").HasResultSummary);
        Assert.False(Assert.Single(page.Items, i => i.JobName == "Without summary").HasResultSummary);
    }

    [Fact]
    public async Task GetExecutionAsync_ReturnsTheSummaryWithItsNewlinesIntact()
    {
        using var factory = new SqliteDbContextFactory();
        var summary = "Totals\n------\n  Rows: 12\n  Skipped: 3";
        long executionId;

        await using (var dbContext = factory.CreateDbContext())
        {
            var execution = new JobExecution
            {
                ScheduledJobId = Guid.NewGuid(),
                JobName = "Job A",
                JobTypeName = "Test.JobA",
                StartedAt = DateTimeOffset.UtcNow,
                Status = ExecutionStatus.Succeeded,
                MachineName = "test",
                ResultSummary = summary
            };
            dbContext.JobExecutions.Add(execution);
            await dbContext.SaveChangesAsync();
            executionId = execution.Id;
        }

        var loaded = await new JobExecutionQueryService(factory, TimeProvider.System).GetExecutionAsync(executionId);

        Assert.Equal(summary, loaded!.ResultSummary);
    }

    [Fact]
    public async Task GetDistinctJobNamesAsync_DoesNotRequeryWithinTheCacheWindow()
    {
        // The dropdown query has to look at every row to produce a distinct list, and prerendering
        // means the list page asks for it twice per view. Caching turns that into one query a minute.
        using var sqlite = new SqliteDbContextFactory();
        var factory = new CountingDbContextFactory(sqlite);
        await SeedJobNamesAsync(sqlite, "Catalog Reindex", "Nightly Import");
        var queryService = new JobExecutionQueryService(factory, new AdjustableTimeProvider());

        var first = await queryService.GetDistinctJobNamesAsync();
        var second = await queryService.GetDistinctJobNamesAsync();

        Assert.Equal(["Catalog Reindex", "Nightly Import"], first);
        Assert.Equal(first, second);
        Assert.Equal(1, factory.Count);
    }

    [Fact]
    public async Task GetDistinctJobNamesAsync_RefreshesOnceTheCacheWindowHasPassed()
    {
        using var sqlite = new SqliteDbContextFactory();
        var factory = new CountingDbContextFactory(sqlite);
        var clock = new AdjustableTimeProvider();
        await SeedJobNamesAsync(sqlite, "Nightly Import");
        var queryService = new JobExecutionQueryService(factory, clock);

        Assert.Equal(["Nightly Import"], await queryService.GetDistinctJobNamesAsync());

        // A job runs for the first time — the only thing that changes this list.
        await SeedJobNamesAsync(sqlite, "Brand New Job");
        Assert.Equal(["Nightly Import"], await queryService.GetDistinctJobNamesAsync());  // still cached

        clock.Advance(TimeSpan.FromMinutes(2));

        Assert.Equal(["Brand New Job", "Nightly Import"], await queryService.GetDistinctJobNamesAsync());
        Assert.Equal(2, factory.Count);
    }

    [Fact]
    public async Task GetDistinctJobNamesAsync_ConcurrentCallers_ShareASingleQuery()
    {
        // Prerender and the circuit start within milliseconds of each other, so without the gate the
        // saved query would simply happen twice on every cold cache.
        using var sqlite = new SqliteDbContextFactory();
        var factory = new CountingDbContextFactory(sqlite);
        await SeedJobNamesAsync(sqlite, "Nightly Import");
        var queryService = new JobExecutionQueryService(factory, new AdjustableTimeProvider());

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => queryService.GetDistinctJobNamesAsync()));

        Assert.Equal(1, factory.Count);
    }

    private static async Task SeedJobNamesAsync(SqliteDbContextFactory factory, params string[] jobNames)
    {
        await using var dbContext = factory.CreateDbContext();
        foreach (var jobName in jobNames)
        {
            dbContext.JobExecutions.Add(new JobExecution
            {
                ScheduledJobId = Guid.NewGuid(),
                JobName = jobName,
                JobTypeName = jobName,
                StartedAt = DateTimeOffset.UtcNow,
                Status = ExecutionStatus.Succeeded,
                MachineName = "test"
            });
        }
        await dbContext.SaveChangesAsync();
    }
}

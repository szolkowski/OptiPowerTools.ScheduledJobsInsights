using EPiServer.DataAbstraction;
using Microsoft.Extensions.Options;
using NSubstitute;
using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Jobs;
using OptiPowerTools.ScheduledJobsInsights.Logging;
using OptiPowerTools.ScheduledJobsInsights.Repositories;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Jobs;

public class ScheduledJobsInsightsCleanupJobTests
{
    [Fact]
    public void ExecuteJob_DeletesInBatchesUntilExhausted_AndReturnsTotal()
    {
        var writer = Substitute.For<IJobExecutionWriter>();
        writer.BeginExecution(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Returns(1L);
        var scheduledJobRepository = Substitute.For<IScheduledJobRepository>();
        var cleanupRepository = Substitute.For<ICleanupRepository>();
        cleanupRepository.DeleteExecutionsOlderThan(Arg.Any<DateTimeOffset>(), Arg.Any<int>())
            .Returns(500, 500, 137, 0);
        var options = Options.Create(new OptiPowerToolScheduledJobsInsightsOptions { RetentionDays = 30, CleanupBatchSize = 500 });

        var job = new ScheduledJobsInsightsCleanupJob(writer, scheduledJobRepository, cleanupRepository, options);

        var result = job.Execute();

        Assert.Equal("Deleted 1137 job execution(s) older than 30 day(s).", result);
        cleanupRepository.Received(4).DeleteExecutionsOlderThan(Arg.Any<DateTimeOffset>(), 500);
        writer.Received(1).RecordMetric(1L, "ExecutionsDeleted", 1137, null);
    }

    [Fact]
    public void ExecuteJob_WhenNothingToDelete_StopsAfterFirstEmptyBatch()
    {
        var writer = Substitute.For<IJobExecutionWriter>();
        writer.BeginExecution(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Returns(1L);
        var scheduledJobRepository = Substitute.For<IScheduledJobRepository>();
        var cleanupRepository = Substitute.For<ICleanupRepository>();
        cleanupRepository.DeleteExecutionsOlderThan(Arg.Any<DateTimeOffset>(), Arg.Any<int>()).Returns(0);
        var options = Options.Create(new OptiPowerToolScheduledJobsInsightsOptions());

        var job = new ScheduledJobsInsightsCleanupJob(writer, scheduledJobRepository, cleanupRepository, options);

        var result = job.Execute();

        Assert.Equal("Deleted 0 job execution(s) older than 30 day(s).", result);
        cleanupRepository.Received(1).DeleteExecutionsOlderThan(Arg.Any<DateTimeOffset>(), Arg.Any<int>());
    }
}

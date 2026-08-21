using Bunit;
using NSubstitute;
using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Data.Entities;
using DetailPage = OptiPowerTools.ScheduledJobsInsights.Components.Pages.Detail;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Components;

/// <summary>
/// The live-updating half of the detail page: polling while a run is in flight, noticing that it
/// finished, and reading once more afterwards.
/// </summary>
/// <remarks>
/// Previously left untested because every case needs a second poll tick and the interval was a
/// hard-coded two seconds. It is now an internal seam, which costs nothing on the public surface —
/// and this is the trickiest logic in the UI, with a real bug already found in it once (metrics
/// arriving after the status flipped, leaving a finished run showing an empty metrics table).
/// </remarks>
[Collection(DetailTestCollection.Name)]
public class DetailPollingTests : ComponentTestBase
{
    private static readonly TimeSpan RealPollInterval = DetailPage.PollInterval;

    public DetailPollingTests()
    {
        DetailPage.PollInterval = TimeSpan.FromMilliseconds(20);
        Options.LogFlushInterval = TimeSpan.FromMilliseconds(10);
    }

    /// <summary>Restores the real interval so no other class inherits this one's seam.</summary>
    protected override void Dispose(bool disposing)
    {
        DetailPage.PollInterval = RealPollInterval;
        base.Dispose(disposing);
    }

    private IRenderedComponent<DetailPage> RenderDetail() =>
        Render<DetailPage>(parameters => parameters.Add(p => p.Id, 1L));

    /// <summary>Returns <paramref name="first"/> once, then <paramref name="rest"/> from then on.</summary>
    private void GivenExecutionSequence(JobExecution first, JobExecution rest) =>
        QueryService.GetExecutionAsync(1, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<JobExecution?>(first), _ => Task.FromResult<JobExecution?>(rest));

    [Fact]
    public void WhileTheRunIsInFlight_ThePageKeepsReadingIt()
    {
        var running = AnExecution(status: ExecutionStatus.Running, completedAt: null);
        QueryService.GetExecutionAsync(1, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<JobExecution?>(running));

        var page = RenderDetail();

        page.WaitForAssertion(
            () => QueryService.Received(3).GetExecutionAsync(1, Arg.Any<CancellationToken>()),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void WhenTheRunFinishes_ThePageShowsTheOutcomeAndStopsPolling()
    {
        var running = AnExecution(status: ExecutionStatus.Running, completedAt: null);
        var finished = AnExecution(status: ExecutionStatus.Succeeded, resultMessage: "Imported 12 items.");
        GivenExecutionSequence(running, finished);

        var page = RenderDetail();

        page.WaitForAssertion(
            () => Assert.Contains("Imported 12 items.", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // Wait past the trailing read (LogFlushInterval + 500ms) before snapshotting, or this
        // measures the race against that one deliberate extra read rather than the poll loop.
        Thread.Sleep(Options.LogFlushInterval + TimeSpan.FromMilliseconds(800));
        var settled = QueryService.ReceivedCalls().Count();

        // Several poll intervals' worth of nothing: the loop really has stopped, rather than merely
        // being between ticks.
        Thread.Sleep(200);
        Assert.Equal(settled, QueryService.ReceivedCalls().Count());
    }

    [Fact]
    public void AfterTheRunFinishes_ThePageReadsOnceMoreForTheTrailingWrites()
    {
        // The bug this exists for: Complete is written synchronously, but the final log lines and
        // *all* the automatic metrics go through the buffered channel. A loop that stopped the
        // instant the status flipped left a finished run showing an empty metrics table until
        // somebody reloaded by hand.
        var running = AnExecution(status: ExecutionStatus.Running, completedAt: null);
        var finished = AnExecution(status: ExecutionStatus.Succeeded);
        GivenExecutionSequence(running, finished);

        var metrics = new List<JobMetric>();
        QueryService.GetMetricsAsync(1, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<JobMetric>>(metrics));

        var page = RenderDetail();

        page.WaitForAssertion(
            () => Assert.Contains("Succeeded", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // The metrics land after the status flipped — exactly the window the trailing read covers.
        metrics.Add(new JobMetric { Id = 1, JobExecutionId = 1, Name = "DurationMs", Value = 42, RecordedAt = Noon });

        page.WaitForAssertion(
            () => Assert.Contains("DurationMs", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void AnExecutionThatWasAlreadyFinished_IsNeverPolled()
    {
        QueryService.GetExecutionAsync(1, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<JobExecution?>(AnExecution(status: ExecutionStatus.Succeeded)));

        RenderDetail();
        Thread.Sleep(150);

        QueryService.Received(1).GetExecutionAsync(1, Arg.Any<CancellationToken>());
    }
}

using Bunit;
using NSubstitute;
using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Data.Entities;
using OptiPowerTools.ScheduledJobsInsights.Repositories;
using DetailPage = OptiPowerTools.ScheduledJobsInsights.Components.Pages.Detail;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Components;

/// <summary>
/// The live-updating half of the detail page: polling while a run is in flight, noticing that it
/// finished, and reading once more afterwards.
/// </summary>
/// <remarks>
/// Previously left untested because every case needs a second poll tick and the interval was a
/// hard-coded two seconds. It is now a configuration option, which is a legitimate thing for an
/// installation to tune and happens to make these tests possible — and this is the trickiest logic in
/// the UI, with a real bug already found in it once (metrics arriving after the status flipped,
/// leaving a finished run showing an empty metrics table).
/// </remarks>
public class DetailPollingTests : ComponentTestBase
{
    public DetailPollingTests()
    {
        // Per-instance options now, not a static seam — so these tests no longer have to share an
        // xUnit collection to keep out of each other's way.
        Options.DetailPollInterval = TimeSpan.FromMilliseconds(20);
        Options.LogFlushInterval = TimeSpan.FromMilliseconds(10);
    }

    private IRenderedComponent<DetailPage> RenderDetail() =>
        Render<DetailPage>(parameters => parameters.Add(p => p.Id, 1L));

    /// <summary>Returns <paramref name="first"/> once, then <paramref name="rest"/> from then on.</summary>
    private void GivenExecutionSequence(JobExecution first, JobExecution rest) =>
        QueryService.GetExecutionAsync(1, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<JobExecution?>(first), _ => Task.FromResult<JobExecution?>(rest));

    /// <summary>
    /// Stubs the narrow projection a poll tick reads, to agree with <paramref name="execution"/>.
    /// </summary>
    /// <remarks>
    /// Without this the substitute returns null and the component falls back to a full read, which is
    /// the safety net rather than the path under test.
    /// </remarks>
    private void GivenStatusMatching(JobExecution execution, int? resultSummaryLength = null) =>
        QueryService.GetExecutionStatusAsync(1, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ExecutionStatusSnapshot?>(new ExecutionStatusSnapshot(
                execution.Status,
                execution.CompletedAt,
                execution.ResultMessage,
                execution.ExceptionMessage,
                resultSummaryLength ?? execution.ResultSummary?.Length ?? 0)));

    [Fact]
    public void WhileTheRunIsInFlight_ThePageKeepsReadingIt()
    {
        var running = AnExecution(status: ExecutionStatus.Running, completedAt: null);
        QueryService.GetExecutionAsync(1, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<JobExecution?>(running));
        GivenStatusMatching(running);

        var page = RenderDetail();

        page.WaitForAssertion(
            () => QueryService.Received(3).GetExecutionStatusAsync(1, Arg.Any<CancellationToken>()),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void WhileNothingChanges_ThePageDoesNotReReadTheWholeRow()
    {
        // The row carries ResultSummary, InputDataJson and ExceptionStackTrace, all unbounded. Polling
        // used to re-read all three every couple of seconds, per viewer, for the life of the run —
        // the very cost the incremental log fetch exists to avoid, paid on a different column.
        var running = AnExecution(status: ExecutionStatus.Running, completedAt: null);
        QueryService.GetExecutionAsync(1, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<JobExecution?>(running));
        GivenStatusMatching(running);

        var page = RenderDetail();

        page.WaitForAssertion(
            () => QueryService.Received(3).GetExecutionStatusAsync(1, Arg.Any<CancellationToken>()),
            TimeSpan.FromSeconds(5));

        // Once, at initialisation. Every tick after that read only the projection and the new lines.
        QueryService.Received(1).GetExecutionAsync(1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void WhenACheckpointingJobGrowsItsSummary_ThePageReReadsIt()
    {
        // A job calling FlushSummary mid-run changes nothing else about the row, so the length is what
        // tells the page to go back for the text. Without it the summary would freeze at whatever it
        // was on first render and only a manual reload would move it.
        var running = AnExecution(status: ExecutionStatus.Running, completedAt: null, resultSummary: "one line\n");
        var grown = AnExecution(status: ExecutionStatus.Running, completedAt: null, resultSummary: "one line\ntwo lines\n");
        GivenExecutionSequence(running, grown);
        GivenStatusMatching(running, resultSummaryLength: grown.ResultSummary!.Length);

        var page = RenderDetail();

        page.WaitForAssertion(
            () => Assert.Contains("two lines", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task WhenTheRunFinishes_ThePageShowsTheOutcomeAndStopsPolling()
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
        await Task.Delay(Options.LogFlushInterval + TimeSpan.FromMilliseconds(800));
        var settled = QueryService.ReceivedCalls().Count();

        // Several poll intervals' worth of nothing: the loop really has stopped, rather than merely
        // being between ticks. Proving an absence needs elapsed time — there is no event to await.
        await Task.Delay(TimeSpan.FromMilliseconds(200));
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
    public async Task AnExecutionThatWasAlreadyFinished_IsNeverPolled()
    {
        QueryService.GetExecutionAsync(1, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<JobExecution?>(AnExecution(status: ExecutionStatus.Succeeded)));

        RenderDetail();

        // Several poll intervals: if a loop had started, the count would have climbed by now.
        await Task.Delay(TimeSpan.FromMilliseconds(150));

        // Discarded, not awaited: the assertion is the call itself.
        _ = QueryService.Received(1).GetExecutionAsync(1, Arg.Any<CancellationToken>());
    }
}

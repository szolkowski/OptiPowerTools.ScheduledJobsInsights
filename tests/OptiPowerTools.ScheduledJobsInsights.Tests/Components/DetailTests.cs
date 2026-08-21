using Bunit;
using NSubstitute;
using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Data.Entities;
using DetailPage = OptiPowerTools.ScheduledJobsInsights.Components.Pages.Detail;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Components;

[Collection(DetailTestCollection.Name)]
public class DetailTests : ComponentTestBase
{
    private IRenderedComponent<DetailPage> RenderDetail(long id = 1, string? viewerTimeZone = null) =>
        Render<DetailPage>(parameters => parameters
            .Add(p => p.Id, id)
            .Add(p => p.ViewerTimeZone, viewerTimeZone));

    private void GivenExecution(JobExecution? execution, long id = 1) =>
        QueryService.GetExecutionAsync(id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(execution));

    [Fact]
    public void AMissingExecution_SaysSo_RatherThanLoadingForever()
    {
        // Without the _loaded flag these two states are indistinguishable and a bad id sits on
        // "Loading…" permanently.
        GivenExecution(null, id: 42);

        var page = RenderDetail(id: 42);

        Assert.Contains("Execution #42 was not found.", page.Markup);
        Assert.DoesNotContain("Loading…", page.Markup);
    }

    [Fact]
    public void AnExecution_RendersItsHeadlineFacts()
    {
        GivenExecution(AnExecution(jobName: "Nightly Import", resultMessage: "Imported 12 items."));

        var page = RenderDetail();

        Assert.Contains("Nightly Import", page.Find("h1").TextContent);
        Assert.Contains("Succeeded", page.Find(".status-badge").TextContent);
        Assert.Contains("Imported 12 items.", page.Markup);
        Assert.Contains("web-01", page.Markup);
    }

    [Fact]
    public void WithNoSummary_TheSummarySectionIsAbsentEntirely()
    {
        GivenExecution(AnExecution(resultSummary: null));

        var page = RenderDetail();

        Assert.DoesNotContain(page.FindAll(".accordion-title"), e => e.TextContent.Contains("Result summary"));
    }

    [Fact]
    public void AShortSummary_RendersOpen_WithItsSizeInTheHeader()
    {
        GivenExecution(AnExecution(resultSummary: "Totals\n  Rows: 12\n"));

        var page = RenderDetail();

        var section = SectionTitled(page, "Result summary");
        Assert.True(section.HasAttribute("open"), "A short summary is what the reader came for; it should not need a click.");
        Assert.Contains("2 lines", section.QuerySelector(".accordion-badge")!.TextContent);
    }

    [Fact]
    public void ALongSummary_StartsCollapsed()
    {
        // Past the threshold an expanded section is several screens of text that pushes the log out
        // of view. The badge still advertises the size so the reader knows what is behind the click.
        GivenExecution(AnExecution(resultSummary: ASummaryOf(2_000)));

        var page = RenderDetail();

        var section = SectionTitled(page, "Result summary");
        Assert.False(section.HasAttribute("open"));
        Assert.Contains("2,000 lines", section.QuerySelector(".accordion-badge")!.TextContent);
    }

    [Theory]
    [InlineData(200, true)]   // exactly at the limit still opens
    [InlineData(201, false)]  // one past it does not
    public void TheCollapseThreshold_IsInclusive(int lines, bool expectedOpen)
    {
        GivenExecution(AnExecution(resultSummary: ASummaryOf(lines)));

        Assert.Equal(expectedOpen, SectionTitled(RenderDetail(), "Result summary").HasAttribute("open"));
    }

    [Fact]
    public void TheSummaryBody_PreservesItsNewlines()
    {
        GivenExecution(AnExecution(resultSummary: "first\nsecond\n"));

        var body = RenderDetail().Find(".summary-text").TextContent;

        Assert.Equal("first\nsecond\n", body);
    }

    [Fact]
    public void Metrics_RenderWithInvariantValues()
    {
        GivenExecution(AnExecution());
        QueryService.GetMetricsAsync(1, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<JobMetric>>(
        [
            new JobMetric { Id = 1, JobExecutionId = 1, Name = "DurationMs", Value = 310.994, Unit = "ms" }
        ]));

        var page = RenderDetail();

        var section = SectionTitled(page, "Metrics");
        Assert.True(section.HasAttribute("open"));
        Assert.Contains("310.99", page.Find(".metrics-table").TextContent);
    }

    [Fact]
    public void InputData_IsPresentButCollapsed()
    {
        GivenExecution(AnExecution(inputDataJson: """{"source":"ERP"}"""));

        var section = SectionTitled(RenderDetail(), "Input data");

        Assert.False(section.HasAttribute("open"));
        Assert.Contains("\"source\":\"ERP\"", section.TextContent);
    }

    [Fact]
    public void AStackTrace_AppearsOnlyForAFailure()
    {
        GivenExecution(AnExecution(
            status: ExecutionStatus.Failed,
            resultMessage: null,
            exceptionMessage: "Malformed record at row 42.",
            exceptionStackTrace: "   at Contoso.Jobs.NightlyImportJob.ExecuteJob()"));

        var page = RenderDetail();

        Assert.Contains("Malformed record at row 42.", page.Find(".error-text").TextContent);
        Assert.Contains("ExecuteJob()", SectionTitled(page, "Stack trace").TextContent);
    }

    [Fact]
    public void ASuccessfulExecution_HasNoStackTraceSection()
    {
        GivenExecution(AnExecution());

        Assert.DoesNotContain(RenderDetail().FindAll(".accordion-title"), e => e.TextContent.Contains("Stack trace"));
    }

    [Fact]
    public void Timestamps_RenderInTheViewerTimeZone()
    {
        GivenExecution(AnExecution());

        var page = RenderDetail(viewerTimeZone: "Europe/Warsaw");

        // Stored 12:00Z, summer, so UTC+02:00.
        Assert.Contains("2026-08-19 14:00:00 UTC+02:00", page.Find(".execution-meta").TextContent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Not/A/Zone")]
    public void Timestamps_FallBackToUtc_WhenTheZoneIsUnusable(string? zone)
    {
        GivenExecution(AnExecution());

        var page = RenderDetail(viewerTimeZone: zone);

        Assert.Contains("2026-08-19 12:00:00 UTC", page.Find(".execution-meta").TextContent);
    }

    [Fact]
    public void AnEmptyLog_SaysSo()
    {
        GivenExecution(AnExecution());

        Assert.Contains("No log lines recorded.", RenderDetail().Find(".console").TextContent);
    }

    [Fact]
    public void LogLines_RenderWithTheirSeverityAndViewerLocalTime()
    {
        GivenExecution(AnExecution());
        QueryService.GetLogEntriesAsync(1, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<IReadOnlyList<JobLogEntry>>(
            [
                new JobLogEntry
                {
                    Id = 1, JobExecutionId = 1, Sequence = 1, Timestamp = Noon,
                    Severity = LogSeverity.Error, Source = LogEntrySource.DevLog, Message = "upstream 503"
                }
            ]));

        var page = RenderDetail(viewerTimeZone: "Europe/Warsaw");

        var line = page.Find(".console-line");
        Assert.Contains("14:00:00.000", line.TextContent);
        Assert.Contains("Error", line.TextContent);
        Assert.Contains("upstream 503", line.TextContent);
        Assert.Contains("1 line", page.Find(".log-count").TextContent);
    }

    [Fact]
    public void TheFirstLoad_FetchesTheLogFromSequenceZero()
    {
        // Only the first read is pinned here. The incremental behaviour that matters — later polls
        // passing the highest sequence already held, rather than re-reading a 5,000-line log every
        // two seconds — needs a second poll tick and belongs with the timing tests.
        GivenExecution(AnExecution());

        RenderDetail();

        QueryService.Received().GetLogEntriesAsync(1, 0, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AJobWithNoCmsDefinition_HidesTheSettingsLink()
    {
        // SeedHistoryJob's synthetic rows carry ids that resolve to nothing.
        GivenExecution(AnExecution(scheduledJobId: Guid.Empty));

        Assert.Empty(RenderDetail().FindAll("a.action-link"));
    }

    private static AngleSharp.Dom.IElement SectionTitled(IRenderedComponent<DetailPage> page, string title) =>
        page.FindAll("details.accordion")
            .Single(section => section.QuerySelector(".accordion-title")!.TextContent.Trim() == title);
    [Fact]
    public void ANonDefaultCmsShellPath_FlowsIntoTheBackLink()
    {
        Options.CmsShellPath = "/custom/insights";
        GivenExecution(AnExecution());

        Assert.Equal("/custom/insights", RenderDetail().Find("a").GetAttribute("href"));
    }

}

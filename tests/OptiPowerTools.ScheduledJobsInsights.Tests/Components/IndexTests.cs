using Bunit;
using NSubstitute;
using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Repositories;
using IndexPage = OptiPowerTools.ScheduledJobsInsights.Components.Pages.Index;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Components;

public class IndexTests : ComponentTestBase
{
    private IRenderedComponent<IndexPage> RenderList(string? viewerTimeZone = null) =>
        Render<IndexPage>(parameters => parameters.Add(p => p.ViewerTimeZone, viewerTimeZone));

    private void GivenPage(params ExecutionListItem[] items) =>
        GivenPage(new ExecutionPage(items, null, false));

    private void GivenPage(ExecutionPage page) =>
        QueryService.GetExecutionsAsync(
                Arg.Any<ExecutionFilter>(), Arg.Any<ExecutionCursor?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(page));

    private static ExecutionListItem ARow(
        long id = 1,
        string jobName = "Nightly Import",
        ExecutionStatus status = ExecutionStatus.Succeeded,
        DateTimeOffset? completedAt = null,
        string? resultMessage = "Imported 12 items.",
        string? exceptionMessage = null,
        bool hasResultSummary = false) =>
        new(id, jobName, status,
            Noon,
            status == ExecutionStatus.Running ? null : completedAt ?? Noon.AddSeconds(3),
            resultMessage, exceptionMessage, hasResultSummary);

    [Fact]
    public void WithNoExecutionsAndNoFilter_SaysNothingHasRunYet()
    {
        GivenPage();

        Assert.Contains("No job executions recorded yet.", RenderList().Markup);
    }

    [Fact]
    public void ARow_LinksToItsDetailPageByQueryString()
    {
        // A path segment here would stop the CMS shell resolving its navigation — the id has to stay
        // in the query string. See ScheduledJobsInsightsCmsRouteConvention.
        GivenPage(ARow(id: 42));

        var link = RenderList().Find(".executions-table tbody a");

        Assert.Equal("/ScheduledJobsInsightsCms/Index?id=42", link.GetAttribute("href"));
        Assert.Equal("Nightly Import", link.TextContent);
    }

    [Fact]
    public void ARow_IsAlwaysARealAnchor_NotOnlyAClickHandler()
    {
        // Progressive enhancement: the list is rendered before the circuit connects, and must stay
        // navigable by keyboard, screen reader and open-in-new-tab regardless.
        GivenPage(ARow());

        Assert.NotEmpty(RenderList().FindAll(".executions-table tbody td a[href]"));
    }

    [Fact]
    public void TheSummaryMarker_AppearsOnlyOnRowsThatRecordedOne()
    {
        GivenPage(
            ARow(id: 1, jobName: "With summary", hasResultSummary: true),
            ARow(id: 2, jobName: "Without summary", hasResultSummary: false));

        var rows = RenderList().FindAll(".executions-table tbody tr");

        Assert.NotNull(rows[0].QuerySelector(".summary-marker"));
        Assert.Null(rows[1].QuerySelector(".summary-marker"));
    }

    [Fact]
    public void ARunningExecution_ShowsTheRunningBadgeAndNoDuration()
    {
        GivenPage(ARow(status: ExecutionStatus.Running, resultMessage: null));

        var cells = RenderList().FindAll(".executions-table tbody td");

        Assert.Contains("Running", cells[1].TextContent);
        Assert.Equal("—", cells[3].TextContent.Trim());
    }

    [Theory]
    [InlineData(310, "310 ms")]
    [InlineData(60_400, "60.4 s")]
    public void Duration_RendersInvariantly(int elapsedMs, string expected)
    {
        GivenPage(ARow(completedAt: Noon.AddMilliseconds(elapsedMs)));

        Assert.Equal(expected, RenderList().FindAll(".executions-table tbody td")[3].TextContent.Trim());
    }

    [Fact]
    public void AFailedExecution_ShowsItsExceptionInPlaceOfAResult()
    {
        GivenPage(ARow(
            status: ExecutionStatus.Failed,
            resultMessage: null,
            exceptionMessage: "The remote endpoint is unavailable (503)."));

        Assert.Contains("unavailable (503)", RenderList().Find(".result-cell").TextContent);
    }

    [Fact]
    public void Timestamps_RenderInTheViewerTimeZone_AndTheNoteSaysWhich()
    {
        GivenPage(ARow());

        var page = RenderList(viewerTimeZone: "Europe/Warsaw");

        Assert.Equal("Times shown in Europe/Warsaw", page.Find(".timezone-note").TextContent.Trim());
        Assert.Equal("2026-08-19 14:00", page.FindAll(".executions-table tbody td")[2].TextContent.Trim());
    }

    [Fact]
    public void WithNoUsableZone_TheNoteSaysUtc_RatherThanLeavingItAmbiguous()
    {
        GivenPage(ARow());

        var page = RenderList(viewerTimeZone: null);

        Assert.Equal("Times shown in UTC", page.Find(".timezone-note").TextContent.Trim());
        Assert.Equal("2026-08-19 12:00", page.FindAll(".executions-table tbody td")[2].TextContent.Trim());
    }

    [Fact]
    public void TheJobFilter_IsPopulatedFromTheDistinctJobNames()
    {
        QueryService.GetDistinctJobNamesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["Catalog Reindex", "Nightly Import"]));
        GivenPage(ARow());

        var options = RenderList().FindAll(".filters select")[0].QuerySelectorAll("option");

        Assert.Equal(["All jobs", "Catalog Reindex", "Nightly Import"], options.Select(o => o.TextContent));
    }

    [Fact]
    public void PagingButtons_ReflectWhetherThereIsAnywhereToGo()
    {
        GivenPage(new ExecutionPage([ARow()], new ExecutionCursor(Noon, 1), HasMore: true));

        var buttons = RenderList().FindAll(".pagination button");

        Assert.True(buttons[0].HasAttribute("disabled"), "Previous should be disabled on the first page.");
        Assert.False(buttons[1].HasAttribute("disabled"), "Next should be enabled while HasMore is true.");
    }

    [Fact]
    public void Next_AdvancesByTheCursor_AndPreviousComesBack()
    {
        // Keyset paging, not offset: the second page is requested with the first page's cursor, and
        // going back re-requests with no cursor at all rather than an arithmetic offset.
        var firstCursor = new ExecutionCursor(Noon, 1);
        GivenPage(new ExecutionPage([ARow()], firstCursor, HasMore: true));
        var page = RenderList();

        page.FindAll(".pagination button")[1].Click();
        QueryService.Received().GetExecutionsAsync(
            Arg.Any<ExecutionFilter>(), firstCursor, Arg.Any<int>(), Arg.Any<CancellationToken>());

        page.FindAll(".pagination button")[0].Click();
        QueryService.Received(2).GetExecutionsAsync(
            Arg.Any<ExecutionFilter>(), null, Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ChangingTheStatusFilter_RequeriesFromTheFirstPage()
    {
        GivenPage(new ExecutionPage([ARow()], new ExecutionCursor(Noon, 1), HasMore: true));
        var page = RenderList();

        page.FindAll(".pagination button")[1].Click();          // move off page one
        page.FindAll(".filters select")[1].Change("Failed");    // then filter

        QueryService.Received().GetExecutionsAsync(
            Arg.Is<ExecutionFilter>(f => f.Status == ExecutionStatus.Failed),
            null,
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void WithAFilterApplied_TheEmptyStateSaysSo()
    {
        QueryService.GetDistinctJobNamesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["Nightly Import"]));
        GivenPage();
        var page = RenderList();

        page.FindAll(".filters select")[0].Change("Nightly Import");

        Assert.Contains("No executions match this filter.", page.Markup);
    }

    [Fact]
    public void ThePageSizeOption_IsWhatGetsRequested()
    {
        Options.PageSize = 7;
        GivenPage(ARow());

        RenderList();

        QueryService.Received().GetExecutionsAsync(
            Arg.Any<ExecutionFilter>(), Arg.Any<ExecutionCursor?>(), 7, Arg.Any<CancellationToken>());
    }
    [Fact]
    public void ANonDefaultCmsShellPath_FlowsIntoEveryLinkTheListRenders()
    {
        // CmsShellPath is simultaneously a route template, a menu URL and the base for these links.
        // Only the first two were covered, so a link that quietly kept the default path would have
        // shipped: it 404s on any installation that moved the page.
        Options.CmsShellPath = "/custom/insights";
        Options.CmsRetentionPath = "/custom/retention";
        GivenPage(ARow(id: 7));

        var page = RenderList();

        Assert.Equal("/custom/insights?id=7", page.Find(".executions-table tbody a").GetAttribute("href"));
        // The retention link is CmsRetentionPath, not a query string on CmsShellPath: the CMS shell
        // highlights the menu entry whose URL equals the request path and never reads the query
        // string, so a shared path highlighted the execution list while retention was open.
        Assert.Equal("/custom/retention", page.Find("a.action-link").GetAttribute("href"));
    }


    [Fact]
    public void AFailedJobFilter_StillShowsTheExecutionList()
    {
        // The bug this pins: the catch around GetDistinctJobNamesAsync set _loadError, and the render
        // guard on _loadError short-circuited the whole table — so a dropdown failure replaced a list
        // that had loaded perfectly well with "Could not read the execution history".
        QueryService.GetDistinctJobNamesAsync(Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<string>>>(_ => throw new InvalidOperationException("dropdown died"));
        GivenPage(ARow(id: 5, jobName: "Nightly Import"));

        var page = RenderList();

        Assert.Single(page.FindAll("table.executions-table"));
        Assert.Contains("Nightly Import", page.Find("table.executions-table").TextContent);
        Assert.Contains("dropdown died", page.Find(".filter-error").TextContent);
        Assert.Empty(page.FindAll(".load-error"));
    }

    [Fact]
    public void AFailedExecutionRead_IsStillFatalToTheList()
    {
        // The other half of the same decision: when the list itself cannot be read there is nothing
        // to show, and that must stay a load error rather than a quiet notice.
        QueryService.GetExecutionsAsync(
                Arg.Any<ExecutionFilter>(), Arg.Any<ExecutionCursor?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<Task<ExecutionPage>>(_ => throw new InvalidOperationException("history unreadable"));

        var page = RenderList();

        Assert.Contains("history unreadable", page.Find(".load-error").TextContent);
        Assert.Empty(page.FindAll("table.executions-table"));
    }

    [Theory]
    [InlineData(ExecutionStatus.Succeeded, "sji-status-succeeded")]
    [InlineData(ExecutionStatus.Failed, "sji-status-failed")]
    [InlineData(ExecutionStatus.Stopped, "sji-status-stopped")]
    [InlineData(ExecutionStatus.Interrupted, "sji-status-interrupted")]
    [InlineData(ExecutionStatus.Running, "sji-status-running")]
    public void AStatusBadge_CarriesItsColourAsAClass_NotAnInlineStyle(ExecutionStatus status, string expectedClass)
    {
        // An inline style attribute is dropped outright by a CMS served under a style-src policy
        // without 'unsafe-inline' — the normal case for a back office — which left every badge
        // unpainted with nothing to indicate why.
        GivenPage(ARow(status: status));

        var badge = RenderList().Find(".status-badge");

        Assert.Contains(expectedClass, badge.GetAttribute("class"), StringComparison.Ordinal);
        Assert.Null(badge.GetAttribute("style"));
    }
}

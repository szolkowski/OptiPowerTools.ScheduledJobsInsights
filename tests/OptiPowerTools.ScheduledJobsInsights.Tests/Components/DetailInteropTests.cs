using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using NSubstitute;
using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Data.Entities;
using DetailPage = OptiPowerTools.ScheduledJobsInsights.Components.Pages.Detail;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Components;

/// <summary>
/// The detail page's JS interop: the Copy button and the console jump controls.
/// </summary>
/// <remarks>
/// Runs in <b>strict</b> JS interop mode, unlike <see cref="DetailTests"/>. That is the point of
/// these tests — strict mode fails on any call that has not been set up, so the module URL and every
/// function name are asserted implicitly, and a typo in either is a test failure rather than a button
/// that silently does nothing in the browser.
/// </remarks>
[Collection(DetailTestCollection.Name)]
public class DetailInteropTests : ComponentTestBase
{
    /// <summary>Must match the import in Detail.razor exactly; strict mode is what enforces that.</summary>
    private const string ModuleUrl = "./_content/OptiPowerTools.ScheduledJobsInsights/js/detail-interop.js";

    private readonly BunitJSModuleInterop _module;

    public DetailInteropTests()
    {
        // Strict, so an unrecognised call is a failure rather than a silent no-op. Verified by
        // mutation: pointing the component at a different module URL fails every test in this class.
        // Virtualize needs no stubbing of its own — bUnit handles its interop natively even here.
        JSInterop.Mode = JSRuntimeMode.Strict;
        _module = JSInterop.SetupModule(ModuleUrl);
    }

    private IRenderedComponent<DetailPage> RenderDetail(JobExecution execution, params JobLogEntry[] log)
    {
        QueryService.GetExecutionAsync(execution.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<JobExecution?>(execution));
        QueryService.GetLogEntriesAsync(execution.Id, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<JobLogEntry>>(log));

        return Render<DetailPage>(parameters => parameters.Add(p => p.Id, execution.Id));
    }

    private static JobLogEntry ALogLine(int sequence = 1) =>
        new()
        {
            Id = sequence,
            JobExecutionId = 1,
            Sequence = sequence,
            Timestamp = Noon,
            Severity = LogSeverity.Info,
            Source = LogEntrySource.DevLog,
            Message = $"line {sequence}"
        };

    private static IElement Button(IRenderedComponent<DetailPage> page, string label) =>
        page.FindAll("button").Single(b => b.TextContent.Trim() == label);

    [Fact]
    public void Copy_SendsTheSummaryTextToTheClipboard_AndConfirmsOnTheButton()
    {
        _module.Setup<bool>("copyText", _ => true).SetResult(true);
        var page = RenderDetail(AnExecution(resultSummary: "Totals\n  Rows: 12\n"));

        Button(page, "Copy").Click();

        var invocation = Assert.Single(_module.Invocations["copyText"]);
        Assert.Equal("Totals\n  Rows: 12\n", Assert.Single(invocation.Arguments));
        Assert.Equal("Copied", Button(page, "Copied").TextContent.Trim());
    }

    [Fact]
    public void Copy_SaysSo_WhenTheClipboardIsUnavailable()
    {
        // navigator.clipboard only exists in a secure context, so the JS side returns false rather
        // than throwing. The button has to report that instead of silently doing nothing.
        _module.Setup<bool>("copyText", _ => true).SetResult(false);
        var page = RenderDetail(AnExecution(resultSummary: "anything"));

        Button(page, "Copy").Click();

        Assert.Equal("Copy unavailable", Button(page, "Copy unavailable").TextContent.Trim());
    }

    [Fact]
    public void Copy_RevertsItsLabel_OnceTheConfirmationHasBeenSeen()
    {
        // The one test here that waits on wall-clock: the confirmation window is a hard-coded two
        // seconds. WaitForAssertion polls rather than sleeping a fixed amount, so it is not flaky,
        // but it does make this the slowest test in the file.
        _module.Setup<bool>("copyText", _ => true).SetResult(true);
        var page = RenderDetail(AnExecution(resultSummary: "anything"));

        Button(page, "Copy").Click();
        Assert.Equal("Copied", Button(page, "Copied").TextContent.Trim());

        page.WaitForAssertion(
            () => Assert.Single(page.FindAll("button"), b => b.TextContent.Trim() == "Copy"),
            timeout: TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void JumpToEnd_ScrollsTheConsoleElement()
    {
        // Virtualize keeps no element at either end of the log, so jumping cannot be done with an
        // anchor — it has to set scrollTop on the console itself, through the module.
        _module.SetupVoid("scrollToEnd", _ => true).SetVoidResult();
        var page = RenderDetail(AnExecution(), ALogLine());

        Button(page, "Jump to end").Click();

        var invocation = Assert.Single(_module.Invocations["scrollToEnd"]);
        Assert.IsType<ElementReference>(Assert.Single(invocation.Arguments));
    }

    [Fact]
    public void JumpToStart_ScrollsTheConsoleElement()
    {
        _module.SetupVoid("scrollToTop", _ => true).SetVoidResult();
        var page = RenderDetail(AnExecution(), ALogLine());

        Button(page, "Jump to start").Click();

        Assert.Single(_module.Invocations["scrollToTop"]);
    }

    [Fact]
    public void TheJumpControls_AreAbsent_WhenThereIsNothingToScroll()
    {
        var page = RenderDetail(AnExecution());

        Assert.DoesNotContain(page.FindAll("button"), b => b.TextContent.Contains("Jump to"));
    }

    [Fact]
    public void TheModule_IsImportedFromTheStaticWebAssetPath()
    {
        // The path is a package static web asset ("_content/{PackageId}/..."), which nothing else in
        // the build verifies. Strict mode would already have failed every test above if it were
        // wrong; this states the contract outright so the reason is obvious when it does.
        RenderDetail(AnExecution());

        Assert.Contains(JSInterop.Invocations["import"], i => i.Arguments.Contains(ModuleUrl));
    }
}

using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OptiPowerTools.ScheduledJobsInsights.Retention;
using RetentionPage = OptiPowerTools.ScheduledJobsInsights.Components.Pages.Retention;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Components;

public class RetentionPageTests : ComponentTestBase
{
    private readonly IJobRetentionService _retention = Substitute.For<IJobRetentionService>();

    public RetentionPageTests()
    {
        _retention.DefaultPeriod.Returns(RetentionPeriod.OfDays(30));
        Services.AddSingleton(_retention);
    }

    private void GivenJobs(params JobRetention[] jobs) =>
        _retention.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<JobRetention>>(jobs));

    private static JobRetention AJob(
        string jobTypeName = "Contoso.Jobs.NightlyImport",
        string displayName = "Nightly Import",
        bool isRegistered = true,
        bool existsInCode = true,
        RetentionPeriod? attribute = null,
        string? attributeDescription = null,
        bool hasInvalidAttribute = false,
        RetentionPeriod? overridden = null,
        bool hasInvalidOverride = false,
        string? modifiedBy = null,
        DateTimeOffset? modifiedAt = null,
        int executionCount = 0) =>
        new(jobTypeName, displayName, isRegistered, existsInCode, attribute, attributeDescription,
            hasInvalidAttribute, overridden, hasInvalidOverride, modifiedBy, modifiedAt, executionCount);

    private IRenderedComponent<RetentionPage> RenderPage(string? viewerTimeZone = null, string? currentUser = "alice") =>
        Render<RetentionPage>(p => p
            .Add(c => c.ViewerTimeZone, viewerTimeZone)
            .Add(c => c.CurrentUser, currentUser));

    [Fact]
    public void TheInstallationDefault_IsAlwaysShown()
    {
        GivenJobs(AJob());

        Assert.Contains("30 days", RenderPage().Find(".default-note").TextContent);
    }

    [Fact]
    public void AJobWithNoRuleOfItsOwn_ShowsTheDefaultAsInForce()
    {
        GivenJobs(AJob());

        var row = RenderPage().Find("tbody tr");

        Assert.Contains("30 days", row.QuerySelector(".effective")!.TextContent);
        Assert.Contains("default", row.QuerySelector(".tag-source")!.TextContent);
    }

    [Fact]
    public void AnAttribute_IsShownWithItsDescriptionAsAClue()
    {
        // The point of the description: whoever is about to override it can see what the job's author
        // intended and why.
        GivenJobs(AJob(
            attribute: RetentionPeriod.OfDays(7),
            attributeDescription: "Logs one line per row; a week is plenty."));

        var row = RenderPage().Find("tbody tr");

        Assert.Contains("7 days", row.QuerySelector(".declared")!.TextContent);
        Assert.Contains("Logs one line per row", row.TextContent);
        Assert.Contains("from job", row.QuerySelector(".tag-source")!.TextContent);
    }

    [Fact]
    public void AnOverride_WinsAndIsLabelledAsSuch()
    {
        GivenJobs(AJob(attribute: RetentionPeriod.OfDays(7), overridden: RetentionPeriod.OfDays(90)));

        var row = RenderPage().Find("tbody tr");

        Assert.Contains("90 days", row.QuerySelector(".effective")!.TextContent);
        Assert.Contains("override", row.QuerySelector(".tag-source")!.TextContent);
        // The attribute stays visible, so it is clear what is being overridden.
        Assert.Contains("7 days", row.QuerySelector(".declared")!.TextContent);
    }

    [Fact]
    public void IndefiniteRetention_ReadsAsKeepForever()
    {
        GivenJobs(AJob(overridden: RetentionPeriod.Indefinite));

        Assert.Contains("Keep forever", RenderPage().Find(".effective").TextContent);
    }

    [Fact]
    public void AnUnusableAttribute_IsFlagged()
    {
        GivenJobs(AJob(hasInvalidAttribute: true));

        Assert.NotNull(RenderPage().Find("tbody tr").QuerySelector(".tag-invalid"));
    }

    [Fact]
    public void AJobWhoseCodeIsGone_IsMarkedAsHistoryOnly()
    {
        GivenJobs(AJob(isRegistered: false, existsInCode: false, executionCount: 12));

        Assert.Contains("history only", RenderPage().Find(".tag-orphan").TextContent);
    }

    [Fact]
    public void AStaleSettingWithNoHistoryLeft_IsNotCalledHistoryOnly()
    {
        // Neither code nor executions: "only its history remains" describes history that does not
        // exist. The row is still worth showing — the setting is there and can be cleared — but not
        // under a label that misstates why.
        GivenJobs(AJob(isRegistered: false, existsInCode: false, executionCount: 0, overridden: RetentionPeriod.OfDays(90)));

        var tag = RenderPage().Find(".tag-orphan");

        Assert.Contains("setting only", tag.TextContent);
        Assert.DoesNotContain("history only", tag.TextContent);
    }

    [Fact]
    public void ASingleDay_ReadsAsOneDay()
    {
        GivenJobs(AJob(overridden: RetentionPeriod.OfDays(1)));

        Assert.Contains("1 day", RenderPage().Find(".effective").TextContent);
        Assert.DoesNotContain("1 days", RenderPage().Find(".effective").TextContent);
    }

    [Fact]
    public void SavingOneRow_LeavesTheOtherRowsUsable()
    {
        // The whole table used to go inert while one save was in flight, which on a slow database
        // reads as the screen having frozen.
        GivenJobs(AJob(jobTypeName: "Contoso.Jobs.First"), AJob(jobTypeName: "Contoso.Jobs.Second"));
        var page = RenderPage();

        Assert.All(page.FindAll("tbody select"), select => Assert.False(select.HasAttribute("disabled")));
    }

    [Fact]
    public void AnUnusableStoredOverride_IsFlaggedInTheRow()
    {
        // Otherwise the row silently shows the default in force while a broken row sits behind it.
        GivenJobs(AJob(hasInvalidOverride: true));

        Assert.Contains("stored value ignored", RenderPage().Find(".tag-invalid").TextContent);
    }

    [Fact]
    public void AValueThisRowNeverOffered_IsRefusedRatherThanSaved()
    {
        // The chosen value arrives over the circuit rather than from the rendered <option> list, so
        // a crafted message can name anything. Zero would delete the job's entire history.
        GivenJobs(AJob());
        var page = RenderPage();

        page.Find("tbody select").Change("0");

        _retention.DidNotReceive().SetOverrideAsync(
            Arg.Any<string>(), Arg.Any<RetentionPeriod?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Contains("not one of the available options", page.Find("[role=alert]").TextContent);
    }

    [Fact]
    public void TheDropdownStillAcceptsAnOverrideSetOutsideThePresets()
    {
        // The guard above must not reject the non-preset value this row deliberately offers.
        GivenJobs(AJob(overridden: RetentionPeriod.OfDays(42)));

        RenderPage().Find("tbody select").Change("42");

        _retention.Received(1).SetOverrideAsync(
            Arg.Any<string>(), RetentionPeriod.OfDays(42), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null, "inherit")]
    [InlineData(90, "90")]
    public void TheDropdown_ShowsWhateverIsStored(int? overriddenDays, string expected)
    {
        // Nothing covered the read side of the select. It uses a bare value= attribute rather than
        // @bind, which is exactly the construct that silently fails to apply — a page whose every
        // dropdown was stuck on "Inherit" passed the whole of this file.
        GivenJobs(AJob(overridden: overriddenDays is { } days ? RetentionPeriod.OfDays(days) : null));

        Assert.Equal(expected, RenderPage().Find("tbody select").GetAttribute("value"));
    }

    [Fact]
    public void TheDropdown_ShowsKeepForeverForAnIndefiniteOverride()
    {
        GivenJobs(AJob(overridden: RetentionPeriod.Indefinite));

        Assert.Equal("indefinite", RenderPage().Find("tbody select").GetAttribute("value"));
    }

    [Fact]
    public void TheDropdown_ShowsANonPresetOverrideAsTheSelectedValue()
    {
        // Someone may have set 42 days directly in the database; the row offers it *and* selects it.
        GivenJobs(AJob(overridden: RetentionPeriod.OfDays(42)));

        var select = RenderPage().Find("tbody select");

        Assert.Equal("42", select.GetAttribute("value"));
        Assert.Contains("42", select.InnerHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void ChoosingAPeriod_SavesItAgainstTheCurrentUser()
    {
        GivenJobs(AJob());
        var page = RenderPage(currentUser: "alice");

        page.Find("tbody select").Change("90");

        _retention.Received(1).SetOverrideAsync(
            "Contoso.Jobs.NightlyImport",
            RetentionPeriod.OfDays(90),
            "alice",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ChoosingKeepForever_SavesIndefinite()
    {
        GivenJobs(AJob());

        RenderPage().Find("tbody select").Change("indefinite");

        _retention.Received(1).SetOverrideAsync(
            Arg.Any<string>(), RetentionPeriod.Indefinite, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ChoosingInherit_ClearsTheOverride()
    {
        // Null, not a value: clearing is what lets the attribute or default apply again.
        GivenJobs(AJob(overridden: RetentionPeriod.OfDays(90)));

        RenderPage().Find("tbody select").Change("inherit");

        _retention.Received(1).SetOverrideAsync(
            Arg.Any<string>(), null, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AnOverrideSetOutsideThePresets_IsStillOfferedRatherThanSilentlyReset()
    {
        // Someone could set 42 days directly in the database; the dropdown must not quietly drop it.
        GivenJobs(AJob(overridden: RetentionPeriod.OfDays(42)));

        var values = RenderPage().FindAll("tbody select option").Select(o => o.GetAttribute("value"));

        Assert.Contains("42", values);
    }

    [Fact]
    public void AFailedSave_IsReportedRatherThanSwallowed()
    {
        // A retention change that silently failed would leave the administrator believing history was
        // being kept, or removed, when it is not.
        GivenJobs(AJob());
        _retention.SetOverrideAsync(Arg.Any<string>(), Arg.Any<RetentionPeriod?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("database unavailable")));
        var page = RenderPage();

        page.Find("tbody select").Change("90");

        Assert.Contains("database unavailable", page.Find("[role=alert]").TextContent);
    }

    [Fact]
    public void TheAuditTrail_ShowsWhoChangedItAndWhen()
    {
        GivenJobs(AJob(
            overridden: RetentionPeriod.OfDays(90),
            modifiedBy: "alice",
            modifiedAt: new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero)));

        var audit = RenderPage(viewerTimeZone: "Europe/Warsaw").Find(".audit").TextContent;

        Assert.Contains("2026-08-20 14:00", audit);
        Assert.Contains("alice", audit);
    }
    [Fact]
    public void ANonDefaultCmsShellPath_FlowsIntoTheBackLink()
    {
        Options.CmsShellPath = "/custom/insights";
        GivenJobs(AJob());

        Assert.Equal("/custom/insights", RenderPage().Find("a.action-link").GetAttribute("href"));
    }


    [Fact]
    public void AFailedRead_IsReportedRatherThanFaultingTheCircuit()
    {
        // This page was the only one of the three catching just OperationCanceledException — and it
        // issues the most expensive query in the UI, so it is the likeliest to fail, not the least.
        _retention.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<JobRetention>>>(_ => throw new InvalidOperationException("retention unreadable"));

        var page = Render<RetentionPage>();

        Assert.Contains("retention unreadable", page.Markup, StringComparison.Ordinal);
    }
}

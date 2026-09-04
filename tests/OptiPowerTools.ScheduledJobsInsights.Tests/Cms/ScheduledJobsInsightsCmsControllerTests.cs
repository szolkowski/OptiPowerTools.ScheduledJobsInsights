using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OptiPowerTools.ScheduledJobsInsights.Cms;
using OptiPowerTools.ScheduledJobsInsights.Components.Shared;
using OptiPowerTools.ScheduledJobsInsights.Configuration;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Cms;

public class ScheduledJobsInsightsCmsControllerTests
{

    /// <summary>The typed model the view is rendered with.</summary>
    /// <remarks>
    /// Was <c>ViewData</c>, until these values moved onto a record: a renamed key used to hand the
    /// components null with nothing failing to say so, which is the whole reason for the change.
    /// </remarks>
    private static ScheduledJobsInsightsPageModel ModelOf(IActionResult result) =>
        Assert.IsType<ScheduledJobsInsightsPageModel>(Assert.IsType<ViewResult>(result).Model);

    /// <summary>The view name the result renders.</summary>
    private static string? ViewNameOf(IActionResult result) => Assert.IsType<ViewResult>(result).ViewName;
    private static ScheduledJobsInsightsCmsController CreateController(
        OptiPowerToolsScheduledJobsInsightsOptions options, ClaimsPrincipal user, string? timeZoneCookie = null)
    {
        var httpContext = new DefaultHttpContext { User = user };
        if (timeZoneCookie is not null)
            httpContext.Request.Headers.Cookie = $"{ViewerClock.CookieName}={timeZoneCookie}";

        return new ScheduledJobsInsightsCmsController(Options.Create(options))
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    [Theory]
    [InlineData(null)]
    [InlineData(42L)]
    public void Index_PassesExecutionIdToView(long? id)
    {
        // The id arrives from the "id" query string rather than a route segment, so that the request
        // path keeps matching the registered CMS menu item and the shell navigation still resolves.
        var options = new OptiPowerToolsScheduledJobsInsightsOptions();
        var controller = CreateController(options, new ClaimsPrincipal(new ClaimsIdentity()));

        var result = controller.Index(id);

        Assert.Equal(id, ModelOf(result).ExecutionId);
    }

    [Fact]
    public void TheController_IsGuardedByThePackagePolicy()
    {
        // Authorization is endpoint metadata rather than a check inside the action, so that the
        // framework enforces it and the menu can ask the identical question. Asserting on the
        // attribute is the only way to see that from a unit test.
        var attribute = Assert.Single(
            typeof(ScheduledJobsInsightsCmsController)
                .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false)
                .Cast<AuthorizeAttribute>());

        Assert.Equal(ScheduledJobsInsightsAuthorization.PolicyName, attribute.Policy);
    }

    [Fact]
    public void Index_ReturnsViewWithOptionValues()
    {
        var options = new OptiPowerToolsScheduledJobsInsightsOptions
        {
            AuthorizedRoles = ["Administrators"],
            PageTitle = "Title"
        };
        var identity = new ClaimsIdentity(authenticationType: "Test");
        identity.AddClaim(new Claim(ClaimTypes.Role, "Administrators"));
        var controller = CreateController(options, new ClaimsPrincipal(identity));

        var result = controller.Index(id: null);

        var model = ModelOf(result);
        Assert.Null(model.ExecutionId);
        Assert.Equal("Title", model.PageTitle);
    }

    [Fact]
    public void Index_PassesTheViewerTimeZoneCookieToView()
    {
        // Read here rather than inside the components: IHttpContextAccessor only has a context during
        // prerendering, so a component that looked it up itself would resolve the zone on the
        // prerender pass and lose it the moment the circuit took over.
        var options = new OptiPowerToolsScheduledJobsInsightsOptions();
        var controller = CreateController(options, new ClaimsPrincipal(new ClaimsIdentity()), "Europe/Warsaw");

        var result = controller.Index(id: null);

        Assert.Equal("Europe/Warsaw", ModelOf(result).ViewerTimeZone);
    }

    [Fact]
    public void Index_WithNoTimeZoneCookie_PassesNull()
    {
        // The first ever page view, before the view's inline script has set the cookie. The
        // components fall back to UTC rather than guessing.
        var options = new OptiPowerToolsScheduledJobsInsightsOptions();
        var controller = CreateController(options, new ClaimsPrincipal(new ClaimsIdentity()));

        var result = controller.Index(id: null);

        Assert.Null(ModelOf(result).ViewerTimeZone);
    }

    [Fact]
    public void Retention_RendersTheRetentionScreen()
    {
        // Its own action on its own route, not a "view" query string on Index. The CMS shell resolves
        // the highlighted menu entry and the product whose navigation to render by comparing the
        // request path with each registered menu item's URL, ignoring the query string — so while
        // retention lived on Index's path, the execution list's entry was the one that matched and the
        // list stayed highlighted with retention open.
        var controller = CreateController(
            new OptiPowerToolsScheduledJobsInsightsOptions(), new ClaimsPrincipal(new ClaimsIdentity()));

        var model = ModelOf(controller.Retention());

        Assert.True(model.ShowRetention);
        Assert.Null(model.ExecutionId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(42L)]
    public void Index_NeverRendersTheRetentionScreen(long? id)
    {
        var controller = CreateController(
            new OptiPowerToolsScheduledJobsInsightsOptions(), new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.False(ModelOf(controller.Index(id)).ShowRetention);
    }

    [Fact]
    public void BothActions_RenderTheSharedShellView()
    {
        // One view for both: it is the CMS shell document that hosts whichever component the model
        // asks for, so neither action owns it and neither is named in it.
        var controller = CreateController(
            new OptiPowerToolsScheduledJobsInsightsOptions(), new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.Equal(ScheduledJobsInsightsCmsController.ShellView, ViewNameOf(controller.Index(id: null)));
        Assert.Equal(ScheduledJobsInsightsCmsController.ShellView, ViewNameOf(controller.Retention()));
    }

    [Fact]
    public void Retention_PassesTheCurrentUserAndTimeZoneToView()
    {
        // The retention screen audits who changed a rule, and a component has no HttpContext once the
        // circuit takes over — so both values travel as parameters from here.
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "admin")], authenticationType: "Test");
        var controller = CreateController(
            new OptiPowerToolsScheduledJobsInsightsOptions(), new ClaimsPrincipal(identity), "Europe/Warsaw");

        var model = ModelOf(controller.Retention());

        Assert.Equal("admin", model.CurrentUser);
        Assert.Equal("Europe/Warsaw", model.ViewerTimeZone);
    }
}

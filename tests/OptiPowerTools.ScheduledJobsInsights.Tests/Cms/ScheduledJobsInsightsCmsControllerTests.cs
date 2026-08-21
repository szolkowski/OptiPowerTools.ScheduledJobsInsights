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
    private static ScheduledJobsInsightsCmsController CreateController(
        OptiPowerToolScheduledJobsInsightsOptions options, ClaimsPrincipal user, string? timeZoneCookie = null)
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
        var options = new OptiPowerToolScheduledJobsInsightsOptions();
        var controller = CreateController(options, new ClaimsPrincipal(new ClaimsIdentity()));

        var result = controller.Index(id);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal(id, viewResult.ViewData["ExecutionId"]);
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
        var options = new OptiPowerToolScheduledJobsInsightsOptions
        {
            AuthorizedRoles = ["Administrators"],
            PageTitle = "Title"
        };
        var identity = new ClaimsIdentity(authenticationType: "Test");
        identity.AddClaim(new Claim(ClaimTypes.Role, "Administrators"));
        var controller = CreateController(options, new ClaimsPrincipal(identity));

        var result = controller.Index(id: null);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Null(viewResult.ViewData["ExecutionId"]);
        Assert.Equal("Title", viewResult.ViewData["PageTitle"]);
    }

    [Fact]
    public void Index_PassesTheViewerTimeZoneCookieToView()
    {
        // Read here rather than inside the components: IHttpContextAccessor only has a context during
        // prerendering, so a component that looked it up itself would resolve the zone on the
        // prerender pass and lose it the moment the circuit took over.
        var options = new OptiPowerToolScheduledJobsInsightsOptions();
        var controller = CreateController(options, new ClaimsPrincipal(new ClaimsIdentity()), "Europe/Warsaw");

        var result = controller.Index(id: null);

        Assert.Equal("Europe/Warsaw", Assert.IsType<ViewResult>(result).ViewData["ViewerTimeZone"]);
    }

    [Fact]
    public void Index_WithNoTimeZoneCookie_PassesNull()
    {
        // The first ever page view, before the view's inline script has set the cookie. The
        // components fall back to UTC rather than guessing.
        var options = new OptiPowerToolScheduledJobsInsightsOptions();
        var controller = CreateController(options, new ClaimsPrincipal(new ClaimsIdentity()));

        var result = controller.Index(id: null);

        Assert.Null(Assert.IsType<ViewResult>(result).ViewData["ViewerTimeZone"]);
    }
}

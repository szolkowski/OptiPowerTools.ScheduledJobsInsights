using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OptiPowerTools.ScheduledJobsInsights.Cms;
using OptiPowerTools.ScheduledJobsInsights.Configuration;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Cms;

public class ScheduledJobsInsightsCmsControllerTests
{
    private static ScheduledJobsInsightsCmsController CreateController(
        OptiPowerToolScheduledJobsInsightsOptions options, ClaimsPrincipal user)
    {
        return new ScheduledJobsInsightsCmsController(Options.Create(options))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            }
        };
    }

    [Theory]
    [InlineData(null)]
    [InlineData(42L)]
    public void Index_PassesExecutionIdToView(long? id)
    {
        // The id arrives from the "id" query string rather than a route segment, so that the request
        // path keeps matching the registered CMS menu item and the shell navigation still resolves.
        var options = new OptiPowerToolScheduledJobsInsightsOptions { EnableStandardAuthorization = false };
        var controller = CreateController(options, new ClaimsPrincipal(new ClaimsIdentity()));

        var result = controller.Index(id);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal(id, viewResult.ViewData["ExecutionId"]);
    }

    [Fact]
    public void Index_UnauthenticatedUser_StandardAuthEnabled_ReturnsForbid()
    {
        var options = new OptiPowerToolScheduledJobsInsightsOptions { EnableStandardAuthorization = true };
        var controller = CreateController(options, new ClaimsPrincipal(new ClaimsIdentity()));

        var result = controller.Index(id: null);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public void Index_AuthenticatedWithoutAuthorizedRole_StandardAuthEnabled_ReturnsForbid()
    {
        var options = new OptiPowerToolScheduledJobsInsightsOptions
        {
            EnableStandardAuthorization = true,
            AuthorizedRoles = ["Administrators"]
        };
        var identity = new ClaimsIdentity(authenticationType: "Test");
        identity.AddClaim(new Claim(ClaimTypes.Role, "Editors"));
        var controller = CreateController(options, new ClaimsPrincipal(identity));

        var result = controller.Index(id: null);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public void Index_AuthenticatedWithAuthorizedRole_ReturnsViewWithOptionValues()
    {
        var options = new OptiPowerToolScheduledJobsInsightsOptions
        {
            EnableStandardAuthorization = true,
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
        Assert.Equal("Title", viewResult.ViewData["PageTitle"]);
    }

    [Fact]
    public void Index_StandardAuthorizationDisabled_UnauthenticatedUser_ReturnsView()
    {
        var options = new OptiPowerToolScheduledJobsInsightsOptions { EnableStandardAuthorization = false };
        var controller = CreateController(options, new ClaimsPrincipal(new ClaimsIdentity()));

        var result = controller.Index(id: null);

        Assert.IsType<ViewResult>(result);
    }
}

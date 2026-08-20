using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OptiPowerTools.ScheduledJobsInsights.Components.Shared;
using OptiPowerTools.ScheduledJobsInsights.Configuration;

namespace OptiPowerTools.ScheduledJobsInsights.Cms;

/// <summary>
/// MVC controller that renders the ScheduledJobsInsights UI inside the Optimizely CMS shell chrome.
/// The view hosts the Blazor components directly through the Component Tag Helper, so they render
/// within the CMS navigation and inherit the shell's styling.
/// </summary>
[Authorize]
public class ScheduledJobsInsightsCmsController : Controller
{
    private readonly OptiPowerToolScheduledJobsInsightsOptions _options;

    /// <summary>
    /// Initializes a new instance of <see cref="ScheduledJobsInsightsCmsController"/>.
    /// </summary>
    public ScheduledJobsInsightsCmsController(IOptions<OptiPowerToolScheduledJobsInsightsOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// Renders the execution list, or a single execution's detail when <paramref name="id"/> is supplied.
    /// </summary>
    /// <param name="id">Execution id from the <c>id</c> query string, or <c>null</c> for the list.</param>
    [HttpGet]
    public IActionResult Index(long? id)
    {
        if (_options.EnableStandardAuthorization
            && (User.Identity?.IsAuthenticated != true
                || _options.AuthorizedRoles is not { } roles
                || !roles.Any(role => User.IsInRole(role))))
            return Forbid();

        ViewBag.ExecutionId = id;
        ViewBag.PageTitle = _options.PageTitle;

        // The reader's time zone, stashed in a cookie by the view's own inline script. Read here and
        // handed to the components as a parameter rather than looked up inside them: IHttpContextAccessor
        // is only meaningful during prerendering, and a component that consulted it would see the right
        // zone on the prerender pass and null once the circuit takes over, flipping the page back to UTC.
        ViewBag.ViewerTimeZone = Request.Cookies[ViewerClock.CookieName];

        return View();
    }
}

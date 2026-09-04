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
/// <remarks>
/// Two actions, on two routes set by <see cref="ScheduledJobsInsightsCmsRouteConvention"/> from
/// <see cref="OptiPowerToolsScheduledJobsInsightsOptions.CmsShellPath"/> and
/// <see cref="OptiPowerToolsScheduledJobsInsightsOptions.CmsRetentionPath"/>. They share one view,
/// which is the shell document rather than a page — see <see cref="ShellView"/>.
/// </remarks>
[Authorize(Policy = ScheduledJobsInsightsAuthorization.PolicyName)]
public sealed class ScheduledJobsInsightsCmsController : Controller
{
    private readonly OptiPowerToolsScheduledJobsInsightsOptions _options;

    /// <summary>
    /// Initializes a new instance of <see cref="ScheduledJobsInsightsCmsController"/>.
    /// </summary>
    public ScheduledJobsInsightsCmsController(IOptions<OptiPowerToolsScheduledJobsInsightsOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// The view both actions render.
    /// </summary>
    /// <remarks>
    /// Named for what it is — the CMS shell document that hosts whichever component the model asks
    /// for — rather than after either action, since neither owns it.
    /// </remarks>
    internal const string ShellView = "Shell";

    /// <summary>
    /// Renders the execution list, or a single execution's detail when <paramref name="id"/> is
    /// supplied.
    /// </summary>
    /// <param name="id">Execution id from the <c>id</c> query string, or <c>null</c> for the list.</param>
    [HttpGet]
    public IActionResult Index(long? id) => Shell(executionId: id, showRetention: false);

    /// <summary>
    /// Renders the per-job retention screen.
    /// </summary>
    /// <remarks>
    /// A route of its own rather than a <c>view</c> query string on <see cref="Index"/>, because the
    /// CMS shell resolves both the highlighted menu entry and the product whose navigation to render
    /// by comparing the request <em>path</em> with each registered menu item's URL — the query string
    /// is ignored on both sides, server side in <c>MenuItem.IsSelected</c> and client side against
    /// <c>location.pathname</c>. With the retention screen on the list's path, the list's entry
    /// matched and the retention entry could not, so opening retention highlighted the list.
    /// </remarks>
    [HttpGet]
    public IActionResult Retention() => Shell(executionId: null, showRetention: true);

    /// <summary>
    /// Renders the shell view with the model both actions build the same way.
    /// </summary>
    private IActionResult Shell(long? executionId, bool showRetention)
    {
        // Authorization is the policy on the class, enforced by the framework before this runs —
        // not a check written out here, which the menu could then disagree with.
        //
        // The time zone comes from a cookie set by the page's own script, read here and handed to the
        // components as a parameter rather than looked up inside them: IHttpContextAccessor is only
        // meaningful during prerendering, so a component that consulted it would see the right zone on
        // the prerender pass and null once the circuit takes over, flipping the page back to UTC. The
        // current user travels the same way, because retention changes are audited and a component has
        // no HttpContext once the circuit owns the page.
        return View(ShellView, new ScheduledJobsInsightsPageModel(
            ExecutionId: executionId,
            ViewerTimeZone: Request.Cookies[ViewerClock.CookieName],
            ShowRetention: showRetention,
            CurrentUser: User.Identity?.Name,
            PageTitle: _options.PageTitle));
    }
}

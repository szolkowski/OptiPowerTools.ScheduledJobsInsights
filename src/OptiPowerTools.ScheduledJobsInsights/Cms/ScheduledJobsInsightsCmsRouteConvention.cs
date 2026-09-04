using Microsoft.AspNetCore.Mvc.ApplicationModels;
using OptiPowerTools.ScheduledJobsInsights.Configuration;

namespace OptiPowerTools.ScheduledJobsInsights.Cms;

/// <summary>
/// Application model convention that sets the CMS shell controller's routes from
/// <see cref="OptiPowerToolsScheduledJobsInsightsOptions.CmsShellPath"/> and
/// <see cref="OptiPowerToolsScheduledJobsInsightsOptions.CmsRetentionPath"/> at startup.
/// </summary>
internal sealed class ScheduledJobsInsightsCmsRouteConvention : IApplicationModelConvention
{
    private readonly string _path;
    private readonly string _retentionPath;

    /// <summary>The configured route template applied to the CMS shell controller's Index action.</summary>
    internal string Path => _path;

    /// <summary>The configured route template applied to the controller's Retention action.</summary>
    internal string RetentionPath => _retentionPath;

    public ScheduledJobsInsightsCmsRouteConvention(string path, string retentionPath)
    {
        _path = path;
        _retentionPath = retentionPath;
    }

    public void Apply(ApplicationModel application)
    {
        var controller = application.Controllers
            .FirstOrDefault(c => c.ControllerType == typeof(ScheduledJobsInsightsCmsController));

        if (controller is null)
            return;

        SetRoute(controller, nameof(ScheduledJobsInsightsCmsController.Index), _path);

        // Its own path, not a query string on the one above: the CMS shell highlights the menu entry
        // whose URL equals the request path and ignores the query string, so a retention screen sharing
        // the list's path could only ever highlight the list. A sibling path rather than a segment
        // beneath it — see CmsRetentionPath.
        SetRoute(controller, nameof(ScheduledJobsInsightsCmsController.Retention), _retentionPath);
    }

    private static void SetRoute(ControllerModel controller, string actionName, string template)
    {
        var action = controller.Actions.FirstOrDefault(a => a.ActionName == actionName);

        if (action is null)
            return;

        // Rebuilt from the existing selector rather than replaced outright. Clearing and adding a
        // bare SelectorModel threw away the HttpMethodActionConstraint that [HttpGet] produces, so the
        // endpoint answered every verb — POST, PUT and DELETE all rendered the page.
        var existing = action.Selectors.FirstOrDefault();

        var replacement = new SelectorModel
        {
            AttributeRouteModel = new AttributeRouteModel
            {
                // Exactly the configured path, with no extra segments. A single execution is
                // addressed by an "id" query string instead: the CMS shell resolves which product's
                // navigation to show by matching the request path against the registered menu items,
                // so any extra path segment leaves it unable to find one and the left-hand menu spins
                // on its loading dots forever.
                Template = template
            }
        };

        if (existing is not null)
        {
            foreach (var constraint in existing.ActionConstraints)
                replacement.ActionConstraints.Add(constraint);

            foreach (var metadata in existing.EndpointMetadata)
                replacement.EndpointMetadata.Add(metadata);
        }

        action.Selectors.Clear();
        action.Selectors.Add(replacement);
    }
}

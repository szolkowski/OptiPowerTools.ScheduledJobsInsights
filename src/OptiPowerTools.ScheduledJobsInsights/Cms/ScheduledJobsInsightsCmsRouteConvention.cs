using Microsoft.AspNetCore.Mvc.ApplicationModels;
using OptiPowerTools.ScheduledJobsInsights.Configuration;

namespace OptiPowerTools.ScheduledJobsInsights.Cms;

/// <summary>
/// Application model convention that sets the CMS shell controller route
/// from <see cref="OptiPowerToolScheduledJobsInsightsOptions.CmsShellPath"/> at startup.
/// </summary>
internal sealed class ScheduledJobsInsightsCmsRouteConvention : IApplicationModelConvention
{
    private readonly string _path;

    /// <summary>The configured route template applied to the CMS shell controller's Index action.</summary>
    internal string Path => _path;

    public ScheduledJobsInsightsCmsRouteConvention(string path) => _path = path;

    public void Apply(ApplicationModel application)
    {
        var controller = application.Controllers
            .FirstOrDefault(c => c.ControllerType == typeof(ScheduledJobsInsightsCmsController));

        if (controller is null)
            return;

        var action = controller.Actions.FirstOrDefault(a => a.ActionName == nameof(ScheduledJobsInsightsCmsController.Index));

        if (action is null)
            return;

        action.Selectors.Clear();
        action.Selectors.Add(new SelectorModel
        {
            AttributeRouteModel = new AttributeRouteModel
            {
                // Exactly the configured path, with no extra segments. A single execution is
                // addressed by an "id" query string instead: the CMS shell resolves which product's
                // navigation to show by matching the request path against the registered menu items,
                // so any extra path segment leaves it unable to find one and the left-hand menu spins
                // on its loading dots forever.
                Template = _path
            }
        });
    }
}

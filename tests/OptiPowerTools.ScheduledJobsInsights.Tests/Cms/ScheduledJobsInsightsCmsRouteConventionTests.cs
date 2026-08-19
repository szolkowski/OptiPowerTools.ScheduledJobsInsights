using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using OptiPowerTools.ScheduledJobsInsights.Cms;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Cms;

public class ScheduledJobsInsightsCmsRouteConventionTests
{
    private static (ApplicationModel Application, ActionModel Action) CreateApplicationModel()
    {
        var controllerModel = new ControllerModel(
            typeof(ScheduledJobsInsightsCmsController).GetTypeInfo(), Array.Empty<object>());
        var actionMethod = typeof(ScheduledJobsInsightsCmsController)
            .GetMethod(nameof(ScheduledJobsInsightsCmsController.Index))!;
        var actionModel = new ActionModel(actionMethod, Array.Empty<object>())
        {
            ActionName = nameof(ScheduledJobsInsightsCmsController.Index),
            Controller = controllerModel
        };
        controllerModel.Actions.Add(actionModel);

        var application = new ApplicationModel();
        application.Controllers.Add(controllerModel);

        return (application, actionModel);
    }

    [Fact]
    public void Apply_SetsIndexActionRoute_ToConfiguredPathExactly()
    {
        var (application, actionModel) = CreateApplicationModel();
        var convention = new ScheduledJobsInsightsCmsRouteConvention("/custom/shell/path");

        convention.Apply(application);

        // No extra route segments: the CMS shell picks the navigation to render by matching the
        // request path against registered menu items, so a single execution is addressed with an
        // "id" query string rather than a path segment. Adding one leaves the left-hand menu stuck
        // on its loading dots.
        var selector = Assert.Single(actionModel.Selectors);
        Assert.Equal("/custom/shell/path", selector.AttributeRouteModel!.Template);
    }

    [Fact]
    public void Apply_NoMatchingController_DoesNotThrow()
    {
        var application = new ApplicationModel();
        var convention = new ScheduledJobsInsightsCmsRouteConvention("/custom/shell/path");

        var exception = Record.Exception(() => convention.Apply(application));

        Assert.Null(exception);
    }
}

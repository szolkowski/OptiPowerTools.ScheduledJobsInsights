using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Routing;
using OptiPowerTools.ScheduledJobsInsights.Cms;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Cms;

public class ScheduledJobsInsightsCmsRouteConventionTests
{
    /// <summary>
    /// The application model MVC would have built for the controller, with both of its actions.
    /// </summary>
    private static (ApplicationModel Application, ActionModel Index, ActionModel Retention) CreateApplicationModel()
    {
        var controllerModel = new ControllerModel(
            typeof(ScheduledJobsInsightsCmsController).GetTypeInfo(), Array.Empty<object>());

        var index = AddAction(controllerModel, nameof(ScheduledJobsInsightsCmsController.Index));
        var retention = AddAction(controllerModel, nameof(ScheduledJobsInsightsCmsController.Retention));

        var application = new ApplicationModel();
        application.Controllers.Add(controllerModel);

        return (application, index, retention);
    }

    private static ActionModel AddAction(ControllerModel controllerModel, string actionName)
    {
        var actionMethod = typeof(ScheduledJobsInsightsCmsController).GetMethod(actionName)!;
        var actionModel = new ActionModel(actionMethod, actionMethod.GetCustomAttributes(inherit: true))
        {
            ActionName = actionName,
            Controller = controllerModel
        };

        // The selector MVC itself would have built from [HttpGet]. Without it the convention has no
        // constraint to lose, and a test asserting Single(Selectors) passes whether or not the
        // convention throws the verb restriction away.
        var httpMethods = actionMethod
            .GetCustomAttributes<HttpGetAttribute>(inherit: true)
            .SelectMany(attribute => attribute.HttpMethods)
            .ToArray();

        Assert.NotEmpty(httpMethods);

        actionModel.Selectors.Add(new SelectorModel
        {
            ActionConstraints = { new HttpMethodActionConstraint(httpMethods) }
        });

        controllerModel.Actions.Add(actionModel);

        return actionModel;
    }

    [Fact]
    public void Apply_SetsIndexActionRoute_ToConfiguredPathExactly()
    {
        var (application, index, _) = CreateApplicationModel();
        var convention = new ScheduledJobsInsightsCmsRouteConvention("/custom/shell/path", "/custom/retention/path");

        convention.Apply(application);

        // No extra route segments: the CMS shell picks the navigation to render by matching the
        // request path against registered menu items, so a single execution is addressed with an
        // "id" query string rather than a path segment. Adding one leaves the left-hand menu stuck
        // on its loading dots.
        var selector = Assert.Single(index.Selectors);
        Assert.Equal("/custom/shell/path", selector.AttributeRouteModel!.Template);
    }

    [Fact]
    public void Apply_SetsRetentionActionRoute_ToItsOwnConfiguredPath()
    {
        // The retention screen is a route of its own rather than a "view" query string on Index,
        // because the CMS shell resolves both the highlighted menu entry and the product whose
        // navigation to render by comparing the request path with each menu item's URL — the query
        // string is dropped on both sides. Sharing Index's path meant the list was highlighted
        // whenever retention was open.
        var (application, _, retention) = CreateApplicationModel();

        new ScheduledJobsInsightsCmsRouteConvention("/custom/shell/path", "/custom/retention/path").Apply(application);

        var selector = Assert.Single(retention.Selectors);
        Assert.Equal("/custom/retention/path", selector.AttributeRouteModel!.Template);
    }

    [Fact]
    public void Apply_KeepsTheVerbRestriction()
    {
        // The convention rewrites the route by replacing the selector. Replacing it outright also
        // discarded the HttpMethodActionConstraint that [HttpGet] produces, leaving an endpoint that
        // answered every verb — POST, PUT and DELETE all rendered the page.
        var (application, index, retention) = CreateApplicationModel();

        new ScheduledJobsInsightsCmsRouteConvention("/custom/shell/path", "/custom/retention/path").Apply(application);

        foreach (var action in new[] { index, retention })
        {
            var constraint = Assert.IsType<HttpMethodActionConstraint>(
                Assert.Single(Assert.Single(action.Selectors).ActionConstraints));
            Assert.Equal(["GET"], constraint.HttpMethods);
        }
    }

    [Fact]
    public void Apply_NoMatchingController_DoesNotThrow()
    {
        var application = new ApplicationModel();
        var convention = new ScheduledJobsInsightsCmsRouteConvention("/custom/shell/path", "/custom/retention/path");

        var exception = Record.Exception(() => convention.Apply(application));

        Assert.Null(exception);
    }
}

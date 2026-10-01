using OptiPowerTools.ScheduledJobsInsights.Cms12.Web.Extensions;
using EPiServer.Cms.Shell;
using EPiServer.Cms.UI.AspNetIdentity;
using EPiServer.ServiceLocation;
using EPiServer.Web.Routing;
using OptiPowerTools.ScheduledJobsInsights.Extensions;

namespace OptiPowerTools.ScheduledJobsInsights.Cms12.Web;

public class Startup
{
    private readonly IWebHostEnvironment _webHostingEnvironment;

    public Startup(IWebHostEnvironment webHostingEnvironment)
    {
        _webHostingEnvironment = webHostingEnvironment;
    }

    public void ConfigureServices(IServiceCollection services)
    {
        if (_webHostingEnvironment.IsDevelopment())
        {
            AppDomain.CurrentDomain.SetData("DataDirectory", Path.Combine(_webHostingEnvironment.ContentRootPath, "App_Data"));

            // The template disables the scheduler in Development here. Deliberately not done: on CMS 12
            // a manual Start in the Scheduled Jobs admin only queues the job for the scheduler service,
            // so with it disabled Start does nothing at all. The samples set no interval, so they still
            // never run unless started (see Samples/SampleDefaults.cs). CMS 13 runs a manual Start
            // without the scheduler, which is why its dev host can keep it off.
        }

        services
            .AddCmsAspNetIdentity<ApplicationUser>()
            .AddCms()
            .AddAlloy()
            .AddAdminUserRegistration()
            .AddEmbeddedLocalization<Startup>();

        services.AddOptiPowerToolsScheduledJobsInsights();

        // Required by Wangkanai.Detection
        services.AddDetection();

        services.AddSession(options =>
        {
            options.IdleTimeout = TimeSpan.FromSeconds(10);
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
        });
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        // Required by Wangkanai.Detection
        app.UseDetection();
        app.UseSession();

        app.UseStaticFiles();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();

        app.UseEndpoints(endpoints =>
        {
            // Mapped on the host's own route builder, ahead of MapContent(): MapContent() consolidates
            // already-published endpoint data sources into its snapshot, so a hub mapped from a
            // UseEndpoints call of the package's own would be registered twice and every Blazor request
            // would fail with AmbiguousMatchException.
            endpoints.MapOptiPowerToolsScheduledJobsInsights();

            endpoints.MapContent();

            // Plain CMS without Commerce: MapContent() maps no attribute-routed controllers, so this is
            // what makes the Insights pages reachable at all.
            endpoints.MapControllers();
        });

        // Migrations and startup diagnostics. The hub is already mapped, so this does not map it again.
        app.UseOptiPowerToolsScheduledJobsInsights();
    }
}

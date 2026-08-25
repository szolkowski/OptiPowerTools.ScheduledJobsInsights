using EPiServer.Scheduler;
using EPiServer.Web.Routing;
using OptiPowerTools.ScheduledJobsInsights.Extensions;

namespace OptiPowerTools.ScheduledJobsInsights.Web;

public class Startup
{
    private readonly MyOptiAlloySite.Startup _alloySiteStartup;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public Startup(IWebHostEnvironment webHostingEnvironment, IConfiguration configuration)
    {
        _alloySiteStartup = new MyOptiAlloySite.Startup(webHostingEnvironment, configuration);
        _configuration = configuration;
        _environment = webHostingEnvironment;
    }

    public void ConfigureServices(IServiceCollection services)
    {
        // Ensure DataDirectory and scheduler config are set for non-Development environments
        // (MyOptiAlloySite.Startup only sets these in Development)
        if (!_environment.IsDevelopment())
        {
            AppDomain.CurrentDomain.SetData("DataDirectory", Path.Combine(_environment.ContentRootPath, "App_Data"));
            services.Configure<SchedulerOptions>(options => options.Enabled = false);
        }

        _alloySiteStartup.ConfigureServices(services);

        services.AddOptiPowerToolsScheduledJobsInsights();
    }

    /// <remarks>
    /// This mirrors <c>MyOptiAlloySite.Startup.Configure</c> rather than delegating to it. The
    /// ScheduledJobsInsights Blazor endpoints and their anti-forgery middleware must be inserted
    /// between <c>UseAuthorization()</c> and the first <c>UseEndpoints(...)</c>, and delegating
    /// hands over the whole pipeline including that <c>UseEndpoints</c> call — leaving nowhere to
    /// put them. Keep in sync with sub/MyOptiAlloySite/MyOptiAlloySite/Startup.cs.
    /// </remarks>
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
            // Mapped on the host's own route builder, ahead of MapContent(). Letting
            // UseOptiPowerToolsScheduledJobsInsights() map it publishes the hub through a UseEndpoints
            // call of its own, and MapContent() then consolidates that already-published data source
            // into its snapshot - so the hub is registered twice and every Blazor request in the
            // application fails with AmbiguousMatchException, not only this package's pages.
            endpoints.MapOptiPowerToolsScheduledJobsInsights();

            endpoints.MapContent();

            // No MapControllers(), deliberately, and this is stack-specific rather than a general
            // rule. With Commerce installed, MapContent() already maps attribute-routed controllers,
            // so calling MapControllers() as well duplicates every one of them - this package's page,
            // Optimizely's own and Commerce's. On a plain CMS host without Commerce the opposite is
            // true: MapContent() maps none of them, and dropping this line makes the Insights page
            // return 404. Measured both ways on this host.
        });

        // Migrations and startup diagnostics. The hub is already mapped, so this does not map it again.
        app.UseOptiPowerToolsScheduledJobsInsights();
    }
}

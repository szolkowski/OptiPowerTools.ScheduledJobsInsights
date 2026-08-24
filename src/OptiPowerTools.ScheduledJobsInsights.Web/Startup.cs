using EPiServer.Scheduler;
using EPiServer.Web.Routing;
using OptiPowerTools.Hangfire.Extensions;
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

        app.UseOptiPowerToolHangfire();

        // Must precede the UseEndpoints below - see the remarks above.
        app.UseOptiPowerToolsScheduledJobsInsights();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapContent();
            endpoints.MapControllers();
        });
    }
}

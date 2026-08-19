using OptiPowerTools.ScheduledJobsInsights.Web;

var webProjectDir = Directory.GetCurrentDirectory();

Host.CreateDefaultBuilder(args)
    .ConfigureCmsDefaults()
    .ConfigureWebHostDefaults(webBuilder =>
    {
        webBuilder.UseStartup<Startup>();
        webBuilder.UseContentRoot(Path.GetFullPath("../../sub/MyOptiAlloySite/MyOptiAlloySite"));

        // Re-apply after UseContentRoot: pointing the content root at the Alloy submodule rebuilds
        // the web root file provider and drops the static web assets ConfigureWebHostDefaults had
        // already composed onto it. Without this, _framework/blazor.web.js 404s and the Scheduled
        // Jobs Insights page renders statically with no interactivity. Only this dev host needs it
        // - a normal Optimizely app does not move its content root.
        webBuilder.UseStaticWebAssets();

        // Override MyOptiAlloySite's configuration with the web project's appsettings files,
        // then re-add environment variables so Docker env vars take precedence
        webBuilder.ConfigureAppConfiguration((context, config) =>
        {
            var env = context.HostingEnvironment;
            config.AddJsonFile(Path.Combine(webProjectDir, "appsettings.json"), optional: true, reloadOnChange: true);
            config.AddJsonFile(Path.Combine(webProjectDir, $"appsettings.{env.EnvironmentName}.json"), optional: true, reloadOnChange: true);
            config.AddEnvironmentVariables();
        });
    })
    .Build()
    .Run();

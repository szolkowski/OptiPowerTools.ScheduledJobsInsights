# OptiPowerTools.ScheduledJobsInsights.Cms12

[![NuGet](https://img.shields.io/nuget/v/OptiPowerTools.ScheduledJobsInsights.Cms12?logo=nuget&label=NuGet)](https://www.nuget.org/packages/OptiPowerTools.ScheduledJobsInsights.Cms12)
[![Downloads](https://img.shields.io/nuget/dt/OptiPowerTools.ScheduledJobsInsights.Cms12?logo=nuget&label=downloads)](https://www.nuget.org/packages/OptiPowerTools.ScheduledJobsInsights.Cms12)

Execution history for **native Optimizely CMS 12 scheduled jobs**. Swap
`EPiServer.Scheduler.ScheduledJobBase` for `LoggedScheduledJobBase` and every run is recorded: each
`OnStatusChanged` message, the job's return value, unhandled exceptions, severity-tagged log lines, an
optional multi-line result summary, and automatic metrics — persisted to EF Core-backed SQL tables,
browsable in a log viewer embedded in the CMS admin, and aged out by an automatic retention cleanup job.

> **On Optimizely CMS 13?** This package targets CMS 12. Use
> [`OptiPowerTools.ScheduledJobsInsights`](https://www.nuget.org/packages/OptiPowerTools.ScheduledJobsInsights)
> instead.

This is the CMS 12 build of `OptiPowerTools.ScheduledJobsInsights`, compiled from the same source:
the same API, namespaces, options and database. **The [full documentation](README.md) covers both** —
installation, wiring, writing a logged job, configuration, retention and the rest.

## Quick start

```bash
dotnet add package OptiPowerTools.ScheduledJobsInsights.Cms12
```

```xml
<!-- In the application's .csproj: serves _framework/blazor.server.js for the package's Blazor UI. -->
<PropertyGroup>
  <RequiresAspNetWebAssets>true</RequiresAspNetWebAssets>
</PropertyGroup>
```

Then wire it up as described in [Wiring it up](README.md#wiring-it-up), and derive jobs from
`LoggedScheduledJobBase`:

```csharp
using EPiServer.DataAbstraction;
using EPiServer.PlugIn;
using OptiPowerTools.ScheduledJobsInsights.Logging;

[ScheduledPlugIn(DisplayName = "Nightly Catalog Sync", IntervalType = ScheduledIntervalType.Days, IntervalLength = 1)]
public class CatalogSyncJob : LoggedScheduledJobBase
{
    public CatalogSyncJob(JobLoggingContext context)
        : base(context)
    {
    }

    protected override string ExecuteJob()
    {
        Log("Starting sync");
        return "Synced.";
    }
}
```

## What differs on CMS 12

The package behaves the same; the CMS does not, in four ways worth knowing. They are explained in
[Optimizely CMS 12](README.md#optimizely-cms-12):

- the job attribute is `[ScheduledPlugIn]`, not `[ScheduledJob]`;
- `DefaultEnabled = false` hides a job entirely rather than registering it as disabled;
- **Start Manually** only works while the scheduler is enabled;
- unit tests using NSubstitute report NU1608, which is harmless.

## Moving to CMS 13 later

Swap this package for `OptiPowerTools.ScheduledJobsInsights` and keep the database: both ship the same
migrations, so the execution history carries over unchanged.

## Support policy

This package is maintained alongside the CMS 13 one until Optimizely ends support for CMS 12, or until
usage has substantially moved to CMS 13, whichever comes first. The end-of-life date will be announced
in the documentation and the release notes at least three months in advance.

## License

[MIT](LICENSE.txt)

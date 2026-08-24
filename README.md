# OptiPowerTools.ScheduledJobsInsights

[![Quality gate status](https://sonarcloud.io/api/project_badges/measure?project=szolkowski_OptiPowerTools.ScheduledJobsInsights&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=szolkowski_OptiPowerTools.ScheduledJobsInsights)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=szolkowski_OptiPowerTools.ScheduledJobsInsights&metric=coverage)](https://sonarcloud.io/summary/new_code?id=szolkowski_OptiPowerTools.ScheduledJobsInsights)
[![Bugs](https://sonarcloud.io/api/project_badges/measure?project=szolkowski_OptiPowerTools.ScheduledJobsInsights&metric=bugs)](https://sonarcloud.io/summary/new_code?id=szolkowski_OptiPowerTools.ScheduledJobsInsights)
[![Code Smells](https://sonarcloud.io/api/project_badges/measure?project=szolkowski_OptiPowerTools.ScheduledJobsInsights&metric=code_smells)](https://sonarcloud.io/summary/new_code?id=szolkowski_OptiPowerTools.ScheduledJobsInsights)

Execution history for **native Optimizely CMS 13 scheduled jobs**. Swap `EPiServer.Scheduler.ScheduledJobBase` for `LoggedScheduledJobBase` and every run is recorded: each `OnStatusChanged` message, the job's return value, unhandled exceptions, severity-tagged log lines, an optional multi-line result summary, and automatic metrics (duration, allocations, CPU time, GC counts) — persisted to EF Core-backed SQL tables, browsable in a paginated list and console-style log viewer embedded in the CMS admin, and aged out by an automatic retention cleanup job.

Part of the [OptiPowerTools](https://github.com/szolkowski) family — see also [OptiPowerTools.Hangfire](https://github.com/szolkowski/OptiPowerTools.Hangfire) if your background jobs run on Hangfire instead of (or alongside) Optimizely's native scheduler.

## Features

- One-liner DI bootstrap: `services.AddOptiPowerToolsScheduledJobsInsights()`.
- `LoggedScheduledJobBase` — a drop-in replacement for `ScheduledJobBase` that captures execution history automatically.
- Per-run log lines with severity/color (`Default`/`Info`/`Success`/`Warning`/`Error`/`Debug`), plus a dedicated `LogInputData` call for capturing what a run started with.
- Automatic execution metrics — wall-clock duration, bytes allocated on the job's own thread, process CPU time and GC generation counts — plus a `RecordMetric` API for custom domain metrics. The names say whose thread and whose CPU they measure, because on a CMS serving requests the process-wide numbers include everything else the application was doing.
- An optional per-run **result summary** — a multi-line report the job builds as it works, shown in its own collapsible section of the detail view, separate from the one-line message Optimizely shows in its admin grid.
- Paginated, filterable Blazor execution list and a console-style scrolling log viewer for a single run, embedded in the CMS shell like any native admin page.
- Menu entries in the CMS's own navigation — including one under **Settings › Data & Sync Management**, beside the native **Scheduled Jobs** page — and links from the UI across to a job's CMS settings.
- Automatic retention cleanup — itself a native `[ScheduledJob]`, visible and manageable in the CMS's own Scheduled Jobs admin list. It also resolves runs abandoned by a recycled process, which would otherwise sit at *Running* for ever.
- Per-job retention, including indefinite: declare it on the job with `[JobRetention]`, or set it per job in a CMS screen that shows what each job declared and why.
- Unhandled exceptions are never swallowed — native CMS admin's `HasLastExecutionFailed`/`LastExecutionMessage` tracking is completely unaffected.
- Cannot take your site down: if the insights database is unreachable, the application still starts, jobs still run, and `OnStatusChanged` still drives the CMS status column — only the history is lost, and it says so in the log.
- Authorization by Optimizely role out of the box, or by an authorization policy of your own — applied as ordinary endpoint metadata, so the page, the retention screen and the menu entries can never disagree about who may see them.
- Configurable via `IOptions<T>` or `appsettings.json`, validated at startup so a misconfiguration fails there rather than silently once jobs are running.

## Screenshots

**Execution list** — filterable and keyset-paginated, with status badges, a **summary** marker on runs that recorded one, and a link across to the CMS's own Scheduled Jobs page:

![Execution list](images/ExecutionList.jpg)

**Execution detail** — result summary, metrics, input data and stack trace in collapsible sections, above a console-style log viewer with severity colouring, a line count and jump controls. Shown here for a failed run, whose summary survives the exception:

![Execution detail — console view](images/ExecutionDetail.jpg)

**Result summary** — a multi-line report the job builds as it works, newlines and alignment intact, with a copy button:

![Result summary section](images/ResultSummary.jpg)

**Job Retention overview** — the list covers every job deriving from LoggedScheduledJobBase — so a job can be configured before its first run — plus every job type that only exists in history, so records left behind by deleted code can still be trimmed. Those rows are marked history only.

![Job Retention overview](images/JobRetentionView.png)

## Quick Start

### Install

```bash
dotnet add package OptiPowerTools.ScheduledJobsInsights
```

### Package sources

The package itself is on nuget.org, but its Optimizely dependencies are not — they come from
Optimizely's own feed, which any CMS project already has configured:

```xml
<add key="Optimizely" value="https://api.nuget.optimizely.com/v3/index.json" />
```

### Required project setting

Add this to the application's `.csproj` before anything else:

```xml
<PropertyGroup>
  <RequiresAspNetWebAssets>true</RequiresAspNetWebAssets>
</PropertyGroup>
```

The UI is a Blazor Server component, so the application has to serve `_framework/blazor.server.js`.
That file comes from the `Microsoft.AspNetCore.App.Internal.Assets` pack, which the Web SDK
references only when the *application project itself* contains `.razor` files — this package's
components live in the package, so the SDK does not notice. Without the setting the page renders but
never becomes interactive, and the browser console shows a 404 for `blazor.server.js`.

Applications that already contain their own `.razor` files get it automatically and can skip this.
If it is missing, the package says so at startup with a warning naming this setting — the failure is
otherwise silent, and looks like a page that renders but does nothing.

### Wiring it up

```csharp
// Program.cs or Startup.cs
services.AddOptiPowerToolsScheduledJobsInsights(options =>
{
    options.ConnectionString = Configuration.GetConnectionString("EPiServerDB");
});

// ... then in the middleware pipeline, after UseAuthorization()
app.UseEndpoints(endpoints =>
{
    // Map the hub on your own route builder, ahead of MapContent().
    endpoints.MapOptiPowerToolsScheduledJobsInsights();

    endpoints.MapContent();
    endpoints.MapControllers();
});

// Migrations and startup diagnostics. The hub is already mapped, so this does not map it again.
app.UseOptiPowerToolsScheduledJobsInsights();
```

Connection string can point to the same database as Optimizely or to a separate one — there is no fallback, it must be set explicitly.

> **Why the hub is mapped inside your own `UseEndpoints(...)` block.**
> `UseOptiPowerToolsScheduledJobsInsights()` can map the hub itself, and on a simple host that is
> fine. On an Optimizely host it is not always: called before your `UseEndpoints(...)`, it publishes
> the hub through a `UseEndpoints` call of its own, and `MapContent()` then consolidates that
> already-published data source into its own snapshot — so the hub ends up registered twice and
> *every* Blazor request in the application fails with `AmbiguousMatchException`, this package's pages
> and the host's alike. This was reported from a real CMS 13 + Commerce 15 site. Mapping it on your
> route builder, before `MapContent()`, avoids the whole question.
>
> `UseOptiPowerToolsScheduledJobsInsights()` is still needed — it applies migrations and runs the
> startup diagnostics — and it detects that the hub is already mapped, so calling both is safe and is
> the recommended shape above.
>
> It deliberately does **not** call `MapControllers()`. See the note below on that, which is less
> obvious than it looks.

On a minimal-hosting app (`WebApplication`) there is also
`app.MapOptiPowerToolsScheduledJobsInsights()`, which maps the hub *without* applying migrations —
`Use…` is `Map…` plus the migration step. Reach for it only when you have deliberately taken the schema
into your own hands (`AutoMigrateDatabase = false` plus the shipped script); otherwise call `Use…`, or
you get a working UI over an empty database with nothing anywhere saying why. Calling both is safe:
the hub is mapped at most once per application.

### If you get `AmbiguousMatchException`

Endpoint matching runs *before* authentication, so this reproduces on an anonymous request and does
not need a CMS login to diagnose. The package logs a named error at startup when it can see the
duplication itself, which is usually faster than reading the exception.

There are two independent causes, and they need opposite fixes.

**The hub is registered twice.** Every Blazor request in the application fails, not only this
package's pages. Cause: `UseOptiPowerToolsScheduledJobsInsights()` ran before your own
`UseEndpoints(...)` on a host whose `MapContent()` consolidates already-published endpoints. Fix: use
the wiring at the top of this section — `MapOptiPowerToolsScheduledJobsInsights()` inside your block,
before `MapContent()`. If your host maps its own hub, set `MapBlazorHub` to `false`.

**Every attribute-routed action is registered twice**, this package's page among them, along with
Optimizely's and Commerce's own. Cause: on some stacks `MapContent()` already maps attribute-routed
controllers, and an additional `MapControllers()` maps them a second time. This was reported on
CMS 13 + Commerce 15.

> **Do not simply delete `MapControllers()`.** Whether `MapContent()` maps controllers depends on the
> stack, and getting it wrong fails in the other direction. Measured on a plain CMS 13 Alloy site with
> no Commerce: removing `MapControllers()` leaves `MapContent()` alone, and this package's page then
> returns **404** — the site still starts and nothing is logged, so the page has simply vanished.
> Change one thing, restart, and check that the page still answers before you keep the change.

### Writing a logged job

Derive from `LoggedScheduledJobBase` and implement `ExecuteJob()` instead of the usual `Execute()`:

```csharp
using EPiServer.Scheduler;
using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Logging;

[ScheduledJob(DisplayName = "Nightly Catalog Sync", IntervalType = ScheduledIntervalType.Days)]
public class CatalogSyncJob : LoggedScheduledJobBase
{
    public CatalogSyncJob(JobLoggingContext context)
        : base(context)
    {
    }

    protected override string ExecuteJob()
    {
        LogInputData(new { Source = "ERP", Mode = "Incremental" });

        Log("Starting catalog sync.");
        // ... do the work, calling OnStatusChanged(...) as usual if you like ...
        Log("42 products updated, 1 skipped.", LogSeverity.Warning);

        RecordMetric("ProductsUpdated", 42);

        Summary.AppendSection("Totals");
        Summary.AppendLine("  Updated : 42");
        Summary.AppendLine("  Skipped : 1");

        return "Synced 42 products.";
    }
}
```

- `Execute()` is sealed — the wrapper always runs, so implement `ExecuteJob()` instead.
- Every `OnStatusChanged(...)` call you make is captured automatically, in addition to the native event.
- If `ExecuteJob()` throws, the exception is recorded (message, stack trace) and then rethrown unchanged — native CMS admin's failure tracking behaves exactly as it would without this package.
- Constructor parameters are resolved via DI, the same way Optimizely already constructs every `ScheduledJobBase` — add your own alongside `JobLoggingContext` and forward only the context to `base`.
- **Stoppable jobs**: set `IsStoppable = true` in your constructor and check `IsStopRequested` between units of work. A run that ends after a stop request is recorded as **Stopped** rather than as a success, so the history says what actually happened.

### Result summary

`Summary` is an optional multi-line report for the run. It is not the same thing as the string
`ExecuteJob()` returns:

| | where it shows | shape |
|---|---|---|
| the returned `string` | Optimizely's **Last execution message**, one cell of the CMS admin grid, *and* the Result line here | keep it to one sentence |
| `Summary` | the **Result summary** section of the execution detail view | as many lines as you need |

Nothing is written unless you append something, so jobs that do not use it pay nothing.

```csharp
Summary.AppendLine($"Export for {from:yyyy-MM-dd} … {to:yyyy-MM-dd}");

Summary.AppendSection("Rows by region");        // underlined heading, blank line before it
foreach (var region in regions)
    Summary.AppendLine($"  {region.Name,-6} {region.Rows,6:N0}");

Summary.AppendSection("Totals");
Summary.AppendLine($"  Rows exported : {total:N0}");
```

`Append`, `AppendLine`, and `AppendSection` all return the summary, so a line built from parts can be
chained: `Summary.Append("  ").Append(name).AppendLine(count.ToString())`.

- **Newlines are preserved** end to end — stored as written, rendered as written.
- **It survives failure.** The summary is persisted on the way out of `ExecuteJob()` whether it
  returned or threw, so whatever a failing job managed to record is still on the page.
- **It is bounded.** Appends past `MaxResultSummaryLength` (100,000 characters by default) are
  discarded and the stored text ends with a truncation notice — a job appending one line per
  processed row cannot write megabytes into every execution row. `Summary.IsTruncated` tells you
  whether that happened.
- **It is safe to append to from parallel tasks**, like `Log`.
- `SetSummary("…")` replaces the whole thing at once, for jobs that already hold the finished text.
- `FlushSummary()` persists it mid-run. Only needed for a long job that wants its summary visible in
  the detail view while it is still running; otherwise the automatic flush at the end is enough.

Code that records an execution without being a scheduled job at all can call
`IJobExecutionWriter.SetResultSummary(executionId, text)` directly — the same bound applies.

## Viewing logs

The CMS menu item opens a paginated execution list — job name, status, start time, duration, result —
with filters for job and status, and a link across to the CMS's own **Scheduled Jobs** page.

### Execution statuses

| | |
|---|---|
| **Running** | Started, no outcome reported yet. |
| **Succeeded** | `ExecuteJob()` returned without throwing. |
| **Failed** | It threw. The message and stack trace are recorded, and the exception is rethrown unchanged. |
| **Stopped** | An administrator pressed Stop and the job noticed — see [Writing a logged job](#writing-a-logged-job). Distinct from Succeeded: the work was cut short. |
| **Interrupted** | No outcome was ever reported and the run has been given up on: the process was recycled, the container replaced, or the host crashed mid-job. Applied retrospectively by the cleanup job after `InterruptedExecutionThreshold`, because a process that dies mid-run cannot record anything itself. The completion time stays empty — it is genuinely unknown. |

The last two exist so the history says what actually happened. Without them a stopped job reads as a
success and an abandoned one sits at *Running* for ever, quietly distorting every count and filter.

**Timestamps are shown in your own time zone**, stated above the table and suffixed with the offset
on the detail page (`2026-08-19 17:37:16 UTC+02:00`). The browser's IANA zone is recorded in a
`sji-timezone` cookie by the page itself and applied server-side, so it survives prerendering rather
than flickering into place after the circuit connects. Two consequences worth knowing:

- **The very first page view renders in UTC**, labelled as such, because the cookie is written by that
  page and does not exist until it has been served once. Every view afterwards is correct at prerender
  with no flicker. Nothing reloads to avoid that single occurrence.
- **Only the zone follows you; the format does not.** Dates stay ISO-ordered and numbers stay
  invariant, so `2026-08-19` never has to be read as either August or the 19th month, and a duration
  reads the same in a ticket as it does on the host that produced it.

### Log severities

| Severity | Color |
|---|---|
| `Info` | Blue |
| `Success` | Green |
| `Warning` | Yellow |
| `Error` | Red |
| `Debug` | Gray |
| `Default` | Neutral |

![Log severities in the console viewer](images/LogSeverities.jpg)

### Watching a job that is still running

A still-running execution re-reads itself every `DetailPollInterval` (two seconds by default) and
appends new lines live, marked with a **live** indicator; only lines newer than those already shown
are fetched, so following a long run stays cheap.

Leave that page open and the run finishes underneath you, and the page catches up on its own — no
reload. The status badge flips, the duration and completion time fill in, and any section that did
not exist yet appears: **Metrics** (the automatic ones are only recorded as the job ends) and
**Result summary**, if the job wrote one without checkpointing it. Because log lines and metrics go
through the buffered writer while completion is written straight through, the page reads once more a
moment after the run ends, so the last batch of both lands too.

## Automatic metrics

Recorded for every execution, alongside anything you record yourself via `RecordMetric`. The names
are constants on the public `JobMetricNames` class, so a dashboard or alert can reference them without
hard-coding strings:

| Metric | Notes |
|---|---|
| `DurationMs` | Wall-clock time around `ExecuteJob()`. Always reliable — one dedicated thread per execution. |
| `ThreadAllocatedBytes` | Bytes allocated on the job's own thread. Named for its scope: a job that fans work out to the thread pool under-reports, and the delta can even come out negative if it resumes on a different thread than it started on. |
| `ProcessCpuTimeMs` | CPU time consumed by the *whole process* during the job's window. On a CMS serving requests this includes everything else the application did, and on a multi-core host it can exceed the job's own duration. Per-job CPU is not something this package can measure, so the name says whose CPU it is. |
| `GcGen0Collections` / `GcGen1Collections` / `GcGen2Collections` | Process-wide GC collection count deltas. Same concurrency caveat as CPU time, but still a useful trend signal across repeated runs of the same job. |

## Configuration

### Code configuration

```csharp
services.AddOptiPowerToolsScheduledJobsInsights(options =>
{
    // Required
    options.ConnectionString = "Server=.;Database=MyDb;Trusted_Connection=True;";

    // Optional — all values below are the defaults
    options.AutoMigrateDatabase = true;
    options.RetentionDays = 30;
    options.CleanupBatchSize = 500;

    options.LogChannelCapacity = 10_000;
    options.LogBatchSize = 100;
    options.LogFlushInterval = TimeSpan.FromMilliseconds(500);

    options.PageSize = 50;

    options.PageTitle = "Scheduled Jobs Insights";

    // Authorization. AuthorizedRoles starts empty, which means the built-in set —
    // Administrators, CmsAdmins, WebAdmins. Assigning it REPLACES that set rather than adding to it,
    // so the line below narrows access to one role rather than granting a fourth.
    options.AuthorizedRoles = ["SecOps"];
    // Or name a policy of your own with options.AuthorizationPolicy, or set
    // options.AllowAnyAuthenticatedUser if access is already restricted elsewhere.
    options.EnableCmsMenu = true;
    options.MenuPlacement = CmsMenuPlacement.CmsSection;
    options.MenuPath = null;
    options.MenuSortIndex = null;
    options.CustomSectionName = "OptiPowerTools";
    options.CustomMenuItemName = string.Empty;
    options.ShowInDataSyncManagement = true;
    options.CmsShellPath = "/ScheduledJobsInsightsCms/Index";
});
```

### appsettings.json

```json
{
  "OptiPowerTools": {
    "ScheduledJobsInsights": {
      "ConnectionString": "Server=.;Database=MyDb;Trusted_Connection=True;",
      "AutoMigrateDatabase": true,
      "RetentionDays": 30,
      "CleanupBatchSize": 500,
      "LogChannelCapacity": 10000,
      "LogBatchSize": 100,
      "LogFlushInterval": "00:00:00.500",
      "PageSize": 50,
      "MaxResultSummaryLength": 100000,
      "PageTitle": "Scheduled Jobs Insights",
      "AuthorizedRoles": ["SecOps"],

      "EnableCmsMenu": true,
      "MenuPlacement": "CmsSection",
      "ShowInDataSyncManagement": true,
      "CustomSectionName": "OptiPowerTools",
      "CmsShellPath": "/ScheduledJobsInsightsCms/Index"
    }
  }
}
```

Code overrides configuration when both are used.

`AuthorizedRoles` is worth one note, because it is the one option where the obvious reading is wrong.
Omit it and the built-in set applies — `Administrators`, `CmsAdmins`, `WebAdmins`. List roles, as
above, and that set is **replaced**: the example authorizes `SecOps` alone, not `SecOps` plus the
three. This is why the option's own default is empty rather than the three role names — a non-empty
default could not be replaced from `appsettings.json` at all, because the configuration binder adds
into an existing collection instead of clearing it.

### Options reference

| Option | Type | Default | Description |
| ------ | ---- | ------- | ----------- |
| `ConnectionString` | `string` | `""` | **Required.** SQL Server connection string for job execution/log/metric storage. |
| `AutoMigrateDatabase` | `bool` | `true` | Apply pending EF Core migrations automatically at startup. |
| `MaxLogMessageLength` | `int` | `4000` | Longest log message stored; longer ones are truncated with an ellipsis. The column itself is unbounded, so this is what stops a job that logs a response body per iteration writing megabytes per row. |
| `MaxLogEntriesPerExecution` | `int` | `20000` | Most log lines the detail page reads *and holds* for one execution. A Blazor circuit holds every line it is given for as long as the page is open, so this is a server-side memory bound, once per viewer — and it bounds the total across all the polls of a running job, not just one read. A longer log is displayed truncated, with a notice above it saying so. |
| `MaxLogCharactersPerExecution` | `int` | `4000000` | The companion bound, and the one that actually describes the cost: about 8 MB of text per open page. A line count alone is only a proxy — multiplied by `MaxLogMessageLength` it permits far more than it appears to. Whichever bound is reached first stops the buffer, and ordinary logs reach neither. One line is always kept however long it is, so a single oversized line cannot leave the log looking empty. |
| `DetailPollInterval` | `TimeSpan` | `00:00:02` | How often the detail page re-reads an execution that is still running. One query per open page per interval, so it scales with viewers rather than with history; each tick reads a narrow projection plus any new log lines, not the whole row. |
| `ConfigureDbContext` | `Action<DbContextOptionsBuilder>?` | `null` | Applied to the insights `DbContext` after its connection string, for what this package does not decide: `EnableRetryOnFailure()`, a command timeout, a connection interceptor, a managed-identity token provider. Code-only — a delegate cannot come from `appsettings.json`. |
| `AddBlazorServices` | `bool` | `true` | Whether to register Blazor Server and cascading authentication state. Set to `false` when the host registers Blazor itself — its registrations must be equivalent, or the retention screen loses the authorization re-check it makes before a destructive write. The service-side counterpart to `MapBlazorHub`. |
| `InterruptedExecutionThreshold` | `TimeSpan` | `24:00:00` | How long a run may sit unfinished before the cleanup job records it as **Interrupted**. `TimeSpan.Zero` disables the sweep. |
| `RetentionDays` | `int` | `30` | How many days of execution history to keep for jobs with no rule of their own. Enforced by the cleanup job; overridden per job by `[JobRetention]` or the retention screen. `0` or less means keep indefinitely — the resolved value is stated in the startup log, and a *negative* one is called out as a warning, since `0` is the documented way to ask for indefinite and a negative number is nearly always a typo for a day count. |
| `CleanupBatchSize` | `int` | `500` | Max executions deleted per batch by the cleanup job. |
| `LogChannelCapacity` | `int` | `10000` | Capacity of the in-memory buffer for log/metric writes before falling back to a synchronous insert. |
| `LogBatchSize` | `int` | `100` | Max buffered records flushed to the database per batch. |
| `LogFlushInterval` | `TimeSpan` | `00:00:00.5` | Max time buffered records wait before being flushed, even if `LogBatchSize` isn't reached. |
| `PageSize` | `int` | `50` | Executions shown per page in the Blazor list. |
| `MaxResultSummaryLength` | `int` | `100000` | Character limit for an execution's result summary. Appends past it are discarded and the stored text ends with a truncation notice. Values of zero or less fall back to the default. |
| `PageTitle` | `string` | `"Scheduled Jobs Insights"` | Title shown in the CMS shell chrome and browser tab. |
| `AuthorizedRoles` | `IList<string>` | *empty* | Optimizely roles allowed to reach the page, the retention screen and the menu entries. Empty means the built-in set — `Administrators`, `CmsAdmins`, `WebAdmins` — so leaving it unset cannot lock you out. Naming any role **replaces** that set rather than adding to it, from `appsettings.json` and from code alike. Ignored when `AuthorizationPolicy` or `AllowAnyAuthenticatedUser` is set. |
| `AuthorizationPolicy` | `string?` | `null` | Name of an authorization policy **you** registered, used instead of the role check. Startup fails with a named error if no such policy exists. |
| `AllowAnyAuthenticatedUser` | `bool` | `false` | Drops the role check entirely. ⚠️ On a site with front-end membership, "authenticated" includes ordinary visitors — who could then read execution history and any captured input data. |
| `MapBlazorHub` | `bool?` | `null` | Whether `UseOptiPowerToolsScheduledJobsInsights` maps the Blazor hub. `null` detects an existing `/_blazor` mapping and skips its own. |
| `EnableCmsMenu` | `bool` | `true` | Add a menu item to the Optimizely CMS navigation. |
| `MenuPlacement` | `CmsMenuPlacement` | `CmsSection` | Where the menu item appears: `CmsSection`, `TopLevel`, or `CustomSection`. |
| `MenuPath` | `string?` | `null` | Overrides the auto-derived menu path. |
| `MenuSortIndex` | `int?` | `null` | Overrides the auto-derived sort index. |
| `CustomSectionName` | `string` | `"OptiPowerTools"` | Section name for `TopLevel`/`CustomSection` placement. |
| `CustomMenuItemName` | `string` | *(empty)* | Overrides the menu item label; falls back to `PageTitle`. |
| `ShowInDataSyncManagement` | `bool` | `true` | Also adds an entry under **Settings › Data & Sync Management**, directly below the CMS's own **Scheduled Jobs** page. Independent of `MenuPlacement` — see below. |
| `ShowRetentionMenuItem` | `bool` | `true` | Adds a menu entry for the **Job Retention** screen beside the insights one. The screen stays reachable at `?view=retention` either way. |
| `CmsShellPath` | `string` | `"/ScheduledJobsInsightsCms/Index"` | URL path where the UI is served. A single execution is addressed with an `id` query string, e.g. `/ScheduledJobsInsightsCms/Index?id=42`. |

### Data & Sync Management entry

By default the UI is reachable from two places. `MenuPlacement` positions the primary entry, and
`ShowInDataSyncManagement` adds a second one inside the CMS's own admin tree, immediately below the
native **Scheduled Jobs** page:

```
Settings
  Data & Sync Management
    Scheduled Jobs             (Optimizely's own)
    Scheduled Jobs Insights    (this package)
```

That is where an administrator looking at a job tends to look for its history, so it is on by
default. The two settings are independent; set `ShowInDataSyncManagement` to `false` for a single
menu entry positioned solely by `MenuPlacement`.

### Menu Placement

Same three placement modes as the rest of the OptiPowerTools family:

#### `CmsSection` (default)

Nests the menu item under the existing CMS section.

#### `TopLevel`

```json
{ "OptiPowerTools": { "ScheduledJobsInsights": { "MenuPlacement": "TopLevel" } } }
```

Places the menu item directly in the global navigation bar.

#### `CustomSection`

```json
{
  "OptiPowerTools": {
    "ScheduledJobsInsights": {
      "MenuPlacement": "CustomSection",
      "CustomSectionName": "Background Jobs"
    }
  }
}
```

Creates a new collapsible section and nests the item underneath it.

## When the insights database is unavailable

This package observes scheduled jobs; it is never allowed to prevent them. If its database cannot be
reached:

- **The application still starts.** Startup migrations log a critical error and continue rather than
  aborting `Configure`, which would otherwise stop the whole CMS from booting.
- **Jobs still run, and still report correctly.** `BeginExecution` returns null, recording is skipped
  for that run, and everything else behaves normally — including rethrowing a job's own exception, so
  Optimizely's success/failure tracking is unchanged.
- **The CMS status column still updates**, because `OnStatusChanged` raises the native event before
  any recording is attempted.
- **Nothing throws into job code.** No member of `IJobExecutionWriter` throws, and neither does
  anything around it. `LogInputData` survives anything the object you hand it can do — an object graph
  JSON cannot serialize (a cycle through an EF navigation), *and* a property getter that throws on
  access (a lazy-loading proxy whose `DbContext` is gone, a computed `IContent` property). Either way
  the run records why the input could not be captured and carries on. The automatic metrics are
  captured and recorded defensively too, so a counter this package cannot read costs a metric rather
  than the run, and a metrics failure can no longer report a successful job as failed, nor replace the
  job's own exception with its own.

What you lose is the execution history for the affected period. The UI itself needs the database to
show anything, so it reports that it could not read the history rather than rendering an empty list —
and rather than dropping the CMS's own circuit-error bar over the page.

## Database & migrations

Tables live in a fixed SQL Server schema (`scheduled_jobs_insights`) via standard EF Core Migrations — there is no `SchemaName` option, so the schema location is not runtime-configurable.

### Applying the schema

By default the package applies pending migrations at startup. If the application's identity has no
DDL rights set `AutoMigrateDatabase = false` and apply the schema yourself.

> **Running more than one instance?** Every instance calls `Migrate()` on startup, and nothing
> coordinates them. Two instances starting together can attempt the same migration at once, which at
> worst leaves one of them logging a failure and continuing without recording history — the failure is
> caught, so it never stops the application. If you deploy to a farm, or roll instances during an
> upgrade, set `AutoMigrateDatabase = false` and apply the shipped script once as part of the
> deployment instead.

**Every release ships an idempotent SQL script** for exactly this, attached to the
[GitHub release](https://github.com/szolkowski/OptiPowerTools.ScheduledJobsInsights/releases) as
`scheduled-jobs-insights-<version>.sql`. Run it with any tool you like; it is safe to re-run against a
database at any migration level, including one that is already current:

```bash
sqlcmd -S <server> -d <database> -i scheduled-jobs-insights-1.0.0.sql
```

That script is the supported route for a consuming application. The `dotnet ef` command below works
only inside a checkout of *this* repository, because `ScheduledJobsInsightsDbContext` and its
design-time factory are `internal`:

```bash
dotnet ef database update \
  --project src/OptiPowerTools.ScheduledJobsInsights \
  --context OptiPowerTools.ScheduledJobsInsights.Data.ScheduledJobsInsightsDbContext
```

No `--startup-project` is needed — `Data/ScheduledJobsInsightsDbContextFactory` is a design-time
`IDesignTimeDbContextFactory`, so the library serves as its own startup project. (Passing the `.Web`
host instead fails: `Microsoft.EntityFrameworkCore.Design` is a `PrivateAssets="All"` reference of the
library and does not flow to it.)

## Retention

How long each job's execution history is kept, resolved in this order:

| | set by | wins over |
|---|---|---|
| **1. Override** | an administrator, in the **Job Retention** screen | everything |
| **2. `[JobRetention]`** | the job's own code | the default |
| **3. `RetentionDays`** | configuration (default 30) | — |

Any of the three can be **indefinite**, meaning the cleanup job skips that history entirely.

### Declaring retention on a job

```csharp
[ScheduledJob(DisplayName = "Nightly Catalog Sync", IntervalType = ScheduledIntervalType.Days)]
[JobRetention(7, Description = "Logs one line per SKU; a week is enough to diagnose a bad run.")]
public class CatalogSyncJob : LoggedScheduledJobBase { }
```

Use `JobRetentionAttribute.Indefinite` to keep a job's history forever. The attribute travels with the
code, so a fresh deployment gets it right without anyone remembering to configure anything — but it is
a *default*, not a mandate: an administrator can still override it.

The `Description` is shown beside the value in the retention screen, so whoever is deciding whether to
override it can see what the job's author intended and why.

### The Job Retention screen

Reached from the **Retention** link on the execution list, or from its own entry under **Settings ›
Data & Sync Management**. For every job it shows the declared value and its rationale, what is
actually in force and where that came from, how many executions are currently stored, and who last
changed the setting.

The list covers every job deriving from `LoggedScheduledJobBase` — so a job can be configured before
its first run — plus every job type that only exists in history, so records left behind by deleted
code can still be trimmed. Those rows are marked **history only**.

Jobs on Optimizely's own `ScheduledJobBase` are deliberately absent: they never write execution
history, so there is nothing to retain, and listing the CMS's two dozen built-ins would bury the
handful that matter.

Choosing a value saves it straight away — there is no Save button, so there is no half-applied state.
*Inherit* clears the override, letting the attribute or the default apply again.

Changes take effect on the next run of the cleanup job. Nothing is deleted at the moment you save.

## Cleanup job

`ScheduledJobsInsightsCleanupJob` is auto-discovered into the CMS's own Scheduled Jobs admin list, like any other native job. It deletes executions (and their cascade-deleted logs/metrics) in batches of `CleanupBatchSize`, and reports what it removed as its execution message.

Each run does three passes:

1. **The default sweep** — everything older than `RetentionDays`, *excluding* every job type that has a retention of its own. Jobs with their own rule are skipped whether that rule is shorter or longer than the default, so the default can never delete history a job explicitly asked to keep.
2. **One pass per governed job type**, each against its own cutoff. Job types set to indefinite are skipped entirely.
3. **Give up on abandoned runs.** Executions still `Running` since longer ago than
   `InterruptedExecutionThreshold` are recorded as **Interrupted**. A process recycled mid-run writes
   nothing further, so nothing else would ever finish those rows — and until they are resolved, every
   count and status filter is wrong. `CompletedAt` stays empty: the end time is genuinely unknown.

**Neither sweep ever deletes a row that is still `Running`**, however old it is, and the third pass
runs last for that reason. A job may legitimately run longer than its own retention — a 25-hour import
under a one-day rule — and age alone cannot tell a stranded run from a working one. Marking a row
`Interrupted` moves it out of `Running` and so makes it deletable, so resolving abandoned runs *before*
the sweeps would hand them the history of jobs that were still going. Doing it last costs a stranded
row one extra cleanup interval before it ages out, which is the cheaper side of that trade.

After installation, the job's run interval and enabled/disabled state are managed from the CMS Scheduled Jobs screen, not from options — `RetentionDays`/`CleanupBatchSize` are the only settings that keep working post-install. The job is itself a `LoggedScheduledJobBase`, so its own runs appear in the execution list like any other.

Being a logged job, it records metrics of its own, alongside the automatic ones:

| Metric | Notes |
|---|---|
| `ExecutionsDeleted` | Executions removed in that run, across the default sweep and every per-job rule. Their log and metric rows go with them, by database cascade. |
| `ExecutionsMarkedInterrupted` | Executions given up on and moved from *Running* to *Interrupted* by the third pass. |

### If your application already uses Blazor

`UseOptiPowerToolsScheduledJobsInsights` maps the Blazor Server hub the UI connects over. Mapping
`/_blazor` twice puts two endpoints on one route pattern and every Blazor request in the application
then fails with `AmbiguousMatchException` — with nothing in the message naming this package.

By default it detects an existing mapping and skips its own, so most hosts need do nothing. Set
`MapBlazorHub` explicitly if yours maps its hub **after** this call, which detection cannot see:

```csharp
options.MapBlazorHub = false;   // your application owns /_blazor
```

Note the hub this package maps carries its authorization policy. A hub the host already mapped is
left exactly as the host configured it.

## Removing this package

Unlike a fully self-contained storage layer, this package owns its own SQL Server tables. Removing it stops new executions from being recorded and drops the cleanup job from the CMS's Scheduled Jobs list, but existing `scheduled_jobs_insights.*` tables and their data are left in place until you drop them manually.

## Development

Building the package, running the dev CMS in Docker, the submodule, seeding Alloy content and
regenerating migrations are all covered in
[CONTRIBUTING.md](CONTRIBUTING.md).
None of it is needed to *use* the package.

## Versioning

This package follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html), and what that
promises is worth stating precisely, because the public surface here is deliberately narrow.

**Inside the SemVer contract** — a breaking change to any of these means a new major version:

- `LoggedScheduledJobBase` and its protected members, including the `ExecuteJob()` /
  `OnStopRequested()` seams.
- `JobLoggingContext` as a constructor parameter type, and `JobLoggingContext.ForWriter(...)`.
- `JobResultSummary`, `RetentionPeriod`, `JobRetentionAttribute`, `JobMetricNames`.
- `IJobExecutionWriter`. Implementing or mocking it is supported — that is what
  `JobLoggingContext.ForWriter(...)` is for — and **no member will be added to it outside a major
  version**, so an implementation written against 1.0 keeps compiling for the whole of 1.x.
- `OptiPowerToolsScheduledJobsInsightsOptions` and its option names, plus the
  `OptiPowerTools:ScheduledJobsInsights` configuration section.
- The `Add…` / `Use…` / `Map…` extension methods.
- The persisted enums `ExecutionStatus`, `LogSeverity` and `LogEntrySource`, and the database schema
  itself — migrations only ever move forward.

**Outside it** — these may change in any release:

- Anything `internal`, which is most of the implementation.
- The Razor components. They are public because Razor generates them so, and are marked
  `[EditorBrowsable(Never)]`; render them through the package's own route, not directly.
- The exact text of log messages, result messages and rendered markup.

Released versions are listed in the [changelog](CHANGELOG.md).


## Compatibility

| Package version | .NET | Optimizely CMS |
|---|---|---|
| 1.x | 10.0 | 13.x |

## License

[MIT](LICENSE.txt) — see `LICENSE.txt`.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

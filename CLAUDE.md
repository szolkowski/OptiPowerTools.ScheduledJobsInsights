# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repo is

A NuGet package for Optimizely (EPiServer) CMS 13 that adds structured logging, metrics, and a Blazor
admin UI on top of Optimizely's **native** scheduled jobs (`EPiServer.Scheduler.ScheduledJobBase`).
Part of the `OptiPowerTools` family (sibling: `OptiPowerTools.Hangfire`, which does the equivalent for
Hangfire-based background jobs instead of native scheduled jobs). This repo was originally generated
from the `OptiPowerTools.PackageName` scaffold template and has since been fully built out — the
scaffold's placeholder tokens have already been renamed throughout.

## Commands

```bash
# Build/test the packable library + tests (works without the submodule)
dotnet build src/OptiPowerTools.ScheduledJobsInsights/OptiPowerTools.ScheduledJobsInsights.csproj
dotnet test tests/OptiPowerTools.ScheduledJobsInsights.Tests/OptiPowerTools.ScheduledJobsInsights.Tests.csproj

# Run a single test (xUnit fully-qualified name filter)
dotnet test tests/OptiPowerTools.ScheduledJobsInsights.Tests/OptiPowerTools.ScheduledJobsInsights.Tests.csproj --filter "FullyQualifiedName~LoggedScheduledJobBaseTests"

# Pack the NuGet package
dotnet pack src/OptiPowerTools.ScheduledJobsInsights/OptiPowerTools.ScheduledJobsInsights.csproj -c Release

# Regenerate EF Core migrations after changing the model in Data/
dotnet ef migrations add <Name> \
  --project src/OptiPowerTools.ScheduledJobsInsights \
  --startup-project src/OptiPowerTools.ScheduledJobsInsights.Web \
  --context OptiPowerTools.ScheduledJobsInsights.Data.ScheduledJobsInsightsDbContext \
  --output-dir Data/Migrations

# Full solution (requires the Alloy submodule to be populated first)
git submodule update --init --recursive
dotnet build

# Run the whole dev stack in Docker (db + web). Web on :5103, SQL Server on :6003.
cp .env.example .env    # first time only
docker compose up -d

# ...or just the database, running the web host locally on https://localhost:5001
docker compose up db -d
dotnet run --project src/OptiPowerTools.ScheduledJobsInsights.Web
```

The CMS back office is at `/Optimizely/CMS/` on CMS 13 — the old `/episerver` path returns a bare 404.
On a fresh database, register the first administrator at `/util/register` (that page disappears once a
user exists).

`App_Data/` is gitignored in the submodule, so a fresh clone has **no** CMS content: Optimizely imports
`sub/MyOptiAlloySite/MyOptiAlloySite/App_Data/DefaultSiteContent.episerverdata` into an empty database
and builds the site definition from it. Without that file the CMS starts and the admin UI works, but
`/` returns 404 and there is no site. Copy it from any other MyOptiAlloySite checkout, then
`docker compose restart web`. See the README's "Seed the Alloy content" section.

The Optimizely scheduler is disabled in Development by the Alloy site's own startup, so jobs never fire
on their interval in this dev host — trigger them with **Start Manually** in the CMS Scheduled Jobs
admin page. `src/OptiPowerTools.ScheduledJobsInsights.Web/Samples/` holds ten sample jobs, all
`DefaultEnabled = false`, which between them exercise every logging API and every state the two UI
pages can render (including `SeedHistoryJob`, which writes ~60 synthetic executions so paging and the
filters have data).

`TreatWarningsAsErrors` is enabled repo-wide via `Directory.Build.props`, and `GenerateDocumentationFile`
is on for the main library — every publicly *and protected*-visible member needs an XML doc comment or
the build fails (CS1591). Because of this, the public API surface is deliberately narrow: most
implementation types (`JobExecutionWriter`, `JobLogBackgroundWriter`, `CleanupRepository`,
`JobExecutionQueryService`, the EF entities, `ScheduledJobsInsightsDbContext`, `JobRecord`/`LogRecordItem`/
`MetricRecordItem`) are `internal` and only need doc comments if you choose to add them. Only
`LoggedScheduledJobBase`, `IJobExecutionWriter`, `ICleanupRepository`, the config/enum types, and the
extension methods are `public` — keep new additions to that surface fully documented, or make them
`internal` instead (the test project has `InternalsVisibleTo` access to everything).

`NuGetAuditMode` is set to `direct` only (transitive EPiServer CVEs are out of scope), and `NU1608` is
suppressed for a known Castle.Core version conflict between NSubstitute and EPiServer.

## Architecture

### The core integration point: `LoggedScheduledJobBase`

`Logging/LoggedScheduledJobBase.cs` is what job authors actually use — a drop-in replacement for
`ScheduledJobBase`. The design is driven entirely by three verified facts about
`EPiServer.Scheduler.ScheduledJobBase`:

1. `Execute()` is `abstract` (not virtual) and synchronous, returning the string shown as the CMS
   admin's "last execution message". `LoggedScheduledJobBase` provides a **sealed** override of
   `Execute()` that wraps a new `protected abstract string ExecuteJob()` — job authors implement
   `ExecuteJob()` instead of `Execute()`, guaranteeing the capture wrapper always runs.
2. `OnStatusChanged(string)` is `protected virtual`. `LoggedScheduledJobBase` seals its own override,
   which calls `base.OnStatusChanged(...)` first (preserving the native `StatusChanged` event that CMS
   admin polls for live status) and then persists the message as a side effect.
3. Job instances are constructed fresh per execution via
   `EPiServer.Scheduler.Internal.DefaultScheduledJobFactory` → `ActivatorUtilities.GetServiceOrCreateInstance`,
   i.e. full constructor DI, with no instance reuse across runs. This is why correlating logs/metrics
   to "the current execution" via a plain instance field (`_executionId`) is safe — there's no ambient
   context (`AsyncLocal`) machinery anywhere in this codebase, and there shouldn't be.

**Exceptions thrown from `ExecuteJob()` are always rethrown, never swallowed** — Optimizely's own job
executor sets `HasLastExecutionFailed`/`LastExecutionMessage` by catching whatever `Execute()` throws.
This package only *observes* failures (recording them before rethrowing); it never changes native
success/failure semantics. If you touch `Execute()` in `LoggedScheduledJobBase`, preserve this.

Job name resolution (`IScheduledJobRepository.Get(ScheduledJobId)`) is wrapped in try/catch with a
fallback to `GetType().Name` — this is deliberate, not just defensive: it's what makes the class
constructible in unit tests without a registered `ScheduledJob` definition.

### Write path: why some writes are sync and others are buffered

`Logging/IJobExecutionWriter.cs` / `JobExecutionWriter.cs` split writes into two tiers:

- `BeginExecution`/`Complete`/`SetInputData` are **synchronous, immediate** EF Core writes via a
  short-lived `IDbContextFactory<ScheduledJobsInsightsDbContext>`. These are low-frequency (at most a
  couple of calls per execution) and `BeginExecution` must return the DB-generated `Id` *before*
  `ExecuteJob()` runs, so there's nothing to gain from buffering them.
- `Log`/`RecordMetric` go through a bounded `Channel<JobRecord>` (`System.Threading.Channels`), drained
  in batches by `Logging/JobLogBackgroundWriter.cs` (a `BackgroundService`). This exists because a
  chatty job emitting thousands of log lines in a loop would otherwise issue one `SaveChanges()` round
  trip per line. `Channel.Writer.TryWrite` is attempted first (non-blocking, keeping `Log()`
  synchronous like `Execute()` itself); if the channel is momentarily full, `JobExecutionWriter` falls
  back to a direct synchronous single-row insert for that one entry — this guarantees **zero log loss**
  under backpressure, at the cost of an occasional synchronous write.
- `JobLogBackgroundWriter` drains and flushes any remaining buffered records on shutdown
  (`StopAsync`/`ExecuteAsync`'s catch block) so nothing written right as a job finishes gets lost.

If you change the batching logic, the two tests that actually exercise this
(`Logging/JobExecutionWriterTests.cs`'s channel-full fallback test, and
`Logging/JobLogBackgroundWriterTests.cs`'s shutdown-drain test) are the ones to keep passing.

### Data model and schema

`Data/Entities/` has three tables: `JobExecution` (one row per run), `JobLogEntry` (many rows per run,
ordered by `(JobExecutionId, Sequence)` — **not** by `Timestamp` alone, since a tight logging loop can
produce timestamp collisions), and `JobMetric` (many rows per run — both automatic metrics and
`RecordMetric()`-recorded custom ones share this one table for a uniform query surface). Child rows
cascade-delete with their parent `JobExecution` at the DB level.

Schema (`scheduled_jobs_insights`, a fixed constant on `ScheduledJobsInsightsDbContext.SchemaName`) is
**not** runtime-configurable — deliberately, unlike Hangfire's own `SchemaName` option — because this
package uses standard EF Core Migrations (`Data/Migrations/`), and a migration-baked schema name can't
safely be made a runtime option without hand-rolled SQL scripting (which this package intentionally
avoids in favor of the standard `dotnet ef` workflow). `UseOptiPowerToolScheduledJobsInsights(app)`
calls `Database.Migrate()` at startup, gated by `AutoMigrateDatabase` (default `true`).
`Data/ScheduledJobsInsightsDbContextFactory.cs` is a design-time-only `IDesignTimeDbContextFactory` that
exists purely so `dotnet ef migrations add` works without a `Startup`/`Program` in this library project
— its connection string is never used at runtime.

### Blazor UI

Unlike `OptiPowerTools.Hangfire`, which iframes a third-party dashboard it does not own, this package
owns its markup and renders **inline inside the CMS chrome** — no iframe. The MVC controller + view
(`Cms/ScheduledJobsInsightsCmsController`, `Views/ScheduledJobsInsightsCms/Index.cshtml`) draws the
Optimizely shell and hosts the components through the Component Tag Helper, so they inherit the
shell's Axiom styling instead of needing a stand-in stylesheet. That means **Blazor Server**
(`AddServerSideBlazor()` + `MapBlazorHub()`), not the Blazor Web App model — the latter wants to own
the whole page, which is what forced the iframe originally. There is no `App.razor`/`Routes.razor`
and the components have no `@page`.

Five things silently break this UI. Each was a real bug; none produce an obvious error:

1. **`Views/_ViewImports.cshtml`** must register `@addTagHelper *, EPiServer.Shell.UI`. Without it
   `<episerver-resources>` and `<platform-navigation>` render as *literal tags* and there is no CMS
   chrome at all.
2. **`<base href>`** must be in the view. `blazor.server.js` resolves the hub relative to it; without
   it the client negotiates `/ScheduledJobsInsightsCms/_blazor/negotiate` (404), the circuit never
   starts, and the page silently stays prerendered — the list still looks right, but nothing is
   interactive and the `Virtualize`-backed log renders as an empty black box.
3. **`RequiresAspNetWebAssets=true` in the hosting application's csproj**, which is what pulls in
   `Microsoft.AspNetCore.App.Internal.Assets` — the only source of `_framework/blazor.server.js`. The
   Web SDK sets it automatically only when the *application* contains `.razor` files, and ours live
   in the package. The package cannot set this for consumers: NuGet resolves the implicit pack during
   restore, before package MSBuild assets are imported. Verified against a throwaway consumer app.
4. **A single execution is addressed by `?id=42`, never `/42`.** The CMS shell decides which product's
   navigation to render by matching the request path against registered menu items; any extra path
   segment matches nothing, `data-epi-product-id` comes back empty, and the left-hand menu spins on
   its loading dots forever. `ScheduledJobsInsightsCmsRouteConvention` therefore maps `CmsShellPath`
   exactly, with no `{id?}`.
5. **`UseOptiPowerToolScheduledJobsInsights()` must run before the host's own `UseEndpoints(...)`**,
   and must not call `MapControllers()` — mapping controllers from two `UseEndpoints` blocks registers
   every action twice and throws `AmbiguousMatchException` at request time.

Two pages, both hosted by that view rather than routed:

- `Components/Pages/Index.razor` — paginated/filterable execution list. Uses **keyset (seek) pagination**
  (`StartedAt DESC, Id DESC` with a cursor), not offset/`Skip(n)` — this is a large, append-heavy,
  time-ordered table, and offset paging both degrades and shifts under concurrent inserts. Rows carry a
  real `<a href>` as well as the row click, so the list works before the circuit connects.
- `Components/Pages/Detail.razor` — console-style log viewer for one execution, `Virtualize`-backed,
  polling every 2s via `PeriodicTimer` while the execution is still `Running`. `Id` arrives as a
  component parameter from the MVC route, not from Blazor routing. Log lines are fetched
  **incrementally** (`GetLogEntriesAsync(id, afterSequence)`) — re-reading a 5,000-line log on every
  poll tick is quadratic. Polling and JS interop both start in `OnAfterRenderAsync(firstRender)`, which
  does not run during prerendering.

Styling and scripts:

- `wwwroot/css/scheduled-jobs-insights.css` ships as a static web asset (`_content/...`) and holds
  page-level layout, scoped under `.sji-content`. Per-component rules stay in the Razor scoped CSS,
  which is bundled into the **host application's** `{ApplicationName}.styles.css` — the view resolves
  that name at runtime, since the package cannot know it.
- The console must **not** set `scrollbar-width`/`scrollbar-color`: since Chrome 121 their presence
  makes it ignore `::-webkit-scrollbar`, which brings back the auto-hiding overlay scrollbar. With a
  5,000-line log the scroll range is ~250,000px, so the thumb also needs a `min-height`.
- `wwwroot/js/console-scroll.js` is a JS module loaded via `IJSRuntime` `import` (JS isolation), used
  by the Jump to start / Jump to end buttons. Virtualize keeps no element at either end, so scrolling
  has to be done by setting `scrollTop`.

`Components/Shared/LogSeverityStyles.cs`/`ExecutionStatusStyles.cs` are the **only** places that map the
persisted `LogSeverity`/`ExecutionStatus` enums to actual colors — the data model itself has no color
concept, by design. Note these cannot be extracted into shared Razor components: both enums are
`internal`, and a `[Parameter]` of an internal type on a (necessarily public) component is a compile
error.

`Cms/CmsAdminUrls.cs` holds the hard-coded URLs of the CMS's own scheduled job screens, used by the
cross-links on both pages. Optimizely exposes no resolver for them, so this is the single place to
change if a future CMS release moves the Settings SPA.

### CMS menu

`Cms/ScheduledJobsInsightsMenuProvider` contributes up to two entries. `MenuPlacement` positions the
primary one (`CmsSection`/`TopLevel`/`CustomSection`); `ShowInDataSyncManagement` (default `true`)
independently adds a second under the CMS's own **Settings > Data & Sync Management**, as a sibling of
the native Scheduled Jobs page at `/global/cms/admin/scheduledjobs/scheduledjobsinsights`. The parent
group is Optimizely's, so only the leaf is contributed.

### Cleanup job

`Jobs/ScheduledJobsInsightsCleanupJob.cs` is itself a `[ScheduledJob]`-attributed class built on
`LoggedScheduledJobBase` (dogfooding — its own runs are logged the same way). It's auto-discovered by
Optimizely into the CMS's own Scheduled Jobs admin list; after installation, its interval and
enabled/disabled state are managed there, not through options — only `RetentionDays`/`CleanupBatchSize`
keep working post-install. Deletion is batched (`ExecuteDelete()` in a loop until a batch returns 0),
deliberately without an `OrderBy` — the loop's correctness doesn't depend on which rows go first.

### Tests

xUnit + NSubstitute, mirroring the `src/` folder structure. Pure logic (options binding, DI
registration, the menu provider, `LoggedScheduledJobBase`'s success/failure/status-changed paths, the
cleanup job's batch loop) is tested with NSubstitute alone — no database involved. Anything that needs
real EF Core query translation (`JobExecutionQueryService`, cascade deletes, the background writer) uses
a **Sqlite in-memory** provider (`Tests/Data/SqliteDbContextFactory.cs`), not the EF Core `InMemory`
provider — `InMemory` doesn't support `ExecuteDelete` (used by the cleanup repository) and doesn't
enforce FK/cascade behavior. Note `ScheduledJobsInsightsDbContext.OnModelCreating` has a
Sqlite-specific `DateTimeOffsetToBinaryConverter` workaround for a real Sqlite provider limitation
(no native `ORDER BY` translation over `DateTimeOffset`) — this only applies when `Database.ProviderName`
is Sqlite, so it has zero effect on the production SQL Server schema/migrations.

These Sqlite tests validate the C#-side query/repository/cascade logic only — they do not validate the
literal SQL Server migration SQL. That's only exercised by running the `.Web` dev host against real
SQL Server in Docker.

## Code Style (from CONTRIBUTING.md)

- File-scoped namespaces, Allman braces, 4-space indent, `var` for apparent types; 2-space indent for
  JSON/XML/YAML
- `_camelCase` private fields, `PascalCase` public members
- Options pattern (`IOptions<T>`) + DI via extension methods; expression bodies preferred; pattern
  matching over casts
- Tests: AAA pattern, `[Theory]`/`[InlineData]` for parameterized cases

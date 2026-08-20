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
# (no --startup-project: Data/ScheduledJobsInsightsDbContextFactory serves as one. Passing the .Web
#  host instead fails — Microsoft.EntityFrameworkCore.Design is PrivateAssets="All" on the library
#  and does not flow to it.)
dotnet ef migrations add <Name> \
  --project src/OptiPowerTools.ScheduledJobsInsights \
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
admin page. `src/OptiPowerTools.ScheduledJobsInsights.Web/Samples/` holds twelve sample jobs, all
`DefaultEnabled = false`, which between them exercise every logging API and every state the two UI
pages can render (including `SeedHistoryJob`, which writes ~60 synthetic executions so paging and the
filters have data, and `SummaryShowcaseJob` and `BulkSummaryJob`, which stress the summary section from opposite ends —
long lines that overrun the character limit, and ~2,000 short lines that fit inside it).

`TreatWarningsAsErrors` is enabled repo-wide via `Directory.Build.props`, and `GenerateDocumentationFile`
is on for the main library — every publicly *and protected*-visible member needs an XML doc comment or
the build fails (CS1591). Because of this, the public API surface is deliberately narrow: most
implementation types (`JobExecutionWriter`, `JobLogBackgroundWriter`, `CleanupRepository`,
`JobExecutionQueryService`, the EF entities, `ScheduledJobsInsightsDbContext`, `JobRecord`/`LogRecordItem`/
`MetricRecordItem`) are `internal` and only need doc comments if you choose to add them. Only
`LoggedScheduledJobBase`, `JobResultSummary`, `IJobExecutionWriter`, `ICleanupRepository`, the
config/enum types, and the extension methods are `public` — keep new additions to that surface fully
documented, or make them `internal` instead (the test project has `InternalsVisibleTo` access to
everything). Razor components are generated as public types, so their `[Parameter]` properties need
doc comments too.

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

### Result summary

`Logging/JobResultSummary.cs` backs the optional `protected Summary` property — a multi-line report a
job builds as it works, persisted to `JobExecution.ResultSummary` and rendered in its own accordion
section. Three things about it are deliberate:

1. It is **flushed once, just before `Complete`, on both the success and failure paths**. Recording
   it on failure is the point: whatever a job managed to summarise before throwing is usually the
   most useful thing on the page. `FlushSummary()` is exposed for long jobs that want a mid-run
   checkpoint, and each write overwrites rather than appends.
2. `StringBuilder` is wrapped rather than exposed so appends can be **bounded** — the column is
   unbounded and a job appending a line per processed row would otherwise write megabytes into every
   execution. The truncation notice's length is budgeted for up front, so `ToString()` never exceeds
   `MaxLength` and nothing downstream has to truncate a second time. `JobExecutionWriter` applies the
   same bound independently, since `SetResultSummary` can be called directly.
3. The configured limit reaches the base class through **`IJobExecutionWriter.MaxResultSummaryLength`**.
   That looks like an odd place for a config value, and it is — but derived jobs forward a fixed pair
   of arguments to `base(writer, repository)`, so an `IOptions<T>` constructor parameter would break
   every one of them, and the writer is the only DI-resolved collaborator the base class holds. A
   writer reporting a non-positive value (an NSubstitute double at its default) falls back to
   `JobResultSummary.DefaultMaxLength` rather than throwing.

Job name resolution (`IScheduledJobRepository.Get(ScheduledJobId)`) is wrapped in try/catch with a
fallback to `GetType().Name` — this is deliberate, not just defensive: it's what makes the class
constructible in unit tests without a registered `ScheduledJob` definition.

### Write path: why some writes are sync and others are buffered

`Logging/IJobExecutionWriter.cs` / `JobExecutionWriter.cs` split writes into two tiers:

- `BeginExecution`/`Complete`/`SetInputData`/`SetResultSummary` are **synchronous, immediate** EF Core
  writes via a short-lived `IDbContextFactory<ScheduledJobsInsightsDbContext>`. These are
  low-frequency (at most a couple of calls per execution) and `BeginExecution` must return the
  DB-generated `Id` *before* `ExecuteJob()` runs, so there's nothing to gain from buffering them.
  `SetResultSummary` in particular must **not** be buffered: `Complete` follows immediately after it,
  and a channel-buffered summary could land after the row is already marked finished.
- `Log`/`RecordMetric` go through a bounded `Channel<JobRecord>` (`System.Threading.Channels`), drained
  in batches by `Logging/JobLogBackgroundWriter.cs` (a `BackgroundService`). This exists because a
  chatty job emitting thousands of log lines in a loop would otherwise issue one `SaveChanges()` round
  trip per line. `Channel.Writer.TryWrite` is attempted first (non-blocking, keeping `Log()`
  synchronous like `Execute()` itself); if the channel is momentarily full, `JobExecutionWriter` falls
  back to a direct synchronous single-row insert for that one entry — this means **no log loss under
  backpressure**, at the cost of an occasional synchronous write. (Loss under a *database failure* is
  a different matter — see below.) The first fallback is logged as a warning and the rest at Debug:
  under sustained backpressure it fires on every write, and a warning per log line would bury the one
  message that matters in the noise it describes.
- `JobLogBackgroundWriter` flushes anything still buffered on shutdown, so what a job logged just
  before the application stopped is not lost. Two non-obvious constraints hold that together, both
  found by a CI failure rather than by reading the code:
  1. **The drain lives in an overridden `StopAsync`, not only at the end of `ExecuteAsync`.**
     `BackgroundService` starts `ExecuteAsync` on the thread pool, so a host that stops promptly can
     cancel that task *before its body is ever entered* — it ends up `Canceled` having executed
     nothing at all. A drain that only existed inside it was skipped exactly then.
  2. **The in-flight batch is a field (`_pending`), not a local.** Collecting takes records *out* of
     the channel, so a batch abandoned when cancellation interrupts the collector is unreachable to
     a drain that only reads the channel.
  `StopAsync_FlushesBufferedRecords_EvenIfExecuteAsyncNeverRan` and
  `StopAsync_FlushesRecordsAlreadyTakenFromTheChannel` pin the two cases; both fail against the
  previous implementation. The original `StopAsync_FlushesAnyRecordsStillBuffered` passed for months
  because it happened to stop before the collector ran — it only failed once a slower CI runner
  scheduled things the other way round.

**Nothing in the write path may take down what it is observing.** Two rules, both load-bearing:

1. **Nothing throws out of `JobLogBackgroundWriter.ExecuteAsync`.** Since .NET 6 the default
   `BackgroundServiceExceptionBehavior` is `StopHost`, so an unhandled exception from a hosted
   service stops the whole application — a transient SQL error while writing *log lines* would have
   taken the CMS offline. `FlushAsync` retries a batch `MaxFlushAttempts` times with a short backoff
   and then logs an error and drops it. A dropped batch costs some history; an escaping exception
   costs the site. `JobLogBackgroundWriterTests` covers both the survival and the
   keeps-writing-afterwards paths, and both fail against the pre-fix code.
2. **No member of `IJobExecutionWriter` throws.** They all run while a job is executing, and this
   package only *observes* an execution — a failure to record must never become a failure of the run.
   `BeginExecution` signals failure by returning **`long?` null** rather than throwing.
3. **A null execution id disables recording for that run.** `LoggedScheduledJobBase` keeps
   `_executionId` as a `long?` and every write is guarded on it, so an unreachable insights database
   costs the *history* of a run, never the run. Two details that are easy to break:
   `base.OnStatusChanged(...)` is still called unconditionally (the CMS admin's live status column is
   Optimizely's, not ours, and must keep working), and `ExecuteJob()`'s exception is still rethrown
   unchanged, so a failed job is never silently reported as successful.
4. **`Database.Migrate()` at startup is caught too.** It runs inside `Configure`, so an exception
   there aborts application startup — an unreachable *reporting* database stopped the entire CMS from
   booting. Verified: pointing the insights connection string at a dead host used to produce
   `Application startup exception`, and now produces one Critical log line followed by
   `Application started`.

The whole shape is one rule: **a tool that observes scheduled jobs must never be able to prevent
them.** `LoggedScheduledJobBaseTests`' four `WhenTheExecutionCannotBeRecorded_*` tests pin it and all
four fail against the pre-fix code.

What is deliberately *not* protected: the Blazor UI. With the database down the execution list and
detail pages error, which is correct — that is the one part whose whole job is reading that database.

If you change the batching logic, the two tests that actually exercise this
(`Logging/JobExecutionWriterTests.cs`'s channel-full fallback test, and
`Logging/JobLogBackgroundWriterTests.cs`'s shutdown-drain test) are the ones to keep passing.

### Data model and schema

`Data/Entities/` has four tables. Three record executions: `JobExecution` (one row per run, including
the optional `ResultSummary` — distinct from `ResultMessage`, which is the one-line value Optimizely
renders in its admin grid), `JobLogEntry` (many rows per run,
ordered by `(JobExecutionId, Sequence)` — **not** by `Timestamp` alone, since a tight logging loop can
produce timestamp collisions), and `JobMetric` (many rows per run — both automatic metrics and
`RecordMetric()`-recorded custom ones share this one table for a uniform query surface). Child rows
cascade-delete with their parent `JobExecution` at the DB level.

The fourth, `JobRetentionPolicy`, is configuration rather than history: one row per job type that an
administrator has given an explicit retention, unique on `JobTypeName`, with `RetentionDays` nullable
(null = indefinite) and the `ModifiedBy`/`ModifiedAt` audit pair. It has no relationship to
`JobExecution` — the key is a CLR type name, which deliberately outlives both the history it governs
and the code that produced it.

`JobExecution` carries five indexes and every one of them earns its place. Measured against 100,000
executions / 2,000,000 log rows on real SQL Server (logical reads):

| query | with index | without |
|---|---|---|
| list page 1, unfiltered — `(StartedAt DESC, Id DESC)` | 168 | — |
| list page ~1,800, deep — same index | **171** | offset paging would degrade linearly |
| filtered by job — `(JobName, StartedAt DESC, Id DESC)` | 171 | ~35,000 |
| filtered by status — `(Status, StartedAt DESC, Id DESC)` | **3** | **35,208** (208ms CPU) |
| detail: execution / metrics / log | 3 / 3 / 3 | — |
| cleanup / retention, per job type — `(JobTypeName, StartedAt)` | 9,090 per 500-row batch | — |

The status one is the least obvious and the most valuable. `Running` is a transient state, so that
filter usually matches *nothing* — and without an index, matching nothing means scanning everything.
The descending flags on both composites are what let a keyset page come back already ordered, with no
sort. The `JobName` key is wide (`nvarchar(400)`); narrowing that column is the lever if insert cost
ever outweighs list latency.

The `JobTypeName` one serves the cleanup job's two shapes at once — the default sweep's `NOT IN`
exclusion list and the per-job-type delete — and is what makes per-job retention free: measured at
100,000 executions the exclusion form costs *less* than the unfiltered sweep (9,090 vs 10,584 reads),
and the per-job form matches it. Cleanup cost is dominated by the cascade, not the lookup: ~63,000 of
the ~92,000 reads per 500-execution batch are the `JobLogEntries` rows going with them, which no index
can avoid. A batch of 500 plus cascades runs in ~75 ms either way.

What is *not* indexed, deliberately: `ExecutionFilter` exposes `From`/`To`, but the UI never sets
them, so there is nothing to tune yet.

Everything above is flat in table size — re-measured at both 10,000 and 100,000 executions, no read
path moved by more than 2 logical reads. Only two queries scale linearly, and both must: the
`DISTINCT JobName` behind the filter dropdown (below), and the retention screen's `GROUP BY
JobTypeName` execution count, which grows 104 → 980 reads across that same range. The dropdown is
cached; **the retention count is not**, which makes it the most expensive query in the UI. Deliberate
for now — that screen is opened rarely, unlike the list — but it is the first thing to cache if an
installation ever passes a few hundred thousand executions.

`GetDistinctJobNamesAsync` is **cached for 60 seconds** (`JobExecutionQueryService`, a singleton, so
process-wide). No index can help it — producing a distinct list means looking at every row, 681 reads
at 100,000 executions and growing linearly — while the answer only changes when a job runs for the
very first time. Prerendering meant it ran twice per page view. The refresh is behind a semaphore with
a double-check, because prerender and the circuit start milliseconds apart and would otherwise both
miss. The expiry is stamped only after a *successful* read, so a failed query retries instead of
caching an empty dropdown. `TimeProvider` is injected purely so the expiry is testable without
waiting.

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

Three pages, all hosted by that view rather than routed:

- `Components/Pages/Index.razor` — paginated/filterable execution list. Uses **keyset (seek) pagination**
  (`StartedAt DESC, Id DESC` with a cursor), not offset/`Skip(n)` — this is a large, append-heavy,
  time-ordered table, and offset paging both degrades and shifts under concurrent inserts. Rows carry a
  real `<a href>` as well as the row click, so the list works before the circuit connects. The query
  **projects to `ExecutionListItem`, not the `JobExecution` entity** — the entity carries three
  unbounded columns (`ResultSummary`, `InputDataJson`, `ExceptionStackTrace`) the grid never renders,
  and a page is 50 rows of them. `HasResultSummary` is a `!= null` evaluated in SQL, so the summary
  text stays out of the list query entirely.
- `Components/Pages/Detail.razor` — console-style log viewer for one execution, `Virtualize`-backed,
  polling every 2s via `PeriodicTimer` while the execution is still `Running`. `Id` arrives as a
  component parameter from the MVC route, not from Blazor routing. Log lines are fetched
  **incrementally** (`GetLogEntriesAsync(id, afterSequence)`) — re-reading a 5,000-line log on every
  poll tick is quadratic. Polling and JS interop both start in `OnAfterRenderAsync(firstRender)`, which
  does not run during prerendering. When a watched execution finishes, the loop renders the outcome
  immediately and then reads **once more** after `LogFlushInterval` + 500ms: `Complete` is a
  synchronous write but the final log lines and *all* the automatic metrics go through the buffered
  channel, so a loop that stopped the instant the status flipped left the page showing a finished run
  with an empty metrics table until someone reloaded by hand. Result summary, metrics, input data and stack trace sit in
  `Components/Shared/AccordionSection.razor`, built on native `<details>`/`<summary>` rather than a
  click handler and a bool: it works during prerendering, gets keyboard/AT behaviour for free, and —
  because `Open` is only the initial value and never changes between renders — Blazor's diff never
  touches the attribute again, so the two-second poll cannot collapse a section the user just opened.
  All of its `[Parameter]` types are public, which is what lets it be extracted at all (see the note
  on the severity/status style classes below). `Open` is an *initial* state but still a parameter, so
  a value that changed between renders would patch the attribute and collapse the section — which is
  why the detail page **latches** its auto-collapse decision (`_summaryStartsOpen`, taken the first
  time a summary appears, above `SummaryAutoCollapseLines`). A job checkpointing with `FlushSummary`
  grows its summary across polls, and an unlatched threshold would snap the section shut under the
  reader. The size badge, being informational, does keep updating.
- `Components/Pages/Retention.razor` — per-job retention settings, reached at `?view=retention`. One
  row per job, each with a `<select>` of the day presets plus *Inherit* and *Keep forever*; choosing
  saves immediately (there is no Save button, so there is no half-applied state to reason about).
  `DayChoicesFor` folds a non-preset override — someone may have set 42 days directly in the database
  — into that row's options rather than silently resetting it on the next save. A failed save is
  surfaced in a `[role=alert]` rather than swallowed: retention that quietly failed to change would
  leave an administrator believing history was being kept, or removed, when it is not. `CurrentUser`
  arrives as a parameter alongside `Id` and `ViewerTimeZone`, for the same reason — the audit trail
  needs it and a component has no `HttpContext` once the circuit takes over.

Styling and scripts:

- `wwwroot/css/scheduled-jobs-insights.css` ships as a static web asset (`_content/...`) and holds
  page-level layout, scoped under `.sji-content`. Per-component rules stay in the Razor scoped CSS,
  which is bundled into the **host application's** `{ApplicationName}.styles.css` — the view resolves
  that name at runtime, since the package cannot know it.
- The console must **not** set `scrollbar-width`/`scrollbar-color`: since Chrome 121 their presence
  makes it ignore `::-webkit-scrollbar`, which brings back the auto-hiding overlay scrollbar. With a
  5,000-line log the scroll range is ~250,000px, so the thumb also needs a `min-height`.
- `wwwroot/js/detail-interop.js` is a JS module loaded via `IJSRuntime` `import` (JS isolation), used
  by the Jump to start / Jump to end buttons and the summary's Copy button. Virtualize keeps no element
  at either end, so scrolling has to be done by setting `scrollTop`. `copyText` returns a bool rather
  than throwing: `navigator.clipboard` only exists in a secure context, so the button labels itself
  "Copy unavailable" on plain HTTP instead of failing silently.

`Components/Shared/DisplayFormat.cs` is the **only** place either view formats a number or a
timestamp, and every method there passes an explicit `CultureInfo.InvariantCulture`. Two decisions
worth not undoing:

- **Numbers are invariant.** They are diagnostics inside hard-coded English labels, and they get
  pasted into tickets and compared between environments; `310,99 ms` on one host and `310.99 ms` on
  another is worse than either. `.editorconfig` sets `CA1305` (Specify IFormatProvider) to **error**,
  so a `ToString("…")` without a provider fails the build. Note CA1305 does *not* catch interpolated
  strings (`$"{x:N0}"`) — verified with a probe file — so the rule is a backstop, not full coverage;
  route UI formatting through `DisplayFormat` rather than relying on it.
**Timestamps are `Components/Shared/ViewerClock.cs`'s job**, and are rendered in the *reader's* zone.
They used `DateTimeOffset.LocalDateTime`, which converts to the **server's** zone — so on a UTC
container an administrator elsewhere saw UTC dressed as local time with nothing saying so. Four
things about how the zone gets there:

- The browser's IANA id is stored in a `sji-timezone` cookie by an inline script in the hosting view,
  read by `ScheduledJobsInsightsCmsController`, and passed to the components as a **parameter** —
  the same route `Id` already takes. That indirection is load-bearing: `IHttpContextAccessor` only
  has a context during prerendering, so a component reading the cookie itself would resolve the zone
  on the prerender pass and lose it the instant the circuit took over, flipping the page back to UTC.
- A cookie rather than JS interop for the same reason — the value must exist *before* rendering.
  The cost is that the first ever page view renders in UTC (labelled, never wrong); every view after
  is correct at prerender with no flicker. Nothing reloads to avoid that one occurrence.
- The **zone id** is passed, not an offset. An offset captured "now" and applied to an execution from
  the other side of a daylight-saving change renders it an hour out; `TimeZoneInfo.ConvertTime`
  resolves the rules per timestamp. `ViewerClockTests` pins this with a winter/summer pair.
- Everything degrades to UTC rather than throwing — the id is cookie-borne untrusted input, so it is
  length- and charset-checked before it reaches the zone database, and both `TimeZoneNotFoundException`
  (trimmed container images have no tz data) and `InvalidTimeZoneException` fall back.

Only the *zone* follows the reader; the *format* does not. Dates stay ISO-ordered and numbers stay
invariant, because a locale-ordered date reintroduces the day/month ambiguity ISO ordering exists to
avoid. The list states the zone once above the table rather than suffixing fifty rows.

`Components/Shared/LogSeverityStyles.cs`/`ExecutionStatusStyles.cs` are the **only** places that map the
persisted `LogSeverity`/`ExecutionStatus` enums to actual colors — the data model itself has no color
concept, by design. Note these cannot be extracted into shared Razor components: both enums are
`internal`, and a `[Parameter]` of an internal type on a (necessarily public) component is a compile
error.

`Cms/CmsAdminUrls.cs` holds the hard-coded URLs of the CMS's own scheduled job screens, used by the
cross-links on the execution pages. Optimizely exposes no resolver for them, so this is the single place to
change if a future CMS release moves the Settings SPA.

### CMS menu

`Cms/ScheduledJobsInsightsMenuProvider` contributes up to three entries. `MenuPlacement` positions the
primary one (`CmsSection`/`TopLevel`/`CustomSection`); `ShowInDataSyncManagement` (default `true`)
independently adds a second under the CMS's own **Settings > Data & Sync Management**, as a sibling of
the native Scheduled Jobs page at `/global/cms/admin/scheduledjobs/scheduledjobsinsights`. The parent
group is Optimizely's, so only the leaf is contributed. `ShowRetentionMenuItem` (default `true`) adds
a third leaf beside it for the retention screen, pointing at `CmsShellPath?view=retention` — the menu
*path* has its own segment (`.../scheduledjobsinsightsretention`) so the shell can highlight it, while
the *URL* stays on the one mapped route, since an extra path segment there would break the shell's
navigation resolution (see above). All three entries are gated on `EnableCmsMenu` and on
`AuthorizedRoles`.

### Retention

Three sources, resolved override → `[JobRetention]` attribute → `RetentionDays`, each able to be
indefinite. The order is expressed in exactly one place — `JobRetention.Resolve` — and pinned by
`JobRetentionPrecedenceTests`; nothing else should reimplement it.

- **`RetentionPeriod`** is a struct, not an `int?`, because "indefinite" and "not configured" are both
  naturally null and mean opposite things. A `RetentionPeriod` always *is* a configured period;
  whether one exists is the nullability of the field holding it. `CutoffFrom(now)` returns null for
  indefinite, which is exactly what the cleanup job branches on.
- **Keyed on `JobTypeName`, never `JobName`.** The CLR name survives a job being renamed in the CMS.
  A retention rule that silently stopped applying after a rename would fail in the worst direction —
  quietly keeping everything forever.
- **Two interfaces over one implementation.** `IJobRetentionPolicySource` is public and minimal;
  `IJobRetentionService` is internal and adds the screen's needs. The split exists because
  `ScheduledJobsInsightsCleanupJob` must be public for Optimizely to discover it, so every type in its
  constructor must be public too — and exposing the whole screen-facing surface (audit trails,
  execution counts, orphaned jobs) just to satisfy that would be the tail wagging the dog.
- **An unusable attribute value is surfaced, not swallowed.** An attribute cannot throw usefully at
  startup, so `[JobRetention(0)]` falls back to the default *and* is flagged in the screen. Silently
  ignoring it would leave the author believing retention was configured.
- **Only `LoggedScheduledJobBase` subclasses are listed**, plus anything present in execution history.
  A job on Optimizely's own `ScheduledJobBase` never writes a row here, so it has no history to retain;
  listing every registered job would bury the handful that matter among the CMS's built-ins.
  `LoggedJobTypeIndex` answers both "is this a logged job" and "what does it declare" from one scan.
- **`ExistsInCode` and `IsRegistered` are different questions.** The "history only" tag keys off the
  former: a job can exist in code without the CMS having registered it, and that is not the same as
  history whose code has been deleted.
- **The type scan is lazy and cached for the process.** Attributes are compiled in, so nothing can
  change them without a restart; `ReflectionTypeLoadException` is caught per assembly, since plugins
  failing to load types is routine in a CMS and must not cost the whole index. Because it scans loaded
  assemblies rather than taking an injected list, `JobRetentionServiceTests` needs *real*
  `LoggedScheduledJobBase` subclasses to assert against — `tests/.../Retention/RetentionTestJobs.cs`
  exists for that and nothing else; a substituted type list would not exercise the index at all.
- **The cleanup job excludes governed job types from the default sweep** whether their rule is shorter
  or longer. Otherwise the default would delete history a job explicitly asked to keep.
- The screen is a third component on the same route, `?view=retention` — a query string for the same
  reason the execution id is one (see the CMS-shell constraints above). The current user arrives as a
  parameter from the hosting view, like `Id` and `ViewerTimeZone`, because the audit trail needs it and
  a component has no `HttpContext` once the circuit takes over.

### Cleanup job

`Jobs/ScheduledJobsInsightsCleanupJob.cs` is itself a `[ScheduledJob]`-attributed class built on
`LoggedScheduledJobBase` (dogfooding — its own runs are logged the same way). It's auto-discovered by
Optimizely into the CMS's own Scheduled Jobs admin list; after installation, its interval and
enabled/disabled state are managed there, not through options — only `RetentionDays`/`CleanupBatchSize`
keep working post-install. Deletion is batched (`ExecuteDelete()` in a loop until a batch returns 0),
deliberately without an `OrderBy` — the loop's correctness doesn't depend on which rows go first.

### Tests

xUnit + NSubstitute + bUnit, mirroring the `src/` folder structure. Pure logic (options binding, DI
registration, the menu provider, `LoggedScheduledJobBase`'s success/failure/status-changed paths, the
cleanup job's batch loop, the retention precedence order) is tested with NSubstitute alone — no
database involved. Anything that needs
real EF Core query translation (`JobExecutionQueryService`, cascade deletes, the background writer) uses
a **Sqlite in-memory** provider (`Tests/Data/SqliteDbContextFactory.cs`), not the EF Core `InMemory`
provider — `InMemory` doesn't support `ExecuteDelete` (used by the cleanup repository) and doesn't
enforce FK/cascade behavior. Note `ScheduledJobsInsightsDbContext.OnModelCreating` has a
Sqlite-specific `DateTimeOffsetToBinaryConverter` workaround for a real Sqlite provider limitation
(no native `ORDER BY` translation over `DateTimeOffset`) — this only applies when `Database.ProviderName`
is Sqlite, so it has zero effect on the production SQL Server schema/migrations.

The three Blazor pages are tested with **bUnit** (`tests/.../Components/`), rendering them in-process
with substituted services. Three things about that setup:

- Substituting `IJobExecutionQueryService` (and `IJobRetentionService`) needs `InternalsVisibleTo("DynamicProxyGenAssembly2")` on
  the library, which is why that second entry exists in the csproj. The test project's own access is
  not enough: the interface is internal and NSubstitute emits its proxy into Castle's dynamic
  assembly, not into the test assembly. (Only works because this assembly is not strong-named.)
- `ComponentTestBase` exposes its helpers as `internal`, not `protected` — they traffic in internal
  types, and derived test classes are in the same assembly. The class itself stays public so xUnit
  discovers the tests.
- JS interop runs in **loose** mode. These tests assert on rendered output, and `Virtualize` does its
  own interop for scroll spacers that would otherwise need stubbing in every log test. A test that
  asserts a specific JS call (the Copy button, the jump buttons) has to switch to strict mode and
  `JSInterop.SetupModule(...)` the detail module.

The component tests are mutation-checked rather than trusted because they went green: flipping the
collapse threshold, reverting the detail link to a path segment, reverting timestamps to
`LocalDateTime`, renaming a JS function and breaking the module URL each fail the tests that claim to
cover them. Worth repeating that exercise when adding to them — a component test that renders
successfully but asserts nothing meaningful is easy to write by accident.

What bUnit covers is rendering and interaction. Polling, the trailing read after an execution
finishes, and the incremental log fetch across ticks are **deliberately untested** — a decision, not
an oversight. All three need a second poll tick, and `PollInterval` is a hard-coded two seconds, so
covering them would mean either second-long tests or promoting that interval to an option purely for
testability. Judged not worth it. If you change `PollUntilFinishedAsync` or `LoadTrailingWritesAsync`,
verify by hand against a running job — `SlowMigrationJob` exists for exactly that. Nor does it cover any of the five
CMS-shell constraints above; those are host-integration failures only a real browser catches.

These Sqlite tests validate the C#-side query/repository/cascade logic only — they do not validate the
literal SQL Server migration SQL. **CI covers that separately**: the Build & Test workflow runs a real
SQL Server service container, applies the migrations to an empty database, re-applies them to prove
the run is a no-op (`UseOptiPowerToolScheduledJobsInsights` calls `Migrate()` on every startup, so a
non-idempotent migration breaks every existing installation on upgrade), and then executes
`.github/sql/verify-schema.sql` to assert the schema that came out.

That last script is the only thing checking any of the SQL Server-specific DDL — descending index
keys, `nvarchar(max)`, the custom schema, the cascade rules — because `OnModelCreating` swaps in a
`DateTimeOffsetToBinaryConverter` under Sqlite and the test suite therefore never sees it. It is a
plain `.sql` file precisely so it can also be run by hand against a local database; the header shows
how. Extend it whenever a migration adds something whose shape matters.

## Code Style (from CONTRIBUTING.md)

- File-scoped namespaces, Allman braces, 4-space indent, `var` for apparent types; 2-space indent for
  JSON/XML/YAML
- `_camelCase` private fields, `PascalCase` public members
- Options pattern (`IOptions<T>`) + DI via extension methods; expression bodies preferred; pattern
  matching over casts
- Tests: AAA pattern, `[Theory]`/`[InlineData]` for parameterized cases

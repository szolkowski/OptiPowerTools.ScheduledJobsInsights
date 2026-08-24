# Contributing to OptiPowerTools.ScheduledJobsInsights

Thank you for your interest in contributing! This guide will help you get started.

## Getting Started

1. Fork the repository and clone with submodules:

   ```bash
   git clone --recursive https://github.com/<your-username>/OptiPowerTools.ScheduledJobsInsights.git
   ```

2. Create a branch for your change:

   ```bash
   git checkout -b feature/your-feature-name
   ```

3. Build and run tests:

   ```bash
   dotnet build
   dotnet test
   ```

## Development Setup

- **.NET SDK required:** 10.0
- **Docker** is needed for SQL Server (used by the dev web site)
- **Git with submodule support** — the `sub/MyOptiAlloySite/` submodule provides the Optimizely CMS 13 Alloy site for manual testing
- **`dotnet-ef` global tool** — required if you change the EF Core model in `src/OptiPowerTools.ScheduledJobsInsights/Data/`. Regenerate the migration with:

  ```bash
  dotnet ef migrations add <Name> \
    --project src/OptiPowerTools.ScheduledJobsInsights \
    --context OptiPowerTools.ScheduledJobsInsights.Data.ScheduledJobsInsightsDbContext \
    --output-dir Data/Migrations
  ```

  No `--startup-project`: `Data/ScheduledJobsInsightsDbContextFactory` is a design-time factory and
  serves as one. Passing the `.Web` host instead **fails** — `Microsoft.EntityFrameworkCore.Design` is
  `PrivateAssets="All"` on the library, so it does not flow to a project referencing it.

Full environment setup — Docker, the submodule, seeding Alloy content, the sample jobs — is under
[Development environment](#development-environment) below.

## Code Style

- **C#:** File-scoped namespaces, Allman braces, 4-space indent, `var` for apparent types
- **JSON/XML/YAML:** 2-space indent
- **Naming:** `_camelCase` for private fields, `PascalCase` for public members
- **Patterns:** Options pattern (`IOptions<T>`), DI via extension methods, expression bodies preferred, pattern matching over casts
- **Warnings as errors** is enabled — your code must compile warning-free

## Testing

- Use xUnit with NSubstitute for mocking
- Follow the AAA pattern (Arrange, Act, Assert)
- Use `[Theory]`/`[InlineData]` for parameterized tests
- Tests run against net10.0

Run the full test suite before submitting:

```bash
dotnet test
```

## Submitting Changes

1. Ensure all tests pass and the build is warning-free
2. Keep commits focused — one logical change per commit
3. Write clear commit messages that explain *why*, not just *what*
4. Open a pull request against the `main` branch
5. Describe what your PR does and why in the PR description

## Reporting Issues

- Use [GitHub Issues](https://github.com/szolkowski/OptiPowerTools.ScheduledJobsInsights/issues) to report bugs or suggest features
- Include steps to reproduce for bug reports
- Mention which .NET version and Optimizely CMS version you are using

## Development environment

The solution includes a `.Web` project that references the [MyOptiAlloySite](https://github.com/szolkowski/MyOptiAlloySite) Optimizely CMS 13 site via a git submodule for manual testing. The site runs against SQL Server in Docker.

### Prerequisites

- .NET SDK 10.0
- Docker Desktop (SQL Server for the dev site)
- Git with submodule support

> On Apple Silicon, note that `mcr.microsoft.com/mssql/server` is published for `linux/amd64` only.
> The `db` service pins `platform: linux/amd64` and runs emulated, which is why its healthcheck
> allows a generous start period. No action needed — just expect a slower first start.

### Build and test

The library and its tests build on their own — no submodule, Docker or database required:

```bash
dotnet build src/OptiPowerTools.ScheduledJobsInsights/OptiPowerTools.ScheduledJobsInsights.csproj
dotnet test tests/OptiPowerTools.ScheduledJobsInsights.Tests/OptiPowerTools.ScheduledJobsInsights.Tests.csproj
```

Building the full solution additionally needs the submodule checked out:

```bash
git submodule update --init --recursive
dotnet build
```

`TreatWarningsAsErrors` is on repo-wide, and the library generates XML documentation — every public
or protected member needs a doc comment or the build fails with CS1591.

### Setup

```bash
git clone --recursive https://github.com/szolkowski/OptiPowerTools.ScheduledJobsInsights.git
cd OptiPowerTools.ScheduledJobsInsights
cp .env.example .env   # or edit in place
```

`.env` holds the local SQL Server password, the database name and the host ports:

| Variable | Default | Purpose |
| --- | --- | --- |
| `SA_PASSWORD` | `Episerver123!` | `sa` password inside `sjinsights-db` |
| `DB_NAME` | `ScheduledJobsInsights` | Created on first start by the container entrypoint |
| `WEB_HOST_PORT` | `5103` | Host port for the site |
| `DB_HOST_PORT` | `6003` | Host port for SQL Server |

The ports avoid the ranges used by the sibling `OptiPowerTools.Hangfire` and MyOptiAlloySite stacks,
so all of them can run at once.

### Seed the Alloy content

`App_Data/` is gitignored in the submodule, so a fresh clone has **no** CMS content. Optimizely
imports `App_Data/DefaultSiteContent.episerverdata` automatically into an empty database and creates
the site definition from it. Without that file the CMS still starts and the admin UI works, but there
is no content and no site definition, so `/` returns 404.

Copy it in from any existing MyOptiAlloySite checkout before the first start:

```bash
mkdir -p sub/MyOptiAlloySite/MyOptiAlloySite/App_Data
cp /path/to/MyOptiAlloySite/App_Data/DefaultSiteContent.episerverdata \
   sub/MyOptiAlloySite/MyOptiAlloySite/App_Data/
```

If the site is already running, `docker compose restart web` triggers the import.

### Run via Docker

```bash
docker compose up -d                 # db + web
docker compose up db -d              # just SQL Server
docker compose logs web -f           # follow web logs
docker compose stop                  # stop, keeping the database
docker compose down                  # remove containers; the database volume survives
docker compose down -v               # also delete the database
```

The database lives in the named volume `sjinsights-sqldata` and persists across `down` and Docker
Desktop restarts. Only `down -v` discards it.

| What | Where |
| --- | --- |
| Site | `http://localhost:5103` |
| CMS back office | `http://localhost:5103/Optimizely/CMS/` |
| First-run admin registration | `http://localhost:5103/util/register` |
| Scheduled Jobs Insights | CMS menu item, or `http://localhost:5103/ScheduledJobsInsightsCms/Index` |
| SQL Server | `localhost,6003` — user `sa` |

> On CMS 13 the back office is at `/Optimizely/CMS/`. The CMS 11/12 path `/episerver` returns a bare
> 404 with no redirect.

On a brand-new database there are no users, so visit `/util/register` first and create the
administrator account. Until then the back office is unreachable. The registration page is only
served while no user exists — once the account is created it returns 404, which is expected.

### Run the web host locally

```bash
docker compose up db -d
dotnet run --project src/OptiPowerTools.ScheduledJobsInsights.Web
```

Then open `https://localhost:5001`, log in, and click the CMS menu item.
`appsettings.Development.json` points at `localhost,6003`, so this shares the same containerised
database as the Docker web service — run one or the other, not both against the same data.

### Sample jobs

The `.Web` project includes sample jobs in `Samples/`. These are not part of the NuGet package — they
exist purely to exercise the package's features manually. Between them they cover every logging API
and every state the two UI pages can render.

| Job | What it demonstrates |
| --- | --- |
| `InventorySyncJob` | Multi-phase logging at `Info`/`Success`/`Warning` severities. |
| `ReportBuilderJob` | Building a `Summary` up as the work happens — sections, a per-region breakdown and totals — alongside `LogInputData` and custom `RecordMetric` calls. |
| `FlakyImportJob` | Throws on alternating runs — proves a failure still surfaces correctly in both native CMS admin and this package's UI, and that the summary recorded before the throw is still persisted. |
| `ChattyBatchJob` | Emits ~5,000 log lines in a tight loop — exercises the buffered writer and the virtualized log viewer under load. Also the worked example for `[JobRetention]`, since it is exactly the kind of job that warrants a shorter one. |
| `StatusReportingJob` | `OnStatusChanged` — drives the CMS's live status column and is captured as `LogEntrySource.StatusChanged` lines, interleaved with ordinary `Log` calls. |
| `SlowMigrationJob` | Runs for ~60s so an execution can be watched mid-flight: the `Running` badge, the detail page's 2s polling, the `—` duration, and the seconds duration format. Builds a summary but never flushes it, so the whole **Result summary** section appears on the tick after the job completes. Sets `IsStoppable` and checks `IsStopRequested` between batches, so stopping it mid-run records the execution as **Stopped**. |
| `SeverityShowcaseJob` | One line at every `LogSeverity`, so the console renders the complete colour and label set. |
| `QuietJob` | Logs nothing at all — what an unmodified job looks like after only changing its base class, and the detail page's empty-log state. |
| `ContentAuditJob` | Constructor DI — takes an `IContentLoader` beyond the two parameters the base class needs. |
| `SeedHistoryJob` | Uses the public `IJobExecutionWriter` directly to write ~60 synthetic executions, giving the list enough volume to test keyset paging and the filters. Half of them carry a result summary; one is left permanently `Running`. Also shows the one-shot `SetSummary`. |
| `SummaryShowcaseJob` | Writes long lines past the 100,000-character limit — exercises wrapping and the truncation notice — and checkpoints with `FlushSummary()` so the summary fills in live while the job runs. |
| `BulkSummaryJob` | The volume case: ~2,000 short lines (~55 KB) that fit inside the limit, so the whole report survives. Exercises the summary pane's scrolling and the auto-collapse that keeps a report this long from burying the log. |

All are disabled by default (`DefaultEnabled = false`); enable and trigger them manually from the CMS's Scheduled Jobs admin page.

> The Optimizely scheduler is disabled in Development by the Alloy site's own startup, so jobs never
> fire on their interval in this dev host. Use **Start Manually** in the Scheduled Jobs admin page —
> that still runs the job through `LoggedScheduledJobBase` and records the execution.

### Running tests

```bash
dotnet test
```

Tests run against `net10.0`.

### Project structure

| Project | Purpose |
| ------- | ------- |
| `src/OptiPowerTools.ScheduledJobsInsights` | The NuGet library package. |
| `src/OptiPowerTools.ScheduledJobsInsights.Web` | Dev site for manual testing (references the MyOptiAlloySite submodule). |
| `tests/OptiPowerTools.ScheduledJobsInsights.Tests` | Unit tests — xUnit + NSubstitute, Sqlite in-memory for EF Core-dependent tests, bUnit for the Blazor pages. |
| `sub/MyOptiAlloySite` | Git submodule — [szolkowski/MyOptiAlloySite](https://github.com/szolkowski/MyOptiAlloySite) (Optimizely CMS 13 Alloy site). |

## License

By contributing, you agree that your contributions will be licensed under the [MIT License](LICENSE.txt).

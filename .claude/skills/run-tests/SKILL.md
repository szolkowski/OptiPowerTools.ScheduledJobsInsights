---
name: run-tests
description: Run this repository's test suites — the unit suites for the CMS 13 and CMS 12 packages, and optionally the local Playwright browser tests against throwaway Docker stacks of both CMS versions — and report their status as one table. Use when asked to run the tests, check whether tests pass, verify a change before committing or merging, or check test status. Pass "e2e" or "all" to include the browser tests, and "cms13"/"cms12" to limit them to one CMS version.
---

# Run the tests and report their status

This repository ships two packages from one source tree (CMS 13 and CMS 12) and has three kinds of
tests. Run them through the bundled script, which knows how each one must be run, and report from its
summary table rather than from raw `dotnet test` output.

| Suite | Project | Needs | Time |
|---|---|---|---|
| unit / CMS 13 package | `tests/OptiPowerTools.ScheduledJobsInsights.Tests` | .NET 10 SDK | ~15 s |
| unit / CMS 12 package | `tests/OptiPowerTools.ScheduledJobsInsights.Cms12.Tests` (the same test sources, linked, built against CMS 12) | .NET 10 SDK | ~15 s |
| e2e / cms13, e2e / cms12 | `tests/OptiPowerTools.ScheduledJobsInsights.E2E`, via `scripts/e2e.sh` | Docker running; the Alloy submodule for cms13 | ~4 min for both from cold |

## Choose the scope

- **Default — unit only.** For any code change, and whenever the request just says "run the tests".
- **`all`** (unit, then e2e) before a merge or a release, after touching anything the unit tests
  cannot see — `Views/`, `Components/`, `wwwroot/`, the CMS menu, `Cms/CmsAdminUrls.cs`, Docker or
  hosting code — or when explicitly asked for browser or end-to-end tests.
- **`e2e cms13` / `e2e cms12`** when only one CMS version is in question.
- **`--keep`** leaves the e2e stacks running, so repeated e2e runs skip the cold start. Say so in the
  report, since the stacks keep using memory (SQL Server runs emulated on Apple Silicon).

## Run

From the repository root:

```bash
.claude/skills/run-tests/run-tests.sh            # unit
.claude/skills/run-tests/run-tests.sh all        # unit + e2e on both CMS versions
.claude/skills/run-tests/run-tests.sh e2e cms12  # e2e on one CMS version
```

Run the e2e scopes with `run_in_background`, since they take minutes, and wait for the completion
notification rather than polling. Unit runs finish in well under a minute; run them in the foreground.

If the script exits 3 it stopped before running anything: Docker is not running, or the submodule is
missing. Report that and the fix it printed (start Docker Desktop; `git submodule update --init
--recursive`) — do not start Docker or change the checkout without the user asking.

## Report

The script ends with a table and a `RESULT:` line:

```
SUITE                    STATUS   PASSED FAILED SKIPPED  NOTE
unit / CMS 13 package    PASS        508      0       0
unit / CMS 12 package    PASS        508      0       0
RESULT: ALL PASSED
```

Report the table, the overall result, the branch and commit it printed (and whether there were
uncommitted changes), and where the results were written. Read statuses this way:

- **PASS** — every test that should have run, ran and passed.
- **FAIL with FAILED > 0** — real test failures, listed under `Failures:` with each assertion message.
- **FAIL, "did not build"** — a compile error, quoted in the note. No tests ran.
- **FAIL, "nothing ran: every test was skipped"** (e2e) — the browser tests never received a target.
- **A unit suite with SKIPPED > 0** — unexpected; mention it.

A failure that appears in **both** unit suites is in shared code. One in a single suite is
version-specific: look for `#if CMS12` in the code under test, and remember the CMS 12 suite runs the
CMS 13 suite's files.

## Investigate failures

Everything from a run is in the folder the script printed (also recorded in
`artifacts/test-run/latest`): a `.log` and a `.trx` per suite. Read the `.log` for the full output and
stack traces.

For an e2e failure, `tests/OptiPowerTools.ScheduledJobsInsights.E2E/artifacts/` holds a screenshot
(`<target>-<test>.png`) and a Playwright trace (`.trace.zip`) of each failed test. Look at the
screenshot first — it usually shows the cause outright. The trace opens at <https://trace.playwright.dev>.
If every e2e test failed after about 30 seconds each, the pages never went live (the tests wait for a
`data-sji-interactive` marker): suspect the hosting view or the Blazor wiring, not the individual tests.

Then say what failed and the likely cause, and offer a fix — but do not change code or tests unless
the user asks. Never make a failing test pass by weakening its assertion.

## Rules that keep the results honest

- **Never re-run with `--no-build`.** After a restore or build that failed, it re-runs the previous
  binaries and reports a pass for code that no longer compiles. The script never uses it.
- **Do not run the e2e project without `SJI_E2E_TARGET`** and call it a pass: every test skips.
  `scripts/e2e.sh` (which the script uses) sets it.
- **Read counts from the table, not by grepping console output.** The local `grep` may be ugrep, which
  treats some arguments differently from GNU grep and has silently produced wrong answers here.
- The e2e stacks are separate from the developer's own dev stacks (ports 5105/6005 and 5106/6006 vs
  5103/6003 and 5104/6004) and are removed after each run unless `--keep` is passed. Leave the dev
  stacks alone.
- CI (`.github/workflows/build.yml`) runs the unit suites and only *builds* the e2e project. When the
  user asks about CI status rather than a local run, check the GitHub Actions run for the branch
  instead (`gh run list --branch <branch>` if the GitHub CLI is installed; otherwise ask).

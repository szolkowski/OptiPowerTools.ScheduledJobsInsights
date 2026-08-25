# Security policy

## Reporting a vulnerability

Please report security issues **privately**, not as a public GitHub issue.

Use [GitHub's private vulnerability reporting](https://github.com/szolkowski/OptiPowerTools.ScheduledJobsInsights/security/advisories/new)
for this repository. If that is unavailable to you, open a public issue containing **no details** —
just a request for a private channel — and you will be contacted.

Please include the package version, the CMS version, and enough detail to reproduce. You will get an
acknowledgement within a few days; a fix or a decision follows in the release after triage.

## How a report is handled

So you know what to expect, and by when. This is a single-maintainer project, so these are honest
targets rather than a contractual SLA.

| Stage | Target |
|---|---|
| Acknowledgement that the report arrived | 3 working days |
| Triage: confirmed or declined, with reasoning | 10 working days |
| Fix released for a confirmed vulnerability | 30 days from triage, sooner where the severity warrants |

Confirmed vulnerabilities are published as a [GitHub Security Advisory](https://github.com/szolkowski/OptiPowerTools.ScheduledJobsInsights/security/advisories)
against this package, which is what makes them visible to `dotnet list package --vulnerable` and to
consumers' own auditing. A CVE is requested through GitHub as the CNA where the issue affects
consumers rather than only this repository's own tooling.

Disclosure is coordinated: the advisory is published together with the fixed release, and credit is
given to the reporter unless they ask otherwise. If a fix is going to miss the target above, you will
be told rather than left waiting. If a report is declined, you are free to disclose it publicly.

## Supported versions

The latest released 1.x version receives security fixes. Older minor versions do not — upgrade within
1.x is intended to be non-breaking (see [Versioning](README.md#versioning)).

## What this package handles, and why that matters

Worth knowing when assessing a report:

- **It persists job input data and exception stack traces.** `LogInputData` serialises whatever a job
  passes it, and unhandled exceptions are stored with their stack traces. Both are rendered in the
  admin UI. A job that logs credentials or personal data as "input" puts them in this database and on
  that page — treat the insights database with the same care as the data the jobs handle.
- **`AllowAnyAuthenticatedUser` grants access to every authenticated user.** On an Optimizely site with
  front-end membership that includes ordinary visitors, who could then read execution history and any
  captured input data. The option exists for installations already restricted by a proxy or a network
  boundary; the README flags it accordingly.
- **The retention screen performs destructive writes** — changing retention can cause history to be
  deleted by the next cleanup run. It is guarded by the same authorization policy as the pages, checked
  again on the circuit before each save.
- **The Blazor hub carries its own authorization.** A circuit outlives the request that authorized the
  page, so the hub is not left relying on that.

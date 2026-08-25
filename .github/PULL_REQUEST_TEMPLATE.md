## What this changes

<!-- One or two sentences. If it fixes an issue, link it. -->

## Why

<!-- The reasoning, not a restatement of the diff. This repository's comments explain *why* code is
     the way it is, and a PR description is where that explanation starts. -->

## How it was verified

<!-- Which tests cover it, and — for a bug fix — confirmation that the new test fails without the fix.
     A test that passes against the unfixed code is not evidence of anything. -->

## Checklist

- [ ] `dotnet build -c Release` is clean. Warnings are errors here.
- [ ] `dotnet test` passes.
- [ ] New or changed behaviour has a test, and a bug fix has one that fails without the fix.
- [ ] Public API unchanged — or, if changed, `PublicSurfaceTests` is updated deliberately and the
      change is explained above. 1.0 froze this surface, so additions are permanent and removals are
      breaking.
- [ ] Schema unchanged — or a migration is included, is `internal`, and `.github/sql/verify-schema.sql`
      asserts whatever about its shape matters.
- [ ] Documentation updated if behaviour, options or metric names changed. The README is packed into
      the NuGet package and cannot be corrected without a release.

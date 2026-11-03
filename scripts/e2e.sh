#!/usr/bin/env bash
#
# Runs the local end-to-end tests (tests/OptiPowerTools.ScheduledJobsInsights.E2E) against throwaway
# Docker stacks (docker-compose.e2e.yml): builds the current working tree into a stack, waits for it,
# runs the browser tests, and tears the stack down again.
#
#   scripts/e2e.sh                 both CMS versions, one after the other
#   scripts/e2e.sh cms13           only CMS 13 (web :5105, SQL Server :6005)
#   scripts/e2e.sh cms12           only CMS 12 (web :5106, SQL Server :6006)
#   scripts/e2e.sh cms12 --keep    leave the stack running afterwards; the next run reuses it
#                                  (rebuilt from the working tree) and the tests re-seed their data
#   scripts/e2e.sh --down          remove every e2e stack
#
# Extra arguments after `--` go to `dotnet test`, e.g. `scripts/e2e.sh cms13 -- --filter DetailPage`.
# SJI_E2E_HEADED=1 shows the browser. A failed test leaves a Playwright trace and a screenshot in
# tests/OptiPowerTools.ScheduledJobsInsights.E2E/artifacts/ - open a trace with
# `pwsh tests/OptiPowerTools.ScheduledJobsInsights.E2E/bin/host/Debug/net10.0/playwright.ps1 show-trace <file>`
# or drop it on https://trace.playwright.dev.
#
# The CMS 13 stack needs the Alloy submodule (git submodule update --init --recursive). Neither stack
# needs the Alloy *content*; the tests seed their own data.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COMPOSE=(docker compose -f "${REPO_ROOT}/docker-compose.e2e.yml")
TESTS="${REPO_ROOT}/tests/OptiPowerTools.ScheduledJobsInsights.E2E"

targets=()
keep=false
test_args=()
while [[ $# -gt 0 ]]; do
    case "$1" in
        cms13|cms12) targets+=("$1") ;;
        --keep) keep=true ;;
        --down)
            "${COMPOSE[@]}" --profile cms13 --profile cms12 down --volumes --remove-orphans
            exit 0
            ;;
        --) shift; test_args=("$@"); break ;;
        -h|--help) sed -n '2,23p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) echo "error: unknown argument '$1' (try --help)" >&2; exit 2 ;;
    esac
    shift
done
[[ ${#targets[@]} -gt 0 ]] || targets=(cms13 cms12)

echo "Building the e2e tests..."
dotnet build "${TESTS}" --nologo -v quiet

failed=()
for target in "${targets[@]}"; do
    echo
    echo "=== ${target}: starting the stack (built from the working tree) ==="
    # --wait returns once every container is healthy; the web healthcheck answers only after the CMS
    # has initialised. A cold start takes a few minutes where SQL Server runs emulated.
    "${COMPOSE[@]}" --profile "${target}" up --detach --build --wait

    echo "=== ${target}: running the tests ==="
    if SJI_E2E_TARGET="${target}" dotnet test "${TESTS}" --no-build --nologo "${test_args[@]+"${test_args[@]}"}"; then
        echo "=== ${target}: passed ==="
    else
        failed+=("${target}")
        echo "=== ${target}: FAILED - see tests/OptiPowerTools.ScheduledJobsInsights.E2E/artifacts/ ==="
    fi

    if [[ "${keep}" == false ]]; then
        "${COMPOSE[@]}" --profile "${target}" down --volumes --remove-orphans
    fi
done

echo
if [[ ${#failed[@]} -gt 0 ]]; then
    echo "Failed: ${failed[*]}"
    exit 1
fi
echo "All passed: ${targets[*]}"

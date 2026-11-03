#!/usr/bin/env bash
#
# Runs this repository's test suites and prints one status table. Used by the run-tests skill
# (.claude/skills/run-tests/SKILL.md); fine to run by hand too.
#
#   run-tests.sh                 unit suites: the CMS 13 build and the CMS 12 build (~30 s)
#   run-tests.sh e2e             browser tests on both CMS versions, in throwaway Docker stacks (~4 min)
#   run-tests.sh e2e cms12       browser tests on one CMS version
#   run-tests.sh all             unit, then e2e
#   ... --keep                   leave the e2e stacks running afterwards (faster re-runs)
#   ... --scenarios              e2e also re-runs under each other CMS menu placement and with the
#                                insights database unreachable (~1 extra minute per CMS version)
#
# Every run writes its logs and .trx results to artifacts/test-run/<UTC timestamp>/ (gitignored) and
# records that folder in artifacts/test-run/latest. Exit code: 0 when everything ran and passed.
#
# Deliberately never passes --no-build: after a restore that fails, --no-build happily re-runs the
# previous binaries and reports a pass for code that no longer builds.

set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
cd "${REPO_ROOT}"

mode=unit
targets=()
keep=false
scenarios=false
for arg in "$@"; do
    case "${arg}" in
        unit|e2e|all) mode="${arg}" ;;
        cms13|cms12) targets+=("${arg}") ;;
        --keep) keep=true ;;
        --scenarios) scenarios=true ;;
        -h|--help) sed -n '2,17p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) echo "error: unknown argument '${arg}' (try --help)" >&2; exit 2 ;;
    esac
done
[[ ${#targets[@]} -gt 0 ]] || targets=(cms13 cms12)

RUN_DIR="${REPO_ROOT}/artifacts/test-run/$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p "${RUN_DIR}"
echo "${RUN_DIR}" > "${REPO_ROOT}/artifacts/test-run/latest"

# suite label | trx file | log file | exit code | expectation ("unit" or "e2e")
suites=()

run_unit() {
    local label="$1" project="$2" name="$3"
    echo ">>> ${label}"
    dotnet test "${project}" --nologo \
        --logger "trx;LogFileName=${name}.trx" --results-directory "${RUN_DIR}" \
        > "${RUN_DIR}/${name}.log" 2>&1
    suites+=("${label}|${RUN_DIR}/${name}.trx|${RUN_DIR}/${name}.log|$?|unit")
}

run_e2e() {
    local target="$1" name="e2e-$1"
    echo ">>> e2e / ${target} (builds and starts a throwaway stack; a cold start takes a few minutes)"
    local extra=()
    [[ "${keep}" == true ]] && extra+=(--keep)
    [[ "${scenarios}" == true ]] && extra+=(--scenarios)
    # A prefix, not a file name: with --scenarios, e2e.sh runs the tests once per scenario, and each
    # run gets a .trx of its own (<prefix>_<timestamp>.trx); the summary adds them up.
    scripts/e2e.sh "${target}" "${extra[@]+"${extra[@]}"}" -- \
        --logger "trx;LogFilePrefix=${name}" --results-directory "${RUN_DIR}" \
        > "${RUN_DIR}/${name}.log" 2>&1
    suites+=("e2e / ${target}|${RUN_DIR}/${name}_*.trx|${RUN_DIR}/${name}.log|$?|e2e")
}

preflight_e2e() {
    if ! docker info > /dev/null 2>&1; then
        echo "error: Docker is not running - the e2e tests need it. Start Docker Desktop and retry." >&2
        exit 3
    fi
    if [[ " ${targets[*]} " == *" cms13 "* && ! -f sub/MyOptiAlloySite/MyOptiAlloySite/MyOptiAlloySite.csproj ]]; then
        echo "error: the CMS 13 e2e stack needs the Alloy submodule: git submodule update --init --recursive" >&2
        exit 3
    fi
}

echo "Results: ${RUN_DIR}"
echo "SDK: $(dotnet --version)    branch: $(git branch --show-current 2>/dev/null)    commit: $(git rev-parse --short HEAD 2>/dev/null)$(git diff --quiet 2>/dev/null || echo ' + uncommitted changes')"
echo

if [[ "${mode}" == unit || "${mode}" == all ]]; then
    run_unit "unit / CMS 13 package" tests/OptiPowerTools.ScheduledJobsInsights.Tests/OptiPowerTools.ScheduledJobsInsights.Tests.csproj unit-cms13
    run_unit "unit / CMS 12 package" tests/OptiPowerTools.ScheduledJobsInsights.Cms12.Tests/OptiPowerTools.ScheduledJobsInsights.Cms12.Tests.csproj unit-cms12
fi

if [[ "${mode}" == e2e || "${mode}" == all ]]; then
    preflight_e2e
    for target in "${targets[@]}"; do
        run_e2e "${target}"
    done
fi

echo
python3 - "${RUN_DIR}" "${suites[@]}" <<'PY'
import glob, os, re, sys, xml.etree.ElementTree as ET

run_dir, rows = sys.argv[1], sys.argv[2:]
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
failed_any = False
details = []

print(f"{'SUITE':<24} {'STATUS':<8} {'PASSED':>6} {'FAILED':>6} {'SKIPPED':>7}  NOTE")
for row in rows:
    label, trx, log, code, kind = row.split("|")
    note, status = "", "PASS"
    passed = failed = skipped = None

    files = sorted(glob.glob(trx)) if "*" in trx else ([trx] if os.path.exists(trx) else [])
    if files:
        passed = failed = skipped = 0
        empty_runs = 0
        for f in files:
            root = ET.parse(f).getroot()
            c = root.find("t:ResultSummary/t:Counters", ns).attrib
            passed += int(c["passed"]); failed += int(c["failed"])
            empty_runs += int(c["passed"]) + int(c["failed"]) == 0
            for r in root.iterfind(".//t:UnitTestResult[@outcome='Failed']", ns):
                message = r.findtext(".//t:ErrorInfo/t:Message", default="", namespaces=ns).strip()
                details.append((label, r.attrib["testName"], message.splitlines()[0] if message else ""))
            # A test skipped because it belongs to another e2e scenario is expected; any other skip is not.
            for r in root.iterfind(".//t:UnitTestResult[@outcome='NotExecuted']", ns):
                reason = (r.findtext(".//t:Output/t:StdOut", default="", namespaces=ns)
                          + r.findtext(".//t:Output/t:ErrorInfo/t:Message", default="", namespaces=ns))
                skipped += "scenario" not in reason
        if len(files) > 1:
            note = f"{len(files)} runs: standard + {len(files) - 1} scenarios"
        if failed:
            status = "FAIL"
        elif kind == "e2e" and empty_runs:
            # A run where everything skipped means SJI_E2E_TARGET or the scenario never reached the tests.
            status, note = "FAIL", f"nothing ran in {empty_runs} of {len(files)} runs: every test was skipped"
        elif kind == "unit" and skipped:
            note = f"{skipped} skipped - unit suites normally skip nothing"
    else:
        status = "FAIL"
        text = open(log, encoding="utf-8", errors="replace").read() if os.path.exists(log) else ""
        errors = sorted(set(re.findall(r"error [A-Z]+\d+: [^\[\n]+", text)))
        note = ("did not build: " + errors[0].strip()) if errors else f"no results (exit {code}) - see {os.path.basename(log)}"
        for e in errors[1:6]:
            details.append((label, "build", e.strip()))

    if status == "PASS" and code != "0":
        status, note = "FAIL", f"exit code {code} - see {os.path.basename(log)}"
    failed_any |= status != "PASS"
    fmt = lambda v: "-" if v is None else str(v)
    print(f"{label:<24} {status:<8} {fmt(passed):>6} {fmt(failed):>6} {fmt(skipped):>7}  {note}")

if details:
    print("\nFailures:")
    for label, test, message in details:
        print(f"  [{label}] {test}\n      {message}")

artifacts = "tests/OptiPowerTools.ScheduledJobsInsights.E2E/artifacts"
if any(label.startswith("e2e") for label, *_ in details) and os.path.isdir(artifacts):
    print(f"\ne2e traces and screenshots of the failures: {artifacts}/ (open a .trace.zip at https://trace.playwright.dev)")

print(f"\nLogs and .trx files: {run_dir}")
print("RESULT: " + ("FAILED" if failed_any else "ALL PASSED"))
sys.exit(1 if failed_any else 0)
PY

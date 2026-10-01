#!/usr/bin/env bash
#
# Puts the Alloy demo content the CMS 12 dev host imports on first start into place:
#   src/OptiPowerTools.ScheduledJobsInsights.Cms12.Web/App_Data/DefaultSiteContent.episerverdata
#
# The file is 9 MB of Optimizely sample content, so it is not committed (App_Data/ is gitignored).
# It ships inside Optimizely's own template package, EPiServer.Templates on nuget.org, which is where
# `dotnet new epi-alloy-mvc` copies it from; this script takes it from the same place, pinned to the
# version the host was generated from and checked against a known hash.
#
# Without it the CMS still starts and the Insights pages work, but there is no site: `/` returns 404.
# Optimizely only imports the file into an empty database, so after adding it to a host that has
# already started, drop the database (or the sjinsights12-sqldata volume) and start again.
#
# Usage, from anywhere:  Docker/cms12-fetch-site-content.sh

set -euo pipefail

VERSION="1.7.2"
SHA256="69d028cb5bd9dbfeeee9e2401da7bacdaceff2f70bf6e0628e7731ae6496ae5c"
URL="https://api.nuget.org/v3-flatcontainer/episerver.templates/${VERSION}/episerver.templates.${VERSION}.nupkg"
ENTRY="content/Alloy.Mvc/App_Data/DefaultSiteContent.episerverdata"

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TARGET="${REPO_ROOT}/src/OptiPowerTools.ScheduledJobsInsights.Cms12.Web/App_Data/DefaultSiteContent.episerverdata"

if [[ -f "${TARGET}" ]]; then
    echo "Already present: ${TARGET#"${REPO_ROOT}/"}"
    exit 0
fi

for tool in curl unzip; do
    command -v "${tool}" >/dev/null || { echo "error: ${tool} is required" >&2; exit 1; }
done

# shasum ships with macOS, sha256sum with most Linux distributions.
if command -v sha256sum >/dev/null; then
    hash_of() { sha256sum "$1" | cut -d' ' -f1; }
elif command -v shasum >/dev/null; then
    hash_of() { shasum -a 256 "$1" | cut -d' ' -f1; }
else
    echo "error: sha256sum or shasum is required" >&2
    exit 1
fi

WORK="$(mktemp -d)"
trap 'rm -rf "${WORK}"' EXIT

echo "Downloading EPiServer.Templates ${VERSION} from nuget.org..."
curl -fsSL -o "${WORK}/templates.nupkg" "${URL}"

ACTUAL="$(hash_of "${WORK}/templates.nupkg")"
if [[ "${ACTUAL}" != "${SHA256}" ]]; then
    echo "error: EPiServer.Templates ${VERSION} hash mismatch" >&2
    echo "  expected ${SHA256}" >&2
    echo "  actual   ${ACTUAL}" >&2
    exit 1
fi

mkdir -p "$(dirname "${TARGET}")"
# Extract to a temporary name and move into place, so an interrupted run never leaves a truncated
# file that the next run would mistake for a complete one.
unzip -p "${WORK}/templates.nupkg" "${ENTRY}" > "${TARGET}.partial"
mv "${TARGET}.partial" "${TARGET}"

echo "Wrote ${TARGET#"${REPO_ROOT}/"}"

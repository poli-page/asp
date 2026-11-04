#!/usr/bin/env bash
# scripts/check-release-versions.sh: a release's major.minor must be the
# pinned API contract's and the required PoliPage SDK's.
#
# Usage: scripts/check-release-versions.sh X.Y.Z [path/to/PoliPage.AspNetCore.X.Y.Z.nupkg]
#        (X.Y.Z may also be vX.Y.Z or a prerelease such as v0.13.1-rc1)
#
# The package shares its major.minor with the Poli Page API, and integration
# 0.N requires the PoliPage SDK 0.N.x. This script:
#   - fails unless the PoliPage SDK is required as [X.Y.Z,X.(Y+1).0) with the
#     release's major.minor (>= X.Y.Z and < X.(Y+1)), for every target
#     framework of the library, as MSBuild evaluates it (Central Package
#     Management in Directory.Packages.props, and no VersionOverride);
#   - with a .nupkg, fails unless its .nuspec declares that same range for
#     PoliPage in every dependency group (what NuGet users get);
#   - reads the contract commit pinned in .github/workflows/ci.yml (never its
#     `# vX.Y.Z` comment), requires .github/workflows/release.yml to pin the
#     same commit, lists the contract repository's tags, and fails unless that
#     commit carries a stable tag vX.Y.Z with the release's major.minor.
# Patch numbers are free.
#
# Run by the Release workflow. Needs the .NET SDK, jq and git, plus unzip and
# python3 with a .nupkg. API_CONTRACT_REMOTE overrides the contract repository
# (to check against a local one).

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$REPO_ROOT/src/PoliPage.AspNetCore/PoliPage.AspNetCore.csproj"
CI_WORKFLOW="$REPO_ROOT/.github/workflows/ci.yml"
RELEASE_WORKFLOW="$REPO_ROOT/.github/workflows/release.yml"
REMOTE="${API_CONTRACT_REMOTE:-https://github.com/poli-page/api-contract.git}"

fail() { echo "check-release-versions: $1" >&2; exit 1; }

# Keep the dotnet CLI's first-run banner out of the output parsed below.
export DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

[ "$#" -eq 1 ] || [ "$#" -eq 2 ] || fail "usage: $(basename "$0") X.Y.Z [package.nupkg]"
version="${1#v}"
nupkg="${2:-}"
if ! [[ "$version" =~ ^([0-9]+)\.([0-9]+)\.[0-9]+(-[A-Za-z0-9.-]+)?$ ]]; then
    fail "release version '$1' is not X.Y.Z"
fi
release_major="${BASH_REMATCH[1]}"
release_minor_number="${BASH_REMATCH[2]}"
release_minor="$release_major.$release_minor_number"
next_minor="$release_major.$((10#$release_minor_number + 1))"
wanted="[$release_minor.0,$next_minor.0)"

# A NuGet range is the SDK of the release's minor when it is
# [X.Y.Z,X.(Y+1).0) (or its X.(Y+1) spelling), spaces ignored.
check_range() { # check_range <range> <where>
    local range="${1// /}" where="$2" upper
    if ! [[ "$range" =~ ^\[([0-9]+)\.([0-9]+)\.[0-9]+,([0-9]+\.[0-9]+)(\.0)?\)$ ]]; then
        fail "release v$version needs PoliPage $wanted, but $where requires '$1' (not a [X.Y.Z,X.(Y+1).0) range)"
    fi
    upper="${BASH_REMATCH[3]}"
    local sdk_minor="${BASH_REMATCH[1]}.${BASH_REMATCH[2]}"
    if [ "$sdk_minor" != "$release_minor" ] || [ "$upper" != "$next_minor" ]; then
        fail "release v$version and the required PoliPage $1 ($where) differ in major.minor: require PoliPage $wanted, or release v$sdk_minor.x"
    fi
}

# The PoliPage requirement, as MSBuild evaluates it for each target framework.
frameworks="$(dotnet msbuild "$PROJECT" -getProperty:TargetFrameworks)" \
    || fail "cannot evaluate $PROJECT"
frameworks="$(tr -d '[:space:]' <<<"$frameworks")"
[ -n "$frameworks" ] || fail "$PROJECT has no TargetFrameworks"
IFS=';' read -r -a tfms <<<"$frameworks"
for tfm in "${tfms[@]}"; do
    [ -n "$tfm" ] || continue
    items="$(dotnet msbuild "$PROJECT" -p:TargetFramework="$tfm" \
        -getItem:PackageReference -getItem:PackageVersion)" \
        || fail "cannot evaluate $PROJECT for $tfm"
    references="$(jq '[.Items.PackageReference[]? | select(.Identity == "PoliPage")] | length' <<<"$items")"
    [ "$references" = "1" ] || fail "the library does not reference PoliPage once for $tfm ($references references)"
    override="$(jq -r '.Items.PackageReference[] | select(.Identity == "PoliPage") | (.VersionOverride // "") + (.Version // "")' <<<"$items")"
    [ -z "$override" ] || fail "the library's PoliPage reference sets its own version '$override' for $tfm: require it in Directory.Packages.props only"
    range="$(jq -r '[.Items.PackageVersion[]? | select(.Identity == "PoliPage") | .Version] | if length == 1 then .[0] else "" end' <<<"$items")"
    [ -n "$range" ] || fail "Directory.Packages.props does not set one PoliPage version for $tfm"
    check_range "$range" "Directory.Packages.props, $tfm"
done
echo "PoliPage $wanted is required for ${tfms[*]}, matching release v$version"

if [ -n "$nupkg" ]; then
    [ -f "$nupkg" ] || fail "no package at $nupkg"
    nuspec="$(unzip -p "$nupkg" '*.nuspec')" || fail "cannot read the .nuspec of $nupkg"
    # One line per dependency group: "<targetFramework> <PoliPage range>", the
    # range "-" when the group has no PoliPage dependency.
    groups="$(
        python3 -I -c '
import sys
import xml.etree.ElementTree as ET
root = ET.fromstring(sys.stdin.buffer.read())
def local(tag):
    return tag.rsplit("}", 1)[-1]
for deps in (e for e in root.iter() if local(e.tag) == "dependencies"):
    for group in (g for g in deps if local(g.tag) == "group"):
        ranges = [d.get("version", "") for d in group
                  if local(d.tag) == "dependency" and d.get("id", "").lower() == "polipage"]
        print(group.get("targetFramework", "(any)"), ranges[0].replace(" ", "") if len(ranges) == 1 else "-")
' <<<"$nuspec"
    )" || fail "cannot parse the .nuspec of $nupkg"
    [ -n "$groups" ] || fail "$nupkg declares no dependency groups"
    while IFS=' ' read -r tfm range; do
        [ "$range" != "-" ] || fail "$nupkg does not depend on PoliPage once for $tfm"
        check_range "$range" "$(basename "$nupkg"), $tfm"
    done <<<"$groups"
    echo "$(basename "$nupkg") depends on PoliPage $wanted in every group"
fi

pinned_sha() { # the poli-page/api-contract commit a workflow pins
    sed -n 's|.*uses: poli-page/api-contract@\([0-9a-f]\{40\}\).*|\1|p' "$1" | sort -u
}
contract_sha="$(pinned_sha "$CI_WORKFLOW")"
[ -n "$contract_sha" ] || fail "no poli-page/api-contract pin found in .github/workflows/ci.yml"
[ "$(wc -l <<<"$contract_sha")" -eq 1 ] || fail ".github/workflows/ci.yml pins more than one poli-page/api-contract commit"
release_sha="$(pinned_sha "$RELEASE_WORKFLOW")"
if [ "$release_sha" != "$contract_sha" ]; then
    fail ".github/workflows/ci.yml and release.yml pin different poli-page/api-contract commits: pin the same commit in both"
fi

tags="$(git ls-remote --tags "$REMOTE")" || fail "cannot list the tags of $REMOTE"

# An annotated tag is listed twice: its own object, then `^{}` with the commit
# it points at. A lightweight tag is listed once, with its commit.
stable_tags="$(
    awk -v sha="$contract_sha" '$1 == sha { ref = $2; sub(/^refs\/tags\//, "", ref); sub(/\^\{\}$/, "", ref); print ref }' <<<"$tags" \
        | grep -E '^v[0-9]+\.[0-9]+\.[0-9]+$' | sort -u -V || true
)"
short_sha="${contract_sha:0:8}"
if [ -z "$stable_tags" ]; then
    fail "release v$version needs the API contract v$release_minor.x, but the pinned contract commit $short_sha has no stable vX.Y.Z tag: pin a released contract"
fi

while IFS= read -r tag; do
    if [[ "$tag" =~ ^v([0-9]+)\.([0-9]+)\. ]] && [ "${BASH_REMATCH[1]}.${BASH_REMATCH[2]}" = "$release_minor" ]; then
        echo "API contract $tag ($short_sha) matches release v$version"
        exit 0
    fi
done <<<"$stable_tags"

pinned="$(tr '\n' ' ' <<<"$stable_tags" | sed 's/ $//')"
# The newest of the commit's tags, which may carry two (an unchanged contract).
[[ "$(tail -n 1 <<<"$stable_tags")" =~ ^v([0-9]+)\.([0-9]+)\. ]]
fail "release v$version and the pinned API contract $pinned ($short_sha) differ in major.minor: pin the contract v$release_minor.x, or release v${BASH_REMATCH[1]}.${BASH_REMATCH[2]}.x"

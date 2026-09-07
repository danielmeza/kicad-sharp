#!/usr/bin/env bash
#
# Builds this repository against a local checkout of danielmeza/sexpressions instead of the
# published SExpressions package.
#
#   scripts/use-local-libs.sh [path-to-sexpressions-repo]      (default: ../sexpressions)
#
# It packs SExpressions with a distinct local version into ./local-packages (a git-ignored folder
# already registered as a package source in NuGet.config) and prints the build command that selects
# it. There is deliberately no ProjectReference swap: consuming the real .nupkg is what proves the
# package works, which is the thing that actually breaks.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SEXPR_REPO="$(cd "${1:-$REPO_ROOT/../sexpressions}" 2>/dev/null && pwd || true)"

if [[ -z "$SEXPR_REPO" || ! -f "$SEXPR_REPO/src/SExpressions/SExpressions.csproj" ]]; then
    echo "error: no SExpressions checkout at '${1:-$REPO_ROOT/../sexpressions}'." >&2
    echo "       usage: scripts/use-local-libs.sh [path-to-sexpressions-repo]" >&2
    exit 2
fi

# A distinct version, so restore can never silently fall back to (or prefer) the published package.
LOCAL_VERSION="0.1.0-local.$(date -u +%Y%m%d%H%M%S)"
FEED="$REPO_ROOT/local-packages"
mkdir -p "$FEED"

echo "Packing SExpressions $LOCAL_VERSION from $SEXPR_REPO ..."
dotnet pack "$SEXPR_REPO/src/SExpressions/SExpressions.csproj" \
    -c Release -p:Version="$LOCAL_VERSION" -o "$FEED" >/dev/null

cat <<MSG

Packed into $FEED

Build against it with:

    dotnet build KiCadSharp.slnx -c Release -p:SExpressionsVersion=$LOCAL_VERSION

To pack as well, stamp this repository's own packages with the same prerelease version:

    dotnet pack KiCadSharp.slnx -c Release -p:SExpressionsVersion=$LOCAL_VERSION -p:Version=$LOCAL_VERSION

Both properties are needed for pack, and that is NuGet being right rather than a workaround: a
stable 0.1.0 KiCadSharp may not depend on a prerelease SExpressions (NU5104), so while the
dependency is a local prerelease, these packages have to be prereleases too.

Omit the properties to go back to the published package. Nothing is committed: local-packages/ is
git-ignored and the default version in Directory.Build.props is unchanged.
MSG

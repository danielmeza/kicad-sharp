#!/usr/bin/env bash
#
# Re-fetches the vendored KiCad .proto definitions from the pinned commit.
#
#   scripts/sync-protos.sh           refresh protos/ from the pin
#   scripts/sync-protos.sh --check   verify protos/ still matches the pin (exit 1 if not)
#
# The pin (protos/KICAD_PIN) is a commit, KICAD_COMMIT, plus the tag or branch it came from,
# KICAD_REF. The commit is what gets fetched and compared, so a branch pin cannot float: the branch
# moving on upstream changes nothing here until someone edits KICAD_COMMIT. KICAD_REF is checked
# too: a tag that no longer resolves to the pinned commit is an error (an upstream tag was moved),
# a branch that has moved on is reported so the drift is visible.
#
# Why vendored instead of a submodule: KiCadSharp.Protos needs 15 files totalling ~100 KB. Getting
# them through a submodule costs a 1.4 GB checkout of the whole KiCad source tree on every clone and
# every CI run (measured, `git clone --depth 1 --branch 10.0.6`). The files change only when KiCad
# adds to its API, and --check makes the pin enforceable, so vendoring loses nothing and the build
# works offline.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PIN_FILE="$REPO_ROOT/protos/KICAD_PIN"
DEST="$REPO_ROOT/protos"
UPSTREAM="https://gitlab.com/kicad/code/kicad.git"

# shellcheck source=/dev/null
KICAD_REF=""; KICAD_COMMIT=""
source <(grep -E '^(KICAD_REF|KICAD_COMMIT)=' "$PIN_FILE")
if [[ -z "$KICAD_REF" || -z "$KICAD_COMMIT" ]]; then
    echo "error: KICAD_REF or KICAD_COMMIT missing from $PIN_FILE" >&2
    exit 2
fi
if [[ ! "$KICAD_COMMIT" =~ ^[0-9a-f]{40}$ ]]; then
    echo "error: KICAD_COMMIT must be a full 40-character commit hash, got '$KICAD_COMMIT'" >&2
    exit 2
fi

CHECK=0
[[ "${1:-}" == "--check" ]] && CHECK=1

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# Where does KICAD_REF point upstream right now? A tag lists twice when annotated (the tag object
# and, under ^{}, the commit it peels to); the peeled line is the one that matters.
echo "Resolving KiCad ref $KICAD_REF ..."
REFS="$(git ls-remote "$UPSTREAM" "refs/tags/$KICAD_REF" "refs/tags/$KICAD_REF^{}" "refs/heads/$KICAD_REF")"
TAG_COMMIT="$(awk '$2 == "refs/tags/'"$KICAD_REF"'^{}" { print $1 }' <<<"$REFS")"
[[ -z "$TAG_COMMIT" ]] && TAG_COMMIT="$(awk '$2 == "refs/tags/'"$KICAD_REF"'" { print $1 }' <<<"$REFS")"
BRANCH_COMMIT="$(awk '$2 == "refs/heads/'"$KICAD_REF"'" { print $1 }' <<<"$REFS")"

if [[ -n "$TAG_COMMIT" ]]; then
    if [[ "$TAG_COMMIT" != "$KICAD_COMMIT" ]]; then
        echo "error: tag $KICAD_REF now points at $TAG_COMMIT, but the pin records $KICAD_COMMIT." >&2
        echo "       An upstream tag was moved. Do not sync until that is understood." >&2
        exit 1
    fi
    echo "Tag $KICAD_REF resolves to the pinned commit."
elif [[ -n "$BRANCH_COMMIT" ]]; then
    if [[ "$BRANCH_COMMIT" != "$KICAD_COMMIT" ]]; then
        echo "note: branch $KICAD_REF is now at $BRANCH_COMMIT; the pin stays at $KICAD_COMMIT."
        echo "      To follow the branch, edit KICAD_COMMIT in $PIN_FILE and re-run this script."
    else
        echo "Branch $KICAD_REF is at the pinned commit."
    fi
else
    echo "error: KiCad has no tag or branch named '$KICAD_REF'." >&2
    exit 1
fi

echo "Fetching KiCad api/proto at $KICAD_COMMIT ..."
# A blobless, sparse, depth-1 fetch of the one commit: this pulls ~100 KB of .proto and nothing else.
# GitLab serves a fetch by commit hash, which is what makes a commit pin (rather than a ref) work.
git init --quiet "$WORK/kicad"
git -C "$WORK/kicad" remote add origin "$UPSTREAM"
git -C "$WORK/kicad" sparse-checkout set api/proto
git -C "$WORK/kicad" fetch --quiet --depth 1 --filter=blob:none origin "$KICAD_COMMIT"
git -C "$WORK/kicad" checkout --quiet --detach FETCH_HEAD

ACTUAL_COMMIT="$(git -C "$WORK/kicad" rev-parse HEAD)"
if [[ "$ACTUAL_COMMIT" != "$KICAD_COMMIT" ]]; then
    echo "error: fetched $ACTUAL_COMMIT, but the pin records $KICAD_COMMIT." >&2
    exit 1
fi

if [[ "$CHECK" == "1" ]]; then
    if diff -r --brief "$WORK/kicad/api/proto" "$DEST" --exclude=KICAD_PIN; then
        echo "OK: vendored protos match KiCad $KICAD_REF ($KICAD_COMMIT)."
    else
        echo "error: vendored protos differ from KiCad $KICAD_REF ($KICAD_COMMIT). Run scripts/sync-protos.sh and commit." >&2
        exit 1
    fi
    exit 0
fi

# Replace rather than merge, so a file deleted upstream disappears here too.
find "$DEST" -name '*.proto' -delete
find "$DEST" -type d -empty -delete
cp -r "$WORK/kicad/api/proto/." "$DEST/"
echo "Synced $(find "$DEST" -name '*.proto' | wc -l) .proto files from KiCad $KICAD_REF ($KICAD_COMMIT)."

#!/usr/bin/env bash
#
# Re-fetches the vendored KiCad .proto definitions from the pinned release tag.
#
#   scripts/sync-protos.sh           refresh protos/ from the pin
#   scripts/sync-protos.sh --check   verify protos/ still matches the pin (exit 1 if not)
#
# Why vendored instead of a submodule: KiCadSharp.Protos needs 12 files totalling 128 KB. Getting
# them through a submodule costs a 1.4 GB checkout of the whole KiCad source tree on every clone and
# every CI run (measured, `git clone --depth 1 --branch 10.0.6`). The files change only when KiCad
# cuts a release, and --check makes the pin enforceable, so vendoring loses nothing and the build
# works offline.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PIN_FILE="$REPO_ROOT/protos/KICAD_PIN"
DEST="$REPO_ROOT/protos"
UPSTREAM="https://gitlab.com/kicad/code/kicad"

# shellcheck source=/dev/null
KICAD_TAG=""; KICAD_COMMIT=""
source <(grep -E '^(KICAD_TAG|KICAD_COMMIT)=' "$PIN_FILE")
if [[ -z "$KICAD_TAG" || -z "$KICAD_COMMIT" ]]; then
    echo "error: KICAD_TAG or KICAD_COMMIT missing from $PIN_FILE" >&2
    exit 2
fi

CHECK=0
[[ "${1:-}" == "--check" ]] && CHECK=1

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

echo "Fetching KiCad api/proto at tag $KICAD_TAG ..."
# A blobless, sparse, depth-1 clone: this pulls the 128 KB of .proto and nothing else.
git clone --quiet --filter=blob:none --sparse --depth 1 --branch "$KICAD_TAG" "$UPSTREAM" "$WORK/kicad"
git -C "$WORK/kicad" sparse-checkout set api/proto

ACTUAL_COMMIT="$(git -C "$WORK/kicad" rev-parse HEAD)"
if [[ "$ACTUAL_COMMIT" != "$KICAD_COMMIT" ]]; then
    echo "error: tag $KICAD_TAG now points at $ACTUAL_COMMIT, but the pin records $KICAD_COMMIT." >&2
    echo "       An upstream tag was moved. Do not sync until that is understood." >&2
    exit 1
fi

if [[ "$CHECK" == "1" ]]; then
    if diff -r --brief "$WORK/kicad/api/proto" "$DEST" --exclude=KICAD_PIN; then
        echo "OK: vendored protos match KiCad $KICAD_TAG ($KICAD_COMMIT)."
    else
        echo "error: vendored protos differ from KiCad $KICAD_TAG. Run scripts/sync-protos.sh and commit." >&2
        exit 1
    fi
    exit 0
fi

# Replace rather than merge, so a file deleted upstream disappears here too.
find "$DEST" -name '*.proto' -delete
find "$DEST" -type d -empty -delete
cp -r "$WORK/kicad/api/proto/." "$DEST/"
echo "Synced $(find "$DEST" -name '*.proto' | wc -l) .proto files from KiCad $KICAD_TAG ($KICAD_COMMIT)."

#!/usr/bin/env bash
#
# Builds libnng for one of the two runtime identifiers nng.NET does not publish, from the nng release
# native/NNG_PIN pins, and checks what it built.
#
#   scripts/build-nng.sh <rid>           build it, and write native/<rid>/<file> and its SHA-256 in the pin
#   scripts/build-nng.sh <rid> --check   build it, and fail unless it is byte for byte the committed file
#
#   osx-arm64   on an Apple silicon Mac, with Xcode's command-line tools and CMake
#   win-arm64   on Windows on Arm, from Git Bash, with Visual Studio 2022's C++ tools and CMake
#
# The nng workflow (.github/workflows/nng.yml) runs --check for both, on GitHub's macos-14 and
# windows-11-arm runners, and uploads what it built.
#
# The flags are the ones the other six libraries in the package were built with. nng.NET's
# scripts/build_nng.ps1 and dockerfiles/build_nng/Dockerfile (jeikabu/nng.NETCore) say:
#
#   -DBUILD_SHARED_LIBS=ON -DCMAKE_BUILD_TYPE=Release -DNNG_ELIDE_DEPRECATED=ON -DNNG_TESTS=OFF -DNNG_TOOLS=OFF
#
# with one correction, taken from the binaries rather than the script: NNG_ELIDE_DEPRECATED is off.
# nng 1.3.2 has no such option, and nng.NET's Windows nng.dll came from an nng snapshot whose CMake
# never passed it to the compiler (nng fixed that in 44b8e23f), so all six export nng's deprecated
# nng_getopt/nng_setopt family. Built from the 1.4.0 tag with the flag on, win-arm64 exported 427
# nng functions where win-x64 and win-x86 export 497; with it off, it exports the same 497. KiCadSharp
# calls none of the 70, and a platform should not be the one where a function is missing.
#
# Beyond that, only what the platform needs:
#   osx-arm64   -DCMAKE_OSX_ARCHITECTURES=arm64, and a deployment target of 11.0, the first macOS on
#               Apple silicon. Left unset it would be the build machine's macOS, and the library would
#               not load on anything older.
#   win-arm64   -A ARM64, and /Brepro for the compiler and the linker. /Brepro changes no code: it
#               replaces the link time stamped into the DLL with a hash of its content, without which
#               no two builds could be compared byte for byte.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PIN_FILE="$REPO_ROOT/native/NNG_PIN"
UPSTREAM="https://github.com/nanomsg/nng"

RID="${1:-}"
CHECK=0
case "${2:-}" in
    "") ;;
    --check) CHECK=1 ;;
    *) echo "usage: $0 <osx-arm64|win-arm64> [--check]" >&2; exit 2 ;;
esac

read -r _ TAG COMMIT FILE PINNED_SHA < <(awk -v rid="$RID" '$1 == rid' "$PIN_FILE") || true
if [[ -z "$RID" || -z "${TAG:-}" || -z "${COMMIT:-}" || -z "${FILE:-}" ]]; then
    echo "error: '$RID' has no line in $PIN_FILE; it names the runtime identifiers built here" >&2
    exit 2
fi

case "$RID" in
    osx-arm64)
        if [[ "$(uname -s)" != Darwin || "$(uname -m)" != arm64 ]]; then
            echo "error: $RID is built on an Apple silicon Mac; this is $(uname -s) $(uname -m)" >&2
            exit 2
        fi
        ;;
    win-arm64)
        if [[ "${OS:-}" != Windows_NT ]]; then
            echo "error: $RID is built on Windows, from Git Bash; this is $(uname -s)" >&2
            exit 2
        fi
        ;;
    *)
        echo "error: this script knows how to build osx-arm64 and win-arm64, not $RID" >&2
        exit 2
        ;;
esac

sha256() {
    if command -v sha256sum >/dev/null 2>&1; then
        sha256sum "$1" | cut -d' ' -f1
    else
        shasum -a 256 "$1" | cut -d' ' -f1
    fi
}

# A fixed place under the git-ignored artifacts/, rather than a fresh temporary directory, so that
# every build compiles the same paths.
WORK="$REPO_ROOT/artifacts/nng/$RID"
rm -rf "$WORK"
mkdir -p "$WORK/out"

echo "Fetching nng $TAG ..."
git -c advice.detachedHead=false clone --quiet --depth 1 --branch "$TAG" "$UPSTREAM" "$WORK/src"
ACTUAL_COMMIT="$(git -C "$WORK/src" rev-parse HEAD)"
if [[ "$ACTUAL_COMMIT" != "$COMMIT" ]]; then
    echo "error: nng's tag $TAG now points at $ACTUAL_COMMIT, but native/NNG_PIN records $COMMIT." >&2
    echo "       An upstream tag was moved. Do not build from it until that is understood." >&2
    exit 1
fi

FLAGS=(-DBUILD_SHARED_LIBS=ON -DNNG_TESTS=OFF -DNNG_TOOLS=OFF)

echo "Building nng $TAG ($COMMIT) for $RID with $(cmake --version | head -n 1) ..."
case "$RID" in
    osx-arm64)
        echo "$(xcrun clang --version | head -n 1), SDK $(xcrun --show-sdk-version)"
        cmake -S "$WORK/src" -B "$WORK/build" -G "Unix Makefiles" \
            -DCMAKE_BUILD_TYPE=Release "${FLAGS[@]}" \
            -DCMAKE_OSX_ARCHITECTURES=arm64 -DCMAKE_OSX_DEPLOYMENT_TARGET=11.0
        cmake --build "$WORK/build" --parallel
        BUILT="$WORK/build/libnng.dylib"
        ;;
    win-arm64)
        # -Brepro, not /Brepro: Git Bash would rewrite an argument that starts with a slash into a
        # Windows path. MSVC takes either spelling.
        cmake -S "$WORK/src" -B "$WORK/build" -G "Visual Studio 17 2022" -A ARM64 \
            "${FLAGS[@]}" -DNNG_ELIDE_DEPRECATED=OFF \
            -DCMAKE_C_FLAGS_INIT=-Brepro -DCMAKE_SHARED_LINKER_FLAGS_INIT=-Brepro
        cmake --build "$WORK/build" --config Release
        BUILT="$WORK/build/Release/nng.dll"
        ;;
esac

# libnng.dylib is a symlink to libnng.1.<version>.dylib; -L copies the library, not the link.
cp -L "$BUILT" "$WORK/out/$FILE"
OUT="$WORK/out/$FILE"
"$REPO_ROOT/scripts/check-natives.sh" --file "$OUT" "$RID"

case "$RID" in
    osx-arm64)
        otool -L "$OUT"
        otool -l "$OUT" | grep -A4 -E 'LC_BUILD_VERSION|LC_ID_DYLIB' || true
        codesign --verify --verbose "$OUT"
        ;;
esac

BUILT_SHA="$(sha256 "$OUT")"
COMMITTED="$REPO_ROOT/native/$RID/$FILE"

if [[ "$CHECK" == 1 ]]; then
    failed=0
    if [[ ! -f "$COMMITTED" ]]; then
        echo "error: there is no native/$RID/$FILE to compare the build with." >&2
        failed=1
    elif ! cmp "$OUT" "$COMMITTED"; then
        echo "error: the build is not native/$RID/$FILE: $BUILT_SHA against $(sha256 "$COMMITTED")." >&2
        failed=1
    fi
    if [[ "$BUILT_SHA" != "$PINNED_SHA" ]]; then
        echo "error: the build's SHA-256 is $BUILT_SHA; native/NNG_PIN records $PINNED_SHA." >&2
        failed=1
    fi
    if [[ "$failed" != 0 ]]; then
        echo "The build is in $OUT." >&2
        exit 1
    fi
    echo "OK: nng $TAG for $RID rebuilds byte for byte as native/$RID/$FILE ($BUILT_SHA)."
    exit 0
fi

mkdir -p "$(dirname "$COMMITTED")"
cp "$OUT" "$COMMITTED"
awk -v rid="$RID" -v sha="$BUILT_SHA" '$1 == rid { sub(/[0-9a-f]+$/, sha) } { print }' "$PIN_FILE" > "$WORK/NNG_PIN"
cp "$WORK/NNG_PIN" "$PIN_FILE"
echo "Wrote native/$RID/$FILE ($BUILT_SHA) and its SHA-256 in native/NNG_PIN."

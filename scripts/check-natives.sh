#!/usr/bin/env bash
#
# Checks the native libnng that KiCadSharp ships: that the package carries one for every runtime
# identifier the README names and for no other, and that each file is a shared library built for
# its runtime identifier's architecture.
#
#   scripts/check-natives.sh <KiCadSharp.nupkg>    the package: the exact set of runtimes/<rid>/native
#                                                   files, each one's architecture, and the pinned
#                                                   SHA-256 of the two built here (native/NNG_PIN)
#   scripts/check-natives.sh --file <path> <rid>   one file against one runtime identifier
#
# The architecture comes out of each file's own header (the ELF, Mach-O or PE machine field), read
# with od. That needs neither `file` nor a platform toolchain, so the same check runs on Linux, on
# macOS and in Git Bash on Windows.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PIN_FILE="$REPO_ROOT/native/NNG_PIN"

# Every runtime identifier the package carries libnng for, the file, and what its header must say.
# README.md ("Platforms") and docs/ipc.md name the same eight. The first six come from the nng.NET
# package, the last two are built from nng's source here.
EXPECTED="
linux-x64    libnng.so     ELF 64-bit x86-64
linux-arm64  libnng.so     ELF 64-bit arm64
linux-arm    libnng.so     ELF 32-bit arm
osx-x64      libnng.dylib  Mach-O 64-bit x86-64
win-x64      nng.dll       PE32+ x86-64
win-x86      nng.dll       PE32 x86
osx-arm64    libnng.dylib  Mach-O 64-bit arm64
win-arm64    nng.dll       PE32+ arm64
"

# The unsigned little-endian integer of $3 bytes at offset $2 of file $1.
le() {
    local value=0 shift=0 byte
    for byte in $(od -An -tu1 -v -j "$2" -N "$3" "$1"); do
        value=$(( value | (byte << shift) ))
        shift=$(( shift + 8 ))
    done
    echo "$value"
}

hex() {
    od -An -tx1 -v -j "$2" -N "$3" "$1" | tr -d ' \n'
}

# What a file's header says it is, as "<format> <architecture>" in the words of $EXPECTED, or why it
# is not a shared library at all.
describe() {
    local f="$1" magic
    magic="$(hex "$f" 0 4)"
    case "$magic" in
        7f454c46)
            local class type machine bits arch
            class="$(le "$f" 4 1)"; type="$(le "$f" 16 2)"; machine="$(le "$f" 18 2)"
            [[ "$type" == 3 ]] || { echo "ELF, but not a shared object (e_type $type)"; return; }
            case "$class" in 1) bits=32 ;; 2) bits=64 ;; *) bits="class $class" ;; esac
            case "$machine" in 62) arch=x86-64 ;; 183) arch=arm64 ;; 40) arch=arm ;; 3) arch=x86 ;; *) arch="e_machine $machine" ;; esac
            echo "ELF ${bits}-bit $arch"
            ;;
        cffaedfe)
            local cpu filetype arch
            cpu="$(le "$f" 4 4)"; filetype="$(le "$f" 12 4)"
            [[ "$filetype" == 6 ]] || { echo "Mach-O, but not a dylib (filetype $filetype)"; return; }
            case "$cpu" in 16777223) arch=x86-64 ;; 16777228) arch=arm64 ;; *) arch="cputype $cpu" ;; esac
            echo "Mach-O 64-bit $arch"
            ;;
        cafebabe)
            echo "a universal (fat) Mach-O, not a single-architecture dylib"
            ;;
        4d5a*)
            local pe machine characteristics optional kind arch
            pe="$(le "$f" 60 4)"
            [[ "$(hex "$f" "$pe" 4)" == 50450000 ]] || { echo "an MZ executable with no PE header"; return; }
            machine="$(le "$f" $(( pe + 4 )) 2)"
            characteristics="$(le "$f" $(( pe + 22 )) 2)"
            optional="$(le "$f" $(( pe + 24 )) 2)"
            (( characteristics & 0x2000 )) || { echo "PE, but not a DLL"; return; }
            case "$optional" in 267) kind=PE32 ;; 523) kind=PE32+ ;; *) kind="PE (optional header $optional)" ;; esac
            case "$machine" in 34404) arch=x86-64 ;; 332) arch=x86 ;; 43620) arch=arm64 ;; *) arch="machine $machine" ;; esac
            echo "$kind $arch"
            ;;
        *)
            echo "not a shared library (magic $magic)"
            ;;
    esac
}

sha256() {
    if command -v sha256sum >/dev/null 2>&1; then
        sha256sum "$1" | cut -d' ' -f1
    else
        shasum -a 256 "$1" | cut -d' ' -f1
    fi
}

# Checks one file against one runtime identifier's line in $EXPECTED.
check_file() {
    local path="$1" rid="$2" line file want got
    line="$(awk -v rid="$rid" '$1 == rid' <<<"$EXPECTED")"
    if [[ -z "$line" ]]; then
        echo "error: KiCadSharp ships no libnng for $rid" >&2
        return 1
    fi
    file="$(awk '{ print $2 }' <<<"$line")"
    want="$(awk '{ $1 = ""; $2 = ""; sub(/^ +/, ""); print }' <<<"$line")"

    if [[ "$(basename "$path")" != "$file" ]]; then
        echo "error: $rid: expected $file, got $(basename "$path")" >&2
        return 1
    fi
    got="$(describe "$path")"
    if [[ "$got" != "$want" ]]; then
        echo "error: $rid: $path is $got; it must be $want" >&2
        return 1
    fi
    echo "ok  $rid  $file  $got  $(wc -c < "$path" | tr -d ' ') bytes  sha256 $(sha256 "$path")"
}

if [[ "${1:-}" == "--file" ]]; then
    [[ $# == 3 ]] || { echo "usage: $0 --file <path> <rid>" >&2; exit 2; }
    check_file "$2" "$3"
    exit
fi

[[ $# == 1 && -f "$1" ]] || { echo "usage: $0 <KiCadSharp.nupkg> | --file <path> <rid>" >&2; exit 2; }
NUPKG="$1"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

echo "Native libraries in $(basename "$NUPKG"):"
unzip -l "$NUPKG" 'runtimes/*'

failed=0
listed="$(unzip -Z1 "$NUPKG" 'runtimes/*' 2>/dev/null || true)"

# Nothing under runtimes/ that is not one of the eight, such as runtimes/any/, which is nng.NET's
# managed assembly and must never come along.
while IFS= read -r entry; do
    [[ -z "$entry" || "$entry" == */ ]] && continue
    rid="$(cut -d/ -f2 <<<"$entry")"
    file="$(awk -v rid="$rid" '$1 == rid { print $2 }' <<<"$EXPECTED")"
    if [[ "$entry" != "runtimes/$rid/native/$file" || -z "$file" ]]; then
        echo "error: the package carries $entry, which is not one of the libraries it should ship" >&2
        failed=1
    fi
done <<<"$listed"

unzip -q "$NUPKG" 'runtimes/*' -d "$WORK"

while read -r rid file _; do
    [[ -z "$rid" ]] && continue
    path="$WORK/runtimes/$rid/native/$file"
    if [[ ! -f "$path" ]]; then
        echo "error: the package has no runtimes/$rid/native/$file" >&2
        failed=1
        continue
    fi
    check_file "$path" "$rid" || failed=1
done <<<"$EXPECTED"

# The two built here must be the exact files native/NNG_PIN records, not merely the right shape.
while read -r rid _tag _commit file pinned; do
    [[ -z "$rid" || "$rid" == \#* ]] && continue
    path="$WORK/runtimes/$rid/native/$file"
    [[ -f "$path" ]] || continue
    actual="$(sha256 "$path")"
    if [[ "$actual" != "$pinned" ]]; then
        echo "error: runtimes/$rid/native/$file has SHA-256 $actual; native/NNG_PIN records $pinned" >&2
        failed=1
    fi
done < "$PIN_FILE"

if [[ "$failed" != 0 ]]; then
    exit 1
fi
echo "OK: $(grep -c . <<<"$EXPECTED") runtime identifiers, each with its libnng built for its own architecture."

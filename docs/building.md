# Building

## Building

```
dotnet build KiCadSharp.slnx -c Release
scripts/sync-protos.sh --check
```

### Developing against local SExpressions sources

`KiCadSharp` and `KiCadSharp.Cli` consume `SExpressions` as a `PackageReference`, not a project
reference or a submodule. To build against an unreleased local checkout of it:

```
scripts/use-local-libs.sh ../sexpressions
dotnet build KiCadSharp.slnx -c Release -p:SExpressionsVersion=0.2.0-local.<stamp>

# to pack too, stamp this repo's own packages with the same prerelease version:
dotnet pack KiCadSharp.slnx -c Release -p:SExpressionsVersion=0.2.0-local.<stamp> -p:Version=0.2.0-local.<stamp>
```

`pack` needs both properties, and that is NuGet being right rather than a workaround: a stable
`KiCadSharp` may not declare a prerelease `SExpressions` dependency (NU5104). While the dependency
is a local prerelease, these packages are prereleases too.

The script packs `SExpressions` at a distinct `-local.<timestamp>` version into `local-packages/`, a
git-ignored folder already registered as a package source in `NuGet.config`. The distinct version is
the point: restore can never silently fall back to, or prefer, the published package. Omit the
property to go back to the released one.

There is deliberately no "swap to `ProjectReference`" switch. Consuming the real `.nupkg` is what
proves the package works, and the package is the thing that actually breaks.

### The two libnng built here

`osx-arm64` and `win-arm64` get their `libnng` from `native/`, because `nng.NET` publishes none for
them ([docs/ipc.md](ipc.md#nng-and-which-platforms-it-reaches) has the whole table). The files are
committed, so building and packing need nothing extra. To rebuild one:

```
scripts/build-nng.sh osx-arm64        # on an Apple silicon Mac, with Xcode's tools and CMake
scripts/build-nng.sh win-arm64        # on Windows on Arm, in Git Bash, with Visual Studio 2022 and CMake
scripts/build-nng.sh <rid> --check    # rebuild, and fail unless it is the committed file
```

Without `--check` the script writes `native/<rid>/<file>` and puts its SHA-256 in `native/NNG_PIN`.
The `nng` workflow runs `--check` for both whenever `native/` or either script changes, and uploads
what it built. A newer Xcode or MSVC on a runner image can change the bytes with the pin unchanged.
When that is why it fails, commit the uploaded file and its new SHA-256. Do not loosen the check. To
move to another nng release, change the tag and the commit in `native/NNG_PIN`, and do the same.

`scripts/check-natives.sh <KiCadSharp.nupkg>` checks a packed package the way CI and the release
workflow do: exactly the eight libraries, each built for its own architecture.

## Why these keep the `Sharp` suffix

This repository was split out of
[danielmeza/kicad-ultra](https://github.com/danielmeza/kicad-ultra) alongside
[danielmeza/sexpressions](https://github.com/danielmeza/sexpressions). That sibling **was** renamed
on the way out — `SExpressionSharp` became `SExpressions` — and these packages were deliberately
**not**.

The suffix is doing real work here. `KiCadSharp` reads as what it is: a third-party .NET client for
somebody else's application. A bare `KiCad.*` prefix would read as something the KiCad project
publishes, which it is not, and would be trading on their trademark to say so. `kicadsharp` stays
the tool command for the same reason.

S-expressions, by contrast, are nobody's product. There was nothing for the suffix to disambiguate,
so the generic half of the split got the generic name.

Recorded here so the asymmetry is not mistaken for an oversight and re-litigated later.

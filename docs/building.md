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
dotnet build KiCadSharp.slnx -c Release -p:SExpressionsVersion=0.1.1-local.<stamp>

# to pack too, stamp this repo's own packages with the same prerelease version:
dotnet pack KiCadSharp.slnx -c Release -p:SExpressionsVersion=0.1.1-local.<stamp> -p:Version=0.1.1-local.<stamp>
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

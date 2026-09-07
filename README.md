# KiCadSharp

A .NET client for [KiCad](https://www.kicad.org/), plus the `kicadsharp` command-line tool.

| Package | What it is |
|---|---|
| `KiCadSharp` | Talks to a running KiCad over its nng IPC API to read and edit boards, schematics and project settings, and reads the on-disk s-expression formats (symbol and footprint libraries, design rules). |
| `KiCadSharp.Protos` | The generated C# message types for KiCad's IPC API (`Kiapi.*`). Its own package because those types appear in `KiCadSharp`'s public surface, so consumers have to be able to name them. |
| `KiCadSharp.Cli` | A dotnet tool, installed as `kicadsharp`, for working with KiCad s-expression files from a shell. |

```
dotnet add package KiCadSharp
dotnet tool install --global KiCadSharp.Cli
```

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

## The KiCad `.proto` definitions

`KiCadSharp.Protos` compiles 12 `.proto` files from KiCad's `api/proto`. They are **vendored** under
[`protos/`](protos/) and pinned by [`protos/KICAD_PIN`](protos/KICAD_PIN) to a KiCad **release tag** —
never a branch, because the generated client speaks one KiCad version's wire format and a moving
branch would desynchronise it silently.

Currently pinned to **KiCad 10.0.6** (`caf7377e9cb6fa1535ec3596dcb8c99bf44a996e`).

They used to arrive through a `submodules/kicad` git submodule pointed at the full KiCad source
tree. Measured, at the same tag:

| | Cost to obtain the `.proto` files |
|---|---|
| Submodule, `git clone --depth 1 --branch 10.0.6` | **1.4 GB** on disk (248 MB `.git` + 1.1 GB working tree), 18,347 files, 18.7 s |
| Sparse checkout of `api/proto` only | 3.5 MB, 2.2 s — but `actions/checkout` does a plain clone for submodules, so CI pays the full 1.4 GB anyway |
| **Vendored (this repo)** | **128 KB**, 12 files, already present — no fetch, builds offline |

Vendoring wins on the numbers and loses nothing, because the pin is enforced rather than trusted:

```
scripts/sync-protos.sh            # re-fetch protos/ from the pinned tag
scripts/sync-protos.sh --check    # fail if protos/ has drifted from the pin  (runs in CI)
```

`--check` also fails if the upstream tag itself has been moved to a different commit. Both use a
blobless sparse clone, so the check costs about 3 MB, not 1.4 GB.

To move to a newer KiCad: edit both lines of `protos/KICAD_PIN`, run `scripts/sync-protos.sh`, and
commit the result.

## Building

```
dotnet build KiCadSharp.slnx -c Release
dotnet test  KiCadSharp.slnx -c Release
```

### Developing against local SExpressions sources

`KiCadSharp` and `KiCadSharp.Cli` consume `SExpressions` as a `PackageReference`, not a project
reference or a submodule. To build against an unreleased local checkout of it:

```
scripts/use-local-libs.sh ../sexpressions
dotnet build KiCadSharp.slnx -c Release -p:SExpressionsVersion=0.1.0-local.<stamp>

# to pack too, stamp this repo's own packages with the same prerelease version:
dotnet pack KiCadSharp.slnx -c Release -p:SExpressionsVersion=0.1.0-local.<stamp> -p:Version=0.1.0-local.<stamp>
```

`pack` needs both properties, and that is NuGet being right rather than a workaround: a stable
`0.1.0` `KiCadSharp` may not declare a prerelease `SExpressions` dependency (NU5104). While the
dependency is a local prerelease, these packages are prereleases too.

The script packs `SExpressions` at a distinct `-local.<timestamp>` version into `local-packages/`, a
git-ignored folder already registered as a package source in `NuGet.config`. The distinct version is
the point: restore can never silently fall back to, or prefer, the published package. Omit the
property to go back to the released one.

There is deliberately no "swap to `ProjectReference`" switch. Consuming the real `.nupkg` is what
proves the package works, and the package is the thing that actually breaks.

## License

MIT — see [LICENSE](LICENSE).

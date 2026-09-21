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

## Testing

```
dotnet test KiCadSharp.slnx -c Release
```

The suite needs nothing installed. Three environment variables change what it does:

| Variable | Effect |
| --- | --- |
| `KICADSHARP_KICAD_CLI` | A `kicad-cli` to shell out to. The tests that hand a file to KiCad itself return early without it, since CI has no KiCad. |
| `KICADSHARP_IPC_SOCKET` | The API socket of a running KiCad, for the live tests in `IpcTests`. `scripts/kicad-ipc-container.sh` starts one and sets it. |
| `KICADSHARP_KEEP_TEST_FILES` | Set to `1` to keep every test's scratch directory instead of deleting it. |

### Scratch directories

A test that writes files gets a directory of its own under the system temp folder,
`<temp>/kicadsharp-tests/` (`kicadsharp-fluent-tests/` for the fluent package), named after the
test that asked for it. The test deletes it when it finishes, pass or fail. When a file is still
held open, for example by a `kicad-cli` that has just exited on Windows, the delete is retried for
under a second and then given up without failing the test. A directory that no test disposes, such
as one shared by every case of a theory, is deleted when the test process exits.

To look at what a failing test wrote, run it with `KICADSHARP_KEEP_TEST_FILES=1`. Nothing is
deleted, so clear the folder yourself afterwards.

A new test takes its directory with `using`:

```csharp
using var scratch = TestData.NewScratchDirectory();   // TestSupport.NewScratchDirectory() in KiCadSharp.Fluent.Tests
var output = Path.Combine(scratch, "board.kicad_pcb");
```

`scratch` converts to its path wherever a `string` is expected. Do not write to `Path.GetTempPath()`
directly. The one exception is a Unix socket: its path has to fit in 104 bytes on macOS, which a
scratch directory's path does not leave room for. The IPC tests' sockets therefore sit directly in
the temp folder, and each test removes its socket when it closes it.

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

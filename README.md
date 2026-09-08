# KiCadSharp

[![NuGet](https://img.shields.io/nuget/v/KiCadSharp.svg)](https://www.nuget.org/packages/KiCadSharp)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

A .NET client for [KiCad](https://www.kicad.org/). Drive a running pcbnew over KiCad's IPC API, and
read symbol and footprint libraries off disk.

```
dotnet add package KiCadSharp
```

```csharp
using KiCadSharp;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddLogging();
services.AddKiCad("my-plugin");

var kicad = services.BuildServiceProvider()
                    .GetRequiredService<IKiCadFactory>()
                    .Create("my-plugin");

var board = await kicad.GetBoard();
var footprints = await board.GetItems(KiCadObjectType.KotPcbFootprint);

Console.WriteLine($"{footprints.Count} footprints on {board.Name}");
```

KiCad has to be running with **Preferences → Plugins → Enable IPC API** ticked. No token needed —
the client picks one up on the first round trip.

## Before you start

**This works against `pcbnew`. It does not work against `eeschema` on KiCad 10.0.6** — that build
registers essentially nothing over IPC, not even `GetVersion`. The schematic commands land in
KiCad 11. What we measured is in [docs/ipc.md](docs/ipc.md).

For **lossless** work on `.kicad_sch` and `.kicad_pcb` files, reach for
[SExpressions](https://github.com/danielmeza/sexpressions) or the `kicadsharp` CLI below. The typed
document model here covers symbol and footprint libraries well; its write path is partial, and
[docs/status.md](docs/status.md) says exactly where.

## The packages

| Package | What it's for |
|---|---|
| [`KiCadSharp`](https://www.nuget.org/packages/KiCadSharp) | The client: IPC, plus the on-disk symbol and footprint formats. |
| [`KiCadSharp.Protos`](https://www.nuget.org/packages/KiCadSharp.Protos) | Generated C# types for KiCad's protobuf API. Separate package because they appear in `KiCadSharp`'s public surface. |
| [`KiCadSharp.Cli`](https://www.nuget.org/packages/KiCadSharp.Cli) | A dotnet tool, `kicadsharp`, for s-expression files from a shell. |

## The CLI

```
dotnet tool install --global KiCadSharp.Cli
```

```
kicadsharp parse    <file>                 forms, nodes, depth
kicadsharp fmt      <file> [--in-place]    reformat through a verified round trip
kicadsharp query    <file> <path> [--all]  read a value by path
kicadsharp validate <file>                 does it parse? non-zero if not
```

```
$ kicadsharp query board.kicad_pcb kicad_pcb/general/thickness
1.6
```

`fmt` re-parses its own output and compares the trees before writing anything, so a clean run is the
proof the round trip was lossless. It refuses `--in-place` when it would drop content.

## More examples

**Edit the board inside a commit**

```csharp
var board = await kicad.GetBoard();
var commit = await board.BeginCommit();

// ... change items ...

await board.PushCommit(commit, "move the connector");
```

**Read a symbol library from disk**

```csharp
var lib = KiCadSymbolLibrary.Load("orbion.kicad_sym");

foreach (var symbol in lib.Symbols)
    Console.WriteLine($"{symbol.Id}: {symbol.Pins.Count} pins");

lib.Save("orbion.kicad_sym");
```

**Net classes and text variables**

```csharp
var project = await kicad.GetProject();
var classes = await project.GetNetClasses();
var expanded = await project.ExpandTextVariables("${ORBION_PN} rev ${BOARD_REV}");
```

Every type and member: [docs/api.md](docs/api.md).

## Platforms

`net10.0` on Linux, macOS and Windows, x64 and arm64. The transport talks to
[nng](https://nng.nanomsg.org/) through P/Invoke and the native library ships in the package for six
RIDs. Point `KICADSHARP_NNG_LIBRARY` at your own build if you need something else.

## Building

```
git clone https://github.com/danielmeza/kicad-sharp
cd kicad-sharp
dotnet test
```

KiCad's `.proto` files are vendored under `protos/`, pinned to a release tag — no submodule, no
1.4 GB checkout. To build against a local SExpressions checkout, see
[docs/building.md](docs/building.md).

## Contributing

Issues and pull requests welcome. If you're reporting something about IPC, say which KiCad version
and which editor: pcbnew and eeschema behave very differently.

## License

MIT — see [LICENSE](LICENSE).

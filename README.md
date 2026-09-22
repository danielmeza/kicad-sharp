# KiCadSharp

[![NuGet](https://img.shields.io/nuget/v/KiCadSharp.svg)](https://www.nuget.org/packages/KiCadSharp)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

A .NET client for [KiCad](https://www.kicad.org/). Drive a running pcbnew over KiCad's IPC API, and
read and write symbol libraries, footprints, boards and schematics off disk.

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

**On disk, the typed document model is a set of views**: `KiCadSymbolLibrary`, `KiCadFootprintLibrary`,
`KiCadBoard` and `KiCadSchematic` read and write the file's own s-expressions, and an untouched save
is byte-identical. Not every form has a view yet, and the write path is partial;
[docs/status.md](docs/status.md) says exactly where. Anything without a view is still reachable,
losslessly, through `Node`, [SExpressions](https://github.com/danielmeza/sexpressions) or the
`kicadsharp` CLI below.

## The packages

| Package | What it's for |
|---|---|
| [`KiCadSharp`](https://www.nuget.org/packages/KiCadSharp) | The client: IPC, plus the on-disk symbol, footprint, board and schematic formats, copper geometry checked against KiCad's own, and Specctra DSN export and session import for an autorouter. |
| [`KiCadSharp.Fluent`](https://www.nuget.org/packages/KiCadSharp.Fluent) | A fluent style for building documents: a chainable `With*` for every `Add*`. Optional, and layered over `KiCadSharp` — see [below](#two-ways-to-build-a-document). |
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

## Two ways to build a document

`KiCadSharp` builds a document with `Add*` methods. Each returns the child it created, so you keep a
handle on it:

```csharp
using KiCadSharp.Documents;

string[] smd = ["F.Cu", "F.Paste", "F.Mask"];

var footprint = new KiCadFootprint("R_0603_1608Metric");
footprint.AddPad("1", "smd", "rect", -0.8, 0, 0.9, 0.95, smd);
footprint.AddPad("2", "smd", "rect", 0.8, 0, 0.9, 0.95, smd);
footprint.AddLine(-1.5, -0.7, 1.5, -0.7, "F.CrtYd", 0.05);
var model = footprint.AddModel("${KICAD10_3DMODEL_DIR}/Resistor_SMD.3dshapes/R_0603_1608Metric.step");
model.Scale = new KiCadXyz(1, 1, 1);
```

`KiCadSharp.Fluent` adds a `With*` for each of them. It returns the parent, so the calls chain, and
takes an optional callback that receives the child, so chaining never costs you the handle:

```
dotnet add package KiCadSharp.Fluent
```

```csharp
using KiCadSharp.Documents;
using KiCadSharp.Fluent;

string[] smd = ["F.Cu", "F.Paste", "F.Mask"];

var footprint = new KiCadFootprint("R_0603_1608Metric")
    .WithPad("1", "smd", "rect", -0.8, 0, 0.9, 0.95, smd)
    .WithPad("2", "smd", "rect", 0.8, 0, 0.9, 0.95, smd)
    .WithLine(-1.5, -0.7, 1.5, -0.7, "F.CrtYd", 0.05)
    .WithModel("${KICAD10_3DMODEL_DIR}/Resistor_SMD.3dshapes/R_0603_1608Metric.step",
               model => model.Scale = new KiCadXyz(1, 1, 1));
```

Both build the same footprint and save the same bytes; the package's tests hold it to that. Neither
style replaces the other. The `Add*` API is unchanged, both work on the same objects, and you can
mix them. Items that are added through a live list rather than an `Add*` get a `With` too:
`board.Zones.With(zone => zone.WithPoint(0, 0).WithPoint(10, 0).WithPoint(10, 10))`. The full list is
in [docs/api.md](docs/api.md#kicadsharpfluent).

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

`net10.0`. The transport talks to [nng](https://nng.nanomsg.org/) through P/Invoke, and the native
library ships in the package for eight runtime identifiers:

| | x64 | arm64 | 32-bit |
|---|---|---|---|
| Linux (glibc) | `linux-x64` | `linux-arm64` | `linux-arm` |
| macOS | `osx-x64` | `osx-arm64` (Apple silicon) | — |
| Windows | `win-x64` | `win-arm64` | `win-x86` |

Anything else, musl (Alpine) included, gets no `libnng` from the package: point
`KICADSHARP_NNG_LIBRARY` at one you supply. What each library links against, and where that bites (a
slim container, the Visual C++ runtime on Windows), is in
[docs/ipc.md](docs/ipc.md#nng-and-which-platforms-it-reaches).

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

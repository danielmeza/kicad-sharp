# KiCadSharp

A .NET client for [KiCad](https://www.kicad.org/), plus the `kicadsharp` command-line tool. Three
packages, `net10.0`.

| Package | What it is |
|---|---|
| `KiCadSharp` | Talks to a running KiCad over its nng IPC API to read and edit **boards** and project settings, and reads the on-disk s-expression symbol and footprint formats. |
| `KiCadSharp.Protos` | The generated C# message types for KiCad's IPC API (`Kiapi.*`). Its own package because those types appear in `KiCadSharp`'s public surface, so consumers have to be able to name them. |
| `KiCadSharp.Cli` | A dotnet tool, installed as `kicadsharp`, for working with KiCad s-expression files from a shell. |

```
dotnet add package KiCadSharp
dotnet tool install --global KiCadSharp.Cli
```

## Read this first

**The IPC client works against `pcbnew`. It does not work against `eeschema` on KiCad 10.0.6.**
Measured, not inferred — see [The IPC client](#the-ipc-client) below. The schematic side of KiCad's
API is a KiCad 11 story, both here and upstream.

The on-disk document layer (`KiCadSymbolLibrary`, `KiCadFootprintLibrary`) is a **partial** model of
its formats and its write paths are **lossy** — measured losses are listed under
[Pending](#pending). For lossless file work, use
[`SExpressions`](https://github.com/danielmeza/sexpressions) directly or the `kicadsharp` CLI, both
of which round trip byte for byte.

## `KiCadSharp` — what is in the box

### Connecting

| Type | Purpose |
|---|---|
| `KiCadIPCClient : IDisposable` | The transport. `Connect(ct)`, `Send<TResult>(IMessage, ct)`, `Send(IMessage, ct)`, `Disconnect()`, `IsConnected`. |
| `KiCadClientSettings` | `PipeName`, `Token`, `ClientName`, `DefaultClientName = "kicad.client"`. |
| `KiCadIPCProxy` | Abstract base for `KiCad` / `Board` / `Project`; wraps `Send`. |
| `KiCadEnvironment` | Static reader for `KICAD_API_SOCKET`, `KICAD_API_TOKEN`, `KIPRJMOD`, `KICAD_USER_TEMPLATE_DIR`, `KICAD9_3DMODEL_DIR`, `KICAD9_FOOTPRINT_DIR`, `KICAD9_SYMBOL_DIR`, `KICAD9_DESIGN_BLOCK_DIR`, `VIRTUAL_ENV`; plus `GetDefaultSocketPath()`, `GenerateRandomClientName()`, `IsRunningOnKiCad()`. |
| `KiCadServicesExtensions.AddKiCad(...)` | DI registration: a keyed `KiCadIPCClient`, a keyed `KiCad`, the nng load context, and `IKiCadFactory`. |
| `IKiCadFactory` | `KiCad Create(string? clientName = null)`. |
| `KiCadConnectionException` | Dial/send/receive failures. |

Requests are framed as an `ApiRequest` envelope with the command packed into `Any` and a header
carrying the KiCad token; the reply is an `ApiResponse` unpacked back to `TResult`. If no token was
configured, the client adopts the one KiCad returns on the first successful round trip.

### `KiCad` — the connection handle

`Ping()`, `GetVersion()`, `GetKiCadBinaryPath(name)`, `GetPluginSettingsPath(id)`,
`GetOpenDocuments(DocumentType)`, `GetBoard()`, `GetProject()` / `GetProject(DocumentSpecifier)`,
`RunAction(name)`, `RefreshEditor(FrameType)`, `GetTextVariables()`.

### `Board` — the PCB, over IPC

`Save()`, `SaveAs(filename, overwrite, includeProject)`, `Revert()`,
`BeginCommit()` / `PushCommit(commit, message)` / `DropCommit(commit)`,
`GetItems(params KiCadObjectType[])`, `GetSelection(...)`, `ClearSelection()`,
`GetActiveLayer()` / `SetActiveLayer(BoardLayer)`, `GetAsString()`, `RefillZones()`,
`CreateItems(params IMessage[])`, `UpdateItems(...)`, `DeleteItems(params KIID[])`,
plus `Document` and `Name`.

### `Project` — settings, over IPC

`GetNetClasses()` / `SetNetClasses(netClasses, mergeMode)`, `ExpandTextVariables(string)` and
`ExpandTextVariables(string[])`, `GetTextVariables()` / `SetTextVariables(vars, mergeMode)`, plus
`Document`, `Name`, `Path`. Every command in `project_commands.proto` is wrapped.

### On-disk documents

| Type | File | What it actually does |
|---|---|---|
| `KiCadNode` | — | Base of every typed view: `Node` (the live s-expression), `ToSExpression()`. Everything below reads and writes through it. |
| `KiCadNodeList<T>` | — | A live view over a node's children with one token: `Count`, indexer, `Add`, `Remove`, `Insert`. |
| `KiCadSymbolLibrary` | `.kicad_sym` | `Load`/`LoadAsync`/`Parse`, `Save`/`SaveAsync`/`ToText`, `AddSymbol`, `RemoveSymbol`, `GetSymbol`, `Symbols`, `Version`, `Generator`, `Document`, `Node`. |
| `KiCadSymbol` | — | `Id`, `Properties`, `Units`, `Pins`, `GraphicalItems`, `HidePinNumbers`, `HidePinNames`, `InBom`, `OnBoard`, `GetPropertyValue`, `AddProperty`, `AddUnit`, `AddPin`, `CloneAs`. `Pins` and `GraphicalItems` look through the KiCad 6+ sub-units, which is where they live. |
| `KiCadSymbolUnit` | — | One `(symbol "R_1_1" …)` sub-unit: `Id`, `Unit`, `BodyStyle`, `Pins`, `GraphicalItems`, `AddPin`. |
| `KiCadFootprintLibrary` | `.kicad_pcb`, `.kicad_mod` | `Load`/`LoadAsync`/`Parse`, `Save`/`SaveAsync`/`ToText`, `AddFootprint`, `RemoveFootprint`, `GetFootprint`, `Footprints`, `IsSingleFootprint`, `SaveFootprint`. A `.kicad_mod` is one footprint at the root — `footprint` (KiCad 6+) or `module` (KiCad 5). |
| `KiCadFootprint` | — | `Id`, `Layer`, `Description`, `Tags`, `Tedit`/`Tstamp`, `Attributes`, `Properties`, `Models`, `TextItems`, `Pads`, `Lines`, `Rectangles`, `Circles`, `Arcs`, `Polygons`, `GetPropertyValue`, `Add*`, `CloneAs`. |
| `KiCadSchematic` | `.kicad_sch` | `Load`/`LoadAsync`/`Parse`, `Save`, `ToText`, `Uuid`, `Symbols`, `Sheets`, `FilePath`, `IsModified`. |
| `KiCadSchematicSymbol` | — | `Uuid`, `LibId`, `Unit`, `Properties`, `ReferenceProperty`, `IsPowerSymbol`, `GetInstanceReference`, `SetInstanceReference`, `PruneInstances`. |
| `KiCadSheet` | — | `Uuid`, `SheetName`, `SheetFile`, `Properties`. |
| `SchematicHierarchy` | `.kicad_sch` | `Load(root)`, `ProjectName`, `Root`, `SheetInstances`, `Schematics`, `Placements`, `Save()`. |
| `SchematicAnnotator` | `.kicad_sch` | `Annotate`, `AnnotateFile`, `FindDuplicateReferences`. |
| `KiCadUtils` | `.kicad_sym` | `ParseSymbolLibrary`, `ExportSymbolToLibrary`, `ValidateSymbolLibrary`, `CloneSymbol`, `GetLibraryName`. |
| `KiCadFileExtensions` | — | `.kicad_pro`, `.kicad_sch`, `.kicad_pcb`, `.kicad_sym`, `.kicad_mod`, `.kicad_dru`, `.kicad_wks`, `.kicad_prl`. |
| `KiCadSharp.Settings.IWritableOptions<T>` / `WritableOptions<T>` | JSON | A writable `IOptions<T>` that patches one section of a JSON file and reloads configuration. `System.Text.Json`; every other section of the file is carried across untouched. Unrelated to KiCad IPC. |

### Typed vs. generic, by file type

| File | Typed model? |
|---|---|
| `.kicad_sym` | **Yes**, as a view. Reads sub-units; an untouched save is byte-identical. |
| `.kicad_mod` / `(footprint …)` inside `.kicad_pcb` | **Yes**, as a view. An untouched save is byte-identical. |
| `.kicad_pcb` (tracks, vias, zones, nets, layers, stackup) | **No.** Only reachable live, over IPC, or as generic s-expressions. |
| `.kicad_sch` | **Partial.** Symbols, sheets and reference designators, as a view — enough to walk a hierarchy and annotate it. No wires, labels or buses; no IPC path either. |
| `.kicad_pro`, `.kicad_dru`, `.kicad_wks`, `.kicad_prl` | **No.** Generic s-expressions (or JSON, for `.kicad_pro`). |

**These are views, not models.** A `KiCadSymbol` holds no fields — every property reads and writes
the `SExpression` it was built over, and `Save` writes the parsed document back. Nothing is
re-serialised, so a token this library has never heard of survives the round trip untouched, and a
save that changed one property differs from the input in exactly that property's bytes. Reach
anything not modelled through `Node`.

Anything in the "No" rows is still fully readable and *losslessly writable* through
[`SExpressions`](https://github.com/danielmeza/sexpressions), which is a dependency of this package —
you just write the accessors yourself.

### Annotation

`kicad-cli` has no `annotate` command. This does.

```csharp
var result = SchematicAnnotator.AnnotateFile("board.kicad_sch");
Console.WriteLine($"{result.Changes.Count} designators moved, {result.ReferenceCount} now distinct");
```

Or in two steps, when you want to see what it would do first:

```csharp
var hierarchy = SchematicHierarchy.Load("board.kicad_sch");
foreach (var (reference, sheets) in SchematicAnnotator.FindDuplicateReferences(hierarchy))
{
    Console.WriteLine($"{reference} is used in {string.Join(", ", sheets)}");
}

var result = SchematicAnnotator.Annotate(hierarchy);
hierarchy.Save();   // writes only the files that changed
```

**Why this exists.** A hierarchical design instantiates one sheet file more than once. The
designator that decides what a part *is* to the netlist is not the `Reference` property — it is the
entry in the symbol's `(instances (project … (path … (reference …))))` block, keyed by the full path
of the sheet instance. A sheet authored standalone has an entry for its own path and none for the
paths it is used at, so KiCad falls back to the property, both instances answer with the same
designator, and the netlist folds them into one part.

Measured on the fixture in `tests/KiCadSharp.Tests/data/duplicate-refs` — one child sheet
instantiated twice, 14 symbols:

| `kicad-cli sch export netlist` | before | after |
|---|---|---|
| components | 14 | 14 |
| distinct references | **7** | **14** |
| nodes on `GND` | **4** | **8** |
| `schematic has annotation errors` | yes | no |

Four of that board's eight ground connections did not exist, and
`kicad-cli sch erc --severity-all` reported 22 violations without naming one of them. The only
signal KiCad gives is a single line on the netlist exporter's stderr.

**What it does.** Walks the hierarchy from the root sheet, collects one placement per symbol per
sheet instance, and gives each one a designator nothing else in the design uses. The prefix never
changes — only the number, and a number that was written zero-padded stays zero-padded, so
`#PWR001` becomes `#PWR006` and not `#PWR6`. A designator that is already numbered and not already
taken is kept, which is what makes a second run write nothing at all.

**What it does not touch.** The `Reference` property: it is the designator the sheet was authored
with, it is what the editor draws, and a symbol on a sheet used twice has no single correct value
for it. Instance entries filed under a *different* project name, which is what lets a shared sheet
still open on its own. And every byte outside the `(instances …)` blocks — the test asserts that
stripping those blocks from the file before and after leaves two identical texts.

## `KiCadSharp.Protos`

12 `.proto` files from KiCad's `api/proto`, **vendored** under [`protos/`](protos/) and pinned by
[`protos/KICAD_PIN`](protos/KICAD_PIN) to a KiCad **release tag** — never a branch, because the
generated client speaks one KiCad version's wire format and a moving branch would desynchronise it
silently.

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

## `KiCadSharp.Cli`

```
dotnet tool install --global KiCadSharp.Cli
```

Four commands. This is `kicadsharp --help` on the installed tool, not a description of it:

| Command | Arguments and options |
|---|---|
| `parse <file>` | `--json` — structural summary: form count, top-level heads, node and value counts, depth. |
| `fmt <file>` | `-i, --in-place` — round trip through parser and writer, verified by re-parsing and comparing trees. |
| `query <file> <path>` | `-a, --all` — read a value by path. A segment names a child token; `[n]` picks the zero-based nth match. Dots or slashes both work. |
| `validate <file>` | — structural check plus a real parse; exits non-zero when it fails. |

`fmt --in-place` refuses to write when the lexical scanner found more top-level forms than the
parser returned, or when the file has comment lines — both cases where a naive rewrite would delete
something.

Run against a real 145,522-byte schematic:

```
$ kicadsharp parse orbion-esp32s3.kicad_sch
bytes         145522
top-level     1 form(s) parsed, 1 present in the file
comments      0 line(s)
nodes         7068
values        7963
max depth     9

top-level heads
  kicad_sch                1

$ kicadsharp query orbion-esp32s3.kicad_sch kicad_sch/version
20250114

$ kicadsharp validate orbion-esp32s3.kicad_sch
…: ok — 1 top-level form(s), 0 comment line(s), 1 form(s) reached the parser.
```

## The IPC client

### It works against pcbnew

```csharp
using KiCadSharp;
using Kiapi.Board.Types;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection()
    .AddLogging()
    .AddKiCad("my-plugin")          // reads KICAD_API_SOCKET / KICAD_API_TOKEN
    .BuildServiceProvider();

var kicad = services.GetRequiredService<IKiCadFactory>().Create("my-plugin");
Console.WriteLine(await kicad.GetVersion());        // e.g. 10.0.6

var board  = await kicad.GetBoard();
var commit = await board.BeginCommit();
await board.SetActiveLayer(BoardLayer.BlFCu);
await board.PushCommit(commit, "set active layer");
```

`GetBoard()` asks KiCad for open `DOCTYPE_PCB` documents and throws when there are none.

### It does not work against eeschema on KiCad 10.0.6

Measured against KiCad 10.0.6, and the reason there is no schematic API here:

- **`eeschema` answers neither `GetVersion` nor `Ping`.** The socket is there; the schematic frame
  does not reply.
- **`schematic_types.proto` defines exactly six message types** — `Line`, `Text`, `LocalLabel`,
  `GlobalLabel`, `HierarchicalLabel`, `DirectiveLabel`. There is **no** symbol, wire, junction or
  sheet message. `schematic_commands.proto` is a `package` declaration and nothing else: zero
  commands, zero services.
- **`CreateItems` against a schematic segfaults `eeschema`.**
- Upstream is in the same place: `kipy`'s own `kipy.schematic` fails to import against its generated
  protos, and its `Schematic` class carries `versionadded:: (KiCad 11)`.

So a schematic API is a KiCad 11 story, and nothing in `KiCadSharp` pretends otherwise. `.kicad_sch`
files are read and written on disk instead, through `SExpressions` or the CLI.

## Pending

What a reader would reasonably expect and will not find. Everything here is derived from the code
and, where a number is quoted, measured against the KiCad 10 corpus.

**Document layer.**

The document layer is a typed *view* over the s-expression tree, not a parallel model. Loading keeps
the parsed document; every property reads and writes the node it came from; saving writes that same
document back. Nothing is re-serialised from fields, so a token this library has never heard of is
still in the file afterwards — it was never copied out, so it cannot be left behind. Reach anything
this library does not model through `Node`.

Measured on the vendored KiCad 10 fixtures: a 108,583-byte, 35-symbol library reads all **112** pins
across its 67 sub-units and saves back **byte-identical**; changing one property changes **one byte**.
A standalone `.kicad_mod` loads as one footprint and saves byte-identical, `descr`, `tags`,
`property`, `fp_rect`, `embedded_fonts` and all.

What is still missing here:

- **`KiCadSchematic` models symbols, sheets and instances only** — enough to walk a hierarchy and
  annotate it. Wires, junctions, labels, buses, no-connects and text are reachable through `Node`
  and round-trip intact, but have no typed view.
- **`KiCadUtils` has no footprint half** — no `ParseFootprintLibrary`, `ValidateFootprintLibrary` or
  `CloneFootprint`. `KiCadFootprint.CloneAs` covers the last of those.
- **Board-level content has no views.** Zones, groups, tracks, vias and the `setup` block round-trip
  intact but are only reachable as raw s-expressions.

**IPC surface.**

- **The schematic half of the IPC API is not there in KiCad 10.0.6, and this is what that looks
  like.** Measured against `eeschema` in the release container, with a schematic open:

  ```
  Ping        -> AS_UNHANDLED  "no handler available for request of type kiapi.common.commands.Ping"
  GetVersion  -> AS_UNHANDLED  "no handler available for request of type kiapi.common.commands.GetVersion"
  GetOpenDocuments(DOCTYPE_SCHEMATIC) -> 1 document
  ```

  So `eeschema` does answer — it is not deaf, and a timeout is not the symptom to look for. It
  registers a handler set of nearly nothing: even `Ping` and `GetVersion`, which `pcbnew` answers,
  come back `AS_UNHANDLED`. `GetOpenDocuments` is the one command in this library that works
  against it. `schematic_commands.proto` carries no message for a symbol, wire, junction or sheet,
  so there is nothing to wrap even by hand. **Read and write schematics on disk**, through
  `KiCadSchematic` and `SchematicAnnotator`; the IPC route is a KiCad 11 story.

  `scripts/kicad-ipc-container.sh start sheet.kicad_sch` starts `eeschema` instead of `pcbnew` if
  you want to watch this for yourself.
- **Most of the vendored command surface is not wrapped.** `Board` sends 15 command messages
  (counted in `Board.cs`), of which only `RefillZones`, `GetActiveLayer` and `SetActiveLayer` come
  from `board_commands.proto` — that file declares **26** commands. Not wrapped anywhere:
  `GetNets`, `GetItemsByNet`, `GetItemsByNetClass`, `GetConnectedItems`, `GetNetClassForNets`,
  `GetBoardOrigin`/`SetBoardOrigin`, `GetBoardLayerName`, `GetBoardLayerByName`,
  `GetVisibleLayers`/`SetVisibleLayers`, `GetBoardEnabledLayers`/`SetBoardEnabledLayers`,
  `GetBoardStackup`/`UpdateBoardStackup`, `GetGraphicsDefaults`, `GetPadShapeAsPolygon`,
  `CheckPadstackPresenceOnLayers`, `InjectDrcError`, `FlipItems`, `InteractiveMoveItems`,
  `GetBoardEditorAppearanceSettings`/`SetBoardEditorAppearanceSettings`, and from
  `editor_commands.proto`: `GetItemsById`, `GetBoundingBox`, `AddToSelection`,
  `RemoveFromSelection`, `HitTest`, `GetTitleBlockInfo`/`SetTitleBlockInfo`,
  `SaveSelectionToString`, `ParseAndCreateItemsFromString`. Send them by hand with
  `KiCadIPCClient.Send<TResult>` — the message types are all in `KiCadSharp.Protos`.
- **`KiCad.RefreshPaths()` and `KiCad.ImportLibrary(...)` are no-ops.** The commands they would send
  do not exist in KiCad's IPC API; the methods return `ValueTask.CompletedTask` and do nothing. A
  `GetPath(PathType)` is commented out for the same reason.
- **No cancellation on the object model.** `KiCadIPCClient.Send` takes a `CancellationToken`;
  `KiCadIPCProxy.Send` drops it, so nothing on `KiCad`, `Board` or `Project` can be cancelled.
- **`ApiException` is `internal`.** It is what a non-OK API status throws, and the XML docs name it,
  but a consumer in another assembly cannot `catch` it by name — catch `Exception` or
  `KiCadConnectionException`.
- **The configured client name is not transmitted.** `AddKiCad("my-plugin")` sets
  `KiCadClientSettings.ClientName`, but the envelope header is populated from `PipeName`.
- **`KiCadEnvironment.GetDefaultSocketPath()` is never called.** `AddKiCad` wires `PipeName`
  straight from `KICAD_API_SOCKET`, so with that variable unset `Connect()` throws rather than
  falling back to the OS default the method computes.
- **No `Async` suffixes, and one sync/async asymmetry**: `GetProject(DocumentSpecifier)` is
  synchronous while the parameterless `GetProject()` is not.

**Repository.**

- **`KiCadSharp` depends on `Rebus`, a message bus, and nothing uses it.** The dependency is
  declared as `Rebus.nng`, which is what supplies the `nng` bindings the IPC client needs — and it
  brings `Rebus` 8.6.1 along, which in turn is the only reason `Newtonsoft.Json` is still in the
  restore graph. The `using nng;` in `KiCadIpcClient` and `KiCadServicesExtensions` resolves from
  `nng.NET` / `nng.NET.Shared`; there is no `using Rebus` anywhere in this repository. Referencing
  `nng.NET` directly would drop two packages from every consumer's graph. Not done here, because
  swapping a transport package is a consumer-visible packaging decision and belongs in its own
  change.
- **`tests/KiCadSharp.Tests` is the only gate on the document layer.** 56 tests over the vendored
  KiCad 10 fixtures, with the byte counts in the assertions. There is no test for the IPC surface at
  all — that needs a running KiCad, and nothing here fakes one. The one test that shells out to
  `kicad-cli` returns early unless `KICADSHARP_KICAD_CLI` points at one. Run them with
  `dotnet test KiCadSharp.slnx`.

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

## License

MIT — see [LICENSE](LICENSE).

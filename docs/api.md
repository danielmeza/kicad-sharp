# API reference

Every type and member. The short version is in the [README](../README.md).

## `KiCadSharp` — what is in the box

### Connecting

| Type | Purpose |
|---|---|
| `KiCadIPCClient : IDisposable` | The transport. `Connect(ct)`, `Send<TResult>(IMessage, ct)`, `Send(IMessage, ct)`, `Disconnect()`, `IsConnected`. |
| `KiCadClientSettings` | `PipeName`, `Token`, `ClientName`, `RequestTimeout`, `DefaultClientName = "kicad.client"`. |
| `KiCadIPCProxy` | Abstract base for `KiCad` / `Board` / `Project`; wraps `Send`. |
| `KiCadEnvironment` | Static reader for `KICAD_API_SOCKET`, `KICAD_API_TOKEN`, `KIPRJMOD`, `KICAD_USER_TEMPLATE_DIR`, `KICAD9_3DMODEL_DIR`, `KICAD9_FOOTPRINT_DIR`, `KICAD9_SYMBOL_DIR`, `KICAD9_DESIGN_BLOCK_DIR`, `VIRTUAL_ENV`; plus `GetDefaultSocketPath()`, `GenerateRandomClientName()`, `IsRunningOnKiCad()`. |
| `KiCadServicesExtensions.AddKiCad(...)` | DI registration: a keyed `KiCadIPCClient`, a keyed `KiCad`, and `IKiCadFactory`. |
| `IKiCadFactory` | `KiCad Create(string? clientName = null)`. |
| `KiCadConnectionException` | Dial/send/receive failures. |

Requests are framed as an `ApiRequest` envelope with the command packed into `Any` and a header
carrying the KiCad token; the reply is an `ApiResponse` unpacked back to `TResult`. If no token was
configured, the client adopts the one KiCad returns on the first successful round trip.

`RequestTimeout` defaults to `Timeout.InfiniteTimeSpan`, which is nng's own default and what this
client has always done. Waiting is not the same as hanging, though: the reply is polled for rather
than blocked on, so a `CancellationToken` is observed **while the request is on the wire** and not
only before it goes out. Pass one with a deadline for a per-call bound, or set `RequestTimeout` for
a client-wide one. There is no useful single default — `Ping` returns in under a millisecond and
`RefillZones` on a large board does not.

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
| `KiCadDocumentTypeException` | — | Thrown by every `Load`/`LoadAsync`/`Parse` above when the file is not the kind of document asked for. `FilePath`, `ExpectedRootTokens`, `ActualRootToken`. Derives from `InvalidOperationException`. |

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

**Every loader checks the root form.** `KiCadSymbolLibrary` takes `(kicad_symbol_lib …)`,
`KiCadFootprintLibrary` takes `(footprint …)`, KiCad 5's `(module …)` and `(kicad_pcb …)`,
`KiCadBoard` takes `(kicad_pcb …)` and `KiCadSchematic` takes `(kicad_sch …)`. Any other file,
including an empty one, JSON or prose, throws `KiCadDocumentTypeException`. It is never loaded as an
empty document that a later `Save` would write over the original. Text that is not s-expressions at
all throws the parser's `SExpressionFormatException`, with a line and column. The constructors that
wrap an existing `SExpression` make the same check and throw `ArgumentException`. KiCad's own
footprint-library reader accepts only `footprint` and `module` in a `.kicad_mod`, so check
`IsSingleFootprint` when a board in that place would be a mistake.

### Copper geometry — `KiCadSharp.Geometry`

What a piece of copper occupies on the board, and how far it is from another — the question every
clearance rule asks — answered from the file, with no KiCad running.

| Type | What it does |
|---|---|
| `BoardPoint`, `BoardBox` | Millimetres in the file's frame, Y down. `BoardPoint.Rotate(degrees)` turns the way `(at x y angle)` does, exactly at quarter turns. |
| `RoundedShape` | A point, segment or polygon swept by a radius — which is every piece of copper, EXACTLY: a via, a track, an oval, a round rectangle (the rectangle shrunk by the corner radius, swept by it). `DistanceTo` is the core distance less both radii. |
| `CopperShape` | One or more of those: a custom pad, an arc's covering capsules. `DistanceTo`, `Clears(other, clearance)`, `Contains`, `Bounds`. |
| `CopperGeometry` | `Pad(footprint, pad)`, `Hole`, `Segment`, `Arc`, `Via`, `Fill(filledPolygon)`, `PadPosition`, `ArcPoints`, `IsChamfered`, `CopperLayers(board)` in physical order, `CopperLayersOf(declared, layers)` for `*.Cu` and `F&B.Cu`. |
| `BoardOutline` | `Of(board)`: the closed shapes on `Edge.Cuts` (footprint graphics included) as `Outers` and `Holes`, every edge as `Edges`, `DistanceToEdge(copper)`, `Contains(point)`, and `OpenChains` for an outline drawn with a gap. |

**Judged against KiCad.** `tests/…/Geometry/CopperGeometryOracleTests` compares `DistanceTo` with
pcbnew 10.0.6's own `GetEffectiveShape().Collide()` on 5,738 pairs across three boards — every pad
shape KiCad has, both sides, rotations, copper offset from the hole — within 1 µm above and 1.5 µm
below. The only approximation is a track arc, covered by capsules grown by the chord error so a
distance can read low, never high.

### Specctra — `KiCadSharp.Specctra`

A board to an autorouter and back: what File → Export → Specctra DSN and File → Import → Specctra
Session do in pcbnew, without pcbnew.

```csharp
var board = KiCadBoard.Load("board.kicad_pcb");
File.WriteAllText("board.dsn", SpecctraDesign.Export(board, new SpecctraOptions
{
    Classes = [new SpecctraNetClass("Power", 0.5, 0.25, 0.8, 0.4) { Nets = ["+3V3", "+5V"] }],
}));

// … route board.dsn into board.ses with Freerouting …

var result = SpecctraSession.Parse(File.ReadAllText("board.ses")).ApplyTo(board, viaDrill: 0.3);
board.Save("board.kicad_pcb");
```

| Type | What it does |
|---|---|
| `SpecctraDesign` | `Export`/`Build(board, options)`: layers, boundary and cutouts, planes, rule-area keepouts, every footprint as an image seen from the top, padstacks, nets, classes, and existing copper as wiring — locked copper as `fix`. `NetNames`, `NetOf`, `ReferenceOf`. |
| `SpecctraOptions`, `SpecctraNetClass` | The net classes — which live in the project file, not the board — and the hole clearance. |
| `SpecctraSession` | `Parse(text)`, `Wires`, `Vias`, `ApplyTo(board, viaDrill)`: replaces every unlocked track, arc and via with the session's, keeps the locked ones, writes nets in whichever spelling the board uses. |
| `SpecctraNode`, `SpecctraReader` | The format itself: its own `string_quote`, and KiCad's rule for which words are quoted. |
| `SpecctraPinRef` | A pin in a net's `(pins …)`: written as KiCad's `PIN_REF` writes it — part and pin each quoted only if they need it, joined by a bare `-` — and read back the same way. |

**Judged against KiCad.** `Specctra/SpecctraDesignOracleTests` compares the design with the one
pcbnew 10.0.6 writes for five boards, item by item — every pin's position, rotation and padstack
geometry, every placement, net, class, plane, keepout, wire and via — and
`SpecctraSessionOracleTests` compares an import with pcbnew's own import of the same Freerouting
session, track by track. `data/oracles/README.md` records how each oracle was made.

**And SPELLED as KiCad spells it.** Those comparisons parse both files, and a parser strips quotes;
Freerouting does not. 0.3.0 quoted every pin reference whole — `"U10-1"` where KiCad writes
`U10-1` — and Freerouting 2.4.1 read no pins from it at all: 0 connections on a board with 95.
`SpecctraSpellingTests` now compares every token's quoting with KiCad's for all five boards.

Freerouting logs *"exported from an old KiCad version"* for these designs: it calls any host whose
name contains "kicad" with a leading version number of 5 or less old, and this one is KiCadSharp
0.x. Read from Freerouting 2.4.1's source, `Communication.hostIsOldKicad` feeds that one log line
and nothing else.

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

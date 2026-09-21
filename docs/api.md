# API reference

Every type and member. The short version is in the [README](../README.md).

## `KiCadSharp` — what is in the box

### Connecting

| Type | Purpose |
|---|---|
| `KiCadIPCClient : IDisposable` | The transport. `Connect(ct)`, `Send<TResult>(IMessage, ct)`, `Send(IMessage, ct)`, `Disconnect()`, `IsConnected`. |
| `KiCadClientSettings` | `PipeName`, `Token`, `ClientName`, `RequestTimeout`, `DefaultClientName = "kicad.client"`. |
| `KiCadIPCProxy` | Abstract base for `KiCad` / `Board` / `Project`; wraps `Send`. |
| `KiCadEnvironment` | Static reader for `KICAD_API_SOCKET`, `KICAD_API_TOKEN`, `KIPRJMOD`, `KICAD_USER_TEMPLATE_DIR`, `VIRTUAL_ENV`, and KiCad's versioned library paths: `GetModelsDirectory()` (`KICAD<n>_3DMODEL_DIR`), `GetFootprintDirectory()` (`KICAD<n>_FOOTPRINT_DIR`), `GetSymbolDirectory()` (`KICAD<n>_SYMBOL_DIR`), `GetDesignBlockDirectory()` (`KICAD<n>_DESIGN_BLOCK_DIR`), `GetTemplateDirectory()` (`KICAD<n>_TEMPLATE_DIR`), `GetThirdPartyDirectory()` (`KICAD<n>_3RD_PARTY`). Each reads the newest version `<n>` that is set, so KiCad 10's `KICAD10_*` wins over a leftover `KICAD9_*`. `GetVersionedVariable(baseName)` does the same for any name, and `GetVersionedVariable(baseName, majorVersion)` tries a known version first. See [what KiCad puts in a plugin's environment](ipc.md#what-kicad-puts-in-a-plugins-environment). Plus `GetDefaultSocketPath()`, `GenerateRandomClientName()`, `IsRunningOnKiCad()`. |
| `KiCadServicesExtensions.AddKiCad(...)` | DI registration: a keyed `KiCadIPCClient`, a keyed `KiCad`, and `IKiCadFactory`. |
| `IKiCadFactory` | `KiCad Create(string? clientName = null)`. |
| `KiCadIpcException` | Abstract base of the two below: the one type to catch for any IPC failure. |
| `KiCadConnectionException : KiCadIpcException` | KiCad could not be reached: no socket path, nothing listening, no native nng, a send or receive nng refused, a reply that is not an `ApiResponse`, or `RequestTimeout` running out. |
| `ApiException : KiCadIpcException` | KiCad answered and the answer was not the result. `StatusCode` (`ApiStatusCode?`) is the status it sent, `ErrorMessage` its text. |

Requests are framed as an `ApiRequest` envelope with the command packed into `Any` and a header
carrying the KiCad token; the reply is an `ApiResponse` unpacked back to `TResult`. If no token was
configured, the client adopts the one KiCad returns on the first successful round trip.

**When a call fails** it throws a `KiCadIpcException`, with the underlying failure as its
`InnerException` when there is one, and a cancellation throws `OperationCanceledException` as itself:

| What happened | Thrown | Inside |
|---|---|---|
| No socket path, or nothing listening at it | `KiCadConnectionException` | nng's error (connection refused) |
| A socket that never completes nng's handshake (a KiCad still starting) | `KiCadConnectionException`, after nng's own 10 s | nng's error (timed out) |
| No native nng for this platform | `KiCadConnectionException` | the loader's `DllNotFoundException` or `BadImageFormatException` |
| `KICADSHARP_NNG_LIBRARY` names something that does not load: no file at that path, or one the platform loader refuses | `KiCadConnectionException`, naming the variable and the path; the shipped nng is not loaded instead | the loader's `DllNotFoundException` or `BadImageFormatException` |
| `KICADSHARP_NNG_LIBRARY` names a library that loads and is not nng: it lacks one of the thirteen functions this client calls | `KiCadConnectionException`, naming the variable, the path and the missing functions | `EntryPointNotFoundException` for the first one missing |
| `RequestTimeout` ran out, waiting for the reply or to send | `KiCadConnectionException` | `TimeoutException` |
| Bytes back that are not an `ApiResponse` | `KiCadConnectionException` | `InvalidProtocolBufferException` |
| KiCad answered a status other than `AS_OK` — `AS_UNHANDLED`, `AS_BAD_REQUEST`, `AS_NOT_READY`, `AS_BUSY`, `AS_TOKEN_MISMATCH`, … | `ApiException`, `StatusCode` = that status | — |
| A reply with no status | `ApiException`, `StatusCode` = `AS_UNKNOWN` | — |
| `AS_OK` with no payload, the wrong type, or one that does not parse | `ApiException`, `StatusCode` = `AS_OK` | `InvalidProtocolBufferException` for the last |
| `GetBoard()` with no board open | `ApiException`, `StatusCode` = `null` | — |
| The caller's token was cancelled: before the request went out, while the send waits for a KiCad to take it, or while the reply is awaited | `OperationCanceledException`, whose `CancellationToken` is the caller's | — |
| `Disconnect()` cut the request off | `OperationCanceledException` | — |

`AS_UNHANDLED` is how KiCad says it has no handler for a command, so it is how a caller finds out a
command is not there:

```csharp
try
{
    await kicad.Ping();
}
catch (ApiException e) when (e.StatusCode == ApiStatusCode.AsUnhandled)
{
    // this editor does not handle Ping -- eeschema on KiCad 10.0.6, for one
}
catch (KiCadIpcException e)
{
    // KiCad is not there, or it refused
}
```

A token that was empty is not a failure: KiCad answers it with its own, which the client adopts. A
token from another KiCad instance is `AS_TOKEN_MISMATCH`.

`RequestTimeout` defaults to `Timeout.InfiniteTimeSpan`, which is nng's own default and what this
client has always done. Waiting is not the same as hanging, though: neither the send nor the reply
is blocked on in nng, both are polled for, so a `CancellationToken` is observed **while the request
is on the wire** and not only before it goes out. That includes a KiCad that went away after the
dial: the send then has nobody to hand the request to, and it waits for somebody without holding a
thread, until the token or `RequestTimeout` ends the wait. Pass a token with a deadline for a
per-call bound, or set `RequestTimeout` for a client-wide one. There is no useful single default —
`Ping` returns in under a millisecond and `RefillZones` on a large board does not.

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
| `KiCadNodeList<T>` | — | A live view over a node's children with one token: `Count`, indexer, `Add`, `Remove`, `Insert`. A `foreach` walks the children that were there when it started, so the loop may move, remove or append. |
| `KiCadSymbolLibrary` | `.kicad_sym` | `Load`/`LoadAsync`/`Parse`, `Save`/`SaveAsync`/`ToText`, `AddSymbol`, `RemoveSymbol`, `GetSymbol`, `Symbols`, `Version`, `Generator`, `Document`, `Node`. |
| `KiCadSymbol` | — | `Id`, `Properties`, `Units`, `Pins`, `GraphicalItems`, `HidePinNumbers`, `HidePinNames`, `InBom`, `OnBoard`, `GetPropertyValue`, `AddProperty`, `AddUnit`, `AddPin`, `CloneAs`. `Pins` and `GraphicalItems` look through the KiCad 6+ sub-units, which is where they live. |
| `KiCadSymbolUnit` | — | One `(symbol "R_1_1" …)` sub-unit: `Id`, `Unit`, `BodyStyle`, `Pins`, `GraphicalItems`, `AddPin`. |
| `KiCadText` | — | Text inside a symbol: `Text`, `Position`, `RotationDegrees`, `FontEffects`. KiCad stores this angle in **tenths of a degree**, so a vertical text is `(at x y 900)` and `Position.Rotation` reads 900. `RotationDegrees` converts. The four-argument constructor writes its angle unchanged and is obsolete. `KiCadSchematicText`, which is text on a sheet, stores degrees, so there `RotationDegrees` is the number in the file. |
| `KiCadFootprintLibrary` | `.kicad_pcb`, `.kicad_mod` | `Load`/`LoadAsync`/`Parse`, `Save`/`SaveAsync`/`ToText`, `AddFootprint`, `RemoveFootprint`, `GetFootprint`, `Footprints`, `IsSingleFootprint`, `SaveFootprint`, `Version`, `Generator`. A `.kicad_mod` is one footprint at the root — `footprint` (KiCad 6+) or `module` (KiCad 5). `Version` is the version the file declares, or `null` when it declares none. |
| `KiCadFootprint` | — | `Id`, `Version`, `Layer`, `Description`, `Tags`, `Tedit`/`Tstamp`, `Attributes`, `Properties`, `Models`, `TextItems`, `Pads`, `Lines`, `Rectangles`, `Circles`, `Arcs`, `Polygons`, `GetPropertyValue`, `Add*`, `CloneAs`. |
| `KiCadBoard` | `.kicad_pcb` | `Load`/`LoadAsync`/`Parse`, `Save`/`SaveAsync`/`ToText`, `Version`, `Generator`, `GeneratorVersion`, `Paper`, `EmbeddedFonts`, `General`, `TitleBlock`, `Setup` (and its `Stackup`), `Layers`, `GetLayer`, `Nets`, `GetNet` (by code or by name), `AddNet`, `Footprints`, `Segments`, `TrackArcs`, `Vias`, `Zones`, `GraphicLines`, `GraphicRectangles`, `GraphicCircles`, `GraphicArcs`, `GraphicPolygons`, `GraphicCurves`, `Texts`, `Dimensions`, `Groups`, `RequireGeneral`/`RequireTitleBlock`/`RequireSetup`, `Document`, `Node`. What it has no view for yet is listed in [status.md](status.md). |
| `KiCadSchematic` | `.kicad_sch` | `Load`/`LoadAsync`/`Parse`, `Save`, `ToText`, `Uuid`, `Version`, `Generator`, `GeneratorVersion`, `Paper`, `EmbeddedFonts`, `TitleBlock`, `Symbols`, `Sheets`, `Wires`, `Buses`, `BusEntries`, `BusAliases`, `Junctions`, `NoConnects`, `Labels`, `GlobalLabels`, `HierarchicalLabels`, `NetClassFlags`, `TextItems`, `TextBoxes`, `Polylines`, `Rectangles`, `Circles`, `Arcs`, `Beziers`, `Images`, `LibrarySymbols`, `SheetInstances`, `RequireTitleBlock`/`RequireLibrarySymbols`/`RequireSheetInstances`, `FilePath`, `IsModified`, `Document`. What it has no view for yet is listed in [status.md](status.md). |
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
| `.kicad_pcb` | **Yes**, as a view: `KiCadBoard`. The header, layers and stackup, nets, footprints, tracks, vias, zones, graphics, text, dimensions and groups; an untouched save is byte-identical. A few top-level forms, such as text boxes, tables and `generated` items, have no view yet ([status.md](status.md)). An open board is also reachable live, over IPC, through `Board`. |
| `.kicad_sch` | **Yes**, as a view: `KiCadSchematic`. Symbols, sheets, wires, buses, junctions, no-connects, labels, text, graphics, images, the symbol cache and the sheet instances; an untouched save is byte-identical. Rule areas, tables, groups and embedded files have no view yet ([status.md](status.md)). No IPC path. |
| `.kicad_pro`, `.kicad_dru`, `.kicad_wks`, `.kicad_prl` | **No.** Generic s-expressions (or JSON, for `.kicad_pro`). |

**These are views, not models.** A `KiCadSymbol` holds no fields — every property reads and writes
the `SExpression` it was built over, and `Save` writes the parsed document back. Nothing is
re-serialised, so a token this library has never heard of survives the round trip untouched, and a
save that changed one property differs from the input in exactly that property's bytes. Reach
anything not modelled through `Node`.

**A new child goes where KiCad reads it.** A property set for the first time adds its child at the
end of the form, except in the few forms KiCad 10 reads partly by position, where a child anywhere
else makes it refuse the whole file (#59). There the child goes in KiCad's place, whatever order
the properties are set in:

| Form | What comes first, in this order |
|---|---|
| `fp_poly`, `gr_poly`, `fp_curve`, `gr_curve` | `pts` |
| `fp_line`, `gr_line`, `fp_rect`, `gr_rect` | `start`, `end` |
| `fp_circle`, `gr_circle` | `center`, `end` |
| `fp_arc`, `gr_arc` | `start`, `mid`, `end`, or KiCad 5's `start`, `end`, `angle` |
| `dimension` | `type` |
| a zone's `polygon` | `pts`, and nothing else |
| a zone's `filled_polygon` | `layer`, `island`, then `pts` last |
| `kicad_pcb`, `kicad_sch`, `kicad_symbol_lib`, `footprint` | `version` |

Nothing that is already in a form moves, so a loaded file still saves byte for byte. The schematic
and symbol-library grammar has no such form: a `polyline`'s, `bezier`'s or `wire`'s `pts` may go
anywhere. `src/KiCadSharp/Documents/KiCadChildOrder.cs` cites KiCad 10.0.6's parser line for each
rule.

**A fill is spelled the way its file's parser reads it.** KiCad 10 spells `(fill …)` two ways, and
each parser refuses the whole file when it meets the other's (#64). `RequireFill()` adds an empty
`(fill)`, which both read, and the first `Type` set writes the owner's spelling:

| Owner | `RequireFill().Type = "no"` writes | Words `Type` takes |
|---|---|---|
| a board's `gr_*` shapes; a footprint's `fp_rect`, `fp_circle`, `fp_poly` | `(fill no)` | `yes`, `no`, `solid`, `none`, `hatch`, `reverse_hatch`, `cross_hatch` |
| a symbol's or a schematic's shapes, text boxes and sheets | `(fill (type none))` | `none`, `outline`, `background`, `color`, `hatch`, `reverse_hatch`, `cross_hatch` |

A word from the other row that means the same fill in KiCad's own model is written as this row's
word: `no` becomes `none` in a symbol, `yes` and `solid` become `outline`, and `outline` becomes
`yes` on a board. Any other word throws `ArgumentException`, and so do `background` and `color` on a
board, which has neither fill. A fill loaded from a file keeps the spelling it has. A zone's fill,
`(fill yes (thermal_gap …) …)`, is a third form, `KiCadZoneFill`. `KiCadFillSpelling` cites KiCad
10.0.6's parser and writer lines for each spelling.

**A zone is filled solid or hatched, and `solid` is not a word in the file.** KiCad writes
`(mode hatch)` for a hatched zone and no `(mode …)` for a solid one. Its parser takes `hatch`,
`polygon` and `segment`, the last two meaning solid, and refuses the whole board over `(mode solid)`
(#72). `KiCadZoneFill.Mode` reads `solid` when there is no `(mode …)`. Setting it to `"solid"` removes
the `(mode …)`, and `hatch`, `polygon` and `segment` are written as they are. Any other word throws
`ArgumentException` and writes nothing. Writing back the value just read leaves the file as it was.
A new mode does not refill the zone: its `filled_polygon`s keep the old copper until KiCad fills it
again. `Mode` cites KiCad 10.0.6's parser and writer lines.

**Adding a view moves its node.** `AddSymbol`, `AddFootprint`, `AddPin`, `AddGraphicalItem` and
`KiCadNodeList<T>.Add`/`Insert` put the node you pass into the destination and take it out of
wherever it was, another file included. The view you hold is then the element in the destination,
bytes and all. To leave the source as it was, add a copy: `new KiCadSymbol(symbol.Node.Clone())`.

```csharp
foreach (var symbol in source.Symbols)      // walks the 35 that were there when it started
    destination.AddSymbol(symbol);          // all 35 move; source.Symbols is now empty
```

`Count` and the indexer stay live, so a forward `for` loop over a list you are moving out of steps
over every other element. Use `foreach`, walk backwards, or take `[0]` until `Count` is 0.

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

**A symbol library's `version` changes how KiCad reads its symbols.** In KiCad 10.0.6, a lone `~`
is an empty value, pin name or pin number before `20250318`. An arc over 180° is redrawn as a
shorter one up to `20230121`. A second body style is inferred before `20250827` and dropped after it
unless the symbol declares one. So a library that holds no symbols yet takes the version of the
library its first `AddSymbol` comes from. A library that already holds symbols keeps its version,
because changing it would change how those symbols read. A symbol built in memory, a copy (`CloneAs`
or the `Node.Clone()` above) and one from a schematic leave the version alone, so when you copy, set
`Version` to the source's. Nothing converts symbols between versions, so symbols from libraries
of different versions cannot all be read as written from one file.

**A footprint built in memory carries KiCad 10's format stamp.** `new KiCadFootprint(id)` starts with
`(version 20260206)` and `(generator "KiCad Library Importer")`, where KiCad 10.0.6 writes them in a
footprint library (#63). Without a version, KiCad reads a `.kicad_mod` as format 0, which is KiCad 5's:
it refuses an arc drawn by `start`, `mid` and `end`, and it makes a footprint with no `attr` a
through-hole one. A footprint read from a file keeps the version it has, or none, and so does a copy
of one. A KiCad 5 module's `start`/`end`/`angle` arcs depend on having none. `KiCadFootprint.Version`
reads it and sets it, and `null` removes it. KiCad writes a footprint inside a board without a
version. A footprint that has one there makes KiCad read the rest of the board under the greater of
the two versions. For example, a via after a new footprint on a `new KiCadBoard()` (`20241229`) loses
its explicit "no" covering and plugging. To place a new footprint on a board stamped with an older
format, set its `Version` to `null` first (#73).

**A footprint built in memory is written in KiCad 10's forms.** `new KiCadFootprint(id)` starts with
the four fields KiCad gives every footprint, written as `(property …)`: `Reference` (`REF**`, on
`F.SilkS`), `Value` (the name, on `F.Fab`), and an empty `Datasheet` and `Description`, hidden, on
`F.Fab`. So `GetPropertyValue("Reference")` answers on a new footprint as it does on one KiCad wrote,
and `TextItems` starts empty (#75). The `(fp_text reference …)` and `(fp_text value …)` it wrote
before date from before format `20230620`, when fields replaced them. A width set on a new `fp_*`
shape is written as `(stroke (width w) (type solid))`, not as the bare `(width w)` of older files. kicad-cli 10.0.6 reads the old
and new spellings the same way. A footprint read from a file keeps its forms: its text items stay
text items, and a bare `width` stays bare when written to.

**A new board-shaped `KiCadFootprintLibrary` starts with a layer table**, the same one a new
`KiCadBoard` has. It used to write an empty `(layers)`, which KiCad refuses as "0 is not a valid layer
count" (#74). `KiCadFootprintLibrary.Version` reports only what the file declares: `null` for a file
with no version, which KiCad reads as format 0 (a `.kicad_mod`) or `20201115` (a board). It used to
report `20211014` there (#76).

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

## `KiCadSharp.Fluent`

```
dotnet add package KiCadSharp.Fluent
```

A second way to build the same documents. `using KiCadSharp.Fluent;` brings in a `With*` extension
for every public `Add*` on the document types (`KiCadSharp.Documents` and `KiCadSharp.Schematics`).
The core `Add*` API is unchanged. The fluent layer adds no behaviour of its own. Each call follows
one rule:

> `parent.WithX(args, configure)` is `configure?.Invoke(parent.AddX(args))`, then `return parent`.

- **It returns the parent**, the object it was called on, so calls chain.
- **It takes a callback exactly when the `Add*` returns a child.** The callback receives that child
  after it has been appended. `AddPoint` and `AddMember` return nothing, so their `With*` take no
  callback.
- **Same arguments, same names, same exceptions.** When the caller passes the child in (a symbol,
  a pin, a graphical item), the method is generic in its type, so the callback sees what was passed:
  `symbol.WithGraphicalItem(new KiCadPolyline(), p => p.WithPoint(0, 0))` needs no cast.
- **A `With*` that takes an existing view moves it, as its `Add*` does.** A node has one parent,
  so a symbol, footprint, pin, item or list element that belonged somewhere else leaves it. That
  includes another file. `foreach (var s in source.Symbols) destination.WithSymbol(s)` moves every
  symbol. To leave the original where it is, pass a copy: `new KiCadSymbol(s.Node.Clone())`.
- **An optional `width` is an overload, not a default.** `AddLine` and `AddCircle` default `width`.
  Leave it out of `WithLine`/`WithCircle` and they call the `Add*` without one, so the default stays
  the core's.

`tests/KiCadSharp.Fluent.Tests/MirrorTests` checks the mirror by reflection in both directions.
Every `Add*` must have its `With*`, and every `With*` must match an `Add*`. A new `Add*` in the
core fails that test until it is mirrored.

| Receiver | `Add*` in `KiCadSharp` | `With*` in `KiCadSharp.Fluent` | Callback gets |
|---|---|---|---|
| `KiCadFootprintLibrary` | `AddFootprint(footprint)` | `WithFootprint(footprint, configure?)` | the footprint |
| `KiCadFootprint` | `AddFpText(type, text, x, y, layer)` | `WithFpText(…, configure?)` | `KiCadFpText` |
| `KiCadFootprint` | `AddPad(number, type, shape, x, y, width, height, layers)` | `WithPad(…, configure?)` | `KiCadPad` |
| `KiCadFootprint` | `AddLine(startX, startY, endX, endY, layer, width = 0.12)` | `WithLine(…, layer, configure?)`, `WithLine(…, layer, width, configure?)` | `KiCadFpLine` |
| `KiCadFootprint` | `AddCircle(centerX, centerY, endX, endY, layer, width = 0.12)` | `WithCircle(…, layer, configure?)`, `WithCircle(…, layer, width, configure?)` | `KiCadFpCircle` |
| `KiCadFootprint` | `AddModel(path)` | `WithModel(path, configure?)` | `KiCadModel` |
| `KiCadFpPoly` | `AddPoint(x, y)` | `WithPoint(x, y)` | — |
| `KiCadSymbolLibrary` | `AddSymbol(symbol)` | `WithSymbol(symbol, configure?)` | the symbol |
| `KiCadSymbolLibrary` | `AddSymbol(id)` | `WithSymbol(id, configure?)` | `KiCadSymbol` |
| `KiCadSymbol` | `AddProperty(key, value)` | `WithProperty(key, value, configure?)` | `KiCadProperty`, new or updated |
| `KiCadSymbol` | `AddPin(pin)` | `WithPin(pin, configure?)` | the pin |
| `KiCadSymbol` | `AddGraphicalItem(item)` | `WithGraphicalItem(item, configure?)` | the item, as its own type |
| `KiCadSymbol` | `AddUnit(name)` | `WithUnit(name, configure?)` | `KiCadSymbolUnit` |
| `KiCadSymbolUnit` | `AddPin(pin)` | `WithPin(pin, configure?)` | the pin |
| `KiCadPolyline` | `AddPoint(x, y)` | `WithPoint(x, y)` | — |
| `KiCadBoard` | `AddNet(code, name)` | `WithNet(code, name, configure?)` | `KiCadNet` |
| `KiCadZone` | `AddPoint(x, y)` | `WithPoint(x, y)` | — |
| `KiCadGrPoly` | `AddPoint(x, y)` | `WithPoint(x, y)` | — |
| `KiCadGrCurve` | `AddPoint(x, y)` | `WithPoint(x, y)` | — |
| `KiCadDimension` | `AddPoint(x, y)` | `WithPoint(x, y)` | — |
| `KiCadGroup` | `AddMember(uuid)` | `WithMember(uuid)` | — |
| `KiCadNodeList<T>` | `Add()` | `With(configure?)` | the new `T` |
| `KiCadNodeList<T>` | `Add(item)` | `With(item, configure?)` | the item |
| `KiCadSchematicLine` (`KiCadWire`, `KiCadBus`) | `AddPoint(x, y)` | `WithPoint(x, y)`, returning the wire or bus as its own type | — |
| `KiCadBusAlias` | `AddMember(net)` | `WithMember(net)` | — |

**What is not mirrored:** `SpecctraNode.Add` already returns the form it was called on, so it
chains as it is. `AddKiCad` registers services in DI and does not build a document.

**The `KiCadNodeList<T>` rows matter more than they look.** Most board and schematic items (zones,
tracks, drawings, wires, labels) have no `Add*` of their own. They are appended through a live list,
`board.Zones.Add()`. `With` returns the list, not the board, so it starts its own statement.

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

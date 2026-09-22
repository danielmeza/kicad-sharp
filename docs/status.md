# Status and known gaps

What is not done, and what is lossy. Kept honest on purpose: the write
path of the typed document model is partial.

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

- **`KiCadSchematic` has no view for rule areas, tables, groups or embedded files** (`rule_area`,
  `table`, `group`, `embedded_files`), all of which KiCad 10.0.6 writes at a sheet's top level.
  Everything else it writes there has one: symbols, sheets, wires, buses, bus entries, junctions,
  no-connects, the four kinds of label, text, text boxes, graphics, images, the symbol cache and the
  page map.
- **`KiCadBoard` has no view for text boxes, tables, reference images, targets, points, barcodes or
  generated items** (`gr_text_box`, `table`, `image`, `target`, `point`, `barcode`, `generated`),
  nor for the board's `property`, `variants` and `embedded_files`. KiCad 10.0.6 writes all of them at
  a board's top level. Of `setup`, `KiCadBoardSetup` names the stackup, the mask and paste clearances
  and whether footprints may bridge solder mask; the via tenting and plugging, the zone defaults, the
  origins and the plot parameters are raw.

What has no view round-trips intact and is reachable through `Node`.

**Copper geometry and Specctra.**

- **A padstack that differs per layer** (KiCad 9's `(padstack (mode custom) …)`) is described by its
  main shape on every layer, in both `CopperGeometry` and the design a board exports.
- **A custom pad** is exported to a design as the convex hull of its primitives, which covers it;
  KiCad exports the outline of their union. In `CopperGeometry` its lines, polygons, square-cornered
  rectangles and filled circles are exact, and an arc or a stroked circle among them is grown by
  `maxError`, as a track arc is. **A Bézier primitive is dropped from both, and a rounded rectangle
  is drawn with square corners** (#77).
- **A session's `placement` is not applied.** A router does not move parts.
- **Net classes are an input, not something read.** They live in the `.kicad_pro`, resolved through
  patterns and priorities; `SpecctraOptions` takes them resolved.

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
  `GetBoardEditorAppearanceSettings`/`SetBoardEditorAppearanceSettings`; from
  `editor_commands.proto`: `GetItemsById`, `GetBoundingBox`, `AddToSelection`,
  `RemoveFromSelection`, `HitTest`, `GetTitleBlockInfo`/`SetTitleBlockInfo`,
  `SaveSelectionToString`, `ParseAndCreateItemsFromString`; and from `base_commands.proto`:
  `GetTextExtents`, `GetTextAsShapes`. Send them by hand with
  `KiCadIPCClient.Send<TResult>` — the message types are all in `KiCadSharp.Protos`.
- **`KiCad.RefreshPaths()` and `KiCad.ImportLibrary(...)` are no-ops.** The commands they would send
  do not exist in KiCad's IPC API; the methods return `ValueTask.CompletedTask` and do nothing. A
  `GetPath(PathType)` is commented out for the same reason.
- **A KiCad that goes away *after* taking the request is not reported by nng.** Measured against
  the in-process peer: the receive goes on answering "not yet", and the token or `RequestTimeout` is
  what ends the call. With neither, it waits.
- **Nothing interrupts a dial.** `Connect()`, and a `Send` that has to connect first, block in nng's
  dial, which returns at once against a path with nothing at it and after nng's own 10 s against a
  socket that never completes the handshake. The caller's token is read before the dial, not during
  it, and `Dispose()` does not end it either: a call it catches there ends with its
  `OperationCanceledException` only when the dial returns.
- **No `Async` suffixes, and one sync/async asymmetry**: `GetProject(DocumentSpecifier)` is
  synchronous while the parameterless `GetProject()` is not.

**Repository.**

- **The nng wrapper is `internal`, and stays that way.** `KiCadSharp.Interop` is not part of the
  public surface: a consumer that wants nng for its own purposes should reference nng, not reach
  through this. `[InternalsVisibleTo("KiCadSharp.Tests")]` is what lets the tests exercise it.
- **The build still restores `nng.NET`, for its native files only.** `dotnet list package` shows it,
  because it is genuinely a build-time input: the `libnng` binaries this package ships are copied out
  of it, pinned and hash-verified by NuGet rather than committed here. It is declared
  `ExcludeAssets="all" PrivateAssets="all"`, so no consumer sees it and the packed nuspec names no nng
  package at all.
- **`tests/KiCadSharp.Tests` is the only gate on the document layer.** Tests over the vendored KiCad
  10 fixtures, with the byte counts in the assertions. The IPC surface is covered two ways: the
  transport, its timeout behaviour and every way a call fails against an in-process nng peer
  (`NngInteropTests`, `IpcTimeoutTests`, `IpcFailureTests`, no KiCad needed), and the client against a real KiCad (`IpcTests`, whose
  live tests return early unless `KICADSHARP_IPC_SOCKET` names a socket — see
  `scripts/kicad-ipc-container.sh`; the wiring tests at its top need no KiCad). The
  tests that shell out to `kicad-cli` return early unless `KICADSHARP_KICAD_CLI` points at one.
  `tests/KiCadSharp.Fluent.Tests` covers the fluent package: every `With*` against the `Add*` it
  mirrors, a reflection check that the two sets match, and a footprint and a symbol library that
  must save identically in both styles. Run them all with `dotnet test KiCadSharp.slnx`;
  [building.md](building.md#testing) lists the variables they read and where they write files.

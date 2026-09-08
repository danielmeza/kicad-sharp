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
  transport and its timeout behaviour against an in-process nng peer (`NngInteropTests`,
  `IpcTimeoutTests`, no KiCad needed), and the client against a real KiCad (`IpcTests`, which returns
  early unless `KICADSHARP_IPC_SOCKET` names a socket — see `scripts/kicad-ipc-container.sh`). The one
  test that shells out to `kicad-cli` returns early unless `KICADSHARP_KICAD_CLI` points at one. Run
  them with `dotnet test KiCadSharp.slnx`.

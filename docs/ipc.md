# The IPC client

What we measured about KiCad's IPC API: which editors answer, which platforms
the transport reaches, and where it does not work.

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

### nng, and which platforms it reaches

The transport is nng. `KiCadSharp` calls it through a **P/Invoke wrapper of thirteen entry points**
(`src/KiCadSharp/Interop`) — `nng_req0_open`, `nng_dial`, `nng_sendmsg`, `nng_recvmsg`, the four
`nng_msg_*` calls the envelope needs, `nng_close`, `nng_socket_set_ms`, `nng_strerror`,
`nng_version` — and ships the native library itself, under `runtimes/<rid>/native/`.

That native library is not new. It was always there: `libnng.so` is a native asset of `nng.NET`, so
publishing a consumer self-contained has always put a **540,512-byte `libnng.so`** beside the
executable. What is gone is the managed binding on top of it, and the `Rebus` message bus and
`Newtonsoft.Json` that arrived with it — see [Pending](#pending).

| Runtime identifier | Shipped | File |
|---|---|---|
| `linux-x64`, `linux-arm64`, `linux-arm` | yes | `libnng.so` (nng 1.3.2) |
| `osx-x64` | yes | `libnng.dylib` (nng 1.3.2) |
| `win-x64`, `win-x86` | yes | `nng.dll` (nng 1.4.0) |
| `osx-arm64`, `win-arm64`, `linux-musl-*` | **no** | — |

Those six are exactly what upstream publishes, and exactly what this library carried before, so no
platform gains or loses support here. On a platform that is not in the list, or in a container that
does not have libnng's own dependencies, set **`KICADSHARP_NNG_LIBRARY`** to the full path of a
`libnng` to load instead (`brew install nng`, a distribution package, your own build). Nothing else
has to be shipped: on the six above the file arrives with the package and is found by the runtime.

**What libnng itself links against**, read out of the shipped binaries. Nothing here is bundled —
these are the host's own libraries, and the only ones a consumer may have to install are on the
right:

| | Links | Not guaranteed present |
|---|---|---|
| Linux | `librt.so.1`, `libpthread.so.0`, `libnsl.so.1`, `libatomic.so.1`, `libc.so.6` | **`libatomic.so.1`** — a bare `ubuntu:24.04` has none (`apt install libatomic1`). glibc, so **no musl**. |
| macOS | `/usr/lib/libSystem.B.dylib` | — nothing. |
| Windows | `WS2_32`, `ADVAPI32`, `KERNEL32`, the UCRT — all in-box since Windows 10 | **`VCRUNTIME140.dll`** — the Visual C++ 2015–2022 Redistributable. Very widely installed, but not part of Windows and not required by .NET. |

Two failure modes follow from that, both measured:

- **A slim container.** A bare `ubuntu:24.04` has no `libatomic1` and the load fails there. On musl
  (Alpine) this build cannot load at all; supply one and point `KICADSHARP_NNG_LIBRARY` at it.
- **`linux-musl-x64` publishes look fine and are not.** NuGet's RID fallback hands the glibc
  `libnng.so` to a musl publish, so the file is present and unloadable.

All of this is unchanged from when the binding was `Rebus.nng` — it is the same `libnng`, and these
are the same requirements it always had. What is new is that the exception says which one it is.

Either way the exception says which of the two it is, names every path that was tried, and names the
environment variable — rather than the loader's bare `DllNotFoundException`.

### What the master pin adds, and what KiCad answers

`protos/KICAD_PIN` points at KiCad `master` (builds as `10.99.0`, the 11.0 line). Every command
that master declares and 10.0.6 did not has a typed method now; the table is which ones KiCad
registers a handler for at the pinned commit `965fb680`, read out of `api_handler_*.cpp`. A command
without a handler is answered `AS_UNHANDLED`, which the client throws as `ApiException`.

| Area | Methods | Handled on master |
|---|---|---|
| Embedded files (10.0.7+) | `Board.GetEmbeddedFiles` / `AddEmbeddedFiles` / `AddEmbeddedFile` / `SetEmbeddedFiles`, `EmbeddedFileCodec` | yes, pcbnew |
| Design variants (10.0.7+) | `KiCadDocument.GetVariants` … `GetCurrentVariant` | yes, pcbnew and eeschema |
| Commit document header (10.0.7+) | `BeginCommit` / `PushCommit` / `DropCommit` carry `Document` | yes; KiCad says it will require it |
| Jobs | `KiCadDocument.RunJob` with any of the 15 `RunBoardJob*` and 7 `RunSchematicJob*` messages | yes, all 22 |
| Design rules, plot settings, netlist | `Board.GetDesignRules` / `SetDesignRules` / `GetCustomDesignRules` / `SetCustomDesignRules` / `GetPlotSettings` / `SetPlotSettings` / `ImportNetlist` | yes, pcbnew |
| Page settings, modified state, focus | `KiCadDocument.GetPageSettings` / `SetPageSettings` / `GetModifiedState` / `FocusOnItems` | yes, both editors |
| Schematic | `KiCad.GetSchematic`, `Schematic.GetHierarchy` / `GetNetlist` / `PlaceSymbolFromLibrary`, and everything on `KiCadDocument` | yes, eeschema |
| Placing from a library | `Board.PlaceFootprintFromLibrary`, `Schematic.PlaceSymbolFromLibrary` | yes |
| Library queries | `KiCad.GetLibraryStatuses` / `ReloadLibrary` / `LoadAllLibraries` / `GetLibraryItems` / `GetItemsFromLibrary` | yes, `api_handler_libraries.cpp` |
| Library **table editing** | `KiCad.GetLibraryTable` / `AddLibraryTableEntry` / `UpdateLibraryTableEntry` / `DeleteLibraryTableEntry` / `ImportLibrary` / `SearchLibraries` | **no** — declared `Since: 11.0`, no handler yet; `IpcMasterTests` pins the `AS_UNHANDLED` |
| Documents | `KiCad.OpenDocument` / `CreateDocument` / `CloseDocument` / `CloseAllDocuments` | yes, but only in `kicad-cli api-server` mode |
| Library items in their editor | `KiCad.OpenLibraryItem` | footprint editor only |
| Paths, net class assignments | `KiCad.GetPaths`, `Project.GetNetClassAssignments` / `SetNetClassAssignments` | yes |
| Cross-probe | `KiCad.SyncSelection` / `HighlightNets` / `CrossProbeAnnounce` | yes, both editors; `CrossProbeAnnounce` is marked internal by KiCad |
| | `KiCad.FocusOnItem` (by reference or pad; `FocusOnItems` by id is the handled one) | **no** |

Every method's wire shape — which message, which document or header, how the reply unpacks — is
pinned in `tests/KiCadSharp.Tests/IpcCommandTests.cs` against an in-process nng peer. What KiCad
does with them is measured live, against master, by `IpcMasterTests` and `IpcSchematicTests`.

`GetVersion()` is how to tell the two apart at runtime: master reports `10.99.0`, and
`KiCadVersion.SupportsLibraryCommands` and friends turn that into a check a caller can branch on.

### Running the live tests against KiCad master

The harness has a flavor switch. `stable` (the default) runs the 10.0.6 release image;
`nightly` runs the **dev image**, `ghcr.io/danielmeza/orbion-kicad-dev:10.99`, which is the
`dev` stage of the same Containerfile: the nightly PPA's KiCad master installed beside 10.0.6,
as `pcbnew-nightly` / `eeschema-nightly` with its own `kicad/10.99` configuration directory.
`scripts/kicad-dev-image.sh` builds and publishes that image, and says which nightly went in.

```sh
export KICADSHARP_KICAD_FLAVOR=nightly
eval "$(scripts/kicad-ipc-container.sh start tests/KiCadSharp.Tests/data/kicad10-pcbnew.kicad_pcb)"
eval "$(scripts/kicad-ipc-container.sh start tests/KiCadSharp.Tests/data/duplicate-refs/duplicate-refs.kicad_sch)"
dotnet test KiCadSharp.slnx -c Release
scripts/kicad-ipc-container.sh stop
```

One container per editor: the first `start` exports `KICADSHARP_IPC_SOCKET`, the second
`KICADSHARP_IPC_SCHEMATIC_SOCKET`, and each also exports the host directory the container sees
as `/project`, which is where a job's output lands. Every live test asks the KiCad it reached what
it supports and returns early otherwise, so the same suite passes against both flavors; the
`ipc-nightly` job in CI runs it against the dev image on every push.

**MEASURED 2026-09-21**, nightly `10.99.0-unknown-6e93fd642e` (KiCad master of that morning) in
the dev image, pcbnew and eeschema both up:

| Suite | Against master | Against 10.0.6 |
|---|---|---|
| `IpcTests` (the 10.0.6 surface) | 11 pass | 11 pass |
| `IpcMasterTests` (board, master additions) | 17 pass | 17 pass, 14 of them returning early |
| `IpcSchematicTests` (eeschema) | 10 pass | 10 pass, all returning early: eeschema on 10.0.6 answers nothing |

Four things the live run found that the protos do not say:

- **eeschema holds the main loop behind two one-button dialogs** when it opens the fixture
  ("an error was found when loading the schematic that has been automatically fixed", then "Load
  Schematic"), and answers `AS_NOT_READY` until they are gone. The harness gives each dialog focus
  and sends Return. That needs the display's auth file: `xvfb-run` guards its display with an
  Xauthority under `/tmp`, a `podman exec` does not inherit it, and without it `xdotool` connects
  to nothing and reports no windows. An earlier harness clicked coordinates and appeared to do
  nothing for exactly that reason.
- **pcbnew reports a project path its own validation rejects.** `GetOpenDocuments` fills the
  project path from `GetProjectDirectory()`, no trailing separator; `validateProject` in
  `api_handler_common.cpp` compares against `GetProjectPath()`, which by contract ends with one;
  eeschema's `PackProject` reports the latter. Sending pcbnew's specifier straight back to
  `GetNetClasses` gets "the requested project kicad10-pcbnew is not open at path /project".
  `Project` adds the separator, which is what the other two agree on. Worth a report upstream.
- **`LoadAllLibraries` answers `AS_UNIMPLEMENTED` in the GUI** ("not available in GUI mode"),
  where the proto says it is a no-op. It is for `kicad-cli api-server`. `GetLibraryStatuses`,
  `GetLibraryItems` and `GetItemsFromLibrary` work; libraries load in the background after the
  editor starts, and an unloaded library lists as empty, so poll the status first.
- **A placed symbol is a `SchematicSymbolInstance`**, not a `SchematicSymbol` (the library
  definition), the way a placed footprint is a `FootprintInstance`. `Schematic.GetItems` returns
  instances and `PlaceSymbolFromLibrary` unpacks one.

A unix socket path is limited to 107 bytes, so the harness keeps its state under short names and
refuses a path that would not fit; set `KICADSHARP_IPC_STATE` to somewhere shorter if it does.

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

So on 10.0.x a schematic API is a KiCad 11 story. `.kicad_sch` files are read and written on disk
instead, through `SExpressions` or the CLI. Against master, `eeschema` registers handlers for the
whole editor surface plus hierarchy, netlist, variants, jobs and symbol placement, and that is what
`Schematic` wraps — measured, see the table above: hierarchy, netlist, symbols by sheet path,
commits, variants, page settings and an SVG export all answer.

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

### What KiCad puts in a plugin's environment

Measured against KiCad 10.0.6: pcbnew in `ghcr.io/danielmeza/orbion-kicad-release:10.0.6`, with a
fresh `HOME`, running an `exec` API plugin whose action dumps its environment. Before pcbnew started,
the container had set none of these variables. `KiCadEnvironment` reads each one:

| Variable | Value in that run | Read by |
|---|---|---|
| `KICAD_API_SOCKET` | `ipc:///tmp/kicad/api.sock` | `GetApiSocket()` |
| `KICAD_API_TOKEN` | a GUID | `GetApiToken()` |
| `KIPRJMOD` | the open board's directory | `GetProjectDirectory()` |
| `KICAD_USER_TEMPLATE_DIR` | `~/.local/share/kicad/10.0/template/` | `GetUserTemplateDirectory()` |
| `KICAD10_SYMBOL_DIR` | `/usr/share/kicad/symbols/` | `GetSymbolDirectory()` |
| `KICAD10_FOOTPRINT_DIR` | `/usr/share/kicad/footprints/` | `GetFootprintDirectory()` |
| `KICAD10_3DMODEL_DIR` | `/usr/share/kicad/3dmodels/` | `GetModelsDirectory()` |
| `KICAD10_DESIGN_BLOCK_DIR` | `/usr/share/kicad/blocks/` | `GetDesignBlockDirectory()` |
| `KICAD10_TEMPLATE_DIR` | `/usr/share/kicad/template/` | `GetTemplateDirectory()` |
| `KICAD10_3RD_PARTY` | `~/.local/share/kicad/10.0/3rdparty/` | `GetThirdPartyDirectory()` |

A `python` plugin also gets `VIRTUAL_ENV` (`GetPythonVirtualEnvironment()`). An `exec` plugin does not.

Where they come from, in KiCad 10.0.6's source:

- **`KICAD_API_SOCKET` and `KICAD_API_TOKEN`** are added to a copy of KiCad's own environment
  when `API_PLUGIN_MANAGER::InvokeAction` launches the plugin (`common/api/api_plugin_manager.cpp`,
  lines 369-372 for `python` and 464-467 for `exec`). The copy is `wxGetEnvMap`, so the plugin gets
  everything else KiCad has too. `VIRTUAL_ENV` is added at line 390, for `python` only.
- **The path variables are KiCad's own.** `COMMON_SETTINGS::InitializeEnvironment` defines them
  (`common/settings/common_settings.cpp`, lines 865-871). `PGM_BASE::loadCommonSettings`
  (`common/pgm_base.cpp`, 536-558) then sets each one in KiCad's process environment, unless it is
  already set there.
- **`KIPRJMOD`** is set when a project is loaded (`SETTINGS_MANAGER::LoadProject`,
  `common/settings/settings_manager.cpp:1053`).

**The number in the path variables is KiCad's major version.** `ENV_VAR::GetVersionedEnvVarName`
(`common/env_vars.cpp:78-84`) formats `KICAD%d_%s` with the major version from
`GetMajorMinorPatchTuple()`. `cmake/BuildSteps/WriteVersionHeader.cmake` takes that number from the
release number. KiCad 9 set the same six as `KICAD9_*`, by the same code (9.0.9.1,
`common/settings/common_settings.cpp:678-698`). A 10.99 nightly still sets `KICAD10_*`: KiCad's master
branch (checked at `21d616e3`, version 10.99.0) builds the names the same way. By the same rule,
KiCad 11 will set `KICAD11_*`.

**An older name can be there as well.** KiCad also exports every variable saved in
`kicad_common.json`'s `environment.vars`. When it migrates settings from an older version, it drops
only the `KICAD6_*`, `KICAD7_*` and `KICAD8_*` names (`SETTINGS_MANAGER::MigrateFromPreviousVersion`,
`settings_manager.cpp:683-705`). So a `KICAD9_*` value that a user customised in KiCad 9 survives.
With one planted in that file, the plugin got `KICAD9_SYMBOL_DIR` next to `KICAD10_SYMBOL_DIR`.

So the path getters read **the newest version that is set**:

1. Every variable named `KICAD`, then digits, then `_` and the base name is a candidate. The base
   names are `SYMBOL_DIR`, `FOOTPRINT_DIR`, `3DMODEL_DIR`, `DESIGN_BLOCK_DIR`, `TEMPLATE_DIR` and
   `3RD_PARTY`. KiCad 5's unversioned names (`KICAD_SYMBOL_DIR`, `KISYSMOD`, `KISYS3DMOD`) are not
   read.
2. An empty value counts as unset, as it does in KiCad's `InitializeEnvironment`.
3. The highest version wins, compared as a number, so 10 beats 9.

In a plugin that KiCad launched, that is the running KiCad's own variable. KiCad defines all six
for its own version and none for any other version. Another version's name gets there only if the
user's environment or `kicad_common.json` carries it, and in practice that is an older one. The
rule depends on no fixed version, so KiCad 9 still works, and so will KiCad 11. On Windows, names
match regardless of case.

When the version is known, `KiCadEnvironment.GetVersionedVariable(baseName, majorVersion)` reads
that version's name first, and falls back to the newest one. Pass it `KiCadVersion.Major` from
`KiCad.GetVersion()`. This is KiCad's own rule (`ENV_VAR::GetVersionedEnvVarValue`,
`env_vars.cpp:103-118`), with one difference in the fallback: KiCad takes whichever name sorts first
in its map, and this takes the newest.

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

The library that variable names is checked as it loads: it has to export all thirteen functions
above. One that loads and lacks any of them is refused, and every connection attempt fails with a
`KiCadConnectionException` that names the variable, the path and the functions it lacks. The
runtime's `EntryPointNotFoundException` for the first missing function is its inner exception.

A value that does not load at all, because there is no file at that path or the platform loader
refuses it, is refused the same way, with the loader's own exception inside. Once the variable is
set, the library it names is used or the connection fails: the shipped `libnng` is never loaded in
its place. An empty variable counts as not set.

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

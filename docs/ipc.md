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

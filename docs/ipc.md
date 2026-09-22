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

### Where KiCad listens, for a client it did not launch

A plugin that KiCad launches gets `KICAD_API_SOCKET`. Any other client has no such variable: a
test, a CLI, or an application started by hand. For those, `AddKiCad` uses
`KiCadEnvironment.GetDefaultSocketPath()`, which works the address out the way KiCad 10.0.6 does.

`KICAD_API_SERVER::Start` (`common/api/api_server.cpp:79-86`) builds `<temp>/kicad/api.sock` with
`wxFileName`, and listens on `ipc://` followed by that path (lines 127-128). `<temp>` is:

- **macOS:** `/tmp`, always (the `__WXMAC__` branch). macOS sets `TMPDIR` for every user, and
  KiCad ignores it.
- **Everywhere else:** `wxStandardPaths::GetTempDir()`, which is `wxFileName::GetTempDir()`
  (`src/common/stdpbase.cpp`). The code is the same in wxWidgets 3.2.9, which the 10.0.6 image
  links, and in 3.3.1, which KiCad's Windows build pins in `vcpkg.json` (`src/common/filename.cpp`,
  lines 1215-1266 and 1228-1279). It works like this:
  1. It takes the first of `TMPDIR`, `TMP` and `TEMP` that names an **existing** directory.
     `TMPDIR` counts on Windows too.
  2. If none does, Windows falls back to the Win32 `GetTempPath`.
  3. Trailing separators are removed.
  4. On Unix, when nothing matched, it uses `/tmp`. If `/tmp` does not exist either, it uses `.`,
     which is KiCad's working directory.

`wxFileName` then drops empty components, so `/tmp//x/` becomes `/tmp/x`. On Windows, `\` and `/`
both separate, and the result is written with `\`.

**On Windows, `ipc://` is a named pipe, not a file.** Both nng 1.11 (KiCad's Windows build, and the
Linux image) and nng 1.4.0 (what this package ships for Windows) open `\\.\pipe\` followed by the
path (1.11's listener in `win_ipclisten.c:211`, 1.4.0's dialer in `win_ipcdial.c:240`, the prefix
in `win_ipc.h:20` in both). Nothing
appears on disk, and the client has to spell the path exactly as KiCad does. Until #97 this method
wrote `…\Temp\\kicad\api.sock`: `Path.GetTempPath()` already ends in `\`, and the code added
another.

The tests that need a peer which takes nng's connection and holds, or never completes, its
handshake bind a Unix domain socket (`HeldHandshake`), which nng never dials on Windows: the dial
fails at once with "Connection refused". They are `[UnixSocketFact]`s, skipped there with a message
that says so (#106). MEASURED 2026-09-22 on GitHub's `windows-11-arm` runner, nng 1.4.0, in pull
request #125's first run: the same two silent-socket tests, run against a `NamedPipeServerStream`
on the name nng derives, failed "Timed out" after nng's 10 s, as on Linux. So the trap they pin is
real on Windows too; only the peer differs.

Measured on Linux, in `ghcr.io/danielmeza/orbion-kicad-release:10.0.6` with no network and a fresh
`HOME`. Each row started pcbnew with only the variables shown. Three things were read for each:
- the Unix sockets pcbnew was listening on (`/proc/net/unix`);
- the `KICAD_API_SOCKET` it handed an `exec` plugin;
- `GetDefaultSocketPath()` from a probe run with the same variables and no `KICAD_API_SOCKET`.

The probe then connected and asked for `GetVersion()` and `GetBoard()`.

| pcbnew started with | Listening on | Plugin got, and the probe computed | Probe connected |
|---|---|---|---|
| nothing | `/tmp/kicad/api.sock` | `ipc:///tmp/kicad/api.sock` | yes |
| `TMPDIR=/tmp/custom-b` | `/tmp/custom-b/kicad/api.sock` | `ipc:///tmp/custom-b/kicad/api.sock` | yes |
| `TMPDIR=/does/not/exist TMP=/tmp/tmpvar-c` | `/tmp/tmpvar-c/kicad/api.sock` | `ipc:///tmp/tmpvar-c/kicad/api.sock` | yes |
| `TMPDIR=/tmp//dbl-d/` | `/tmp/dbl-d/kicad/api.sock` | `ipc:///tmp/dbl-d/kicad/api.sock` | yes |
| a 92-character `TMPDIR` | its 107-byte path | the same | yes |
| a 93-character `TMPDIR` | **nothing** | its 108-byte path | no: `KiCadConnectionException`, "Address invalid" |
| a 120-character `TMPDIR` | **nothing** | its 135-byte path | no, the same |

Before #97, the method returned `ipc:///tmp/kicad/api.sock` in every row.

**When the path does not fit, KiCad has no API socket at all.** On Linux a socket path has to fit
in `sun_path`: 108 bytes, including the terminating NUL. KiCad adds `/kicad/api.sock`, 15
characters, so a `TMPDIR` of up to 92 characters works and a longer one does not. nng refuses the
path in one of two places:
- over 128 bytes, when the listener is created (`nni_ipc_listener_alloc`,
  `posix_ipclisten.c:463-478`);
- over 107, when it starts (`ipc_listener_listen`, through `nni_posix_nn2sockaddr`,
  `posix_sockaddr.c:74-78`).

KiCad's `KINNG_REQUEST_SERVER::listenThread` (`libs/kinng/src/kinng.cpp:102-114`) logs a failed
create and ignores the result of `nng_listener_start`. Either way, `KICAD_API_SERVER::Running()`
stays true: it only asks whether the thread is joinable. So KiCad still hands its plugins the
address, as the last two rows show, and it leaves `kicad/api.lock` behind with no socket beside it.

`GetDefaultSocketPath()` returns the address KiCad chose even then. A shorter path would lead
nowhere, because nothing listens anywhere. The dial fails at once, as a `KiCadConnectionException`.

On Windows, nng refuses a path of 128 characters or more when the listener is created
(`win_ipclisten.c:321-328`). That case was not measured.

**The client says when the path is the problem.** nng's own refusal is `NNG_EADDRINVAL`, "Address
invalid", which reads like a malformed URL (#108). So before it dials, `KiCadIPCClient` counts the
UTF-8 bytes after `ipc://` against the platform's limit, and a path over it fails as a
`KiCadConnectionException` that says so: "the socket path is 108 bytes long, and Linux takes at
most 107 bytes (sun_path is 108 bytes, with its NUL)". The limits, in `IpcPathLimit`:

| Platform | Longest path | Where nng stops |
|---|---|---|
| Linux | 107 bytes | `sun_path`, 108 bytes with its NUL: nng 1.3.2 `posix_sockaddr.c:66-69`, through `posix_ipcdial.c:168-172` |
| macOS | 103 bytes | `sun_path`, 104 bytes with its NUL; the same code |
| Windows | 127 bytes | `NNG_MAXADDRLEN`, 128: nng 1.4.0 `win_ipcdial.c:230-235`, when the dialer is created |

MEASURED 2026-09-22, from `NngRequestSocket.Dial` against the nng this package ships for each
platform (`NngInteropTests.APathOneByteOverThePlatformsLimitIsAddressInvalidToNng`): a path of the
limit with nothing listening fails with "Connection refused", one byte more with "Address invalid".
On `linux-x64` (107 and 108), on GitHub's `macos-14` runner (`osx-arm64`, 103 and 104) and on its
`windows-11-arm` runner (`win-arm64`, 127 and 128).

One more case has no default address that could be right: **a second KiCad**, started while the
first one holds `api.sock`, listens on `api-<pid>.sock` in the same directory
(`api_server.cpp:115-125`). The default reaches the first.

### KiCad from Flathub, for a client outside its sandbox

A KiCad installed from Flathub (`org.kicad.KiCad`) runs in a Flatpak sandbox, and from the host the
rule above gives an address where nothing listens (#110). So on Linux `GetDefaultSocketPath()` has
one more step: when no socket exists at the address the rule gives, and one exists at
`~/.var/app/org.kicad.KiCad/cache/tmp/kicad/api.sock`, it returns that one. Existence is
`File.Exists`, which is true for a socket.

Why that path, from the sources:

- **The Flathub manifest starts KiCad with `TMPDIR=/var/tmp`** (`flathub/org.kicad.KiCad`,
  `org.kicad.KiCad.yml`, `finish-args`, at `279821a`). Commit `761588f` (2025-02-19) first shared the
  host's `/tmp` into the sandbox, "Required for default path of KiCad's IPC API"; `8d96620`
  (2025-02-21, "Change temp directory to /var/tmp, disable access to host /tmp") replaced that with
  the variable.
- **Flatpak binds the sandbox's `/var/tmp` to `<app data>/cache/tmp`**, and the app data directory
  is `g_get_home_dir()/.var/app/<app id>` (flatpak 1.14.6, `common/flatpak-run.c`, lines 3617 and
  2096). `g_get_home_dir()` is `$HOME`, then the passwd entry, which is also how .NET's
  `Environment.GetFolderPath(UserProfile)` finds the home directory on Unix.
- So `KICAD_API_SERVER::Start` puts the socket at `/var/tmp/kicad/api.sock` inside the sandbox,
  which is `~/.var/app/org.kicad.KiCad/cache/tmp/kicad/api.sock` outside it.

kipy tries the same path (`_default_socket_path` in `kipy/kicad.py`, at `3dcb6c9`), with one
difference: kipy looks in the Flathub directory first, whenever that file exists. Here KiCad's own
rule comes first, so a native KiCad that is listening is never passed over for a socket file the
Flatpak left behind.

Measured on Linux, flatpak 1.14.6, with org.kicad.KiCad 10.0.6 from Flathub installed for the user
(`flatpak install --user --no-related`), pcbnew started with `flatpak run --command=pcbnew` on a
board, and no `TMPDIR` set on the host:

| | Inside the sandbox | On the host |
|---|---|---|
| `TMPDIR` in pcbnew's environment (`/proc/<pid>/environ`) | `/var/tmp` | not set |
| `/var/tmp` | a bind mount of `~/.var/app/org.kicad.KiCad/cache/tmp` (ext4) | that directory |
| `/tmp` | a private tmpfs, `/.flatpak/org.kicad.KiCad/tmp` | the host's, which the sandbox never sees |
| `$XDG_RUNTIME_DIR` (`/run/user/1000`) | a private tmpfs, `/.flatpak/org.kicad.KiCad/xdg-run` | the host's |
| pcbnew listening (`ss -xlp`) | `/var/tmp/kicad/api.sock` | `~/.var/app/org.kicad.KiCad/cache/tmp/kicad/api.sock`, a socket; `File.Exists` is true |
| a raw `connect()` from a host process | | succeeds |
| `GetDefaultSocketPath()` before #110 | | `ipc:///tmp/kicad/api.sock`; the dial failed, "Connection refused (nng error 6)" |
| `GetDefaultSocketPath()` now | | `ipc://~/.var/app/org.kicad.KiCad/cache/tmp/kicad/api.sock`; `GetVersion()` answered 10.0.6 and `GetBoard()` named the open board |
| the same, with a socket bound at `/tmp/kicad/api.sock` as well | | `ipc:///tmp/kicad/api.sock`: KiCad's own address wins |

**No `flatpak override` is needed.** The socket is an ordinary inode on the host's file system, and
the manifest already put it there. A plugin that the Flatpak KiCad launches is not affected either
way: it runs inside the sandbox and gets `KICAD_API_SOCKET`.

The mechanism is not KiCad's: any Flatpak started with `--env=TMPDIR=/var/tmp` puts a socket bound
under `$TMPDIR` at `~/.var/app/<app id>/cache/tmp` on the host, where a host process connects to it.
Measured with a shell in `org.freecad.FreeCAD`'s sandbox and a socket bound from Python, before the
KiCad Flatpak was installed.

**Stale socket files count.** pcbnew sent `SIGTERM` was gone within a second and left `api.sock`
behind; `GetDefaultSocketPath()` then still returned the Flathub address, and the dial failed with
"Connection refused (nng error 6)", as it does for a native KiCad's stale socket. KiCad removes such
a file the next time it starts: it takes `flock` on `api.lock` in the same directory, and holding it
proves the old socket is orphaned (`api_server.cpp:96-112`). Measured: a stale `api.sock` from an
earlier session was gone once pcbnew had started, and an `api-2.sock` from a second instance of that
session, which nothing cleans up, was still there. A clean exit closes the nng listener, which
unlinks the path (nng 1.10.1, `posix_ipclisten.c:59-62`); that was not measured.

What is left out:

- A Flatpak KiCad started while another holds `api.sock` listens on `api-<pid>.sock` there, as a
  native one would; no default can know the pid.
- A Flatpak with another app id, or one whose `TMPDIR` the user overrode
  (`flatpak override --user --env=TMPDIR=… org.kicad.KiCad`), listens somewhere else. Set
  `KICAD_API_SOCKET` to the socket as the host sees it.
- A client that is itself inside a Flatpak sandbox sees its own private `/tmp`, and whether it sees
  another app's `~/.var/app` directory depends on its own permissions. Not measured.
- Nothing changes on macOS or Windows: Flatpak is Linux only, and no file is looked at there.

### nng, and which platforms it reaches

The transport is nng. `KiCadSharp` calls it through a **P/Invoke wrapper of thirteen entry points**
(`src/KiCadSharp/Interop`) — `nng_req0_open`, `nng_dial`, `nng_sendmsg`, `nng_recvmsg`, the four
`nng_msg_*` calls the envelope needs, `nng_close`, `nng_socket_set_ms`, `nng_strerror`,
`nng_version` — and ships the native library itself, under `runtimes/<rid>/native/`.

On six platforms that native library is not new. It was always there: `libnng.so` is a native asset
of `nng.NET`, so publishing a consumer self-contained has always put a **540,512-byte `libnng.so`**
beside the executable. What is gone is the managed binding on top of it, and the `Rebus` message bus
and `Newtonsoft.Json` that arrived with it — see [Pending](#pending).

| Runtime identifier | Shipped | File | From |
|---|---|---|---|
| `linux-x64`, `linux-arm64`, `linux-arm` | yes | `libnng.so` (nng 1.3.2) | `nng.NET` |
| `osx-x64` | yes | `libnng.dylib` (nng 1.3.2) | `nng.NET` |
| `osx-arm64` | yes | `libnng.dylib` (nng 1.3.2) | built here |
| `win-x64`, `win-x86` | yes | `nng.dll` (nng 1.4.0) | `nng.NET` |
| `win-arm64` | yes | `nng.dll` (nng 1.4.0) | built here |
| `linux-musl-*`, anything else | **no** | — | |

`nng.NET` publishes the first six, and the build copies them out of it unchanged. It publishes
nothing for Apple silicon or Windows on Arm, so those two are built in this repository by
`scripts/build-nng.sh` and committed under `native/`:

- **Which nng.** The release the same platform's `nng.NET` binary is, so that every Mac runs one nng
  and every Windows machine runs one nng: 1.3.2 for `osx-arm64`, 1.4.0 for `win-arm64`.
  `native/NNG_PIN` pins each tag to its commit and records the SHA-256 of each file.
- **How.** `nng.NET`'s own CMake flags (a shared library, `Release`, no tests, no tools), the target
  architecture, a deployment target of macOS 11.0 (the first macOS on Apple silicon), and MSVC's
  `/Brepro`, which stamps a content hash where the link time would go so that two builds can be
  compared. `NNG_ELIDE_DEPRECATED` stays off. `nng.NET`'s script asks for it, but its Windows DLLs
  still export the deprecated functions it removes, and the new DLL should export what they export.
  The header of `scripts/build-nng.sh` has the details.
- **Checked.** Each one exports the same nng functions as its x64 counterpart (495 on macOS, 497 on
  Windows) and links the same system libraries. The `nng` workflow rebuilds both on their own
  hardware, GitHub's `macos-14` and `windows-11-arm`, and fails unless each rebuild is the committed
  file byte for byte. CI runs the test suite on those two machines too, on Windows without one
  test that fails there whatever the architecture (#107). Every packed `KiCadSharp` is checked
  for exactly these eight files, each built for its own architecture (`scripts/check-natives.sh`), in
  CI and before a release.

On a platform that is not in the list, or in a container that does not have libnng's own
dependencies, set **`KICADSHARP_NNG_LIBRARY`** to the full path of a `libnng` to load instead (a
distribution package, your own build). Nothing else has to be shipped: on the eight above the file
arrives with the package and is found by the runtime.

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
| macOS | `/usr/lib/libSystem.B.dylib` | — nothing. `osx-x64` is built for macOS 10.14 and later, `osx-arm64` for 11.0 and later. |
| Windows | `WS2_32`, `ADVAPI32`, `KERNEL32`, the UCRT — all in-box since Windows 10 | **`VCRUNTIME140.dll`** — the Visual C++ 2015–2022 Redistributable, for the process's own architecture: `win-arm64` needs the Arm64 one. Very widely installed, but not part of Windows and not required by .NET. |

Two failure modes follow from that, both measured:

- **A slim container.** A bare `ubuntu:24.04` has no `libatomic1` and the load fails there. On musl
  (Alpine) this build cannot load at all; supply one and point `KICADSHARP_NNG_LIBRARY` at it.
- **`linux-musl-x64` publishes look fine and are not.** NuGet's RID fallback hands the glibc
  `libnng.so` to a musl publish, so the file is present and unloadable.

For the six from `nng.NET`, all of this is unchanged from when the binding was `Rebus.nng` — it is
the same `libnng`, and these are the same requirements it always had. The two built here have the
same ones. What is new is that the exception says which one it is.

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

Five things the live run found that the protos do not say:

- **On 10.0.6, pcbnew answers a project-scoped `ExpandTextVariables` before the project handler
  can**, and rejects any document that is not the open board: "the requested document  is not
  open", with the empty name. KiCad's API server stops at the first handler that answers with
  anything but `AS_UNHANDLED`, and 10.0.6's editor validation answers `AS_BAD_REQUEST`; master's
  answers `AS_UNHANDLED` for a non-board document and the request reaches the project handler.
  `GetTextVariables` and `SetTextVariables` are project-handler-only and work on both.
  `KiCadDocument.ExpandTextVariables` sends the board itself, which both versions accept, and the
  board's resolver sees the project's variables too; `Project.ExpandTextVariables` is the
  project-handler form, master and later through pcbnew.

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

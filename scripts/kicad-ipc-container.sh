#!/usr/bin/env bash
# Runs a real KiCad with its IPC API server enabled, for the integration tests.
#
# There is no way to test the IPC client without a running KiCad, and KiCad is a GUI application, so
# this starts one headless in a container: Xvfb, then pcbnew or eeschema, with the API socket
# bind-mounted back out so a test process on the host can dial it. A unix socket on a bind mount
# works across the container boundary -- same kernel, same inode.
#
#   eval "$(scripts/kicad-ipc-container.sh start board.kicad_pcb)"   # exports KICADSHARP_IPC_SOCKET
#   eval "$(scripts/kicad-ipc-container.sh start sheet.kicad_sch)"   # exports KICADSHARP_IPC_SCHEMATIC_SOCKET
#   dotnet test KiCadSharp.slnx -c Release
#   scripts/kicad-ipc-container.sh stop
#
# One container per editor, so a board and a schematic can be up at the same time and the tests for
# each find their own socket. Each `start` also exports KICADSHARP_IPC_PROJECT (or
# KICADSHARP_IPC_SCHEMATIC_PROJECT): the host directory that the container sees as /project, which
# is where a test that asks KiCad to write a file can go and look for it.
#
# WHICH KICAD. KICADSHARP_KICAD_FLAVOR=stable (the default) runs the 10.0.6 release image;
# KICADSHARP_KICAD_FLAVOR=nightly runs KiCad master (10.99, the 11.0 line) from the dev image,
# which carries the nightly PPA build beside 10.0.6 under suffixed names (pcbnew-nightly,
# eeschema-nightly) with its own configuration directory (kicad/10.99). Everything the client added
# for the master proto pin is only measurable against nightly; see docs/ipc.md.
#
# Inside the container the image's own `kicad-ipc-server` does the work: it seeds a configuration
# that will not stop to ask a human anything (the KiCad 10 start wizard is modal and makes the API
# answer AS_NOT_READY to everything while it is up), clears a lock our own killed session left,
# starts the editor under xvfb-run, and only reports "answering" once a GetVersion came back.
#
# Everything lives under .kicad-ipc/ (git-ignored). Short names in there on purpose: a unix socket
# path is limited to 107 bytes, and a checkout under a long directory ran into that.
set -euo pipefail

FLAVOR="${KICADSHARP_KICAD_FLAVOR:-stable}"
case "$FLAVOR" in
  stable)  DEFAULT_IMAGE="ghcr.io/danielmeza/orbion-kicad-release:10.0.6" ;;
  nightly) DEFAULT_IMAGE="ghcr.io/danielmeza/orbion-kicad-dev:10.99" ;;
  *) echo "KICADSHARP_KICAD_FLAVOR must be 'stable' or 'nightly', not '$FLAVOR'" >&2; exit 2 ;;
esac

IMAGE="${KICADSHARP_KICAD_IMAGE:-$DEFAULT_IMAGE}"
CONTAINER="${KICADSHARP_KICAD_CONTAINER:-kicad-sharp-ipc}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
STATE="${KICADSHARP_IPC_STATE:-$ROOT/.kicad-ipc}"
RUNTIME="${KICADSHARP_CONTAINER_RUNTIME:-podman}"

log() { printf '%s\n' "$*" >&2; }

# The KiCad 10 start wizard is seeded away by kicad-first-run, but a one-button dialog can still
# appear when an editor opens a file: eeschema's "Information" ("an error was found when loading
# the schematic that has been automatically fixed") and "Load Schematic", or a "File Open Warning"
# over a lock. Each holds the main loop, and the API answers AS_NOT_READY until it is gone.
#
# MEASURED 2026-09-21: giving the dialog focus and sending Return dismisses it, window manager or
# not -- but only with the display's auth file. xvfb-run guards its display with an Xauthority it
# creates under /tmp, and a `podman exec` does not inherit it, so without XAUTHORITY xdotool never
# connects and silently finds no windows. That is why an earlier version of this, which only
# clicked, appeared to do nothing.
dismiss_dialogs() {
  local container="$1"
  "$RUNTIME" exec "$container" bash -c '
    display=$(ls /tmp/.X11-unix/ 2>/dev/null | head -1 | tr -d X)
    [ -n "$display" ] || exit 0
    export DISPLAY=":$display"
    export XAUTHORITY=$(ls /tmp/xvfb-run.*/Xauthority 2>/dev/null | head -1)
    for window in $(xdotool search --name "^(KiCad Setup|Information|Load Schematic|File Open Warning)$" 2>/dev/null); do
      xdotool windowfocus --sync "$window" 2>/dev/null || continue
      xdotool key --window "$window" Return 2>/dev/null || true
      sleep 1
    done
  ' 2>/dev/null || true
}

start() {
  local document="${1:-}"
  [[ -n "$document" && -f "$document" ]] || { log "usage: $0 start <board.kicad_pcb|sheet.kicad_sch>"; return 2; }

  # Which editor to run follows the file, and so does the variable the socket is exported as.
  local editor variable project_variable
  case "$document" in
    *.kicad_pcb) editor=pcb; variable=KICADSHARP_IPC_SOCKET; project_variable=KICADSHARP_IPC_PROJECT ;;
    *.kicad_sch) editor=sch; variable=KICADSHARP_IPC_SCHEMATIC_SOCKET; project_variable=KICADSHARP_IPC_SCHEMATIC_PROJECT ;;
    *) log "$document is neither a .kicad_pcb nor a .kicad_sch"; return 2 ;;
  esac

  local project="$STATE/$editor/p" socket="$STATE/$editor/s" container="$CONTAINER-$editor"
  if [[ ${#socket} -gt 96 ]]; then
    log "socket path '$socket/api.sock' is too long for a unix socket (107 bytes); set KICADSHARP_IPC_STATE to a shorter directory"
    return 2
  fi

  # A fresh copy every time. The tests modify the document (variants, embedded files, exports),
  # and a schematic needs its sub-sheets and project file beside it.
  rm -rf "$project"
  mkdir -p "$project" "$socket"
  cp "$document" "$project/"
  local sibling
  for sibling in "$(dirname "$document")"/*.kicad_sch "$(dirname "$document")"/*.kicad_pro; do
    [[ -f "$sibling" && ! -e "$project/$(basename "$sibling")" ]] && cp "$sibling" "$project/"
  done
  rm -f "$socket"/api.sock "$socket"/api.lock

  "$RUNTIME" rm -f "$container" >/dev/null 2>&1 || true
  # A fixed hostname, so the image'"'"'s kicad-unlock recognises a lock left by our own killed
  # session and removes it; a lock with any other hostname is left alone, and KiCad then asks.
  "$RUNTIME" run -d --name "$container" --hostname orbion-kicad-ipc \
    -e KICAD_FLAVOR="$FLAVOR" \
    -v "$project":/project:z -v "$socket":/tmp/kicad:z \
    "$IMAGE" kicad-ipc-server "/project/$(basename "$document")" >/dev/null

  # kicad-ipc-server prints "answering on" only after a GetVersion round trip succeeded, which is
  # the readiness that matters: the socket alone appears while a modal dialog can still hold the
  # main loop. If the editor dies first, say so and show why.
  local i
  for i in $(seq 1 60); do
    if "$RUNTIME" logs "$container" 2>&1 | grep -q "answering on"; then
      break
    fi
    if [[ "$("$RUNTIME" inspect -f '{{.State.Running}}' "$container" 2>/dev/null)" != "true" ]]; then
      log "KiCad ($FLAVOR, $editor) exited before its API answered:"
      "$RUNTIME" logs "$container" 2>&1 | tail -20 >&2
      return 1
    fi
    dismiss_dialogs "$container"
    sleep 2
  done
  [[ -S "$socket/api.sock" ]] || { log "KiCad did not open its API socket; check '$RUNTIME logs $container'"; return 1; }
  if ! "$RUNTIME" logs "$container" 2>&1 | grep -q "answering on"; then
    log "KiCad opened its socket but never answered; check '$RUNTIME logs $container'"
    return 1
  fi

  log "KiCad ($FLAVOR, $editor) is up; socket at $socket/api.sock"
  printf 'export %s=%s\n' "$variable" "ipc://$socket/api.sock"
  printf 'export %s=%s\n' "$project_variable" "$project"
}

stop() {
  local editor
  for editor in pcb sch; do
    "$RUNTIME" rm -f "$CONTAINER-$editor" >/dev/null 2>&1 || true
  done
  log "stopped"
}

status() {
  local editor running=0
  for editor in pcb sch; do
    if "$RUNTIME" inspect "$CONTAINER-$editor" >/dev/null 2>&1 && [[ -S "$STATE/$editor/s/api.sock" ]]; then
      log "$editor: running; socket at $STATE/$editor/s/api.sock"
      running=1
    fi
  done
  [[ "$running" == 1 ]] || { log "not running"; return 1; }
}

case "${1:-}" in
  start)  start "${2:-}" ;;
  stop)   stop ;;
  status) status ;;
  *) log "usage: $0 {start <board.kicad_pcb|sheet.kicad_sch>|stop|status}   (KICADSHARP_KICAD_FLAVOR=stable|nightly)"; exit 2 ;;
esac

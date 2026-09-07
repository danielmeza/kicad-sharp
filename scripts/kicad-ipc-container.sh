#!/usr/bin/env bash
# Runs a real KiCad with its IPC API server enabled, for the integration tests.
#
# There is no way to test the IPC client without a running KiCad, and KiCad is a GUI application, so
# this starts one headless in a container: Xvfb, then pcbnew, with the API socket bind-mounted back
# out so a test process on the host can dial it. A unix socket on a bind mount works across the
# container boundary -- same kernel, same inode.
#
#   eval "$(scripts/kicad-ipc-container.sh start board.kicad_pcb)"   # exports KICADSHARP_IPC_SOCKET
#   eval "$(scripts/kicad-ipc-container.sh start sheet.kicad_sch)"   # eeschema instead of pcbnew
#   dotnet test KiCadSharp.slnx -c Release
#   scripts/kicad-ipc-container.sh stop
#
# Everything lives under .kicad-ipc/ (git-ignored). The KiCad configuration in there is bootstrapped
# once and reused: see `bootstrap_config`.
set -euo pipefail

IMAGE="${KICADSHARP_KICAD_IMAGE:-ghcr.io/danielmeza/orbion-kicad-release:10.0.6}"
CONTAINER="${KICADSHARP_KICAD_CONTAINER:-kicad-sharp-ipc}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
STATE="${KICADSHARP_IPC_STATE:-$ROOT/.kicad-ipc}"
CONFIG="$STATE/config"
PROJECT="$STATE/project"
SOCKET="$STATE/socket"
RUNTIME="${KICADSHARP_CONTAINER_RUNTIME:-podman}"

log() { printf '%s\n' "$*" >&2; }

# KiCad shows a first-run wizard when its configuration is incomplete, and a modal dialog makes the
# API answer AS_NOT_READY to everything -- so the config has to be complete before pcbnew starts.
# kicad-cli writes the whole set without a GUI, which is the headless way in.
bootstrap_config() {
  [[ -f "$CONFIG/kicad/10.0/kicad_common.json" ]] && return 0

  log "bootstrapping KiCad configuration (once)"
  mkdir -p "$CONFIG"
  "$RUNTIME" run --rm \
    -v "$CONFIG":/config:z -v "$PROJECT":/project:z \
    --entrypoint bash "$IMAGE" -c '
      export HOME=/root XDG_CONFIG_HOME=/config
      kicad-cli version >/dev/null 2>&1 || true
      kicad-cli pcb export svg --output /tmp/discard.svg --layers F.Cu /project/*.kicad_pcb >/dev/null 2>&1 || true
    '

  local common="$CONFIG/kicad/10.0/kicad_common.json"
  [[ -f "$common" ]] || { log "kicad-cli wrote no configuration; cannot continue"; return 1; }

  python3 - "$common" <<'PY'
import json, sys
path = sys.argv[1]
with open(path) as handle:
    config = json.load(handle)
config.setdefault("api", {})["enable_server"] = True
with open(path, "w") as handle:
    json.dump(config, handle, indent=2)
PY

  # Empty library tables, so KiCad does not offer to install the default ones on first run.
  printf '(fp_lib_table\n  (version 7)\n)\n'  > "$CONFIG/kicad/10.0/fp-lib-table"
  printf '(sym_lib_table\n  (version 7)\n)\n' > "$CONFIG/kicad/10.0/sym-lib-table"
}

# The wizard still appears the very first time pcbnew itself runs. There is no window manager on the
# Xvfb display, so keyboard events have nowhere to go -- but pointer events do not need one, and the
# dialog's buttons are in a fixed place. Once it has been walked through, KiCad writes the rest of
# its configuration and it never comes back.
dismiss_first_run() {
  "$RUNTIME" exec "$CONTAINER" bash -c '
    export DISPLAY=:99
    for attempt in 1 2 3 4 5 6; do
      window=$(xdotool search --name "^(KiCad Setup|Information|Load Schematic)$" 2>/dev/null | head -1)
      [ -z "$window" ] && exit 0
      eval $(xdotool getwindowgeometry --shell "$window")

      # Two candidate button positions, clicked in turn. The wizard is a fixed 680x416 with
      # "Next >" at 148px in from the right edge; the small "Information" / "Load Schematic"
      # dialogs put their one button in the middle. Clicking dead space costs nothing, and this
      # beats guessing one point that fits both -- the midpoint of the wizard lands between
      # "< Back" and "Next >" and dismisses nothing, which is how this was got wrong once already.
      xdotool mousemove --sync $((X + WIDTH - 148)) $((Y + HEIGHT - 28)) click 1
      sleep 1
      xdotool mousemove --sync $((X + WIDTH / 2)) $((Y + HEIGHT - 30)) click 1
      sleep 2
    done
  ' 2>/dev/null || true
}

start() {
  local board="${1:-}"
  mkdir -p "$PROJECT" "$SOCKET"

  if [[ -n "$board" ]]; then
    cp "$board" "$PROJECT/"
  fi
  # Which editor to run follows the file. pcbnew answers the whole common command set; eeschema
  # answers almost none of it -- see the README -- but it is still the only way to reach a
  # schematic over IPC at all, so the harness can start it.
  shopt -s nullglob
  local documents=("$PROJECT"/*.kicad_pcb "$PROJECT"/*.kicad_sch)
  shopt -u nullglob
  [[ ${#documents[@]} -gt 0 ]] || { log "no .kicad_pcb or .kicad_sch in $PROJECT; pass one to 'start'"; return 1; }

  local target="${documents[0]}"
  if [[ -n "$board" ]]; then
    target="$PROJECT/$(basename "$board")"
  fi

  local editor=pcbnew
  [[ "$target" == *.kicad_sch ]] && editor=eeschema

  # A previous run that did not shut down cleanly leaves a lock file, and KiCad then opens a modal
  # "File Open Warning" -- which is another way to get AS_NOT_READY forever.
  rm -f "$PROJECT"/~*.lck
  rm -f "$SOCKET"/api.sock "$SOCKET"/api.lock

  bootstrap_config

  "$RUNTIME" rm -f "$CONTAINER" >/dev/null 2>&1 || true
  "$RUNTIME" run -d --name "$CONTAINER" \
    -v "$CONFIG":/config:z -v "$PROJECT":/project:z -v "$SOCKET":/tmp/kicad:z \
    --entrypoint bash "$IMAGE" -c "
      export HOME=/root XDG_CONFIG_HOME=/config XDG_RUNTIME_DIR=/tmp/xdg
      mkdir -p /tmp/xdg && chmod 700 /tmp/xdg
      Xvfb :99 -screen 0 1280x1024x24 >/dev/null 2>&1 &
      sleep 2
      export DISPLAY=:99
      exec $editor '/project/$(basename "$target")'
    " >/dev/null

  local socket="$SOCKET/api.sock"
  for _ in $(seq 1 60); do
    [[ -S "$socket" ]] && break
    sleep 1
  done
  [[ -S "$socket" ]] || { log "KiCad did not open its API socket; check '$RUNTIME logs $CONTAINER'"; return 1; }

  sleep 6
  dismiss_first_run
  sleep 3

  log "KiCad is up; socket at $socket"
  printf 'export KICADSHARP_IPC_SOCKET=%s\n' "ipc://$socket"
}

case "${1:-start}" in
  start)  start "${2:-}" ;;
  stop)   "$RUNTIME" rm -f "$CONTAINER" >/dev/null 2>&1 || true; log "stopped" ;;
  status)
    if "$RUNTIME" inspect "$CONTAINER" >/dev/null 2>&1 && [[ -S "$SOCKET/api.sock" ]]; then
      log "running; socket at $SOCKET/api.sock"
    else
      log "not running"
      exit 1
    fi
    ;;
  *) log "usage: $0 {start [board.kicad_pcb]|stop|status}"; exit 2 ;;
esac

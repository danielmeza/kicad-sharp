#!/usr/bin/env bash
# Builds the `dev` stage of orbion-kicad-container -- KiCad master (the nightly PPA build, 10.99,
# the 11.0 line) installed beside 10.0.6 -- and publishes it as ghcr.io/danielmeza/orbion-kicad-dev,
# the image the nightly integration tests pull (scripts/kicad-ipc-container.sh with
# KICADSHARP_KICAD_FLAVOR=nightly, and the ipc-nightly job in .github/workflows/ci.yml).
#
#   scripts/kicad-dev-image.sh build      # -> localhost/orbion/kicad-dev:10.99, and says which nightly is inside
#   scripts/kicad-dev-image.sh publish    # build, then push :10.99, :<nightly build> and :ctx-<source hash>
#
# WHY THE RECIPE IS HERE. The Containerfile lives in the orbion monorepo and is mirrored to
# github.com/danielmeza/orbion-kicad-container. Its own image-publish.sh publishes the release stage
# only, on purpose: a release must not be built by a nightly, and it refuses an image that has
# kicad-cli-nightly in it. The dev stage is a different package for a different job -- answering
# "does the client work against what KiCad 11 will be?" -- so the recipe for publishing it sits with
# the tests that need it. The mirror is pinned to a commit so a build is reproducible; the nightly
# inside moves with the PPA, and the :<nightly build> tag records which one went in.
#
# TAGS. :10.99 is what CI pulls and moves with each publish. :10.99.0-<kicad commit> is fixed to the
# nightly inside, so a test run can say exactly which KiCad it measured. :ctx-<hash> is the mirror's
# own source hash (containers/context-hash.sh), the same one the release image carries.
#
# Credential for publish: GHCR_TOKEN, a GitHub PAT with write:packages; or `gh auth token` when the
# CLI is logged in with that scope. The image is labelled with this repository as its source, which
# is what lets this repository's Actions pull it with GITHUB_TOKEN.
set -euo pipefail

MIRROR="https://github.com/danielmeza/orbion-kicad-container"
MIRROR_COMMIT="6f2e406e33cf34b35f372e83885c616e95f2623d"
IMAGE="${KICADSHARP_DEV_IMAGE:-ghcr.io/danielmeza/orbion-kicad-dev}"
LOCAL="localhost/orbion/kicad-dev:10.99"
RUNTIME="${KICADSHARP_CONTAINER_RUNTIME:-podman}"

log() { printf '%s\n' "$*" >&2; }

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

build() {
  log "Fetching orbion-kicad-container at $MIRROR_COMMIT ..."
  # The Containerfile COPYs from containers/, the directory it lives in inside the monorepo, so the
  # build context has to reproduce that layout.
  git init --quiet "$WORK/ctx/containers"
  git -C "$WORK/ctx/containers" remote add origin "$MIRROR"
  git -C "$WORK/ctx/containers" fetch --quiet --depth 1 origin "$MIRROR_COMMIT"
  git -C "$WORK/ctx/containers" checkout --quiet --detach FETCH_HEAD
  rm -rf "$WORK/ctx/containers/.git"

  local ctx
  ctx="$("$WORK/ctx/containers/context-hash.sh")"
  local created
  created="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  local description="KiCad 10.0.6 and the KiCad master nightly (10.99, the 11.0 line) side by side -- pcbnew/eeschema and pcbnew-nightly/eeschema-nightly -- with the Orbion IPC helpers. The dev stage of orbion-kicad-container, published for kicad-sharp's integration tests against master. Not for producing releases."

  log "Building the dev stage (source $ctx) ..."
  "$RUNTIME" build --target dev --build-arg "ORBION_CONTEXT=$ctx" \
    --label "org.opencontainers.image.title=orbion-kicad-dev" \
    --label "org.opencontainers.image.description=$description" \
    --label "org.opencontainers.image.source=https://github.com/danielmeza/kicad-sharp" \
    --label "org.opencontainers.image.version=10.99" \
    --label "org.opencontainers.image.created=$created" \
    --label "org.opencontainers.image.revision=$ctx" \
    --annotation "org.opencontainers.image.title=orbion-kicad-dev" \
    --annotation "org.opencontainers.image.description=$description" \
    --annotation "org.opencontainers.image.source=https://github.com/danielmeza/kicad-sharp" \
    --annotation "org.opencontainers.image.version=10.99" \
    --annotation "org.opencontainers.image.created=$created" \
    --annotation "org.opencontainers.image.revision=$ctx" \
    -t "$LOCAL" -f "$WORK/ctx/containers/Containerfile" "$WORK/ctx" >&2

  # Prove it is the dev stage, and read which nightly went in: "10.99.0-unknown-<commit>~...".
  local stable nightly
  stable="$("$RUNTIME" run --rm --entrypoint kicad-cli "$LOCAL" --version)"
  nightly="$("$RUNTIME" run --rm --entrypoint kicad-cli-nightly "$LOCAL" version --format about \
    | sed -n 's/^Version: \([0-9.]*\)-unknown-\([0-9a-f]*\).*/\1-\2/p')"
  [[ "$stable" == "10.0.6" && -n "$nightly" ]] || { log "error: expected 10.0.6 plus a nightly, got '$stable' and '$nightly'"; return 1; }

  log "Built $LOCAL: KiCad $stable + nightly $nightly, source ctx-$ctx"
  printf '%s %s\n' "$nightly" "$ctx"
}

publish() {
  local built nightly ctx
  built="$(build)"
  read -r nightly ctx <<<"$built"

  local token="${GHCR_TOKEN:-}"
  if [[ -z "$token" ]] && command -v gh >/dev/null 2>&1; then
    token="$(gh auth token 2>/dev/null || true)"
  fi
  [[ -n "$token" ]] || { log "error: no credential. Set GHCR_TOKEN to a PAT with write:packages, or 'gh auth login -s write:packages'."; return 2; }

  printf '%s' "$token" | "$RUNTIME" login ghcr.io -u "${GHCR_USER:-danielmeza}" --password-stdin >&2
  local tag
  for tag in "10.99" "$nightly" "ctx-$ctx"; do
    "$RUNTIME" tag "$LOCAL" "$IMAGE:$tag"
    "$RUNTIME" push "$IMAGE:$tag" >&2
  done
  log "published $IMAGE:10.99, :$nightly and :ctx-$ctx"
}

case "${1:-}" in
  build)   build ;;
  publish) publish ;;
  *) log "usage: $0 {build|publish}"; exit 2 ;;
esac

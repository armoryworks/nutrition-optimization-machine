#!/bin/bash
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
IMAGE="${NOM_HAND_IMAGE:-nom-hand-import}"

usage() {
  cat >&2 <<'EOF'
Runs ops/hand-import.py in the nom-hand-import image (poppler, tesseract), on the application server.

  ops/hand-import-box.sh build
  ops/hand-import-box.sh <fetch|links|text|extract|sample|stage|sources|withhold|release> [hand-import.py arguments]

Environment, read at run time (nothing is baked into the image):
  NOM_HAND_WORK          the work folder on this host, mounted at /work (required for every step with --out)
  NOM_HAND_MODEL_HOST    the model host for extract (as --host), e.g. http://<model host>:11434
  NOM_HAND_MODEL         the model for extract (as --model; default qwen2.5:14b-instruct)
  NOM_API, NOM_API_KEY, NOM_API_TOKEN
                         the NOM API the sources, stage, withhold and release steps talk to (passed by name)
  NOM_HAND_NETWORK       a docker network for the container (host when NOM_API is a loopback address)
  NOM_HAND_IMAGE         the image (default nom-hand-import)
  NOM_HAND_DRY_RUN=1     print the docker command instead of running it
EOF
}

run() {
  if [ "${NOM_HAND_DRY_RUN:-0}" = "1" ]; then
    printf '%q ' "$@"
    printf '\n'
  else
    exec "$@"
  fi
}

[ $# -ge 1 ] || { usage; exit 1; }
step="$1"
shift

case "$step" in
  build)
    run docker build -f "$HERE/hand-import.Dockerfile" -t "$IMAGE" "$@" "$HERE"
    exit 0
    ;;
  fetch|links|text|extract|sample|stage|sources|withhold|release) ;;
  -h|--help|help) usage; exit 0 ;;
  *) echo "unknown step: $step" >&2; usage; exit 1 ;;
esac

args=("$@")
docker_args=(run --rm --read-only --tmpfs /tmp --cap-drop ALL --security-opt no-new-privileges
  --user "$(id -u):$(id -g)" -e NOM_API -e NOM_API_KEY -e NOM_API_TOKEN)

if [ -n "${NOM_HAND_NETWORK:-}" ]; then
  docker_args+=(--network "$NOM_HAND_NETWORK")
fi

case "$step" in
  fetch|links|text|extract|sample|stage)
    if [ -z "${NOM_HAND_WORK:-}" ]; then
      echo "NOM_HAND_WORK is not set: name the work folder on this host (outside any checkout)" >&2
      exit 1
    fi
    if [ ! -d "$NOM_HAND_WORK" ]; then
      echo "NOM_HAND_WORK $NOM_HAND_WORK is not a folder; create it first" >&2
      exit 1
    fi
    work="$(cd "$NOM_HAND_WORK" && pwd)"
    docker_args+=(-v "$work:/work")
    out=""
    for ((i = 0; i < ${#args[@]}; i++)); do
      case "${args[$i]}" in
        --out) out="${args[$((i + 1))]:-}" ;;
        --out=*) out="${args[$i]#--out=}" ;;
      esac
    done
    if [ -z "$out" ]; then
      args+=(--out /work)
    elif [ "$out" != "/work" ] && [ "${out#/work/}" = "$out" ]; then
      echo "--out $out is not in the work folder: give /work or a folder under it (/work/va)" >&2
      exit 1
    fi
    ;;
esac

if [ "$step" = "extract" ] && [ -n "${NOM_HAND_MODEL_HOST:-}" ]; then
  given_host=0
  given_model=0
  for arg in "${args[@]}"; do
    case "$arg" in
      --host|--host=*) given_host=1 ;;
      --model|--model=*) given_model=1 ;;
    esac
  done
  [ "$given_host" = "1" ] || args+=(--host "$NOM_HAND_MODEL_HOST")
  [ "$given_model" = "1" ] || args+=(--model "${NOM_HAND_MODEL:-qwen2.5:14b-instruct}")
fi

run docker "${docker_args[@]}" "$IMAGE" "$step" "${args[@]}"

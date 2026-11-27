#!/bin/bash
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
IMAGE="${NOM_HAND_IMAGE:-nom-hand-import}"

usage() {
  cat >&2 <<'EOF'
Runs ops/hand-import.py in the nom-hand-import image (poppler, tesseract, a word list), on the application server.

  ops/hand-import-box.sh build
  ops/hand-import-box.sh selftest
  ops/hand-import-box.sh <fetch|links|text|images|plan|extract|sample|stage|sources|withhold|release> [hand-import.py arguments]

selftest runs the reading tests (ops/tests/test_hand_import*.py) in the image with its own tesseract, with ops/ and docs/
of this checkout mounted read-only (the determination test reads docs/architecture/crosscheck-sources.md).

Environment, read at run time (nothing is baked into the image):
  NOM_HAND_WORK          the work folder on this host, mounted at /work (required for every step with --out)
  NOM_HAND_MODEL_HOST_FILE
                         a file, mode 600, holding the model host for extract (as --host), e.g. http://<model host>:11434;
                         copy it in only for a model step and remove it after, so a rule-only extract never reaches the
                         host. Without it extract reads by rule only. The address is never taken from the environment.
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
  selftest)
    docs="$(cd "$HERE/.." && pwd)/docs"
    if [ ! -d "$docs" ] || [ ! -d "$HERE/tests" ]; then
      echo "selftest needs ops/tests and docs/ beside ops/ (git archive <tag> ops docs)" >&2
      exit 1
    fi
    run docker run --rm --read-only --tmpfs /tmp --cap-drop ALL --security-opt no-new-privileges --user "$(id -u):$(id -g)" \
      --entrypoint python3 -v "$HERE:/app/ops:ro" -v "$docs:/app/docs:ro" "$IMAGE" \
      -m unittest discover -s /app/ops/tests -p 'test_hand_import*.py' -v
    exit 0
    ;;
  fetch|links|text|images|plan|extract|sample|stage|sources|withhold|release) ;;
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
  fetch|links|text|images|plan|extract|sample|stage)
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

if [ -n "${NOM_HAND_MODEL_HOST:-}" ]; then
  echo "NOM_HAND_MODEL_HOST is not read: put the model host in a mode-600 file named by NOM_HAND_MODEL_HOST_FILE, only for the model step" >&2
  exit 1
fi

model_host=""
if [ "$step" = "extract" ] && [ -n "${NOM_HAND_MODEL_HOST_FILE:-}" ]; then
  if [ ! -f "$NOM_HAND_MODEL_HOST_FILE" ]; then
    echo "extract: rules only (no model host file at NOM_HAND_MODEL_HOST_FILE)" >&2
  else
    mode="$(stat -c '%a' "$NOM_HAND_MODEL_HOST_FILE")"
    owner="$(stat -c '%u' "$NOM_HAND_MODEL_HOST_FILE")"
    if [ "$mode" != "600" ] && [ "$mode" != "400" ] || [ "$owner" != "$(id -u)" ]; then
      echo "NOM_HAND_MODEL_HOST_FILE must be yours and mode 600 (it is $mode, owner $owner)" >&2
      exit 1
    fi
    model_host="$(head -n 1 "$NOM_HAND_MODEL_HOST_FILE" | tr -d '[:space:]')"
    [ -n "$model_host" ] || { echo "NOM_HAND_MODEL_HOST_FILE is empty" >&2; exit 1; }
  fi
elif [ "$step" = "extract" ]; then
  echo "extract: rules only (NOM_HAND_MODEL_HOST_FILE is not set)" >&2
fi

if [ -n "$model_host" ]; then
  given_host=0
  given_model=0
  for arg in "${args[@]}"; do
    case "$arg" in
      --host|--host=*) given_host=1 ;;
      --model|--model=*) given_model=1 ;;
    esac
  done
  [ "$given_host" = "1" ] || args+=(--host "$model_host")
  [ "$given_model" = "1" ] || args+=(--model "${NOM_HAND_MODEL:-qwen2.5:14b-instruct}")
fi

if [ "$step" = "plan" ]; then
  given_model=0
  for arg in "${args[@]}"; do
    case "$arg" in
      --model|--model=*) given_model=1 ;;
    esac
  done
  [ "$given_model" = "1" ] || args+=(--model "${NOM_HAND_MODEL:-qwen2.5:14b-instruct}")
fi

run docker "${docker_args[@]}" "$IMAGE" "$step" "${args[@]}"

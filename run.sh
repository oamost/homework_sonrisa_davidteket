#!/usr/bin/env bash
# Local Docker lifecycle. Run from anywhere: ./run.sh [build|start|stop|restart|status|logs|up|clean-dev]
set -euo pipefail

root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
config="${WORLDALERTS_ENV_FILE:-$root/.env}"
instance="${WORLDALERTS_INSTANCE:-worldalerts-local}"
port="${WORLDALERTS_PORT:-8080}"
command="${1:-up}"
image="$instance:verified"
volume="$instance-data"
keys="$instance-keys"
label="org.worldalerts.instance=$instance"
image_built=false
# Intentionally weak LOCAL-ONLY demo credential. Never publish this Docker port.
local_demo_hash="310000:o4KcI4pukTmLl9YNc07O5/qHipAB6FilN0vXRH7FS/Q=:8Tjil5XZEjiiOBBAeQPxxDlRau0Ve/0twEE17tPVuC0="

usage() { echo 'Usage: ./run.sh [up|build|start|stop|restart|status|logs|clean-dev|help]'; }
case "$command" in
  up|build|start|stop|restart|status|logs|clean-dev) ;;
  help|-h|--help) usage; exit 0 ;;
  *) usage >&2; exit 2 ;;
esac
if [[ ! "$instance" =~ ^[a-z0-9][a-z0-9_.-]*$ ]] ||
   [[ ! "$port" =~ ^[0-9]+$ ]] || (( ${#port} > 5 )) ||
   (( 10#$port < 1 || 10#$port > 65535 )); then
  echo 'Invalid WORLDALERTS_INSTANCE or WORLDALERTS_PORT' >&2
  exit 1
fi
clean_dev_server() {
  if command -v python3 >/dev/null 2>&1; then
    python3 "$root/scripts/stop_dev_server.py" "$root"
  else
    echo 'WARNING: Python 3 unavailable; cannot identify legacy Angular :4200 server.' >&2
  fi
}
prepare_env() {
  if [[ -f "$config" ]]; then return 0; fi
  if [[ -n "${WORLDALERTS_ENV_FILE:-}" ]]; then
    printf 'Configured env file does not exist: %s\n' "$config" >&2; return 1
  fi
  ( umask 077; : > "$config" )
  echo 'Created empty local .env; add private SMTP/Slack settings through the app.'
}
if [[ "$command" == clean-dev ]]; then clean_dev_server; exit 0; fi
if ! command -v docker >/dev/null 2>&1; then
  echo 'Docker Engine/CLI not found. Install Docker Desktop or Docker Engine first.' >&2
  exit 1
fi

# Refuse to interfere with another container that happens to share our name.
assert_owned_container() {
  local owner
  if owner="$(docker container inspect --format '{{ index .Config.Labels "org.worldalerts.instance" }}' "$instance" 2>/dev/null)"; then
    if [[ "$owner" != "$instance" ]]; then
      printf 'Refusing to modify a container not owned by this instance: %s\n' "$instance" >&2
      exit 1
    fi
    return 0
  fi
  return 1
}

build_image() {
  echo 'Building and testing API + Angular UI…'
  docker build --no-cache -f "$root/src/container/Dockerfile" -t "$image" "$root"
  image_built=true
}

stop_owned_if_running() {
  if [[ "$(docker container inspect --format '{{ .State.Running }}' "$instance")" == 'true' ]]; then
    docker container stop "$instance"
  else
    echo "Already stopped: $instance"
  fi
}

stop_instance() {
  clean_dev_server
  if assert_owned_container; then
    stop_owned_if_running
  else
    echo "No container named $instance to stop."
  fi
}

start_instance() {
  prepare_env
  config="$(realpath -- "$config")"
  if [[ "$image_built" != true ]] && ! docker image inspect "$image" >/dev/null 2>&1; then
    echo "Image $image missing. First run './run.sh build' (or './run.sh up')." >&2
    exit 1
  fi
  clean_dev_server
  # A fresh start always replaces an owned container: this reloads the current
  # image and .env, and prevents stale instances or stale credentials.
  if assert_owned_container; then
    stop_owned_if_running
    docker container rm "$instance"
  fi
  # Check both volumes before creating or mounting either one.
  local name owner
  for name in "$volume" "$keys"; do
    if owner="$(docker volume inspect --format '{{ index .Labels "org.worldalerts.instance" }}' "$name" 2>/dev/null)"; then
      if [[ "$owner" != "$instance" ]]; then
        printf 'Refusing to use a volume not owned by this instance: %s\n' "$name" >&2
        exit 1
      fi
    fi
  done
  for name in "$volume" "$keys"; do
    if ! docker volume inspect --format '{{ .Name }}' "$name" >/dev/null 2>&1; then
      docker volume create --label "$label" "$name"
    fi
  done
  docker run -d --name "$instance" --label "$label" \
    --env-file "$config" -e 'ASPNETCORE_URLS=http://+:8080' \
    -e 'WorldAlerts__LocalHttp=true' \
    -e "WorldAlerts__Admin__PasswordHash=$local_demo_hash" \
    -e 'WorldAlerts__DatabasePath=/app/data/worldalerts.sqlite' \
    -p "127.0.0.1:$port:8080" -v "$volume:/app/data" \
    -v "$keys:/home/app/.aspnet/DataProtection-Keys" \
    --restart unless-stopped "$image"
  echo "Started: http://127.0.0.1:$port/"
}

# The legacy no-argument 'up' contract builds before stopping/replacing a
# running container. Failed builds MUST NOT affect an existing installation.
replace_after_build() {
  if assert_owned_container; then
    stop_owned_if_running
    docker container rm "$instance"
  fi
  start_instance
}

case "$command" in
  build) build_image ;;
  start) start_instance ;;
  stop) stop_instance ;;
  status)
    if assert_owned_container; then
      docker container inspect --format '{{ .Name }}: running={{ .State.Running }} status={{ .State.Status }}' "$instance"
    else
      echo "Not created: $instance"
    fi ;;
  logs)
    if assert_owned_container; then docker logs --tail 100 "$instance"; else echo "Not created: $instance" >&2; exit 1; fi ;;
  restart|up)
    prepare_env
    if [[ "$command" == up ]]; then
      build_image
      replace_after_build
    else
      if ! docker image inspect "$image" >/dev/null 2>&1; then
        echo "Image $image missing. First run './run.sh build'." >&2; exit 1
      fi
      if assert_owned_container; then
        stop_owned_if_running
        docker container rm "$instance"
      fi
      start_instance
    fi ;;
esac

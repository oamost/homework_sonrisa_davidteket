#!/usr/bin/env bash
set -euo pipefail

root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
config="${WORLDALERTS_ENV_FILE:-$root/.env}"
instance="${WORLDALERTS_INSTANCE:-worldalerts-local}"
port="${WORLDALERTS_PORT:-8080}"

if [[ ! "$instance" =~ ^[a-z0-9][a-z0-9_.-]*$ ]] ||
   [[ ! "$port" =~ ^[0-9]+$ ]] || (( ${#port} > 5 )) ||
   (( 10#$port < 1 || 10#$port > 65535 )); then
  printf 'Invalid WORLDALERTS_INSTANCE or WORLDALERTS_PORT\n' >&2
  exit 1
fi

if [[ ! -f "$config" ]]; then
  printf 'Private configuration missing: %s (see docs/startup.md)\n' "$config" >&2
  exit 1
fi

config="$(realpath -- "$config")"
volume="$instance-data"
keys="$instance-keys"
image="$instance:verified"
label="org.worldalerts.instance=$instance"

# Never modify an existing container or volume before the verified image exists.
docker build --no-cache -f "$root/src/container/Dockerfile" -t "$image" "$root"

# Inspect both volumes before creating either; never mix with someone else's
# existing volume even if its name collides with our configured instance.
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

if owner="$(docker container inspect --format '{{ index .Config.Labels "org.worldalerts.instance" }}' "$instance" 2>/dev/null)"; then
  if [[ "$owner" != "$instance" ]]; then
    printf 'Refusing to replace a container not owned by this instance: %s\n' "$instance" >&2
    exit 1
  fi
  docker container stop "$instance"
  docker container rm "$instance"
fi

docker run -d --name "$instance" --label "$label" \
  --env-file "$config" -e 'ASPNETCORE_URLS=http://+:8080' \
  -e 'WorldAlerts__DatabasePath=/app/data/worldalerts.sqlite' \
  -p "127.0.0.1:$port:8080" -v "$volume:/app/data" \
  -v "$keys:/home/app/.aspnet/DataProtection-Keys" \
  --restart unless-stopped "$image"

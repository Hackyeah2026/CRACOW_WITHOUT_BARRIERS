#!/usr/bin/env bash
set -euo pipefail
: "${MIKRUS_HOST:?}" "${MIKRUS_SSH_PORT:?}" "${MIKRUS_HTTP_PORT:?}" "${MIKRUS_PUBLIC_URL:?}" "${MIKRUS_KEY:?}" "${MIKRUS_KNOWN_HOSTS:?}" "${IMAGE_TAG:?}"
[[ $MIKRUS_HOST =~ ^[a-zA-Z0-9.:-]+$ && $IMAGE_TAG =~ ^[a-f0-9]{40}$ ]] || exit 2
valid_port() { [[ $1 =~ ^[1-9][0-9]{0,4}$ ]] && (($1 <= 65535)); }
valid_port "$MIKRUS_SSH_PORT" || { echo 'Invalid MIKRUS_SSH_PORT' >&2; exit 2; }
if ! valid_port "$MIKRUS_HTTP_PORT" || ((MIKRUS_HTTP_PORT < 1024)) || [[ $MIKRUS_HTTP_PORT == "$MIKRUS_SSH_PORT" ]]; then
    echo 'Invalid MIKRUS_HTTP_PORT: require 1024..65535, different from the SSH port' >&2
    exit 2
fi
script_dir=$(cd -- "$(dirname -- "$0")" && pwd)
# shellcheck source-path=SCRIPTDIR
# shellcheck source=app-env.sh
source "$script_dir/app-env.sh"
# shellcheck source-path=SCRIPTDIR
# shellcheck source=public-smoke.sh
source "$script_dir/public-smoke.sh"
validate_public_url
image=$script_dir/../../artifacts/image.tar.gz
[[ -s $image ]] || { echo 'Missing artifacts/image.tar.gz' >&2; exit 1; }
ssh_dir=$(mktemp -d)
trap 'rm -rf -- "$ssh_dir"' EXIT
chmod 700 "$ssh_dir"
printf '%s\n' "$MIKRUS_KEY" > "$ssh_dir/key"
printf '%s\n' "$MIKRUS_KNOWN_HOSTS" > "$ssh_dir/known_hosts"
# Rendered before any connection: invalid application configuration never reaches the server.
render_app_env > "$ssh_dir/app.env"
chmod 600 "$ssh_dir/"*
options=(-i "$ssh_dir/key" -o IdentitiesOnly=yes -o BatchMode=yes -o StrictHostKeyChecking=yes -o "UserKnownHostsFile=$ssh_dir/known_hosts" -o ConnectTimeout=15)
on_server() { ssh "${options[@]}" -p "$MIKRUS_SSH_PORT" "root@$MIKRUS_HOST" "$@"; }
remote=$(on_server 'set -eu; umask 077; mktemp -d /root/kbb.XXXXXXXX')
[[ $remote =~ ^/root/kbb\.[a-zA-Z0-9]+$ ]] || exit 1
cleanup() {
    on_server "rm -rf -- '$remote'" || true
    rm -rf -- "$ssh_dir"
}
trap cleanup EXIT
# Secrets travel as a file in the 0700 incoming directory, never as command arguments.
scp "${options[@]}" -P "$MIKRUS_SSH_PORT" "$ssh_dir/app.env" "$script_dir/../mikrus/deploy.sh" "root@[$MIKRUS_HOST]:$remote/"
# No registry: the image built from this commit is streamed straight into the server's Docker.
on_server 'gunzip | docker load' < "$image"
on_server "bash '$remote/deploy.sh' '$IMAGE_TAG' '$MIKRUS_HTTP_PORT' '$remote/app.env'"
public_smoke

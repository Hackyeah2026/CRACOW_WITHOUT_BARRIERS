#!/usr/bin/env bash
# Runs on the Mikrus as root, after the image kbb-web:<tag> has been loaded.
set -euo pipefail
[[ $(id -u) == 0 && $# == 3 ]] || exit 2
tag=$1
port=$2
incoming_env=$3
[[ $tag =~ ^[a-f0-9]{40}$ && $port =~ ^[1-9][0-9]{3,4}$ ]] || exit 2
((port >= 1024 && port <= 65535)) || exit 2
[[ -s $incoming_env && ! -L $incoming_env ]] || { echo 'Missing application environment file' >&2; exit 1; }
command -v docker >/dev/null || { echo 'Docker is not installed' >&2; exit 1; }
command -v curl >/dev/null || { echo 'curl is not installed' >&2; exit 1; }
name=kbb-web
image=$name:$tag
root=/opt/kbb
docker image inspect "$image" >/dev/null
install -d -m 0700 "$root"
exec 9>"$root/deploy.lock"
flock -x 9
# What runs now; used to go back if the new container does not come up.
previous=$(docker inspect -f '{{.Config.Image}}' "$name" 2>/dev/null || true)
rm -f "$root/app.env.previous"
if [[ -f $root/app.env ]]; then cp -p "$root/app.env" "$root/app.env.previous"; fi
start() {
    docker rm -f "$name" >/dev/null 2>&1 || true
    # Secrets come from a root-only file, never from the command line.
    docker run -d --name "$name" --restart unless-stopped \
        --env-file "$root/app.env" \
        -p "$port:8080" \
        -v kbb-keys:/home/app/.aspnet/DataProtection-Keys \
        --read-only --tmpfs /tmp --cap-drop ALL --security-opt no-new-privileges \
        --memory 512m \
        --log-opt max-size=10m --log-opt max-file=3 \
        "$1" >/dev/null
}
status_code() {
    curl --silent --noproxy '*' --max-time 3 -o /dev/null -w '%{http_code}' "http://127.0.0.1:$port$1"
}
healthy() {
    [[ $(curl --fail --silent --noproxy '*' --max-time 3 "http://127.0.0.1:$port/api/health") == OK ]] &&
        [[ $(status_code /) == 200 && $(status_code /_framework/dotnet.js) == 200 && $(status_code /api/unknown) == 404 ]]
}
wait_healthy() {
    for ((i=0; i<45; i++)); do healthy && return 0; sleep 1; done
    return 1
}
install -m 0600 "$incoming_env" "$root/app.env"
if start "$image" && wait_healthy; then
    rm -f "$root/app.env.previous"
    # Keep the running image and the one before it; drop older releases.
    while read -r old; do
        [[ $old == "$image" || $old == "$previous" ]] || docker rmi "$old" >/dev/null 2>&1 || true
    done < <(docker images --format '{{.Repository}}:{{.Tag}}' "$name")
    docker image prune -f >/dev/null || true
    echo "Activated $tag"
    exit 0
fi
echo 'Deployment failed; container output:' >&2
docker logs --tail 50 "$name" >&2 || true
docker rm -f "$name" >/dev/null 2>&1 || true
if [[ -n $previous && -f $root/app.env.previous ]]; then
    echo "Restoring $previous" >&2
    mv -f "$root/app.env.previous" "$root/app.env"
    if ! start "$previous" || ! wait_healthy; then
        echo 'ALERT: rollback health failed; inspect the container manually' >&2
    fi
else
    rm -f "$root/app.env"
fi
docker rmi "$image" >/dev/null 2>&1 || true
exit 1

#!/usr/bin/env bash
set -euo pipefail
: "${IMAGE_TAG:?}"
# Same hardening flags as on the server. No database or API keys here:
# the host must start and serve the app without them.
name=kbb-smoke-$$
docker run -d --name "$name" -p 127.0.0.1:18080:8080 \
    --read-only --tmpfs /tmp --cap-drop ALL --security-opt no-new-privileges \
    "kbb-web:$IMAGE_TAG" >/dev/null
trap 'docker rm -f "$name" >/dev/null' EXIT
fail() { echo "Smoke failed: $1" >&2; docker logs "$name" >&2; exit 1; }
status() { curl --silent -o /dev/null -w '%{http_code}' "http://127.0.0.1:18080$1"; }
for ((i=0; i<45; i++)); do
    if [[ $(curl --fail --silent http://127.0.0.1:18080/api/health) == OK ]]; then
        [[ $(status /api/unknown) == 404 ]] || fail 'unknown API path must be 404'
        # Every script the page loads must be served, including the framework's own.
        page=$(curl --fail --silent http://127.0.0.1:18080/)
        [[ $page == *'Kraków bez barier'* ]] || fail 'start page is not the app'
        scripts=$(grep -o '<script src="[^"]*"' <<<"$page" | cut -d '"' -f 2)
        [[ $scripts == *_framework/blazor.web* ]] || fail 'start page does not load Blazor'
        for script in $scripts _framework/dotnet.js; do
            [[ $(status "/$script") == 200 ]] || fail "missing /$script"
        done
        [[ $(docker exec "$name" id -u) != 0 ]] || fail 'container runs as root'
        echo 'Image smoke passed'
        exit 0
    fi
    sleep 1
done
docker logs "$name"
exit 1

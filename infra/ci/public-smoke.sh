#!/usr/bin/env bash
# Checks the deployment from the outside, through the public HTTPS address. Sourced by upload.sh.
validate_public_url() {
    local label='[a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?'
    local pattern="^https://$label(\\.$label)+(:([1-9][0-9]{0,4}))?/?$"
    [[ ${MIKRUS_PUBLIC_URL:-} =~ $pattern ]] || {
        echo 'Invalid MIKRUS_PUBLIC_URL: require an HTTPS DNS origin only' >&2
        return 2
    }
}
public_smoke() {
    local origin=${MIKRUS_PUBLIC_URL%/} attempt page
    page=$(mktemp)
    public_ok() {
        # No redirects: a 3xx is a failure, never an insecure follow-up hop.
        [[ $(curl --silent --show-error --proto '=https' --connect-timeout 5 --max-time 10 -o "$page" -w '%{http_code}' "$origin/") == 200 ]] &&
            grep -q 'Kraków bez barier' "$page" &&
            [[ $(curl --silent --show-error --proto '=https' --connect-timeout 5 --max-time 10 "$origin/api/health") == OK ]]
    }
    database_ok() {
        [[ $(curl --silent --show-error --proto '=https' --connect-timeout 5 --max-time 15 -o /dev/null -w '%{http_code}' "$origin/api/health/db") == 200 ]]
    }
    for ((attempt=1; attempt<=6; attempt++)); do
        if public_ok; then echo 'Public HTTPS smoke passed'; break; fi
        if ((attempt == 6)); then
            rm -f -- "$page"
            echo 'Public HTTPS check failed: inspect the port/subdomain setup in the Mikrus panel and MIKRUS_PUBLIC_URL. The container is running and is NOT rolled back.' >&2
            return 1
        fi
        sleep 5
    done
    rm -f -- "$page"
    for ((attempt=1; attempt<=3; attempt++)); do
        if database_ok; then echo 'Database reachable from the host'; return 0; fi
        sleep 5
    done
    echo 'Database check failed: inspect MONGO_CONNECTION_STRING and the Atlas network access list. The container is running and is NOT rolled back.' >&2
    return 1
}

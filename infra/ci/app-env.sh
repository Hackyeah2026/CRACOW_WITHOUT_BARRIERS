#!/usr/bin/env bash
# Renders the application's settings as a `docker run --env-file` on stdout.
# Sourced by upload.sh; values come from the GitHub "mikrus" environment.
emit_env() {
    local name=$1 value=$2
    [[ -n $value ]] || return 0
    # Docker takes everything after "=" literally: no quoting, no escapes, one line.
    if [[ $value == *$'\n'* || $value == *$'\r'* ]]; then
        echo "Invalid $name: must be a single line" >&2
        return 2
    fi
    printf '%s=%s\n' "$name" "$value"
}
render_app_env() {
    [[ ${MONGO_CONNECTION_STRING:-} =~ ^mongodb(\+srv)?:// ]] || {
        echo 'Missing or invalid MONGO_CONNECTION_STRING secret: require mongodb:// or mongodb+srv://' >&2
        return 2
    }
    if [[ -n ${OFFICIAL_LOGIN:-} && -z ${OFFICIAL_PASSWORD:-} ]] || [[ -z ${OFFICIAL_LOGIN:-} && -n ${OFFICIAL_PASSWORD:-} ]]; then
        echo 'OFFICIAL_LOGIN and OFFICIAL_PASSWORD must be set together' >&2
        return 2
    fi
    # The app starts without these, but the matching feature answers with an error.
    [[ -n ${OPENROUTESERVICE_API_KEY:-} ]] || echo '::warning::OPENROUTESERVICE_API_KEY is not set: street routing is disabled' >&2
    [[ -n ${OPENAI_API_KEY:-} ]] || echo '::warning::OPENAI_API_KEY is not set: photo analysis is disabled' >&2
    [[ -n ${OFFICIAL_LOGIN:-} ]] || echo '::warning::OFFICIAL_LOGIN is not set: no official account is seeded' >&2
    emit_env Mongo__ConnectionString "$MONGO_CONNECTION_STRING" || return
    emit_env Mongo__Database "${MONGO_DATABASE:-}" || return
    emit_env OpenRouteService__ApiKey "${OPENROUTESERVICE_API_KEY:-}" || return
    emit_env OpenAI__ApiKey "${OPENAI_API_KEY:-}" || return
    emit_env OpenAI__Model "${OPENAI_MODEL:-}" || return
    emit_env Officials__Seed__0__Login "${OFFICIAL_LOGIN:-}" || return
    emit_env Officials__Seed__0__Password "${OFFICIAL_PASSWORD:-}" || return
    emit_env Officials__Seed__0__DisplayName "${OFFICIAL_DISPLAY_NAME:-}" || return
    emit_env Officials__Seed__0__Unit "${OFFICIAL_UNIT:-}" || return
    # QR codes on certificates must point at the public address.
    emit_env Certificates__PublicBaseUrl "${MIKRUS_PUBLIC_URL%/}"
}

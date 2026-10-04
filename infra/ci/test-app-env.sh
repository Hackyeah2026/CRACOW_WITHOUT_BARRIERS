#!/usr/bin/env bash
# Checks the rendering of the container's env file without network or Docker.
set -euo pipefail
script_dir=$(cd -- "$(dirname -- "$0")" && pwd)
# shellcheck source-path=SCRIPTDIR
# shellcheck source=app-env.sh
source "$script_dir/app-env.sh"
fail() { echo "FAIL: $1" >&2; exit 1; }
reset() {
    MONGO_CONNECTION_STRING='mongodb+srv://user:p%40ss@cluster.example.test/?retryWrites=true&w=majority'
    MONGO_DATABASE='' OPENROUTESERVICE_API_KEY='ors-key' OPENAI_API_KEY='sk-key' OPENAI_MODEL=''
    OFFICIAL_LOGIN='urzednik' OFFICIAL_PASSWORD='pa$$ "q" \ `t` #x' OFFICIAL_DISPLAY_NAME='Jan Kowalski' OFFICIAL_UNIT=''
    MIKRUS_PUBLIC_URL='https://name.bieda.it/'
}
reset
expected='Mongo__ConnectionString=mongodb+srv://user:p%40ss@cluster.example.test/?retryWrites=true&w=majority
OpenRouteService__ApiKey=ors-key
OpenAI__ApiKey=sk-key
Officials__Seed__0__Login=urzednik
Officials__Seed__0__Password=pa$$ "q" \ `t` #x
Officials__Seed__0__DisplayName=Jan Kowalski
Certificates__PublicBaseUrl=https://name.bieda.it'
[[ $(render_app_env 2>/dev/null) == "$expected" ]] || fail 'unexpected rendering'
OPENAI_API_KEY=''
[[ $(render_app_env 2>&1 >/dev/null) == *'::warning::OPENAI_API_KEY'* ]] || fail 'missing key must warn'
for case in "MONGO_CONNECTION_STRING=" "MONGO_CONNECTION_STRING=Server=localhost" "OFFICIAL_PASSWORD=" "OFFICIAL_LOGIN=" $'OPENAI_API_KEY=sk\nInjected=1'; do
    reset
    printf -v "${case%%=*}" '%s' "${case#*=}"
    if render_app_env >/dev/null 2>&1; then fail "accepted invalid ${case%%=*}"; fi
done
echo 'app-env tests passed'

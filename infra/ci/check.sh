#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
dotnet restore CracowWithoutBarriers.slnx
dotnet build CracowWithoutBarriers.slnx --no-restore -c Debug -p:TreatWarningsAsErrors=true
dotnet test CracowWithoutBarriers.slnx --no-build --no-restore -c Debug
shellcheck infra/ci/*.sh infra/mikrus/*.sh
bash infra/ci/test-app-env.sh

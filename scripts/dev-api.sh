#!/usr/bin/env bash
# Restart the API locally with the in-memory demo database (data resets on every restart).
set -u
export DOTNET_ROOT="${DOTNET_ROOT:-/root/.dotnet}"
export PATH="$DOTNET_ROOT:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1
cd "$(dirname "$0")/../backend"
for pid in $(pgrep -f 'bin/Debug/net8.0/SchoolErp.Api' ; pgrep -f 'dotnet run --project src/SchoolErp.Api'); do
  [ "$pid" != "$$" ] && kill "$pid" 2>/dev/null
done
sleep 2
dotnet build src/SchoolErp.Api -v q 2>&1 | grep -E " error |Build succeeded" | sort -u
Database__Provider=InMemory ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://0.0.0.0:5080 \
  nohup dotnet run --project src/SchoolErp.Api --no-build --no-launch-profile > /tmp/opencode/api.log 2>&1 &
for _ in $(seq 60); do curl -sf localhost:5080/health >/dev/null && echo "API ready on :5080" && exit 0; sleep 2; done
echo "API failed to start; see /tmp/opencode/api.log"; exit 1

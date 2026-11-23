#!/usr/bin/env bash
# The stack simulated players play against, and the export of what they produced.
#
#   ./simulation/sim.sh up [turns-at-once]   Postgres, migrations, and the API on http://localhost:5101
#   ./simulation/sim.sh export               traces -> simulation/runs/export/{traces.json,turns.jsonl}
#   ./simulation/sim.sh down                 stop the API and Postgres (the data stays in the ww-sim-pg volume)
#
# Simulated games get their own PostgreSQL so they stay out of the dev database, and because SQLite
# lets only one turn write at a time. Needs docker, dotnet, jq, and the model credentials the API
# reads in Development (user secrets).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
API="$ROOT/wretched-whispers-server/WretchedWhispers.Api"
RUNS="$ROOT/simulation/runs"
PORT=5101
export ASPNETCORE_ENVIRONMENT=Development
export WW_DB_PROVIDER=postgres
export WW_DB_CONNECTION="Host=localhost;Port=5433;Database=ww;Username=postgres;Password=sim"

case "${1:-}" in
  up)
    mkdir -p "$RUNS"
    docker start ww-sim-pg > /dev/null 2>&1 || docker run -d --name ww-sim-pg \
      -e POSTGRES_PASSWORD=sim -e POSTGRES_DB=ww -p 127.0.0.1:5433:5432 \
      -v ww-sim-pg:/var/lib/postgresql/data postgres:17-alpine > /dev/null
    until docker exec ww-sim-pg pg_isready -U postgres -q; do sleep 1; done
    dotnet build "$ROOT/wretched-whispers-server/WretchedWhispers.slnx" -v q
    dotnet run --no-build --project "$ROOT/wretched-whispers-server/WretchedWhispers.Migrations"
    # Run from the project folder: that is where Development finds its settings and user secrets.
    # Five turns at once is what the gpt-5-mini deployment took without rate limiting; seven did not.
    cd "$API"
    GameSession__ConcurrentTurns="${2:-5}" ASPNETCORE_URLS="http://localhost:$PORT" \
      nohup dotnet bin/Debug/net10.0/WretchedWhispers.Api.dll > "$RUNS/api.log" 2>&1 &
    echo $! > "$RUNS/api.pid"
    until curl -sf "http://localhost:$PORT/health/ready" > /dev/null; do sleep 1; done
    echo "Ready: WW_API=http://localhost:$PORT"
    ;;
  export)
    cd "$API"
    dotnet bin/Debug/net10.0/WretchedWhispers.Api.dll export-traces "$RUNS/export" | grep -i exported
    # One line per turn, labelled with the persona that played the session.
    jq -c --slurpfile personas <(jq -s 'map({(.sessionId): .persona}) | add' "$RUNS/manifest.jsonl") \
      '.[] | {campaignId, chatSessionId, persona: $personas[0][.campaignId]} as $session | .turns[] | $session + .' \
      "$RUNS/export/traces.json" > "$RUNS/export/turns.jsonl"
    echo "$(wc -l < "$RUNS/export/turns.jsonl") turns -> $RUNS/export/turns.jsonl"
    ;;
  down)
    if [[ -f "$RUNS/api.pid" ]]; then
      kill "$(cat "$RUNS/api.pid")" 2> /dev/null || true
      rm "$RUNS/api.pid"
    fi
    docker stop ww-sim-pg > /dev/null
    echo "Stopped. Games so far are kept in the ww-sim-pg volume."
    ;;
  *)
    sed -n '2,7p' "$0"
    exit 1
    ;;
esac

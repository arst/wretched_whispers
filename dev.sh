#!/usr/bin/env bash
# Run the stack for local testing: API on http://localhost:5007 (Development, auto-migrates its
# SQLite database) and the Next dev server on http://localhost:3000. Ctrl-C stops both.
#
# Usage:  ./dev.sh [--api-only|--web-only]
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
MODE="${1:-}"

if [[ "$MODE" != "--web-only" ]]; then
  dotnet run --project "$ROOT/wretched-whispers-server/WretchedWhispers.Api" &
  API=$!
  # `dotnet run` execs the app as a child, so kill the whole process group, not just $API.
  trap 'trap - EXIT; kill 0' EXIT
fi

if [[ "$MODE" == "--api-only" ]]; then
  wait "$API"
else
  cd "$ROOT/wretched-whispers-web"
  npm run dev
fi

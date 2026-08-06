#!/usr/bin/env bash
set -euo pipefail

base_url=${1:?usage: verify-server-revision.sh BASE_URL}
email="deploy-${GITHUB_RUN_ID:-local}-$(date +%s)@example.com"
password="Wretched-${GITHUB_RUN_ID:-local}-a1!"
work_dir=$(mktemp -d)
trap 'rm -rf "$work_dir"' EXIT
jar="$work_dir/cookies"

curl -fsS "$base_url/health/live" >/dev/null
curl -fsS "$base_url/health/ready" >/dev/null
curl -fsS "$base_url/" >/dev/null
curl -fsS -X POST "$base_url/api/auth/register" -H 'Content-Type: application/json' \
  -d "{\"email\":\"$email\",\"password\":\"$password\"}" >/dev/null

# The SPA's real flow: cookie auth plus the antiforgery header. Bearer-only requests fail the
# antiforgery check on mutating endpoints, so the probe must behave like a browser.
curl -fsS -c "$jar" -X POST "$base_url/api/auth/login?useCookies=true" \
  -H 'Content-Type: application/json' \
  -d "{\"email\":\"$email\",\"password\":\"$password\"}" >/dev/null
csrf=$(curl -fsS -b "$jar" -c "$jar" "$base_url/api/auth/csrf" | jq -r .token)

session_id=$(curl -fsS -b "$jar" -H "X-CSRF-TOKEN: $csrf" -X POST "$base_url/api/sessions" \
  -H 'Content-Type: application/json' \
  -d '{"characterName":"Deployment Wretch"}' | jq -r .sessionId)

# A turn is submit-then-stream. Keyless deployments fail the turn itself, but the failure is
# delivered as an SSE error event — a data line either way, which is all this asserts.
request_id=$(cat /proc/sys/kernel/random/uuid)
turn_id=$(curl -fsS -b "$jar" -H "X-CSRF-TOKEN: $csrf" \
  -X POST "$base_url/api/sessions/$session_id/turns" \
  -H 'Content-Type: application/json' \
  -d "{\"requestId\":\"$request_id\",\"message\":\"Look around.\"}" | jq -r .turnId)
curl -fsSN --max-time 240 -b "$jar" "$base_url/api/turns/$turn_id/events" > "$work_dir/turn.sse"
grep -q '^data:' "$work_dir/turn.sse"

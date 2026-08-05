#!/usr/bin/env bash
set -euo pipefail

base_url=${1:?usage: verify-server-revision.sh BASE_URL}
email="deploy-${GITHUB_RUN_ID:-local}-$(date +%s)@example.com"
password="Wretched-${GITHUB_RUN_ID:-local}-a1!"
work_dir=$(mktemp -d)
trap 'rm -rf "$work_dir"' EXIT

curl -fsS "$base_url/health/live" >/dev/null
curl -fsS "$base_url/health/ready" >/dev/null
curl -fsS "$base_url/" >/dev/null
curl -fsS -X POST "$base_url/api/auth/register" -H 'Content-Type: application/json' \
  -d "{\"email\":\"$email\",\"password\":\"$password\"}" >/dev/null
token=$(curl -fsS -X POST "$base_url/api/auth/login?useCookies=false" \
  -H 'Content-Type: application/json' \
  -d "{\"email\":\"$email\",\"password\":\"$password\"}" | jq -r .accessToken)
session_id=$(curl -fsS -X POST "$base_url/api/sessions" \
  -H "Authorization: Bearer $token" -H 'Content-Type: application/json' \
  -d '{"characterName":"Deployment Wretch"}' | jq -r .sessionId)
curl -fsSN --max-time 240 -X POST "$base_url/api/sessions/$session_id/actions" \
  -H "Authorization: Bearer $token" -H 'Content-Type: application/json' \
  -d '{"message":"Look around."}' > "$work_dir/turn.sse"
grep -q '^data:' "$work_dir/turn.sse"

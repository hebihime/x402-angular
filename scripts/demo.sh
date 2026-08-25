#!/usr/bin/env bash
# Phase 5: agent story through MCP tools + fake facilitator.
#   list/menu → place → replay place → confirm 402 then pay → replay pay →
#   kitchen reject → two injected refund-rail failures → refunded.
# Asserts each beat. Read-model lag is visible and tolerated.
#
# Start the API first (`dotnet run --project src/Ordering.Api`) and optionally
# the dashboard (`cd frontend && npm start`) to watch the board.
set -euo pipefail

ROOT=$(cd "$(dirname "$0")/.." && pwd)
API=${API:-http://localhost:5240}

say() { printf '\n\033[1m» %s\033[0m\n' "$1"; }

say "Checking API at $API"
if ! curl -sf "$API/api/restaurants" >/dev/null; then
  say "API is not running. Start it with: docker compose up -d && dotnet run --project src/Ordering.Api"
  exit 1
fi

export API_URL="$API"
export CUSTOMER_ID="${CUSTOMER_ID:-demo-$(date +%s)}"
export X402_FAKE_PAYER="${X402_FAKE_PAYER:-0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa}"

say "Driving MCP tools against $API (customer=$CUSTOMER_ID)"
cd "$ROOT"
dotnet run --project src/Ordering.Mcp --no-launch-profile --configuration Release -- demo

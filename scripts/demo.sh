#!/usr/bin/env bash
# Scripted demo: place → confirm → reject → refund with 2 injected gateway
# failures → recovered refund. Run the API (dotnet run --project
# src/Ordering.Api) and the dashboard (cd frontend && npm start) first, open
# http://localhost:4200, then run this and watch the board move live.
set -euo pipefail

API=${API:-http://localhost:5240}
RESTAURANT=11111111-1111-1111-1111-111111111111   # Pixel Pizza (seeded)
MARGHERITA=11111111-1111-1111-1111-aaaaaaaaaaa1
SIZE_LARGE=11111111-1111-1111-1111-ccccccccccc2
EXTRA_CHEESE=11111111-1111-1111-1111-ccccccccccc5
CUSTOMER="demo-$(date +%s)"
KEY="demo-key-$(date +%s)"

say() { printf '\n\033[1m» %s\033[0m\n' "$1"; }
field() { python3 -c "import json,sys;print(json.load(sys.stdin).get('$1'))"; }

say "Injecting 2 refund failures into the simulated gateway"
curl -sf -X POST "$API/api/demo/gateway/fail-refunds" -H 'Content-Type: application/json' -d '{"count":2}' >/dev/null

say "Placing an order (server reprices: Margherita + Large + Extra cheese, x2)"
ORDER=$(curl -sf -X POST "$API/api/orders" \
  -H 'Content-Type: application/json' \
  -H "X-Customer-Id: $CUSTOMER" -H "Idempotency-Key: $KEY" \
  -d "{\"restaurantId\":\"$RESTAURANT\",\"lines\":[{\"menuItemId\":\"$MARGHERITA\",\"quantity\":2,\"modifierIds\":[\"$SIZE_LARGE\",\"$EXTRA_CHEESE\"]}]}")
ORDER_ID=$(echo "$ORDER" | field orderId)
echo "  order $ORDER_ID | status $(echo "$ORDER" | field status) | total $(echo "$ORDER" | field total) minor units (locked)"

say "Replaying the same Idempotency-Key (returns the same draft, no new order)"
REPLAY_ID=$(curl -sf -X POST "$API/api/orders" \
  -H 'Content-Type: application/json' \
  -H "X-Customer-Id: $CUSTOMER" -H "Idempotency-Key: $KEY" \
  -d "{\"restaurantId\":\"$RESTAURANT\",\"lines\":[{\"menuItemId\":\"$MARGHERITA\",\"quantity\":1,\"modifierIds\":[\"$SIZE_LARGE\"]}]}" | field orderId)
echo "  replayed orderId: $REPLAY_ID $([ "$REPLAY_ID" = "$ORDER_ID" ] && echo '(same — idempotent)')"

say "Confirming without X-PAYMENT (HTTP 402 challenge from the locked total)"
CONFIRM_URL="$API/api/orders/$ORDER_ID/confirm"
CHALLENGE=$(curl -s -w "\n%{http_code}" -X POST "$CONFIRM_URL" -H "X-Customer-Id: $CUSTOMER")
CHALLENGE_CODE=$(echo "$CHALLENGE" | tail -n1)
CHALLENGE_BODY=$(echo "$CHALLENGE" | sed '$d')
echo "  HTTP $CHALLENGE_CODE | maxAmountRequired $(echo "$CHALLENGE_BODY" | python3 -c "import json,sys; print(json.load(sys.stdin)['accepts'][0]['maxAmountRequired'])")"
[ "$CHALLENGE_CODE" = "402" ] || { say "expected 402, got $CHALLENGE_CODE"; exit 1; }

say "Confirming with fake X-PAYMENT (draft → paid)"
PAYMENT=$(python3 -c "import base64,json; print(base64.b64encode(json.dumps({'payer':'0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa','nonce':'$ORDER_ID'}).encode()).decode())")
curl -sf -X POST "$CONFIRM_URL" -H "X-Customer-Id: $CUSTOMER" -H "X-PAYMENT: $PAYMENT" | field status | sed 's/^/  status: /'
sleep 1

say "Restaurant rejects → refund_pending, refund worker takes over"
curl -sf -X POST "$API/api/restaurants/$RESTAURANT/orders/$ORDER_ID/reject" \
  -H 'Content-Type: application/json' -d '{"reason":"demo: out of dough"}' | field status | sed 's/^/  status: /'

say "Watching the refund retry through 2 injected failures (backoff 2s, 4s)…"
LAST=""
for _ in $(seq 1 60); do
  STATE=$(curl -sf "$API/api/orders/$ORDER_ID" 2>/dev/null || echo '{}')
  STATUS=$(echo "$STATE" | field status)
  ATTEMPTS=$(echo "$STATE" | field refundAttempts)
  LINE="  status: $STATUS | failed attempts so far: $ATTEMPTS"
  [ "$LINE" != "$LAST" ] && echo "$LINE" && LAST="$LINE"
  [ "$STATUS" = "refunded" ] && break
  sleep 1
done

say "Final history (from the read model):"
curl -sf "$API/api/orders/$ORDER_ID/history" | python3 -c "
import json, sys
for h in json.load(sys.stdin):
    reason = f\" — {h['reason']}\" if h['reason'] else ''
    print(f\"  {h['from'] or '∅':>14} → {h['to']:<14} [{h['actor']}]{reason}\")"

[ "$STATUS" = "refunded" ] && say "Demo complete: rejected order recovered to refunded after 2 gateway failures." \
  || { say "Demo did not reach refunded in time — check the API logs."; exit 1; }

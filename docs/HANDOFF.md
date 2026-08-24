# HANDOFF

Read this first each session; append at the end of each session.

## 2026-08-24 — session 1 (initial build)

**What changed**
- Repo bootstrapped from SUPERPROMPT.md (now `docs/DESIGN.md`). Full backend built: solution skeleton, domain core (state machine/money/pricing/guardrails), MediatR pipeline (Logging → Validation → Idempotency → Transaction), EF Core write model + two migrations (`InitialWriteModel`, `ReadModel` — the latter is raw SQL for the Dapper-only projection tables), outbox → projector → read model → SignalR, simulated failure-injectable gateway, refund/expiry/acceptance-timeout workers.
- Smoke-tested end to end against docker-compose Postgres (port **5441**): place → idempotent replay → confirm → reject → refund with 2 injected gateway failures → recovered refund; history and board queries served from the projection.

**Environment notes**
- .NET 8 SDK lives at `~/.dotnet` (machine default is .NET 10); `global.json` pins 8.0.424. If `dotnet` resolves to v10, prefix with `PATH="$HOME/.dotnet:$PATH"`.
- `dotnet ef` is a repo-local tool (`dotnet tool restore`, then `dotnet dotnet-ef …` or `dotnet ef` via the manifest).

**Phases 2–8 (same session)**
- Test suites landed per phase: 39 unit tests (exhaustive state-machine + aggregate no-side-effect proofs, money JSON contract, pricing, guardrails, backoff) and 30 integration tests (Testcontainers; invariants 1–6 incl. atomicity-by-breaking-tables, a 3-way idempotency race, fake-clock day-boundary confirm re-check, refund exhaustion, worker sweeps, projection ordering/broadcasts).
- Angular 22 dashboard (standalone/signals/zoneless, Node 24 via nvm): kanban + payment strip with refund_failed alert + drawer with history timeline; SignalR patches in place, re-sync on failure/reconnect; **all wire payloads parsed with Zod** (`frontend/src/app/api/schemas.ts` — keep in lockstep with the backend contract). Verified in a live browser end to end.
- `scripts/demo.sh` walks place → idempotent replay → confirm → reject → refund with 2 injected failures → refunded, printing the history; it visibly catches read-model lag (first poll shows stale `paid`).
- Full README (CQRS narrative, mermaid diagram, pipeline rationale, eventual-consistency notes, production deltas). CLAUDE.md commands updated (port 5441, demo script, Node/SDK notes).

**Unstable / open**
- Outbox rows are never pruned (noted in README production deltas) — fine for demo scale.
- Frontend has no component tests (backend suite is the contract; add if the dashboard grows logic).

**Needs foundation change**
- (none)

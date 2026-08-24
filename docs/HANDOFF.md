# HANDOFF

Read this first each session; append at the end of each session.

## 2026-08-24 — session 1 (initial build)

**What changed**
- Repo bootstrapped from SUPERPROMPT.md (now `docs/DESIGN.md`). Full backend built: solution skeleton, domain core (state machine/money/pricing/guardrails), MediatR pipeline (Logging → Validation → Idempotency → Transaction), EF Core write model + two migrations (`InitialWriteModel`, `ReadModel` — the latter is raw SQL for the Dapper-only projection tables), outbox → projector → read model → SignalR, simulated failure-injectable gateway, refund/expiry/acceptance-timeout workers.
- Smoke-tested end to end against docker-compose Postgres (port **5441**): place → idempotent replay → confirm → reject → refund with 2 injected gateway failures → recovered refund; history and board queries served from the projection.

**Environment notes**
- .NET 8 SDK lives at `~/.dotnet` (machine default is .NET 10); `global.json` pins 8.0.424. If `dotnet` resolves to v10, prefix with `PATH="$HOME/.dotnet:$PATH"`.
- `dotnet ef` is a repo-local tool (`dotnet tool restore`, then `dotnet dotnet-ef …` or `dotnet ef` via the manifest).

**Unstable / open**
- Frontend (phase 7) and README/demo script (phase 8): frontend must use Zod to parse all API/SignalR payloads at the boundary (user requirement).
- Test suites land per phase in order (unit → invariants → projection → refund/workers).

**Needs foundation change**
- (none)

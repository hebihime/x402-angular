# CLAUDE.md

CQRS-first restaurant ordering: customers place, confirm, and cancel orders; a
restaurant dashboard accepts, prepares, and completes them; a refund lifecycle
handles rejections. Same domain as my TypeScript event-driven monolith
(x402-food), rebuilt in .NET where the architecture is the deliverable: real
command/query separation with MediatR earning its keep through pipeline
behaviors, an EF Core write model, and a Dapper-read denormalized projection.
This is a portfolio demonstration of choosing CQRS on purpose, not a production
system.

This file is the enforcement layer: it exists so that any model or contributor
working in this repo cannot violate the design without noticing. When this file
and your instinct disagree, this file wins. `SUPERPROMPT.md` (or `docs/DESIGN.md`
once it exists) holds the full build plan; when this file and that disagree,
stop and ask.

## Stack (pinned, do not substitute)

- .NET 8 (LTS), nullable reference types enabled, warnings as errors
- ASP.NET Core minimal APIs (thin: endpoints send MediatR requests, nothing else)
- MediatR for commands/queries + pipeline behaviors; FluentValidation for input
- Write model: EF Core + PostgreSQL (Npgsql), migrations checked in
- Read model: denormalized projection tables queried with Dapper only
- Projector + workers: hosted BackgroundServices in the one ASP.NET Core host
  (modular monolith — no separate services, no message broker)
- Dashboard live updates: SignalR
- Tests: xUnit + FluentAssertions; integration via Testcontainers (real Postgres)
- Frontend: Angular (standalone components + signals), hand-rolled CSS
- docker-compose for local Postgres

## Solution layout

```
Ordering.sln
src/Ordering.Domain          state machine, money, pricing/snapshots, guardrails [FOUNDATION]
src/Ordering.Application     commands/queries/handlers, behaviors, validators
src/Ordering.Infrastructure  DbContext + migrations, outbox, projector,
                             Dapper read repos, payment gateway, workers        [FOUNDATION: outbox + projector]
src/Ordering.Api             endpoints, SignalR hub, DI wiring (thin)
tests/Ordering.Tests.Unit
tests/Ordering.Tests.Integration   (invariant suite lives in .../Invariants)
frontend/                    Angular dashboard
```

## Hard invariants

Non-negotiable. Every PR-sized change must leave all of them true. The tests in
`tests/Ordering.Tests.Integration/Invariants` encode them; if an invariant test
blocks you, the test is right and your change is wrong.

1. **The server is the only pricing authority.** Client-supplied prices, totals,
   and discounts are ignored entirely. `PlaceOrderCommand` reprices every line
   from the current menu, applies modifier deltas, snapshots line items (name,
   unit price, selected modifiers) into the order row, locks the total, and sets
   an expiry. After draft creation, the total never changes.

2. **Every state transition is one transaction with three writes.** Update the
   order status, append a StatusHistory row, insert a domain event into the
   Outbox table. Same transaction, always, no exceptions — the TransactionBehavior
   plus `Order.TransitionTo(...)` make this structural. `TransitionTo` is the
   only way to change an order's status. Never set `Order.Status` anywhere else.
   Never emit an event outside the outbox.

3. **The state machine table is law, including the actor column.** A transition
   is valid only if the (from, to, actor) tuple exists in the table below. The
   actor is derived server-side (customer = customer endpoints, restaurant =
   dashboard endpoints, system = workers/settlement), never from request
   payloads. Invalid or repeated transitions return the current state with no
   side effects.

| From           | To             | Actor      | Trigger |
|----------------|----------------|------------|---------|
| (none)         | draft          | customer   | PlaceOrderCommand (reprice, snapshot, lock total, set expiry) |
| draft          | paid           | system     | ConfirmOrderCommand → gateway charge succeeded |
| draft          | cancelled      | customer   | CancelOrderCommand before payment |
| draft          | expired        | system     | expiry worker, draft TTL elapsed |
| paid           | accepted       | restaurant | dashboard |
| paid           | rejected       | restaurant | dashboard |
| paid           | rejected       | system     | acceptance-timeout worker |
| accepted       | preparing      | restaurant | dashboard |
| preparing      | ready          | restaurant | dashboard |
| ready          | completed      | restaurant | dashboard |
| rejected       | refund_pending | system     | automatic, immediately on rejection |
| refund_pending | refunded       | system     | refund worker: gateway refund succeeded |
| refund_pending | refund_failed  | system     | retries exhausted, manual-intervention flag |

4. **Two-phase ordering; payment only at confirm.** Placing is free and creates
   a draft. `ConfirmOrderCommand` charges via `IPaymentGateway`. Refunds are not
   reversals: they are a separate gateway operation with their own lifecycle,
   owned by the refund BackgroundService with retry + exponential backoff and a
   terminal `refund_failed` state that sets a dashboard-visible manual flag.

5. **Idempotency on every mutating surface.**
   - PlaceOrder requires a client `Idempotency-Key`; a unique constraint returns
     the existing draft on retry.
   - Confirm is keyed on the charge id at the DB-constraint level, not just in
     application logic; a replayed confirm settles nothing and returns the
     original success response.
   - Status transitions are idempotent via invariant 3.

6. **Guardrails key on the customer id.** Global max order value; daily
   cumulative spend cap per customer. Enforced at draft creation and re-checked
   at confirm. There is no other identity or auth.

7. **Money is integers.** All amounts are minor units (cents) stored as `long`,
   serialized as strings in JSON. No `decimal`, `double`, or float touches money
   anywhere, including Angular (format at the display edge only, in one pipe).

8. **Read/write separation is real.** Query handlers read ONLY projection tables
   via Dapper and never touch the DbContext. Projections are built by the
   projector BackgroundService draining the outbox in order, then broadcasting
   over SignalR. The write side never serves dashboard reads. Eventual
   consistency is embraced, not patched over.

## MediatR pipeline (order is deliberate; do not reorder)

Logging → Validation → Idempotency (commands marked `IIdempotentCommand`) →
Transaction (commands only — queries must never open a write transaction).
No behavior may contain domain logic. No handler may bypass the pipeline.

## Foundation code

`src/Ordering.Domain` and the outbox/projector in `src/Ordering.Infrastructure`
are foundation code once written:

- Do not restructure, rewrite, or "simplify" them. Consume their exports.
- If a task seems to require changing `Order.TransitionTo`, the outbox schema,
  pricing/snapshotting, or the projector's ordering guarantees: stop, write the
  problem into `docs/HANDOFF.md` under "Needs foundation change", and continue
  with other work. Do not work around it by duplicating logic.
- Adding new pure functions/methods to Domain is allowed; changing existing
  transition, pricing, or guardrail semantics is not.
- Never edit an applied EF Core migration. Schema changes are new migrations.

## Anti-patterns (things you will be tempted to do; do not)

- Event sourcing, aggregate-repository frameworks, MassTransit, RabbitMQ, or any
  broker. The outbox table + projector BackgroundService is the entire event
  system. A future broker swap is one sentence in the README.
- A generic saga/orchestration framework. The saga is the state machine on the
  orders table plus workers. That is the whole point of the design.
- AutoMapper or any mapping library. Map by hand.
- Auth, sessions, API keys, user accounts. `X-Customer-Id` is the only principal.
- Nesting modifier groups or recursive modifiers "for flexibility". One level,
  min/max counts, price deltas.
- Geo indexing or distance math. Location is a city string with equality filter.
- Business logic in endpoints, behaviors, the projector, or Angular. If an
  Angular component contains an if-statement deciding whether a transition is
  legal (beyond choosing which buttons to render), it is wrong — the server is
  the authority and the UI must survive a rejected transition by re-syncing.
- Mocking the DbContext or Postgres in integration tests, or mocking
  `TransitionTo` anywhere.
- Retry loops that re-place orders instead of reusing the idempotency key.
- Money as `decimal`/`double` or decimal strings.

## Testing conventions

- `tests/Ordering.Tests.Integration/Invariants` is the contract suite: state
  machine exhaustiveness (every invalid (from, to, actor) tuple rejected),
  transaction atomicity (fail between the three writes, assert nothing
  persisted), idempotency replays, repricing (client-supplied prices ignored),
  guardrail caps, refund retry exhaustion → refund_failed + flag. Written early;
  later sessions must not weaken, skip, or delete them.
- The only sanctioned fakes: `IPaymentGateway` (deterministic, failure-injectable:
  "fail N times then succeed") and a clock abstraction (`TimeProvider`). Real
  Postgres via Testcontainers everywhere else.

## Commands

```
docker compose up -d               # postgres
dotnet ef database update -p src/Ordering.Infrastructure -s src/Ordering.Api
dotnet run --project src/Ordering.Api    # API + SignalR + workers (one host)
dotnet test                        # unit + integration + invariants
cd frontend && npm start           # Angular dashboard
```

Keep these accurate as the repo grows; a stale command in this file is a bug.
Add a seed mechanism (restaurants + menus with modifier groups) and a scripted
demo (place → confirm → reject → refund with 2 injected gateway failures →
recovered refund) and record their commands here when they exist.

## Session discipline

- Read `docs/HANDOFF.md` at the start of every session; append to it at the end
  (what changed, what is unstable, open questions, "Needs foundation change").
- Record every judgment call that deviates from or refines the design in
  `docs/DECISIONS.md` with one line of rationale.
- Commit in small, labeled increments per build phase (the phase order in
  SUPERPROMPT.md is deliberate; do not reorder it). Never leave the repo in a
  state where `dotnet test` fails at the end of a session.

## Configuration

All settings via `appsettings.json` + environment overrides, bound to validated
options classes at startup (validate-on-start; malformed money or missing values
fail boot). Do not read `IConfiguration` ad hoc in handlers or services.

```
ConnectionStrings__Ordering=
Ordering__DraftTtlSeconds=
Ordering__AcceptanceTimeoutSeconds=
Ordering__MaxOrderValueMinorUnits=
Ordering__DailySpendCapMinorUnits=
Ordering__Refund__MaxAttempts=
Ordering__Refund__BackoffBaseMs=
Ordering__Refund__BackoffCapMs=
Ordering__Gateway__FailNextCharges=   # simulated-gateway failure injection (demo/tests)
Ordering__Gateway__FailNextRefunds=
```

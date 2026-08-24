# Superprompt: CQRS restaurant-ordering backend (ASP.NET Core + MediatR + Angular)

You are building a standalone portfolio repo from scratch in this empty directory.
It re-implements a restaurant-ordering domain I have already built once as a
minimal event-driven TypeScript monolith. This repo is the counterpart: the SAME
domain, built CQRS-first in .NET, where the architecture is the point. The
narrative for a .NET interviewer is: "accept/reject/prepare/refund are naturally
commands, the dashboard is naturally queries — so this domain genuinely earns
CQRS, and here is MediatR earning its keep via pipeline behaviors, not as a
mediator for its own sake."

Do not gold-plate. Every abstraction must be load-bearing. When in doubt, choose
the version of a pattern you could defend line-by-line in an interview.

A `CLAUDE.md` already exists in this repo root — it is the enforcement layer for
everything below. Read it first; when it and this prompt disagree, stop and ask.

## Stack (pinned, do not substitute)

- .NET 8 (LTS), ASP.NET Core, nullable reference types + warnings-as-errors
- MediatR for commands/queries + pipeline behaviors
- FluentValidation, wired in via a MediatR ValidationBehavior
- Write model: EF Core + PostgreSQL (Npgsql)
- Read model: separate denormalized projection tables, queried with Dapper —
  query handlers NEVER touch the EF Core DbContext
- Projection + workers: hosted BackgroundServices in the same ASP.NET Core host
  (modular monolith — no separate services, no message broker)
- Live updates to the dashboard: SignalR
- Tests: xUnit + FluentAssertions; integration tests against real Postgres via
  Testcontainers (never mock the database)
- Frontend: Angular (latest, standalone components + signals), no component
  framework required — hand-rolled CSS is fine
- docker-compose for Postgres

## Solution layout

```
Ordering.sln
src/Ordering.Domain          entities, order state machine, money, guardrails (no deps)
src/Ordering.Application     MediatR commands/queries/handlers, behaviors, validators
src/Ordering.Infrastructure  EF Core DbContext + migrations, outbox, Dapper read
                             repositories, projector, payment gateway, workers
src/Ordering.Api             ASP.NET Core host: endpoints, SignalR hub, DI wiring
tests/Ordering.Tests.Unit
tests/Ordering.Tests.Integration   (Testcontainers; includes tests/invariants suite)
frontend/                    Angular dashboard
```

## Domain

Entities: Restaurant (name, city string — equality filter only, no geo), Menu →
MenuItem → ModifierGroup (min/max select) → Modifier (price delta). Modifiers
are ONE level deep, never recursive. Customers are identified only by a
`X-Customer-Id` header (an opaque string); there is no auth, no user accounts —
out of scope by design, say so in the README.

Money is integer minor units (cents) stored as `long` end-to-end, serialized as
strings in JSON. No decimals or doubles touch money anywhere, including the
Angular app — formatting to "$12.50" happens at the display edge only.

### Order state machine (law, including the actor column)

A transition is valid only if the (from, to, actor) tuple is in this table. The
actor is derived server-side from which surface was called (customer endpoints,
restaurant/dashboard endpoints, or system/worker) — never from request bodies.
Invalid or repeated transitions return the current order state with NO side
effects (idempotent, not an error).

| From           | To             | Actor      | Trigger |
|----------------|----------------|------------|---------|
| (none)         | draft          | customer   | PlaceOrderCommand (reprice, snapshot, lock total, set expiry) |
| draft          | paid           | system     | ConfirmOrderCommand → payment gateway charge succeeded |
| draft          | cancelled      | customer   | CancelOrderCommand before payment |
| draft          | expired        | system     | expiry worker, draft TTL elapsed |
| paid           | accepted       | restaurant | AcceptOrderCommand |
| paid           | rejected       | restaurant | RejectOrderCommand |
| paid           | rejected       | system     | acceptance-timeout worker |
| accepted       | preparing      | restaurant | StartPreparingCommand |
| preparing      | ready          | restaurant | MarkReadyCommand |
| ready          | completed      | restaurant | CompleteOrderCommand |
| rejected       | refund_pending | system     | automatic, immediately on rejection |
| refund_pending | refunded       | system     | refund worker: gateway refund succeeded |
| refund_pending | refund_failed  | system     | retries exhausted → manual-intervention flag |

## Hard invariants (encode these in tests/invariants; the tests are the contract)

1. **The server is the only pricing authority.** PlaceOrderCommand ignores any
   client-supplied prices/totals entirely, reprices every line from the current
   menu + modifier deltas, snapshots line items (name, unit price, chosen
   modifiers) INTO the order row as owned/JSON data, locks the total, sets an
   expiry. After draft creation the total never changes.
2. **Every state transition is one transaction with three writes:** update
   order status, append a StatusHistory row (from, to, actor, at), insert a
   domain event into an Outbox table. Same transaction, no exceptions. The ONLY
   code path that transitions an order is one domain method
   (`Order.TransitionTo(...)` guarded by the table) invoked through command
   handlers; no handler writes status directly.
3. **Two-phase ordering.** Placing is free and creates a draft; payment happens
   at confirm via `IPaymentGateway` (simulated implementation — records a fake
   charge id; can be told to fail N times for demos/tests). Refunds are NOT
   reversals: they are a separate gateway operation with their own lifecycle,
   owned by a refund BackgroundService with retry + exponential backoff and a
   terminal refund_failed state that sets a dashboard-visible manual flag.
4. **Idempotency on every mutating surface.** PlaceOrder requires an
   Idempotency-Key header enforced by a DB unique constraint (retry returns the
   existing draft). Confirm is keyed on the charge id at the constraint level —
   a replayed confirm settles nothing and returns the original response.
   Transitions are idempotent via invariant 2's table check.
5. **Guardrails:** global max order value + per-customer daily cumulative spend
   cap, enforced in the domain at draft creation and re-checked at confirm.
6. **Read/write separation is real.** Query handlers (GetOrderQuery,
   ListRestaurantOrdersQuery, GetOrderHistoryQuery) read ONLY denormalized
   projection tables via Dapper. Projections are built by a BackgroundService
   that drains the Outbox in order (with a cursor/processed flag), updates the
   read tables, then broadcasts the event over SignalR. Eventual consistency is
   embraced and documented; the write side never serves dashboard reads.

## Where MediatR must visibly earn its keep

Implement these pipeline behaviors, ordered deliberately, and document the
ordering rationale in the README:
1. LoggingBehavior (request name, duration, outcome)
2. ValidationBehavior (FluentValidation; short-circuits with 400 problem+json)
3. IdempotencyBehavior (for commands marked `IIdempotentCommand`)
4. TransactionBehavior (opens the EF Core transaction; commands only — queries
   must not open write transactions; this is what makes invariant 2 structural
   rather than disciplined)

Commands return typed results (rich Result type or domain exceptions mapped by
middleware — pick one and be consistent). No behavior may contain domain logic.

## HTTP surface (thin; endpoints just send MediatR requests)

Customer: list restaurants (city filter), get menu, POST place order,
POST confirm, POST cancel. Restaurant/dashboard: list orders (by restaurant,
grouped/filterable by status), get order + history, POST accept / reject /
start-preparing / mark-ready / complete, and the SignalR hub for live events.

## Angular dashboard (this is a re-implementation of a working spec — follow it)

- Kanban board with kitchen columns: New (paid) / Accepted / Preparing / Ready /
  Completed, each with a count badge, cards sorted oldest-first.
- A separate "Payment lifecycle" strip below the board showing
  refund_pending / refunded / refund_failed orders; the strip gets an alert
  style when any order is refund_failed ("needs attention" — the
  manual-intervention flag).
- Clicking a card opens a detail drawer: snapshotted line items with modifiers
  and per-line totals, locked total, customer id, a status-history timeline
  (from → to, actor, timestamp), refund attempt count + last error when present.
- Action buttons in the drawer are derived from the restaurant slice of the
  transition table (paid→accept/reject, accepted→start preparing,
  preparing→mark ready, ready→complete) — but this is display sugar only; the
  server remains the authority and the UI must handle a rejected transition
  gracefully by re-syncing.
- Live updates via SignalR patch the board in place; on reconnect, refetch.
- Money arrives as integer-cent strings and is formatted only in a display pipe.

## Anti-patterns (do not)

- No event sourcing, no aggregate-repository framework, no MassTransit/broker,
  no generic saga library. The outbox table + BackgroundService IS the eventing.
- No AutoMapper anywhere; map by hand.
- No business logic in controllers/endpoints, none in behaviors, none in Angular.
- No mocking the DbContext or Postgres in integration tests; the only sanctioned
  fake is the payment gateway (and a clock abstraction).
- No auth/identity system beyond the customer-id header.

## Build order (commit per phase, keep tests green at every commit)

1. Skeleton: solution, docker-compose Postgres, EF Core model + migrations,
   seed data (a few restaurants/menus with modifier groups).
2. Domain core: state machine + money + snapshot pricing, unit-tested
   exhaustively (every invalid (from,to,actor) tuple rejected).
3. Application layer: commands/queries + the four behaviors + validators;
   integration tests for invariants 1, 2, 4, 5 against Testcontainers.
4. Outbox → projector → Dapper read model → SignalR; integration test that a
   command's event reaches the projection.
5. Payment gateway (simulated, failure-injectable) + confirm + refund worker
   with retry/backoff; test refund exhaustion → refund_failed + flag.
6. Expiry + acceptance-timeout workers.
7. Angular dashboard against the read API + SignalR.
8. README: the CQRS narrative, an architecture diagram, behavior-pipeline
   rationale, eventual-consistency notes, and an honest "what I'd change for
   production" section.

Definition of done: `docker compose up -d && dotnet test` fully green including
the invariants suite; a scripted demo (`demo.http` or a small script) that walks
place → confirm → reject → refund with 2 injected gateway failures →
recovered refund, visible live on the dashboard.

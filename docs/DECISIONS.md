# Decisions

Judgment calls that deviate from or refine the design, one line of rationale each.

- **2026-08-24** Local Postgres maps to host port **5441** (not 5432): 5432 is occupied by another project's container on this machine; in-container port and Testcontainers are unaffected.
- **2026-08-24** .NET 8 SDK installed user-locally at `~/.dotnet` (8.0.424, pinned via `global.json`): the machine only had .NET 10 and the stack pins .NET 8 LTS.
- **2026-08-24** Backend production code for phases 1–6 landed together in the initial commit: the transaction/outbox/pipeline invariants are structural across all four projects, so the layers could not compile independently; per-phase verification lands as separate, phase-ordered test commits.
- **2026-08-24** Money serialization lives on the domain type (`MoneyJsonConverter` attribute on `Money`): every serialization edge (API, outbox payloads, projection columns) then enforces string-of-minor-units for free, instead of per-endpoint configuration.
- **2026-08-24** Catalog (restaurants/menus) is seeded into the read tables directly rather than projected: it is immutable reference data with no lifecycle or events; projecting it would be ceremony. Orders are the only projected aggregate.
- **2026-08-24** Domain events carry a full denormalized `OrderSnapshot`: the projector never reads the write model, which keeps the read side rebuild-from-outbox and the projector trivially ordered.
- **2026-08-24** Input validation failures leave the pipeline as `ValidationException` → 400 problem+json; domain outcomes (not-found, guardrail, payment, conflict) travel as a typed `Result` — one convention per concern, applied consistently.
- **2026-08-24** Daily-spend guardrail counts today's orders in every status except `cancelled`, `expired`, `refunded` (money returned or never settled); drafts count so the cap cannot be bypassed by parallel drafts. Confirm re-checks excluding the order being confirmed (its own draft already counted once).
- **2026-08-24** Confirm on an expired-but-unswept draft transitions it to `expired` inline and returns 409: the TTL is a domain rule, not a worker implementation detail.
- **2026-08-24** Command handlers load orders with `SELECT … FOR UPDATE`: concurrent transitions on one order serialize at the row lock, making the state-machine check race-free without optimistic-concurrency retry loops.
- **2026-08-24** Gateway transaction ids are deterministic per order (`ch_/re_` + order id): the order id doubles as the gateway idempotency key, so a replayed charge/refund settles nothing; the unique constraint on `charge_id` backs this at the DB level.
- **2026-08-24** Outbox is drained by a processed-flag scan in id order (single projector instance) rather than a cursor: a cursor can skip rows when a lower id commits after a higher one was processed; per-order ordering is guaranteed by the row lock serializing writers.
- **2026-08-24** The frontend will use **Zod** to parse every API response and SignalR payload at the boundary (user requirement, 2026-08-24): the money-as-string and snake_case-status contracts are validated at runtime instead of trusted; backend validation remains FluentValidation per the pinned stack.

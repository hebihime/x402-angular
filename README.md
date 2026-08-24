# Ordering — CQRS restaurant ordering in .NET 8

A portfolio re-implementation of a restaurant-ordering domain (originally an
event-driven TypeScript monolith) built CQRS-first: MediatR pipeline behaviors,
an EF Core write model, an outbox-driven projector, a Dapper read model, and a
SignalR-fed Angular dashboard.

> Work in progress — the full README (architecture narrative, diagrams, demo
> script) lands with phase 8. See `CLAUDE.md` for the enforcement layer and
> `docs/DESIGN.md` for the build plan.

## Run

```
docker compose up -d               # postgres on host port 5441
dotnet ef database update -p src/Ordering.Infrastructure -s src/Ordering.Api
dotnet run --project src/Ordering.Api    # API + SignalR + workers (one host)
dotnet test                        # unit + integration + invariants
cd frontend && npm start           # Angular dashboard
```

No auth by design: `X-Customer-Id` is the only principal. Money is integer
minor units end to end, serialized as strings.

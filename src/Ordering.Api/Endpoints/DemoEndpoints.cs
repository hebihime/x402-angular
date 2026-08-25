using Ordering.Infrastructure.Payments;

namespace Ordering.Api.Endpoints;

// Demo-only failure injection so the scripted demo can show facilitator
// declines and the refund worker retrying. Not part of the domain.

public sealed record InjectFailuresRequest(int Count);

public static class DemoEndpoints
{
    public static void MapDemoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/demo/x402/fail-verifies", (InjectFailuresRequest request, FakeFacilitator facilitator) =>
        {
            facilitator.InjectVerifyFailures(request.Count);
            return Results.Ok(new { injected = request.Count });
        });

        app.MapPost("/api/demo/x402/fail-settles", (InjectFailuresRequest request, FakeFacilitator facilitator) =>
        {
            facilitator.InjectSettleFailures(request.Count);
            return Results.Ok(new { injected = request.Count });
        });

        app.MapPost("/api/demo/gateway/fail-refunds", (InjectFailuresRequest request, FakeRefundRail rail) =>
        {
            rail.InjectFailures(request.Count);
            return Results.Ok(new { injected = request.Count });
        });
    }
}

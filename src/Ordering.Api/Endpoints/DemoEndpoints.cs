using Ordering.Infrastructure.Payments;

namespace Ordering.Api.Endpoints;

// Demo-only failure injection for the simulated gateway, so the scripted demo
// can show the refund worker retrying and recovering. Not part of the domain.

public sealed record InjectFailuresRequest(int Count);

public static class DemoEndpoints
{
    public static void MapDemoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/demo/gateway/fail-charges", (InjectFailuresRequest request, SimulatedPaymentGateway gateway) =>
        {
            gateway.InjectChargeFailures(request.Count);
            return Results.Ok(new { injected = request.Count });
        });

        app.MapPost("/api/demo/gateway/fail-refunds", (InjectFailuresRequest request, SimulatedPaymentGateway gateway) =>
        {
            gateway.InjectRefundFailures(request.Count);
            return Results.Ok(new { injected = request.Count });
        });
    }
}

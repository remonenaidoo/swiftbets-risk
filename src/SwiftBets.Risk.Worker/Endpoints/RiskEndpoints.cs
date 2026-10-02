using System.Security.Claims;
using SwiftBets.BuildingBlocks.Web;
using SwiftBets.Contracts.Errors;
using SwiftBets.Risk.Application;
using SwiftBets.Risk.Infrastructure;

namespace SwiftBets.Risk.Worker.Endpoints;

/// <summary>The trader console's risk view: liability per fixture, a fixture's live book, caps and alerts.</summary>
public static class RiskEndpoints
{
    public static IEndpointRouteBuilder MapRiskEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/admin/risk").RequireAuthorization(Roles.Operator);

        admin.MapGet("/fixtures", async (int? limit, IRiskStore store, CancellationToken cancellationToken) =>
            Results.Ok(await store.ViewsAsync(Math.Clamp(limit ?? 100, 1, 500), cancellationToken)));

        // Asks the fixture's actor, so this is the live book, recovered from its snapshot if the actor was not running.
        admin.MapGet("/fixtures/{fixtureId}", async (string fixtureId, RiskActors actors, CancellationToken cancellationToken) =>
            Results.Ok(await actors.FixtureAsync(fixtureId, cancellationToken)));

        // A cap in minor units, 0 to suspend the fixture outright, or null to return to the default cap.
        admin.MapPut("/fixtures/{fixtureId}/cap", async (string fixtureId, CapBody body, HttpContext context, IRiskStore store, RiskActors actors, CancellationToken cancellationToken) =>
        {
            if (body.CapMinor is < 0 || string.IsNullOrWhiteSpace(body.Reason))
            {
                return Error.Validation("invalid_cap", "Give a cap of zero or more (or null for the default) and a reason.").ToHttpResult(context);
            }

            var operatorId = context.User.FindFirstValue("sub") ?? "unknown";
            await store.SetCapAsync(fixtureId, body.CapMinor, body.Reason.Trim(), operatorId, cancellationToken);
            return Results.Ok(await actors.CapChangedAsync(fixtureId, cancellationToken));
        });

        admin.MapGet("/alerts", async (int? limit, IRiskStore store, CancellationToken cancellationToken) =>
            Results.Ok(await store.AlertsAsync(Math.Clamp(limit ?? 50, 1, 200), cancellationToken)));

        return endpoints;
    }

    public sealed record CapBody(long? CapMinor, string? Reason);
}

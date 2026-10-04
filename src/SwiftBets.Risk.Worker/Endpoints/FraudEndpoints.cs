using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text.RegularExpressions;
using SwiftBets.BuildingBlocks.Web;
using SwiftBets.Contracts.Errors;
using SwiftBets.Risk.Application;

namespace SwiftBets.Risk.Worker.Endpoints;

/// <summary>Fraud signals in, and the console's fraud cases out (BACKLOG 33).</summary>
public static partial class FraudEndpoints
{
    public const string Read = "fraud.read";
    public const string Write = "fraud.write";

    public static IEndpointRouteBuilder MapFraudEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // The site reports the device at sign-in, registration, deposit and withdrawal; the server adds the network.
        endpoints.MapPost("/me/device-signals", async (DeviceBody body, HttpContext context, FraudHandler fraud, FraudOptions options, TimeProvider time, CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(context.User.FindFirstValue("sub"), out var userId))
            {
                return Results.Unauthorized();
            }

            if (body.Kind is null || !DeviceSignal.Kinds.Contains(body.Kind) || body.DeviceHash is null || !HashPattern().IsMatch(body.DeviceHash))
            {
                return Error.Validation("invalid_signal", "Give a kind (sign-in, register, deposit or withdrawal) and a SHA-256 device hash.").ToHttpResult(context);
            }

            var headers = context.Request.Headers;
            var signal = new DeviceSignal(userId, body.Kind, body.DeviceHash, IpPrefix(ClientIp(context)), Header(headers, options.AsnHeader, 20),
                Header(headers, options.CountryHeader, 2)?.ToUpperInvariant(), Coordinate(headers, options.LatitudeHeader, 90), Coordinate(headers, options.LongitudeHeader, 180),
                Header(headers, "User-Agent", 200), time.GetUtcNow());
            var bearer = headers.Authorization.ToString() is { } auth && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? auth[7..] : null;
            await fraud.DeviceAsync(signal, bearer, cancellationToken);
            return Results.NoContent();
        }).RequireAuthorization(Roles.Punter);

        var service = endpoints.MapGroup("/internal/fraud").RequireAuthorization(Roles.Service);

        // Payments sends a keyed hash of each saved bank account, never the number.
        service.MapPost("/bank-accounts", async (BankBody body, HttpContext context, FraudHandler fraud, CancellationToken cancellationToken) =>
        {
            if (body.UserId == Guid.Empty || body.Fingerprint is null || !HashPattern().IsMatch(body.Fingerprint))
            {
                return Error.Validation("invalid_fingerprint", "Give a user id and a SHA-256 fingerprint.").ToHttpResult(context);
            }

            await fraud.BankAccountAsync(body.UserId, body.Fingerprint, cancellationToken);
            return Results.NoContent();
        });

        service.MapGet("/holds/{userId:guid}", async (Guid userId, FraudHandler fraud, CancellationToken cancellationToken) =>
            Results.Ok(new { held = await fraud.WithdrawalsHeldAsync(userId, cancellationToken) }));

        var admin = endpoints.MapGroup("/admin/risk/fraud");

        admin.MapGet("/cases", async (string? status, int? limit, IFraudStore store, CancellationToken cancellationToken) =>
            Results.Ok(await store.CasesAsync(status != "resolved", Math.Clamp(limit ?? 100, 1, 500), cancellationToken)))
            .RequireAuthorization(Read);

        admin.MapGet("/cases/{caseId:guid}", async (Guid caseId, IFraudStore store, FraudHandler fraud, CancellationToken cancellationToken) =>
            await store.CaseAsync(caseId, cancellationToken) is { } fraudCase
                ? Results.Ok(new
                {
                    @case = fraudCase,
                    linked = await store.LinkedAsync(fraudCase.UserId, cancellationToken),
                    audit = await store.AuditAsync(caseId, cancellationToken),
                    withdrawalsHeld = await fraud.WithdrawalsHeldAsync(fraudCase.UserId, cancellationToken),
                })
                : Results.NotFound())
            .RequireAuthorization(Read);

        admin.MapPost("/cases/{caseId:guid}/resolve", async (Guid caseId, ResolveBody body, HttpContext context, FraudHandler fraud, CancellationToken cancellationToken) =>
        {
            if (body.Resolution is not ("confirmed" or "cleared") || string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Length > 400)
            {
                return Error.Validation("invalid_resolution", "Choose confirmed or cleared and say why (up to 400 characters).").ToHttpResult(context);
            }

            var actor = context.User.FindFirstValue("sub") ?? "unknown";
            return await fraud.ResolveAsync(caseId, actor, body.Resolution, body.Reason.Trim(), cancellationToken)
                ? Results.NoContent()
                : Error.Conflict("case_not_open", "That case is not open.").ToHttpResult(context);
        }).RequireAuthorization(Write);

        return endpoints;
    }

    /// <summary>The first address the gateway saw, or the connection's.</summary>
    private static IPAddress? ClientIp(HttpContext context) =>
        context.Request.Headers["X-Forwarded-For"].ToString().Split(',')[0].Trim() is { Length: > 0 } forwarded && IPAddress.TryParse(forwarded, out var ip)
            ? ip
            : context.Connection.RemoteIpAddress;

    /// <summary>The network, not the host: /24 for IPv4 and /48 for IPv6.</summary>
    internal static string? IpPrefix(IPAddress? ip)
    {
        if (ip is null)
        {
            return null;
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        var bytes = ip.GetAddressBytes();
        var keep = ip.AddressFamily == AddressFamily.InterNetwork ? 3 : 6;
        for (var i = keep; i < bytes.Length; i++)
        {
            bytes[i] = 0;
        }

        return $"{new IPAddress(bytes)}/{keep * 8}";
    }

    private static string? Header(IHeaderDictionary headers, string name, int max) =>
        headers[name].ToString() is { Length: > 0 } value ? value[..Math.Min(value.Length, max)] : null;

    private static double? Coordinate(IHeaderDictionary headers, string name, double limit) =>
        double.TryParse(headers[name].ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && Math.Abs(value) <= limit ? value : null;

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex HashPattern();

    public sealed record DeviceBody(string? Kind, string? DeviceHash);

    public sealed record BankBody(Guid UserId, string? Fingerprint);

    public sealed record ResolveBody(string? Resolution, string? Reason);
}

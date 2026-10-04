using SwiftBets.Risk.Domain;

namespace SwiftBets.Risk.Application;

/// <summary>
/// One device sighting. The device hash is the client's SHA-256 of its own signals; the IP is cut to its network prefix
/// and the user agent is kept short, so no raw identifier is stored.
/// </summary>
public sealed record DeviceSignal(Guid UserId, string Kind, string DeviceHash, string? IpPrefix, string? Asn, string? Country, double? Latitude, double? Longitude, string? UserAgent, DateTimeOffset At)
{
    public static readonly IReadOnlySet<string> Kinds = new HashSet<string>(["sign-in", "register", "deposit", "withdrawal"], StringComparer.Ordinal);

    public GeoPoint? Location => Latitude is { } lat && Longitude is { } lon ? new GeoPoint(lat, lon, At) : null;
}

public sealed record FraudCase(
    Guid CaseId, Guid UserId, FraudRule Rule, FraudSeverity Severity, IReadOnlyList<Guid> LinkedUserIds, string Summary, string Status, DateTimeOffset RaisedAt,
    string? ResolvedBy, string? Resolution, string? ResolvedReason, DateTimeOffset? ResolvedAt);

/// <summary>Accounts tied together by one shared thing: a device or a bank account.</summary>
public sealed record LinkedGroup(string Link, string Key, IReadOnlyList<Guid> UserIds);

public sealed record CaseAudit(string Actor, string Action, string? Reason, DateTimeOffset At);

public interface IFraudStore
{
    Task<GeoPoint?> LastLocationAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Records the sighting; true when it is the customer's first.</summary>
    Task<bool> RecordDeviceAsync(DeviceSignal signal, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> AccountsOnDeviceAsync(string deviceHash, CancellationToken cancellationToken);

    /// <summary>Saves the customer's bank account fingerprint and answers who else saved the same one.</summary>
    Task<IReadOnlyList<Guid>> SaveBankAccountAsync(Guid userId, string fingerprint, DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>Kind is deposit or withdrawal; stakes come from the liability journal. the reference makes a replayed event count once.</summary>
    Task RecordMoneyAsync(Guid userId, string kind, Guid reference, long amountMinor, DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>Deposits since then, and stakes on coupons placed since then.</summary>
    Task<(long DepositedMinor, long StakedMinor)> TurnoverAsync(Guid userId, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>Opens a case unless one is already open for the same customer and rule.</summary>
    Task<bool> OpenCaseAsync(FraudCase fraudCase, CancellationToken cancellationToken);

    Task<IReadOnlyList<FraudCase>> CasesAsync(bool open, int limit, CancellationToken cancellationToken);

    Task<FraudCase?> CaseAsync(Guid caseId, CancellationToken cancellationToken);

    Task<IReadOnlyList<LinkedGroup>> LinkedAsync(Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CaseAudit>> AuditAsync(Guid caseId, CancellationToken cancellationToken);

    /// <summary>Resolves an open case and writes the audit entry in the same transaction; false if it was not open.</summary>
    Task<bool> ResolveAsync(Guid caseId, string actor, string resolution, string reason, DateTimeOffset at, CancellationToken cancellationToken);

    Task<bool> HasOpenCaseAsync(Guid userId, FraudSeverity atLeast, CancellationToken cancellationToken);
}

/// <summary>The customer's email, read with their own token at registration so it never travels on Kafka.</summary>
public interface IProfileLookup
{
    Task<string?> EmailAsync(string bearerToken, CancellationToken cancellationToken);
}

public sealed class FraudOptions
{
    public const string SectionName = "Fraud";

    /// <summary>Holds withdrawals while a high-severity case is open. The only automatic action fraud takes.</summary>
    public bool HoldWithdrawalsOnHigh { get; set; } = true;

    public int AccountsPerDevice { get; set; } = 3;

    public double MaxTravelKmh { get; set; } = 900;

    public double MinTravelKm { get; set; } = 300;

    /// <summary>How far back deposits count toward a deposit-then-withdraw cycle.</summary>
    public int CycleWindowHours { get; set; } = 48;

    public double CycleWithdrawShare { get; set; } = 0.5;

    public double CycleMinTurnover { get; set; } = 0.2;

    /// <summary>Extra disposable email domains on top of the built-in list.</summary>
    public string[] DisposableDomains { get; set; } = [];

    /// <summary>Request headers the edge fills with the visitor's network and location; missing headers are skipped.</summary>
    public string AsnHeader { get; set; } = "CF-IPASN";

    public string CountryHeader { get; set; } = "CF-IPCountry";

    public string LatitudeHeader { get; set; } = "CF-IPLatitude";

    public string LongitudeHeader { get; set; } = "CF-IPLongitude";

    public string IdentityAddress { get; set; } = "http://identity:8080";

    public FraudSettings Settings() => new(AccountsPerDevice, MaxTravelKmh, MinTravelKm, CycleWithdrawShare, CycleMinTurnover,
        new HashSet<string>(FraudSettings.Default.DisposableDomains.Concat(DisposableDomains), StringComparer.OrdinalIgnoreCase));
}

/// <summary>
/// Fraud signals (BACKLOG 33): device sightings, saved bank accounts and money movements run through the rules, and each
/// hit opens an advisory case for staff. Nothing is blocked, except withdrawals while a high-severity case is open.
/// </summary>
public sealed class FraudHandler(IFraudStore store, IProfileLookup profiles, FraudOptions options, TimeProvider time)
{
    private readonly FraudSettings _settings = options.Settings();

    public async Task<IReadOnlyList<FraudFlag>> DeviceAsync(DeviceSignal signal, string? bearerToken, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        var previous = await store.LastLocationAsync(signal.UserId, cancellationToken);
        var first = await store.RecordDeviceAsync(signal, cancellationToken);
        var flags = new List<FraudFlag?>
        {
            FraudRules.ManyAccounts(signal.UserId, await store.AccountsOnDeviceAsync(signal.DeviceHash, cancellationToken), _settings),
            FraudRules.ImpossibleTravel(previous, signal.Location, _settings),
        };
        // Registration does not sign in, so a customer's first sighting stands in for it.
        if ((first || signal.Kind == "register") && bearerToken is { Length: > 0 })
        {
            flags.Add(FraudRules.DisposableEmail(await profiles.EmailAsync(bearerToken, cancellationToken), _settings));
        }

        return await OpenAsync(signal.UserId, flags, cancellationToken);
    }

    public async Task<IReadOnlyList<FraudFlag>> BankAccountAsync(Guid userId, string fingerprint, CancellationToken cancellationToken) =>
        await OpenAsync(userId, [FraudRules.SharedBank(await store.SaveBankAccountAsync(userId, fingerprint, time.GetUtcNow(), cancellationToken))], cancellationToken);

    public Task DepositAsync(Guid userId, Guid paymentId, long amountMinor, DateTimeOffset at, CancellationToken cancellationToken) =>
        store.RecordMoneyAsync(userId, "deposit", paymentId, amountMinor, at, cancellationToken);

    public async Task<IReadOnlyList<FraudFlag>> WithdrawalAsync(Guid userId, Guid withdrawalId, long amountMinor, DateTimeOffset at, CancellationToken cancellationToken)
    {
        await store.RecordMoneyAsync(userId, "withdrawal", withdrawalId, amountMinor, at, cancellationToken);
        var (deposited, staked) = await store.TurnoverAsync(userId, at.AddHours(-options.CycleWindowHours), cancellationToken);
        return await OpenAsync(userId, [FraudRules.RapidCycle(deposited, staked, amountMinor, _settings)], cancellationToken);
    }

    public async Task<bool> WithdrawalsHeldAsync(Guid userId, CancellationToken cancellationToken) =>
        options.HoldWithdrawalsOnHigh && await store.HasOpenCaseAsync(userId, FraudSeverity.High, cancellationToken);

    public Task<bool> ResolveAsync(Guid caseId, string actor, string resolution, string reason, CancellationToken cancellationToken) =>
        store.ResolveAsync(caseId, actor, resolution, reason, time.GetUtcNow(), cancellationToken);

    private async Task<IReadOnlyList<FraudFlag>> OpenAsync(Guid userId, IEnumerable<FraudFlag?> flags, CancellationToken cancellationToken)
    {
        var raised = new List<FraudFlag>();
        foreach (var flag in flags.OfType<FraudFlag>())
        {
            var fraudCase = new FraudCase(Guid.NewGuid(), userId, flag.Rule, flag.Severity, flag.LinkedUserIds, flag.Summary, "open", time.GetUtcNow(), null, null, null, null);
            if (await store.OpenCaseAsync(fraudCase, cancellationToken))
            {
                raised.Add(flag);
            }
        }

        return raised;
    }
}

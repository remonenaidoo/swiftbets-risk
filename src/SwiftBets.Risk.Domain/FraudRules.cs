namespace SwiftBets.Risk.Domain;

public enum FraudRule
{
    ManyAccountsPerDevice = 1,
    SharedBankAccount = 2,
    RapidDepositWithdraw = 3,
    ImpossibleTravel = 4,
    DisposableEmail = 5,
}

public enum FraudSeverity
{
    Low = 1,
    Medium = 2,
    High = 3,
}

/// <summary>What a rule found: advisory for staff, never a block by itself.</summary>
public sealed record FraudFlag(FraudRule Rule, FraudSeverity Severity, IReadOnlyList<Guid> LinkedUserIds, string Summary);

/// <summary>Where a customer was, coarsely, from the edge's visitor location.</summary>
public sealed record GeoPoint(double Latitude, double Longitude, DateTimeOffset At);

public sealed record FraudSettings(
    int AccountsPerDevice, double MaxTravelKmh, double MinTravelKm, double CycleWithdrawShare, double CycleMinTurnover, IReadOnlySet<string> DisposableDomains)
{
    public static FraudSettings Default { get; } = new(3, 900, 300, 0.5, 0.2,
        new HashSet<string>(["mailinator.com", "guerrillamail.com", "10minutemail.com", "tempmail.com", "temp-mail.org", "yopmail.com", "trashmail.com", "sharklasers.com", "getnada.com", "dispostable.com"], StringComparer.OrdinalIgnoreCase));
}

/// <summary>The fraud rules as pure checks; the handler feeds them what the store remembers.</summary>
public static class FraudRules
{
    /// <summary>Several accounts used from one device.</summary>
    public static FraudFlag? ManyAccounts(Guid userId, IReadOnlyList<Guid> accountsOnDevice, FraudSettings settings)
    {
        ArgumentNullException.ThrowIfNull(accountsOnDevice);
        ArgumentNullException.ThrowIfNull(settings);
        return accountsOnDevice.Count >= settings.AccountsPerDevice
            ? new(FraudRule.ManyAccountsPerDevice, FraudSeverity.High, [.. accountsOnDevice.Where(u => u != userId)], $"{accountsOnDevice.Count} accounts used from one device")
            : null;
    }

    /// <summary>The same bank account saved by more than one customer.</summary>
    public static FraudFlag? SharedBank(IReadOnlyList<Guid> otherHolders)
    {
        ArgumentNullException.ThrowIfNull(otherHolders);
        return otherHolders.Count > 0
            ? new(FraudRule.SharedBankAccount, FraudSeverity.High, otherHolders, $"Bank account also saved by {otherHolders.Count} other customer(s)")
            : null;
    }

    /// <summary>Money in and straight out again with little betting in between.</summary>
    public static FraudFlag? RapidCycle(long depositedMinor, long stakedMinor, long withdrawnMinor, FraudSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (depositedMinor <= 0 || withdrawnMinor < depositedMinor * settings.CycleWithdrawShare || stakedMinor >= depositedMinor * settings.CycleMinTurnover)
        {
            return null;
        }

        return new(FraudRule.RapidDepositWithdraw, FraudSeverity.High, [], $"Withdrew {withdrawnMinor / 100m:0.00} soon after depositing {depositedMinor / 100m:0.00} with {stakedMinor / 100m:0.00} staked");
    }

    /// <summary>Two sign-ins further apart than anyone could travel in the time between.</summary>
    public static FraudFlag? ImpossibleTravel(GeoPoint? previous, GeoPoint? current, FraudSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (previous is null || current is null)
        {
            return null;
        }

        var km = DistanceKm(previous, current);
        var hours = Math.Max((current.At - previous.At).TotalHours, 1.0 / 60);
        return km >= settings.MinTravelKm && km / hours > settings.MaxTravelKmh
            ? new(FraudRule.ImpossibleTravel, FraudSeverity.Medium, [], $"{km:0} km in {hours * 60:0} minutes")
            : null;
    }

    public static FraudFlag? DisposableEmail(string? email, FraudSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var at = email?.LastIndexOf('@') ?? -1;
        return at > 0 && settings.DisposableDomains.Contains(email![(at + 1)..].Trim())
            ? new(FraudRule.DisposableEmail, FraudSeverity.Low, [], $"Registered with a disposable email domain ({email[(at + 1)..]})")
            : null;
    }

    public static double DistanceKm(GeoPoint a, GeoPoint b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        static double Rad(double d) => d * Math.PI / 180;
        var dLat = Rad(b.Latitude - a.Latitude);
        var dLon = Rad(b.Longitude - a.Longitude);
        var h = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(Rad(a.Latitude)) * Math.Cos(Rad(b.Latitude)) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 2 * 6371 * Math.Asin(Math.Sqrt(h));
    }
}

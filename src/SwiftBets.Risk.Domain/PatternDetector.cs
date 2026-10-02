namespace SwiftBets.Risk.Domain;

public enum PatternKind
{
    RepeatedBet = 1,
    CorrelatedStake = 2,
}

/// <summary>A placed coupon as the detectors see it.</summary>
public sealed record PlacedBet(Guid CouponId, Guid PunterId, long StakeMinor, IReadOnlyList<(string FixtureId, Outcome Outcome)> Legs, DateTimeOffset PlacedAt);

public sealed record DetectedPattern(PatternKind Kind, string FixtureId, string? SelectionId, IReadOnlyList<Guid> PunterIds, IReadOnlyList<Guid> CouponIds, long TotalStakeMinor, string Summary);

public sealed record DetectorSettings(int RepeatCount, TimeSpan RepeatWindow, int CorrelatedPunters, long CorrelatedStakeMinor, TimeSpan CorrelatedWindow)
{
    public static DetectorSettings Default { get; } = new(3, TimeSpan.FromMinutes(10), 3, 500_000, TimeSpan.FromMinutes(5));
}

/// <summary>
/// Advisory pattern detection over a sliding window of placed coupons. Each pattern is raised once per window, so a
/// burst of bets produces one alert, not one per bet. Windows live in memory: a restart starts them empty.
/// </summary>
public sealed class PatternDetector(DetectorSettings settings)
{
    private readonly List<PlacedBet> _recent = [];
    private readonly Dictionary<string, DateTimeOffset> _raised = new(StringComparer.Ordinal);

    public IReadOnlyList<DetectedPattern> Observe(PlacedBet bet)
    {
        ArgumentNullException.ThrowIfNull(bet);
        var horizon = bet.PlacedAt - (settings.RepeatWindow > settings.CorrelatedWindow ? settings.RepeatWindow : settings.CorrelatedWindow);
        _recent.RemoveAll(b => b.PlacedAt < horizon);
        _recent.Add(bet);

        var found = new List<DetectedPattern>();
        var signature = Signature(bet);
        var repeats = _recent.Where(b => b.PunterId == bet.PunterId && b.PlacedAt >= bet.PlacedAt - settings.RepeatWindow && Signature(b) == signature).ToList();
        if (repeats.Count >= settings.RepeatCount && Fresh($"repeat|{bet.PunterId}|{signature}", bet.PlacedAt, settings.RepeatWindow))
        {
            found.Add(new DetectedPattern(PatternKind.RepeatedBet, bet.Legs[0].FixtureId, bet.Legs.Count == 1 ? bet.Legs[0].Outcome.SelectionId : null, [bet.PunterId],
                [.. repeats.Select(b => b.CouponId)], repeats.Sum(b => b.StakeMinor),
                $"One customer placed the same {(bet.Legs.Count == 1 ? "selection" : $"{bet.Legs.Count} selections")} {repeats.Count} times in {settings.RepeatWindow.TotalMinutes:0} minutes."));
        }

        foreach (var (fixtureId, outcome) in bet.Legs.Distinct())
        {
            var backers = _recent.Where(b => b.PlacedAt >= bet.PlacedAt - settings.CorrelatedWindow && b.Legs.Contains((fixtureId, outcome))).ToList();
            var punters = backers.Select(b => b.PunterId).Distinct().ToList();
            var stake = backers.Sum(b => b.StakeMinor);
            if (punters.Count >= settings.CorrelatedPunters && stake >= settings.CorrelatedStakeMinor
                && Fresh($"correlated|{fixtureId}|{outcome.MarketId}|{outcome.SelectionId}", bet.PlacedAt, settings.CorrelatedWindow))
            {
                found.Add(new DetectedPattern(PatternKind.CorrelatedStake, fixtureId, outcome.SelectionId, punters, [.. backers.Select(b => b.CouponId)], stake,
                    $"{punters.Count} customers backed {outcome.SelectionId} inside {settings.CorrelatedWindow.TotalMinutes:0} minutes."));
            }
        }

        return found;
    }

    private bool Fresh(string key, DateTimeOffset now, TimeSpan window)
    {
        if (_raised.TryGetValue(key, out var at) && now - at < window)
        {
            return false;
        }

        _raised[key] = now;
        return true;
    }

    private static string Signature(PlacedBet bet) =>
        string.Join(';', bet.Legs.Select(l => $"{l.FixtureId}/{l.Outcome.MarketId}/{l.Outcome.SelectionId}").Order(StringComparer.Ordinal));
}

namespace SwiftBets.Risk.Domain;

/// <summary>One outcome of a market on a fixture.</summary>
public readonly record struct Outcome(string MarketId, string SelectionId);

/// <summary>What one open coupon puts at risk on one fixture: its whole potential payout on each outcome it backs there.</summary>
public sealed record CouponExposure(Guid CouponId, Guid PunterId, IReadOnlyList<Outcome> Outcomes, long StakeMinor, long PayoutMinor);

public sealed record OutcomeTotal(Outcome Outcome, long StakeMinor, long LiabilityMinor, int Coupons);

/// <summary>The book's state for a snapshot: the open coupons, the coupons already settled and the version reached.</summary>
public sealed record FixtureBookState(long Version, IReadOnlyList<CouponExposure> Open, IReadOnlyList<Guid> Settled);

/// <summary>
/// The open liability on one fixture. Every change raises <see cref="Version"/>; a coupon placed twice, settled twice, or
/// settled before its placement arrives changes nothing, so redelivered and reordered events are safe to apply.
/// </summary>
public sealed class FixtureBook
{
    private readonly Dictionary<Guid, CouponExposure> _open = [];
    private readonly HashSet<Guid> _settled = [];

    public long Version { get; private set; }

    public bool Place(CouponExposure coupon)
    {
        ArgumentNullException.ThrowIfNull(coupon);
        if (_settled.Contains(coupon.CouponId) || !_open.TryAdd(coupon.CouponId, coupon))
        {
            return false;
        }

        Version++;
        return true;
    }

    public bool Settle(Guid couponId)
    {
        if (!_settled.Add(couponId))
        {
            return false;
        }

        _open.Remove(couponId);
        Version++;
        return true;
    }

    public IReadOnlyList<OutcomeTotal> Totals() =>
        [.. _open.Values
            .SelectMany(c => c.Outcomes.Distinct().Select(o => (Outcome: o, Coupon: c)))
            .GroupBy(x => x.Outcome)
            .Select(g => new OutcomeTotal(g.Key, g.Sum(x => x.Coupon.StakeMinor), g.Sum(x => x.Coupon.PayoutMinor), g.Count()))
            .OrderByDescending(t => t.LiabilityMinor)
            .ThenBy(t => t.Outcome.MarketId, StringComparer.Ordinal)
            .ThenBy(t => t.Outcome.SelectionId, StringComparer.Ordinal)];

    public long WorstCaseMinor => Totals() is { Count: > 0 } totals ? totals[0].LiabilityMinor : 0;

    public FixtureBookState Snapshot() => new(Version, [.. _open.Values], [.. _settled]);

    public static FixtureBook Restore(FixtureBookState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var book = new FixtureBook { Version = state.Version };
        foreach (var coupon in state.Open)
        {
            book._open[coupon.CouponId] = coupon;
        }

        book._settled.UnionWith(state.Settled);
        return book;
    }
}

using SwiftBets.Risk.Domain;

namespace SwiftBets.Risk.Application;

/// <summary>One journalled change to a fixture's book: a placement or a settlement, at the version it produced.</summary>
public sealed record JournalEntry(long Version, CouponExposure? Placed, Guid? SettledCouponId);

/// <summary>The fixture's book is event-sourced: a snapshot every so often, and the journal entries after it.</summary>
public interface IRiskJournal
{
    Task AppendAsync(string fixtureId, JournalEntry entry, CancellationToken cancellationToken);

    Task<(FixtureBookState? Snapshot, IReadOnlyList<JournalEntry> After)> LoadAsync(string fixtureId, CancellationToken cancellationToken);

    Task SaveSnapshotAsync(string fixtureId, FixtureBookState state, CancellationToken cancellationToken);
}

/// <summary>Which fixtures an open coupon touches; settlement events carry only the coupon id.</summary>
public interface ICouponIndex
{
    Task AddAsync(Guid couponId, IReadOnlyList<string> fixtureIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> FixturesAsync(Guid couponId, CancellationToken cancellationToken);
}

public sealed record FixtureView(string FixtureId, long Version, long WorstCaseMinor, long CapMinor, bool CapOverridden, bool Suspended, IReadOnlyList<OutcomeTotal> Outcomes, DateTimeOffset UpdatedAt);

public sealed record AlertView(Guid AlertId, PatternKind Kind, string FixtureId, string? SelectionId, IReadOnlyList<Guid> PunterIds, IReadOnlyList<Guid> CouponIds, long TotalStakeMinor, string Summary, DateTimeOffset RaisedAt);

/// <summary>What the console reads: every fixture's latest liability, the cap overrides and the alerts raised.</summary>
public interface IRiskStore
{
    Task SaveViewAsync(FixtureView view, CancellationToken cancellationToken);

    Task<IReadOnlyList<FixtureView>> ViewsAsync(int limit, CancellationToken cancellationToken);

    Task<long?> CapAsync(string fixtureId, CancellationToken cancellationToken);

    Task SetCapAsync(string fixtureId, long? capMinor, string reason, string operatorId, CancellationToken cancellationToken);

    Task AddAlertAsync(AlertView alert, CancellationToken cancellationToken);

    Task<IReadOnlyList<AlertView>> AlertsAsync(int limit, CancellationToken cancellationToken);
}

/// <summary>Publishes liability, alerts and the exposure rules placement enforces.</summary>
public interface IRiskPublisher
{
    Task LiabilityAsync(FixtureView view, CancellationToken cancellationToken);

    Task ExposureAsync(FixtureView view, string reason, CancellationToken cancellationToken);

    Task AlertAsync(AlertView alert, CancellationToken cancellationToken);
}

public sealed class RiskOptions
{
    public const string SectionName = "Risk";

    /// <summary>Worst-case liability on one fixture, in minor units, at which it is suspended for new coupons.</summary>
    public long DefaultFixtureCapMinor { get; set; } = 100_000_000;

    /// <summary>Journal entries between snapshots.</summary>
    public int SnapshotEvery { get; set; } = 50;

    public int RepeatCount { get; set; } = 3;

    public int RepeatWindowMinutes { get; set; } = 10;

    public int CorrelatedPunters { get; set; } = 3;

    public long CorrelatedStakeMinor { get; set; } = 500_000;

    public int CorrelatedWindowMinutes { get; set; } = 5;

    public DetectorSettings Detector() =>
        new(RepeatCount, TimeSpan.FromMinutes(RepeatWindowMinutes), CorrelatedPunters, CorrelatedStakeMinor, TimeSpan.FromMinutes(CorrelatedWindowMinutes));
}

using System.Text.Json;
using Dapper;
using Npgsql;
using SwiftBets.BuildingBlocks.Persistence;
using SwiftBets.Risk.Application;
using SwiftBets.Risk.Domain;

namespace SwiftBets.Risk.Infrastructure.Persistence;

/// <summary>The journal, snapshots, coupon index and console views in Postgres <c>sb_risk</c>, with Dapper.</summary>
public sealed class PostgresRisk(NpgsqlDataSource dataSource) : IRiskJournal, ICouponIndex, IRiskStore
{
    private static readonly SqlResources Sql = SqlResources.For<PostgresRisk>();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task AppendAsync(string fixtureId, JournalEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(Sql.Get("Risk.AppendJournal"), new
        {
            FixtureId = fixtureId,
            entry.Version,
            CouponId = entry.Placed?.CouponId ?? entry.SettledCouponId,
            Kind = entry.Placed is null ? "settled" : "placed",
            Payload = JsonSerializer.Serialize(entry, Json),
        }, cancellationToken: cancellationToken));
    }

    public async Task<(FixtureBookState? Snapshot, IReadOnlyList<JournalEntry> After)> LoadAsync(string fixtureId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var state = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(Sql.Get("Risk.LoadSnapshot"), new { FixtureId = fixtureId }, cancellationToken: cancellationToken));
        var snapshot = state is null ? null : JsonSerializer.Deserialize<FixtureBookState>(state, Json);
        var rows = await connection.QueryAsync<string>(new CommandDefinition(Sql.Get("Risk.LoadJournalAfter"), new { FixtureId = fixtureId, After = snapshot?.Version ?? 0 }, cancellationToken: cancellationToken));
        return (snapshot, [.. rows.Select(r => JsonSerializer.Deserialize<JournalEntry>(r, Json)!)]);
    }

    public async Task SaveSnapshotAsync(string fixtureId, FixtureBookState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(Sql.Get("Risk.SaveSnapshot"), new { FixtureId = fixtureId, state.Version, State = JsonSerializer.Serialize(state, Json) }, cancellationToken: cancellationToken));
    }

    public async Task AddAsync(Guid couponId, IReadOnlyList<string> fixtureIds, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(Sql.Get("Risk.IndexCoupon"), new { CouponId = couponId, FixtureIds = fixtureIds.ToArray() }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<string>> FixturesAsync(Guid couponId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return [.. await connection.QueryAsync<string>(new CommandDefinition(Sql.Get("Risk.CouponFixtures"), new { CouponId = couponId }, cancellationToken: cancellationToken))];
    }

    public async Task SaveViewAsync(FixtureView view, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(view);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(Sql.Get("Risk.SaveView"), new
        {
            view.FixtureId,
            view.Version,
            view.WorstCaseMinor,
            view.CapMinor,
            view.CapOverridden,
            view.Suspended,
            Outcomes = JsonSerializer.Serialize(view.Outcomes, Json),
            view.UpdatedAt,
        }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<FixtureView>> ViewsAsync(int limit, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<ViewRow>(new CommandDefinition(Sql.Get("Risk.Views"), new { Limit = limit }, cancellationToken: cancellationToken));
        return [.. rows.Select(r => new FixtureView(r.FixtureId, r.Version, r.WorstCaseMinor, r.CapMinor, r.CapOverridden, r.Suspended,
            JsonSerializer.Deserialize<List<OutcomeTotal>>(r.Outcomes, Json) ?? [], Utc(r.UpdatedAt)))];
    }

    public async Task<long?> CapAsync(string fixtureId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<long?>(new CommandDefinition(Sql.Get("Risk.Cap"), new { FixtureId = fixtureId }, cancellationToken: cancellationToken));
    }

    public async Task SetCapAsync(string fixtureId, long? capMinor, string reason, string operatorId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(Sql.Get(capMinor is null ? "Risk.ClearCap" : "Risk.SetCap"),
            new { FixtureId = fixtureId, CapMinor = capMinor, Reason = reason, OperatorId = operatorId }, cancellationToken: cancellationToken));
    }

    public async Task AddAlertAsync(AlertView alert, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(alert);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(Sql.Get("Risk.AddAlert"), new
        {
            alert.AlertId,
            Kind = alert.Kind.ToString(),
            alert.FixtureId,
            alert.SelectionId,
            PunterIds = alert.PunterIds.ToArray(),
            CouponIds = alert.CouponIds.ToArray(),
            alert.TotalStakeMinor,
            alert.Summary,
            alert.RaisedAt,
        }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<AlertView>> AlertsAsync(int limit, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<AlertRow>(new CommandDefinition(Sql.Get("Risk.Alerts"), new { Limit = limit }, cancellationToken: cancellationToken));
        return [.. rows.Select(r => new AlertView(r.AlertId, Enum.Parse<PatternKind>(r.Kind), r.FixtureId, r.SelectionId, r.PunterIds, r.CouponIds, r.TotalStakeMinor, r.Summary, Utc(r.RaisedAt)))];
    }

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private sealed record ViewRow(string FixtureId, long Version, long WorstCaseMinor, long CapMinor, bool CapOverridden, bool Suspended, string Outcomes, DateTime UpdatedAt);

    private sealed record AlertRow(Guid AlertId, string Kind, string FixtureId, string? SelectionId, Guid[] PunterIds, Guid[] CouponIds, long TotalStakeMinor, string Summary, DateTime RaisedAt);
}

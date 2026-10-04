using Dapper;
using Npgsql;
using SwiftBets.BuildingBlocks.Persistence;
using SwiftBets.Risk.Application;
using SwiftBets.Risk.Domain;

namespace SwiftBets.Risk.Infrastructure.Persistence;

/// <summary>Fraud signals and cases in Postgres <c>sb_risk</c>, with Dapper.</summary>
public sealed class PostgresFraud(NpgsqlDataSource dataSource) : IFraudStore
{
    private static readonly SqlResources Sql = SqlResources.For<PostgresRisk>();

    public async Task<GeoPoint?> LastLocationAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<LocationRow>(Command("Fraud.LastLocation", new { UserId = userId }, cancellationToken));
        return row is null ? null : new GeoPoint(row.Latitude, row.Longitude, Utc(row.SeenAt));
    }

    public async Task RecordDeviceAsync(DeviceSignal signal, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(Command("Fraud.RecordDevice", signal, cancellationToken));
    }

    public async Task<IReadOnlyList<Guid>> AccountsOnDeviceAsync(string deviceHash, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return [.. await connection.QueryAsync<Guid>(Command("Fraud.AccountsOnDevice", new { DeviceHash = deviceHash }, cancellationToken))];
    }

    public async Task<IReadOnlyList<Guid>> SaveBankAccountAsync(Guid userId, string fingerprint, DateTimeOffset at, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return [.. await connection.QueryAsync<Guid>(Command("Fraud.SaveBankAccount", new { UserId = userId, Fingerprint = fingerprint, At = at }, cancellationToken))];
    }

    public async Task RecordMoneyAsync(Guid userId, string kind, Guid reference, long amountMinor, DateTimeOffset at, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(Command("Fraud.RecordMoney", new { Reference = reference, Kind = kind, UserId = userId, Amount = amountMinor, At = at }, cancellationToken));
    }

    public async Task<(long DepositedMinor, long StakedMinor)> TurnoverAsync(Guid userId, DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleAsync<TurnoverRow>(Command("Fraud.Turnover", new { UserId = userId, Since = since }, cancellationToken));
        return (row.Deposited, row.Staked);
    }

    public async Task<bool> OpenCaseAsync(FraudCase fraudCase, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fraudCase);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteAsync(Command("Fraud.OpenCase", new
        {
            fraudCase.CaseId,
            fraudCase.UserId,
            Rule = (short)fraudCase.Rule,
            Severity = (short)fraudCase.Severity,
            LinkedUserIds = fraudCase.LinkedUserIds.ToArray(),
            fraudCase.Summary,
            fraudCase.RaisedAt,
        }, cancellationToken)) > 0;
    }

    public async Task<IReadOnlyList<FraudCase>> CasesAsync(bool open, int limit, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return [.. (await connection.QueryAsync<CaseRow>(Command("Fraud.Cases", new { Status = open ? "open" : "resolved", Limit = limit }, cancellationToken))).Select(r => r.ToCase())];
    }

    public async Task<FraudCase?> CaseAsync(Guid caseId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return (await connection.QuerySingleOrDefaultAsync<CaseRow>(Command("Fraud.Case", new { CaseId = caseId }, cancellationToken)))?.ToCase();
    }

    public async Task<IReadOnlyList<LinkedGroup>> LinkedAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<LinkedRow>(Command("Fraud.Linked", new { UserId = userId }, cancellationToken));
        return [.. rows.Select(r => new LinkedGroup(r.Link, r.Key.Trim()[..12], r.UserIds))];
    }

    public async Task<IReadOnlyList<CaseAudit>> AuditAsync(Guid caseId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<AuditRow>(Command("Fraud.Audit", new { CaseId = caseId }, cancellationToken));
        return [.. rows.Select(r => new CaseAudit(r.Actor, r.Action, r.Reason, Utc(r.At)))];
    }

    public async Task<bool> ResolveAsync(Guid caseId, string actor, string resolution, string reason, DateTimeOffset at, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteAsync(Command("Fraud.Resolve", new { CaseId = caseId, Actor = actor, Resolution = resolution, Reason = reason, At = at }, cancellationToken)) > 0;
    }

    public async Task<bool> HasOpenCaseAsync(Guid userId, FraudSeverity atLeast, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(Command("Fraud.HasOpenCase", new { UserId = userId, Severity = (short)atLeast }, cancellationToken));
    }

    private static CommandDefinition Command(string name, object parameters, CancellationToken cancellationToken) => new(Sql.Get(name), parameters, cancellationToken: cancellationToken);

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private sealed record LocationRow(double Latitude, double Longitude, DateTime SeenAt);

    private sealed record TurnoverRow(long Deposited, long Staked);

    private sealed class LinkedRow
    {
        public string Link { get; init; } = string.Empty;

        public string Key { get; init; } = string.Empty;

        public Guid[] UserIds { get; init; } = [];
    }

    private sealed record AuditRow(string Actor, string Action, string? Reason, DateTime At);

    private sealed class CaseRow
    {
        public Guid CaseId { get; init; }

        public Guid UserId { get; init; }

        public short Rule { get; init; }

        public short Severity { get; init; }

        public Guid[] LinkedUserIds { get; init; } = [];

        public string Summary { get; init; } = string.Empty;

        public string Status { get; init; } = string.Empty;

        public DateTime RaisedAt { get; init; }

        public string? ResolvedBy { get; init; }

        public string? Resolution { get; init; }

        public string? ResolvedReason { get; init; }

        public DateTime? ResolvedAt { get; init; }

        public FraudCase ToCase() => new(CaseId, UserId, (FraudRule)Rule, (FraudSeverity)Severity, LinkedUserIds, Summary, Status, Utc(RaisedAt),
            ResolvedBy, Resolution, ResolvedReason, ResolvedAt is { } at ? Utc(at) : null);
    }
}

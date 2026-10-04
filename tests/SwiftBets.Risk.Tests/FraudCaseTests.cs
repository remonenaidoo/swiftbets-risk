extern alias migrator;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using SwiftBets.BuildingBlocks.Testing;
using SwiftBets.Risk.Application;
using SwiftBets.Risk.Domain;
using SwiftBets.Risk.Infrastructure.Persistence;

namespace SwiftBets.Risk.Tests;

public sealed class FraudCaseTests(PostgresFixture postgres)
{
    private static readonly string Device = new('a', 64);
    private string? _connectionString;
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow); // the journal stamps stakes with the database clock

    [Fact]
    public async Task A_shared_device_opens_one_high_case_that_holds_withdrawals_until_resolved_with_an_audit()
    {
        var (store, fraud) = await RigAsync();
        var ct = TestContext.Current.CancellationToken;
        Guid[] users = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        foreach (var user in users)
        {
            await fraud.DeviceAsync(Signal(user), null, ct);
        }

        await fraud.DeviceAsync(Signal(users[2]), null, ct);
        var fraudCase = (await store.CasesAsync(true, 10, ct)).ShouldHaveSingleItem();
        (fraudCase.UserId, fraudCase.Severity).ShouldBe((users[2], FraudSeverity.High));
        (await store.LinkedAsync(users[2], ct)).ShouldHaveSingleItem().UserIds.Count.ShouldBe(3);
        (await fraud.WithdrawalsHeldAsync(users[2], ct)).ShouldBeTrue();

        (await fraud.ResolveAsync(fraudCase.CaseId, "ops-1", "cleared", "Family sharing a tablet", ct)).ShouldBeTrue();

        (await fraud.WithdrawalsHeldAsync(users[2], ct)).ShouldBeFalse();
        (await store.AuditAsync(fraudCase.CaseId, ct)).Select(a => a.Action).ShouldBe(["raised", "resolved:cleared"]);
    }

    [Fact]
    public async Task A_deposit_withdrawn_after_betting_opens_no_case_and_a_resolved_case_cannot_be_resolved_again()
    {
        var (store, fraud) = await RigAsync();
        var ct = TestContext.Current.CancellationToken;
        var user = Guid.NewGuid();
        await fraud.DepositAsync(user, Guid.NewGuid(), 100_000, _time.GetUtcNow(), ct);
        await AppendStakeAsync(user, 50_000);

        (await fraud.WithdrawalAsync(user, Guid.NewGuid(), 90_000, _time.GetUtcNow(), ct)).ShouldBeEmpty();
        (await fraud.ResolveAsync(Guid.NewGuid(), "ops-1", "cleared", "nothing", ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_first_sighting_checks_the_email_once_and_later_sightings_do_not()
    {
        var (store, fraud) = await RigAsync();
        var ct = TestContext.Current.CancellationToken;
        var user = Guid.NewGuid();

        (await fraud.DeviceAsync(Signal(user), "token", ct)).ShouldHaveSingleItem().Rule.ShouldBe(FraudRule.DisposableEmail);
        await fraud.ResolveAsync((await store.CasesAsync(true, 10, ct))[0].CaseId, "ops-1", "cleared", "Known customer", ct);
        (await fraud.DeviceAsync(Signal(user), "token", ct)).ShouldBeEmpty();
    }

    private DeviceSignal Signal(Guid user) => new(user, "sign-in", Device, "10.0.0.0/24", null, "ZA", null, null, "test", _time.GetUtcNow());

    private async Task AppendStakeAsync(Guid punter, long stake)
    {
        var journal = new PostgresRisk(NpgsqlDataSource.Create(_connectionString!));
        var coupon = new CouponExposure(Guid.NewGuid(), punter, [new Outcome("m", "s")], stake, stake * 2);
        await journal.AppendAsync("f-1", new JournalEntry(1, coupon, null), TestContext.Current.CancellationToken);
    }

    private async Task<(PostgresFraud Store, FraudHandler Fraud)> RigAsync()
    {
        var database = "fraud_" + Guid.NewGuid().ToString("N")[..10];
        await using (var admin = new NpgsqlConnection(postgres.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin);
            await create.ExecuteNonQueryAsync();
        }

        _connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = database }.ConnectionString;
        var entry = typeof(migrator::Program).Assembly.EntryPoint!.Invoke(null, [new[] { $"--ConnectionStrings:SbRisk={_connectionString}" }]);
        (entry is Task<int> task ? await task : (int)entry!).ShouldBe(0);
        var store = new PostgresFraud(NpgsqlDataSource.Create(_connectionString));
        return (store, new FraudHandler(store, new NoProfiles(), new FraudOptions(), _time));
    }

    private sealed class NoProfiles : IProfileLookup
    {
        public Task<string?> EmailAsync(string bearerToken, CancellationToken cancellationToken) => Task.FromResult<string?>("new@yopmail.com");
    }
}

extern alias migrator;
using Akka.Actor;
using Akka.TestKit.Xunit;
using Npgsql;
using SwiftBets.BuildingBlocks.Testing;
using SwiftBets.Risk.Application;
using SwiftBets.Risk.Domain;
using SwiftBets.Risk.Infrastructure.Actors;
using SwiftBets.Risk.Infrastructure.Persistence;

[assembly: AssemblyFixture(typeof(PostgresFixture))]

namespace SwiftBets.Risk.Tests;

public sealed class FixtureActorTests(PostgresFixture postgres) : TestKit
{
    private static readonly RiskOptions Options = new() { SnapshotEvery = 2, DefaultFixtureCapMinor = 10_000 };
    private readonly NullPublisher _publisher = new();

    [Fact]
    public async Task A_restarted_fixture_recovers_its_book_from_the_snapshot_and_the_journal_after_it()
    {
        var store = await StoreAsync();
        var first = Sys.ActorOf(Props(store));
        for (var i = 0; i < 3; i++)
        {
            await first.Ask<Applied>(new PlaceOnFixture("f-1", Coupon(2_000)), TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }

        await first.GracefulStop(TimeSpan.FromSeconds(5));
        var second = Sys.ActorOf(Props(store));
        var view = await second.Ask<FixtureView>(new GetFixture("f-1"), TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        (view.Version, view.WorstCaseMinor, view.Outcomes[0].Coupons).ShouldBe((3L, 6_000L, 3));
    }

    [Fact]
    public async Task A_redelivered_placement_changes_nothing_and_crossing_the_cap_suspends_the_fixture()
    {
        var store = await StoreAsync();
        var actor = Sys.ActorOf(Props(store));
        var coupon = Coupon(12_000);

        (await actor.Ask<Applied>(new PlaceOnFixture("f-1", coupon), TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).Changed.ShouldBeTrue();
        (await actor.Ask<Applied>(new PlaceOnFixture("f-1", coupon), TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).Changed.ShouldBeFalse();

        var view = await actor.Ask<FixtureView>(new GetFixture("f-1"), TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        (view.WorstCaseMinor, view.Suspended).ShouldBe((12_000L, true));
        _publisher.Exposures.ShouldHaveSingleItem().Suspended.ShouldBeTrue();
    }

    [Fact]
    public async Task An_alert_is_stored_and_read_back_for_the_console()
    {
        var store = await StoreAsync();
        var alert = new AlertView(Guid.NewGuid(), PatternKind.RepeatedBet, "f-1", "home", [Guid.NewGuid()], [Guid.NewGuid(), Guid.NewGuid()], 3_000, "Three in a row.",
            new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero));

        await store.AddAlertAsync(alert, TestContext.Current.CancellationToken);

        var read = (await store.AlertsAsync(10, TestContext.Current.CancellationToken)).ShouldHaveSingleItem();
        (read.Kind, read.CouponIds.Count, read.RaisedAt).ShouldBe((PatternKind.RepeatedBet, 2, alert.RaisedAt));
    }

    private Props Props(PostgresRisk store) => FixtureActor.Props("f-1", store, store, _publisher, Options, TimeProvider.System);

    private static CouponExposure Coupon(long payout) => new(Guid.NewGuid(), Guid.NewGuid(), [new Outcome("m-1", "home")], 1_000, payout);

    private async Task<PostgresRisk> StoreAsync()
    {
        var database = "risk_" + Guid.NewGuid().ToString("N")[..10];
        await using (var admin = new NpgsqlConnection(postgres.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin);
            await create.ExecuteNonQueryAsync();
        }

        var connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = database }.ConnectionString;
        var entry = typeof(migrator::Program).Assembly.EntryPoint!.Invoke(null, [new[] { $"--ConnectionStrings:SbRisk={connectionString}" }]);
        (entry is Task<int> task ? await task : (int)entry!).ShouldBe(0);
        return new PostgresRisk(NpgsqlDataSource.Create(connectionString));
    }

    private sealed class NullPublisher : IRiskPublisher
    {
        public List<FixtureView> Exposures { get; } = [];

        public Task LiabilityAsync(FixtureView view, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ExposureAsync(FixtureView view, string reason, CancellationToken cancellationToken)
        {
            Exposures.Add(view);
            return Task.CompletedTask;
        }

        public Task AlertAsync(AlertView alert, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

using Akka.Actor;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SwiftBets.Risk.Application;
using SwiftBets.Risk.Domain;
using SwiftBets.Risk.Infrastructure.Actors;

namespace SwiftBets.Risk.Infrastructure;

/// <summary>The actor system: one fixture region and one detector, stopped cleanly with the host.</summary>
public sealed class RiskActors : IHostedService, IDisposable
{
    private static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(15);
    private readonly ActorSystem _system;
    private readonly IActorRef _region;
    private readonly IActorRef _detector;

    public RiskActors(IRiskJournal journal, IRiskStore store, IRiskPublisher publisher, IOptions<RiskOptions> options, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(options);
        _system = ActorSystem.Create("swiftbets-risk");
        _region = _system.ActorOf(FixtureRegion.Props(new FixtureMessageExtractor(), journal, store, publisher, options.Value, time), "fixtures");
        _detector = _system.ActorOf(DetectorActor.Props(new PatternDetector(options.Value.Detector()), store, publisher, time), "detector");
    }

    public async Task<bool> PlaceAsync(string fixtureId, CouponExposure coupon, CancellationToken cancellationToken) =>
        (await _region.Ask<Applied>(new PlaceOnFixture(fixtureId, coupon), AskTimeout, cancellationToken)).Changed;

    public async Task<bool> SettleAsync(string fixtureId, Guid couponId, CancellationToken cancellationToken) =>
        (await _region.Ask<Applied>(new SettleOnFixture(fixtureId, couponId), AskTimeout, cancellationToken)).Changed;

    public Task<FixtureView> CapChangedAsync(string fixtureId, CancellationToken cancellationToken) =>
        _region.Ask<FixtureView>(new CapChanged(fixtureId), AskTimeout, cancellationToken);

    public Task<FixtureView> FixtureAsync(string fixtureId, CancellationToken cancellationToken) =>
        _region.Ask<FixtureView>(new GetFixture(fixtureId), AskTimeout, cancellationToken);

    public void Detect(PlacedBet bet) => _detector.Tell(bet);

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => CoordinatedShutdown.Get(_system).Run(CoordinatedShutdown.ClrExitReason.Instance);

    public void Dispose() => _system.Dispose();
}

using Akka.Actor;
using Akka.Event;
using SwiftBets.Risk.Application;
using SwiftBets.Risk.Domain;

namespace SwiftBets.Risk.Infrastructure.Actors;

/// <summary>Runs every placed coupon through the pattern detector; each pattern found is stored and published as an alert.</summary>
public sealed class DetectorActor : ReceiveActor
{
    public DetectorActor(PatternDetector detector, IRiskStore store, IRiskPublisher publisher, TimeProvider time)
    {
        var log = Context.GetLogger();
        ReceiveAsync<PlacedBet>(async bet =>
        {
            foreach (var pattern in detector.Observe(bet))
            {
                var alert = new AlertView(Guid.CreateVersion7(), pattern.Kind, pattern.FixtureId, pattern.SelectionId, pattern.PunterIds, pattern.CouponIds,
                    pattern.TotalStakeMinor, pattern.Summary, time.GetUtcNow());
                try
                {
                    await store.AddAlertAsync(alert, CancellationToken.None);
                    await publisher.AlertAsync(alert, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    log.Warning(ex, "Risk alert {0} could not be stored or published", alert.AlertId);
                }
            }
        });
    }

    public static Props Props(PatternDetector detector, IRiskStore store, IRiskPublisher publisher, TimeProvider time) =>
        Akka.Actor.Props.Create(() => new DetectorActor(detector, store, publisher, time));
}

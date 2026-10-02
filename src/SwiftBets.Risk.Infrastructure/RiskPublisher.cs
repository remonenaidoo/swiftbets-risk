using SwiftBets.BuildingBlocks.Core;
using SwiftBets.BuildingBlocks.Messaging;
using SwiftBets.Contracts.Messaging;
using SwiftBets.Contracts.Money;
using SwiftBets.Contracts.Risk;
using SwiftBets.Risk.Application;
using SwiftBets.Risk.Domain;

namespace SwiftBets.Risk.Infrastructure;

/// <summary>Publishes straight to Kafka: every event carries the fixture's whole current state, so a lost one is healed by the next.</summary>
public sealed class RiskPublisher(IEventPublisher publisher, TimeProvider time) : IRiskPublisher
{
    private const string Currency = "ZAR";

    public Task LiabilityAsync(FixtureView view, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(view);
        var payload = new LiabilityChangedV1(view.FixtureId, view.Version,
            [.. view.Outcomes.Select(o => new OutcomeLiability(o.Outcome.MarketId, o.Outcome.SelectionId, new Money(o.StakeMinor, Currency), new Money(o.LiabilityMinor, Currency), o.Coupons))],
            new Money(view.WorstCaseMinor, Currency), view.UpdatedAt);
        return publisher.PublishAsync(Topics.LiabilityChanged, view.FixtureId, Envelope(payload), cancellationToken);
    }

    public Task ExposureAsync(FixtureView view, string reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(view);
        var payload = new ExposureLimitV1(view.FixtureId, view.CapOverridden ? view.CapMinor : null, view.Suspended, reason, view.UpdatedAt);
        return publisher.PublishAsync(Topics.ExposureLimits, view.FixtureId, Envelope(payload), cancellationToken);
    }

    public Task AlertAsync(AlertView alert, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(alert);
        var payload = new RiskAlertV1(alert.AlertId, alert.Kind == PatternKind.RepeatedBet ? RiskAlertKind.RepeatedBet : RiskAlertKind.CorrelatedStake,
            alert.FixtureId, alert.SelectionId, alert.PunterIds, alert.CouponIds, new Money(alert.TotalStakeMinor, Currency), alert.Summary, alert.RaisedAt);
        return publisher.PublishAsync(Topics.RiskAlert, alert.FixtureId, Envelope(payload), cancellationToken);
    }

    private EventEnvelope<T> Envelope<T>(T payload)
        where T : IEventContract =>
        EventEnvelope<T>.Create(payload, time.GetUtcNow(), CorrelationContext.CorrelationId ?? CorrelationContext.NewId());
}

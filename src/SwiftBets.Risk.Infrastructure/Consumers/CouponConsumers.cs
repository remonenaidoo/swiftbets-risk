using SwiftBets.BuildingBlocks.Messaging;
using SwiftBets.Contracts.Placement;
using SwiftBets.Contracts.Settlement;
using SwiftBets.Risk.Application;
using SwiftBets.Risk.Domain;

namespace SwiftBets.Risk.Infrastructure.Consumers;

/// <summary>
/// A placed coupon adds its potential payout to every fixture it touches, on each outcome it backs there; the offset is
/// committed only after every fixture has journalled it.
/// </summary>
public sealed class CouponPlacedConsumer(RiskActors actors, ICouponIndex index) : IEventHandler<CouponPlacedV2>
{
    public async Task HandleAsync(ConsumedEvent<CouponPlacedV2> message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var coupon = message.Envelope.Payload;
        var fixtures = coupon.Legs.Select(l => l.FixtureId).Distinct(StringComparer.Ordinal).ToList();
        await index.AddAsync(coupon.CouponId, fixtures, cancellationToken);
        foreach (var fixtureId in fixtures)
        {
            var outcomes = coupon.Legs.Where(l => l.FixtureId == fixtureId).Select(l => new Outcome(l.MarketId, l.SelectionId)).ToList();
            await actors.PlaceAsync(fixtureId, new CouponExposure(coupon.CouponId, coupon.PunterId, outcomes, coupon.TotalStake.MinorUnits, coupon.PotentialPayout.MinorUnits), cancellationToken);
        }

        actors.Detect(new PlacedBet(coupon.CouponId, coupon.PunterId, coupon.TotalStake.MinorUnits,
            [.. coupon.Legs.Select(l => (l.FixtureId, new Outcome(l.MarketId, l.SelectionId)))], coupon.PlacedAt));
    }
}

/// <summary>A settled, voided or cashed-out coupon owes nothing more, so it leaves every fixture's liability.</summary>
public sealed class CouponSettledConsumer(RiskActors actors, ICouponIndex index) : IEventHandler<CouponSettledV2>
{
    public async Task HandleAsync(ConsumedEvent<CouponSettledV2> message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var couponId = message.Envelope.Payload.CouponId;
        foreach (var fixtureId in await index.FixturesAsync(couponId, cancellationToken))
        {
            await actors.SettleAsync(fixtureId, couponId, cancellationToken);
        }
    }
}

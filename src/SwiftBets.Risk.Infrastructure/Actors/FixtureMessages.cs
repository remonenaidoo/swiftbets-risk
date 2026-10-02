using Akka.Cluster.Sharding;
using SwiftBets.Risk.Domain;

namespace SwiftBets.Risk.Infrastructure.Actors;

/// <summary>Every message to a fixture's actor names the fixture; that is the entity id the shard region routes on.</summary>
public interface IFixtureMessage
{
    string FixtureId { get; }
}

public sealed record PlaceOnFixture(string FixtureId, CouponExposure Coupon) : IFixtureMessage;

public sealed record SettleOnFixture(string FixtureId, Guid CouponId) : IFixtureMessage;

public sealed record CapChanged(string FixtureId) : IFixtureMessage;

public sealed record GetFixture(string FixtureId) : IFixtureMessage;

/// <summary>Sent back once the change is journalled, so a consumer commits its offset only after it is durable.</summary>
public sealed record Applied(bool Changed);

/// <summary>
/// The extractor Akka cluster sharding would use, so moving to a cluster swaps the local region for a ShardRegion with
/// no change to the actors or their messages.
/// </summary>
public sealed class FixtureMessageExtractor() : HashCodeMessageExtractor(100)
{
    public override string EntityId(object message) => (message as IFixtureMessage)?.FixtureId ?? throw new ArgumentException($"Not a fixture message: {message?.GetType().Name}", nameof(message));
}

using Akka.Actor;
using SwiftBets.Risk.Application;

namespace SwiftBets.Risk.Infrastructure.Actors;

/// <summary>
/// Routes each fixture message to that fixture's actor, creating it on first use: a single-node stand-in for a cluster
/// ShardRegion that uses the same <see cref="FixtureMessageExtractor"/>.
/// </summary>
public sealed class FixtureRegion : ReceiveActor
{
    public FixtureRegion(FixtureMessageExtractor extractor, Func<string, Props> fixtureProps)
    {
        ReceiveAny(message =>
        {
            var id = extractor.EntityId(message);
            var name = Uri.EscapeDataString(id);
            var child = Context.Child(name);
            if (child.IsNobody())
            {
                child = Context.ActorOf(fixtureProps(id), name);
            }

            child.Forward(extractor.EntityMessage(message));
        });
    }

    public static Props Props(FixtureMessageExtractor extractor, IRiskJournal journal, IRiskStore store, IRiskPublisher publisher, RiskOptions options, TimeProvider time) =>
        Akka.Actor.Props.Create(() => new FixtureRegion(extractor, id => FixtureActor.Props(id, journal, store, publisher, options, time)));
}

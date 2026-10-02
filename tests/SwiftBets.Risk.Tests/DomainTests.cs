using SwiftBets.Risk.Domain;

namespace SwiftBets.Risk.Tests;

public sealed class DomainTests
{
    private static readonly Outcome Home = new("m-1", "home");
    private static readonly Outcome Draw = new("m-1", "draw");

    [Fact]
    public void Liability_on_an_outcome_is_the_payout_of_every_open_coupon_backing_it()
    {
        var book = new FixtureBook();
        book.Place(new CouponExposure(Guid.NewGuid(), Guid.NewGuid(), [Home], 1_000, 2_500));
        book.Place(new CouponExposure(Guid.NewGuid(), Guid.NewGuid(), [Home], 2_000, 4_000));
        book.Place(new CouponExposure(Guid.NewGuid(), Guid.NewGuid(), [Draw], 1_000, 3_200));

        book.Totals()[0].ShouldBe(new OutcomeTotal(Home, 3_000, 6_500, 2));
        book.WorstCaseMinor.ShouldBe(6_500);
    }

    [Fact]
    public void A_settlement_that_arrives_before_its_placement_keeps_the_coupon_out_of_the_book()
    {
        var book = new FixtureBook();
        var coupon = new CouponExposure(Guid.NewGuid(), Guid.NewGuid(), [Home], 1_000, 2_500);

        book.Settle(coupon.CouponId).ShouldBeTrue();

        book.Place(coupon).ShouldBeFalse();
        book.WorstCaseMinor.ShouldBe(0);
    }

    [Fact]
    public void The_third_identical_bet_inside_the_window_raises_one_repeated_bet_alert()
    {
        var detector = new PatternDetector(DetectorSettings.Default);
        var punter = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero);
        PlacedBet Bet(int minute) => new(Guid.NewGuid(), punter, 1_000, [("f-1", Home)], at.AddMinutes(minute));

        detector.Observe(Bet(0)).ShouldBeEmpty();
        detector.Observe(Bet(1)).ShouldBeEmpty();
        detector.Observe(Bet(2)).ShouldHaveSingleItem().Kind.ShouldBe(PatternKind.RepeatedBet);
        detector.Observe(Bet(3)).ShouldBeEmpty();
    }

    [Fact]
    public void Small_stakes_from_several_customers_are_not_a_correlated_stake()
    {
        var detector = new PatternDetector(DetectorSettings.Default);
        var at = new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero);

        var found = Enumerable.Range(0, 5).SelectMany(i => detector.Observe(new PlacedBet(Guid.NewGuid(), Guid.NewGuid(), 1_000, [("f-1", Draw)], at.AddSeconds(i)))).ToList();

        found.ShouldBeEmpty();
    }
}

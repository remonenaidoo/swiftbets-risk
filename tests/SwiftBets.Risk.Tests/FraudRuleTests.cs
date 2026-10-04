using SwiftBets.Risk.Domain;

namespace SwiftBets.Risk.Tests;

public sealed class FraudRuleTests
{
    private static readonly FraudSettings Settings = FraudSettings.Default;
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Three_accounts_on_one_device_are_flagged_and_two_are_not()
    {
        var me = Guid.NewGuid();
        FraudRules.ManyAccounts(me, [me, Guid.NewGuid(), Guid.NewGuid()], Settings)!.LinkedUserIds.Count.ShouldBe(2);
        FraudRules.ManyAccounts(me, [me, Guid.NewGuid()], Settings).ShouldBeNull();
    }

    [Fact]
    public void A_bank_account_saved_by_someone_else_is_high_severity_and_an_unshared_one_is_clean()
    {
        FraudRules.SharedBank([Guid.NewGuid()])!.Severity.ShouldBe(FraudSeverity.High);
        FraudRules.SharedBank([]).ShouldBeNull();
    }

    [Fact]
    public void Withdrawing_a_deposit_with_little_betting_is_flagged_but_not_after_real_turnover()
    {
        FraudRules.RapidCycle(100_000, 5_000, 90_000, Settings)!.Rule.ShouldBe(FraudRule.RapidDepositWithdraw);
        FraudRules.RapidCycle(100_000, 60_000, 90_000, Settings).ShouldBeNull();
    }

    [Fact]
    public void Johannesburg_to_London_in_an_hour_is_impossible_but_in_a_day_is_not()
    {
        var joburg = new GeoPoint(-26.2, 28.05, Now);
        FraudRules.ImpossibleTravel(joburg, new GeoPoint(51.5, -0.12, Now.AddHours(1)), Settings).ShouldNotBeNull();
        FraudRules.ImpossibleTravel(joburg, new GeoPoint(51.5, -0.12, Now.AddHours(24)), Settings).ShouldBeNull();
    }

    [Fact]
    public void A_disposable_email_domain_is_flagged_and_an_ordinary_one_is_not()
    {
        FraudRules.DisposableEmail("someone@Mailinator.com", Settings)!.Severity.ShouldBe(FraudSeverity.Low);
        FraudRules.DisposableEmail("someone@gmail.com", Settings).ShouldBeNull();
    }
}

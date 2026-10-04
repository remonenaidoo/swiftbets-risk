using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using SwiftBets.BuildingBlocks.Core;
using SwiftBets.BuildingBlocks.Messaging;
using SwiftBets.Contracts.Messaging;
using SwiftBets.Contracts.Payments;
using SwiftBets.Contracts.Placement;
using SwiftBets.Contracts.Settlement;
using SwiftBets.Risk.Application;
using SwiftBets.Risk.Infrastructure.Consumers;
using SwiftBets.Risk.Infrastructure.Identity;
using SwiftBets.Risk.Infrastructure.Persistence;

namespace SwiftBets.Risk.Infrastructure;

public static class InfrastructureRegistration
{
    public static IServiceCollection AddRiskInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddKafkaMessaging(configuration);
        services.AddFaultInjection(configuration);
        services.AddValidatedOptions<RiskOptions>(configuration, RiskOptions.SectionName);
        services.AddSingleton(TimeProvider.System);
        var connectionString = Required(configuration, "ConnectionStrings:SbRisk");
        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        services.AddSingleton<PostgresRisk>();
        services.AddSingleton<IRiskJournal>(sp => sp.GetRequiredService<PostgresRisk>());
        services.AddSingleton<ICouponIndex>(sp => sp.GetRequiredService<PostgresRisk>());
        services.AddSingleton<IRiskStore>(sp => sp.GetRequiredService<PostgresRisk>());
        services.AddSingleton<IRiskPublisher, RiskPublisher>();

        // Fraud signals: cases in sb_risk, the customer's email read from identity with their own token.
        services.AddValidatedOptions<FraudOptions>(configuration, FraudOptions.SectionName);
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<FraudOptions>>().Value);
        services.AddSingleton<IFraudStore, PostgresFraud>();
        services.AddHttpClient<IProfileLookup, IdentityProfileLookup>((sp, http) =>
        {
            http.BaseAddress = new Uri(sp.GetRequiredService<FraudOptions>().IdentityAddress.TrimEnd('/') + "/");
            http.Timeout = TimeSpan.FromSeconds(3);
        });
        services.AddSingleton<RiskActors>();
        services.AddHostedService(sp => sp.GetRequiredService<RiskActors>());
        if (configuration.GetValue("Risk:RunConsumers", true))
        {
            services.AddKafkaConsumer<CouponPlacedV2, CouponPlacedConsumer>(Topics.CouponPlacedV2, "swiftbets.risk.liability");
            services.AddKafkaConsumer<CouponSettledV2, CouponSettledConsumer>(Topics.CouponSettledV2, "swiftbets.risk.settled");
            services.AddKafkaConsumer<DepositSucceededV1, FraudDepositConsumer>(Topics.DepositSucceeded, "swiftbets.risk.fraud-deposits");
            services.AddKafkaConsumer<WithdrawalRequestedV1, FraudWithdrawalConsumer>(Topics.WithdrawalRequested, "swiftbets.risk.fraud-withdrawals");
        }

        return services;
    }

    private static string Required(IConfiguration configuration, string key) =>
        configuration[key] is { Length: > 0 } value ? value : throw new InvalidOperationException($"Configuration '{key}' is required.");
}

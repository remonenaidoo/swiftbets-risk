using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SwiftBets.BuildingBlocks.Core;
using SwiftBets.BuildingBlocks.Messaging;
using SwiftBets.Contracts.Messaging;
using SwiftBets.Contracts.Placement;
using SwiftBets.Contracts.Settlement;
using SwiftBets.Risk.Application;
using SwiftBets.Risk.Infrastructure.Consumers;
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
        services.AddSingleton<RiskActors>();
        services.AddHostedService(sp => sp.GetRequiredService<RiskActors>());
        if (configuration.GetValue("Risk:RunConsumers", true))
        {
            services.AddKafkaConsumer<CouponPlacedV2, CouponPlacedConsumer>(Topics.CouponPlacedV2, "swiftbets.risk.liability");
            services.AddKafkaConsumer<CouponSettledV2, CouponSettledConsumer>(Topics.CouponSettledV2, "swiftbets.risk.settled");
        }

        return services;
    }

    private static string Required(IConfiguration configuration, string key) =>
        configuration[key] is { Length: > 0 } value ? value : throw new InvalidOperationException($"Configuration '{key}' is required.");
}

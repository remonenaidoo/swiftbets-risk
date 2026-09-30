using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SwiftBets.BuildingBlocks.Core;
using SwiftBets.BuildingBlocks.Messaging;

namespace SwiftBets.Risk.Infrastructure;

public static class InfrastructureRegistration
{
    public static IServiceCollection AddRiskInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddKafkaMessaging(configuration);
        services.AddFaultInjection(configuration);
        return services;
    }

    private static string Required(IConfiguration configuration, string key) =>
        configuration[key] is { Length: > 0 } value ? value : throw new InvalidOperationException($"Configuration '{key}' is required.");
}

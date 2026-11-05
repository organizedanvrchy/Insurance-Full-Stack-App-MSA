using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Insurance.BuildingBlocks.Messaging;

/// <summary>
/// The event bus: MassTransit on RabbitMQ locally, or on Azure Service Bus when <c>ServiceBus:ConnectionString</c> is
/// set (as it is in Azure). Each service gets its own queues (prefixed with its name), so every service that subscribes
/// to an event receives its own copy. Failed deliveries are retried; after the last retry MassTransit publishes a
/// Fault&lt;T&gt;, which is how a saga hears that a step failed for good. The services don't know which transport runs.
/// </summary>
public static class Messaging
{
    public static IServiceCollection AddMessaging(this IServiceCollection services, IConfiguration config,
        string serviceName, Action<IBusRegistrationConfigurator>? addConsumers = null)
    {
        services.AddMassTransit(x =>
        {
            x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter(serviceName, includeNamespace: false));
            addConsumers?.Invoke(x);

            if (UsesAzureServiceBus(config, out var connectionString))
            {
                // Service Bus Standard tier or higher: publishing to several services needs topics, which Basic lacks.
                x.UsingAzureServiceBus((context, cfg) =>
                {
                    cfg.Host(connectionString);
                    Configure(context, cfg);
                });
            }
            else
            {
                x.UsingRabbitMq((context, cfg) =>
                {
                    var rabbit = config.GetSection("RabbitMq");
                    cfg.Host(rabbit["Host"] ?? "localhost", rabbit["VirtualHost"] ?? "/", h =>
                    {
                        h.Username(rabbit["Username"] ?? "guest");
                        h.Password(rabbit["Password"] ?? "guest");
                    });
                    Configure(context, cfg);
                });
            }
        });
        return services;
    }

    /// <summary>True when an Azure Service Bus connection string is configured; RabbitMQ is used otherwise.</summary>
    public static bool UsesAzureServiceBus(IConfiguration config, out string connectionString)
    {
        connectionString = config["ServiceBus:ConnectionString"] ?? string.Empty;
        return !string.IsNullOrWhiteSpace(connectionString);
    }

    // The same retries and endpoints on either transport.
    private static void Configure<TEndpoint>(IBusRegistrationContext context, IBusFactoryConfigurator<TEndpoint> cfg)
        where TEndpoint : IReceiveEndpointConfigurator
    {
        cfg.UseMessageRetry(r => r.Intervals(100, 500, 1_000, 5_000, 15_000));
        cfg.ConfigureEndpoints(context);
    }
}

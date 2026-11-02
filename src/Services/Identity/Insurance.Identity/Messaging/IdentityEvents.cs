using MassTransit;

namespace Insurance.Identity.Messaging;

/// <summary>
/// Publishes the Identity service's events (Insurance.Identity.Contracts.Events) straight to the bus.
/// An interface, so the services can be tested without RabbitMQ.
/// </summary>
public interface IIdentityEvents
{
    Task PublishAsync<T>(T message) where T : class;
}

public class BusIdentityEvents : IIdentityEvents
{
    private readonly IPublishEndpoint _bus;
    public BusIdentityEvents(IPublishEndpoint bus) => _bus = bus;

    public Task PublishAsync<T>(T message) where T : class => _bus.Publish(message);
}

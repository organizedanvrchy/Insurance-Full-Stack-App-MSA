using Insurance.BuildingBlocks.Messaging;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Insurance.Architecture.Tests;

/// <summary>Locally the services use RabbitMQ; in Azure, setting ServiceBus:ConnectionString switches them to Service Bus.</summary>
public class MessagingTransportTests
{
    private const string ServiceBus =
        "Endpoint=sb://rainsurance-test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=dGVzdC1rZXk=";

    private static async Task<Uri> BusAddressAsync(Dictionary<string, string?> settings)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection().AddLogging(b => b.ClearProviders());
        services.AddMessaging(config, "test");
        await using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IBusControl>().Address;   // built, never started: nothing connects
    }

    [Fact]
    public async Task Without_a_Service_Bus_connection_string_the_bus_is_RabbitMQ()
    {
        Assert.Equal("rabbitmq", (await BusAddressAsync(new() { ["RabbitMq:Host"] = "localhost" })).Scheme);
    }

    [Fact]
    public async Task A_Service_Bus_connection_string_switches_to_Azure_Service_Bus()
    {
        var address = await BusAddressAsync(new() { ["ServiceBus:ConnectionString"] = ServiceBus, ["RabbitMq:Host"] = "localhost" });

        Assert.Equal("sb", address.Scheme);
        Assert.Equal("rainsurance-test.servicebus.windows.net", address.Host);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(ServiceBus, true)]
    public void Only_a_real_value_counts_as_configured(string? value, bool expected)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ServiceBus:ConnectionString"] = value }).Build();

        Assert.Equal(expected, Messaging.UsesAzureServiceBus(config, out _));
    }
}

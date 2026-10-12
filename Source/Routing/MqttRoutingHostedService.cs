using Microsoft.Extensions.Hosting;

namespace MQTTnet.Extensions.ManagedClient.Routing.Routing;

internal sealed class MqttRoutingHostedService(MqttRouter router) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => router.DisposeAsync().AsTask();
}

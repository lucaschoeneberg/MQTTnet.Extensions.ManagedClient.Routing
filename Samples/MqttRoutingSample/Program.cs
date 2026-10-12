using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Extensions.ManagedClient.Routing.Extensions;
using MQTTnet.Extensions.ManagedClient.Routing.ManagedClient;
using MQTTnet.Server;
using MqttRoutingSample.Controllers;
using MqttRoutingSample.Infrastructure;

const int Port = 18883;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddSimpleConsole(options => options.TimestampFormat = "HH:mm:ss ");
builder.Services.AddMqttControllers(opt => opt.WithRouteInvocationInterceptor<LoggingInterceptor>());

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Sample");

// Self-contained: spin up an in-process broker so `dotnet run` just works.
var serverOptions = new MqttServerOptionsBuilder()
    .WithDefaultEndpoint()
    .WithDefaultEndpointPort(Port)
    .Build();
using var server = new MqttServerFactory().CreateMqttServer(serverOptions);
await server.StartAsync();
logger.LogInformation("Broker listening on localhost:{Port}", Port);

var clientOptions = new MqttClientOptionsBuilder()
    .WithTcpServer("localhost", Port)
    .WithClientId("routing-sample")
    .Build();
var managedOptions = new ManagedMqttClientOptionsBuilder()
    .WithClientOptions(clientOptions)
    .WithAutoReconnectDelay(TimeSpan.FromSeconds(2))
    .Build();

var factory = new MqttClientFactory();
using var client = factory.CreateManagedMqttClient();
// WithAttributeRouting takes the concrete client type.
((ManagedMqttClient)client).WithAttributeRouting(host.Services);

await client.SubscribeAsync("demo/#");

// The managed client connects in the background; only publish once the
// broker acknowledged the subscription, otherwise QoS 0 messages are lost.
var subscribed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
client.SubscriptionsChangedAsync += _ =>
{
    subscribed.TrySetResult();
    return Task.CompletedTask;
};
await client.StartAsync(managedOptions);
await subscribed.Task.WaitAsync(TimeSpan.FromSeconds(10));

await client.EnqueueAsync("demo/telemetry/7", """{"celsius":21.5}""");
await client.EnqueueAsync("demo/motion/kitchen");
await client.EnqueueAsync("demo/unmatched/nothing");

await Task.Delay(TimeSpan.FromSeconds(3));

await client.StopAsync();
await server.StopAsync();

if (TelemetryController.TemperatureCount == 1 && TelemetryController.MotionCount == 1)
{
    logger.LogInformation("Sample completed: all messages routed.");
    return 0;
}

logger.LogError("Sample failed: temperature={Temperature}, motion={Motion}.",
    TelemetryController.TemperatureCount, TelemetryController.MotionCount);
return 1;

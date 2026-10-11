using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MQTTnet.Extensions.ManagedClient.Routing.ManagedClient;
using MQTTnet.Formatter;
using MQTTnet.Server;

namespace MQTTnet.Extensions.ManagedClient.Routing.Tests;

[TestClass]
public class ManagedClientServerProtocolTests
{
    private static async Task<bool> ConnectAsync(MqttProtocolVersion version, int port)
    {
        var mqttFactory = new MqttServerFactory();
        var serverOptions = new MqttServerOptionsBuilder()
            .WithDefaultEndpoint()
            .WithDefaultEndpointPort(port)
            .Build();
        using var mqttServer = mqttFactory.CreateMqttServer(serverOptions);

        await mqttServer.StartAsync();

        var clientFactory = new MqttClientFactory();

        using var client = clientFactory.CreateManagedMqttClient();
        var clientOptions = new MqttClientOptionsBuilder()
            .WithTcpServer("localhost", port)
            .WithProtocolVersion(version)
            .WithTimeout(TimeSpan.FromSeconds(10))
            .Build();
        var options = new ManagedMqttClientOptionsBuilder()
            .WithClientOptions(clientOptions)
            .WithAutoReconnectDelay(TimeSpan.FromSeconds(1))
            .Build();

        var tcs = new TaskCompletionSource<bool>();
        client.ConnectedAsync += _ =>
        {
            Console.WriteLine("Connected!"); // Debug
            tcs.TrySetResult(true);
            return Task.CompletedTask;
        };
        client.ConnectingFailedAsync += args =>
        {
            Console.WriteLine($"Connection failed: {args.Exception?.Message}"); // Debug
            tcs.TrySetResult(false);
            return Task.CompletedTask;
        };

        await client.StartAsync(options);

        var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(15)));

        await client.StopAsync();
        await mqttServer.StopAsync();

        if (completedTask != tcs.Task)
        {
            return false;
        }

        return await tcs.Task.ConfigureAwait(false);
    }

    [TestMethod]
    public async Task Client_can_connect_to_mqtt_3_1_1_server()
    {
        var result = await ConnectAsync(MqttProtocolVersion.V311, 18885);
        Assert.IsTrue(result);
    }

    [TestMethod]
    public async Task Client_can_connect_to_mqtt_5_0_server()
    {
        var result = await ConnectAsync(MqttProtocolVersion.V500, 18886);
        Assert.IsTrue(result);
    }
}

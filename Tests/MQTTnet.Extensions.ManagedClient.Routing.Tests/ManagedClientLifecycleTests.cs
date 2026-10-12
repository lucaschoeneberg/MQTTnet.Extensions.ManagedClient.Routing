using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MQTTnet.Extensions.ManagedClient.Routing.Extensions;
using MQTTnet.Extensions.ManagedClient.Routing.ManagedClient;
using MQTTnet.Extensions.ManagedClient.Routing.Routing;
using MQTTnet.Protocol;
using MQTTnet.Server;

namespace MQTTnet.Extensions.ManagedClient.Routing.Tests;

[TestClass]
public class ManagedClientLifecycleTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private sealed class BlockingStorage : IManagedMqttClientStorage
    {
        public TaskCompletionSource Removing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int SavedCount { get; private set; }

        public Task<IList<ManagedMqttApplicationMessage>> LoadQueuedMessagesAsync() =>
            Task.FromResult<IList<ManagedMqttApplicationMessage>>([]);

        public async Task SaveQueuedMessagesAsync(IList<ManagedMqttApplicationMessage> messages)
        {
            if (messages.Count == 0)
            {
                Removing.TrySetResult();
                await Release.Task;
            }

            SavedCount = messages.Count;
        }
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    [TestMethod]
    [DataRow(MqttQualityOfServiceLevel.AtLeastOnce)]
    [DataRow(MqttQualityOfServiceLevel.ExactlyOnce)]
    public async Task ParallelRouting_RealBrokerReceivesAcknowledgementsOnlyAfterActionsFinish(MqttQualityOfServiceLevel qos)
    {
        var port = GetAvailablePort();
        using var server = new MqttServerFactory().CreateMqttServer(new MqttServerOptionsBuilder()
            .WithDefaultEndpoint().WithDefaultEndpointPort(port).Build());
        await server.StartAsync();
        var state = new DispatchState();
        state.Probes[1] = new DispatchProbe();
        state.Probes[2] = new DispatchProbe();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(state);
        services.AddScoped<DispatchResource>();
        services.AddMqttControllers(opt =>
        {
            opt.FromAssemblies = [typeof(DispatchLifecycleController).Assembly];
            opt.WithMaxConcurrentDispatches(2);
        });
        using var sp = services.BuildServiceProvider();
        using var client = (ManagedMqttClient)new MqttClientFactory().CreateManagedMqttClient();
        client.WithAttributeRouting(sp);
        var subscribed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var acknowledged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var topics = new System.Collections.Concurrent.ConcurrentQueue<string>();
        client.SubscriptionsChangedAsync += _ =>
        {
            subscribed.TrySetResult();
            return Task.CompletedTask;
        };
        server.ClientAcknowledgedPublishPacketAsync += args =>
        {
            if (args.IsCompleted)
            {
                topics.Enqueue(args.PublishPacket.Topic);
                if (topics.Count == 2)
                {
                    acknowledged.TrySetResult();
                }
            }

            return Task.CompletedTask;
        };
        var options = new ManagedMqttClientOptionsBuilder().WithClientOptions(new MqttClientOptionsBuilder()
            .WithClientId("parallel-lifecycle").WithTcpServer("localhost", port).Build()).Build();
        try
        {
            await client.SubscribeAsync([new MQTTnet.Packets.MqttTopicFilter { Topic = "lifecycle/#", QualityOfServiceLevel = qos }]);
            await client.StartAsync(options);
            await subscribed.Task.WaitAsync(TestTimeout);
            for (var i = 1; i <= 2; i++)
            {
                await server.InjectApplicationMessage(new InjectedMqttApplicationMessage(
                    new MqttApplicationMessageBuilder().WithTopic($"lifecycle/async/{i}").WithQualityOfServiceLevel(qos).Build()));
            }

            // Both actions must start while blocked, proving overlap through the
            // real serialized MQTTnet receive callbacks, without an early ACK.
            await Task.WhenAll(state.Probes.Values.Select(probe => probe.Started.Task)).WaitAsync(TestTimeout);
            Assert.IsTrue(topics.IsEmpty);
            state.Probes[2].Release.SetResult();
            state.Probes[1].Release.SetResult();
            await acknowledged.Task.WaitAsync(TestTimeout);
            CollectionAssert.AreEqual(new[] { "lifecycle/async/1", "lifecycle/async/2" }, topics.ToArray());
        }
        finally
        {
            foreach (var probe in state.Probes.Values)
            {
                probe.Release.TrySetResult();
            }

            await client.StopAsync();
            await server.StopAsync();
        }
    }

    [TestMethod]
    public async Task StopAsync_WaitsForInFlightStorageRemoval()
    {
        var port = GetAvailablePort();
        using var server = new MqttServerFactory().CreateMqttServer(new MqttServerOptionsBuilder()
            .WithDefaultEndpoint().WithDefaultEndpointPort(port).Build());
        await server.StartAsync();
        using var client = (ManagedMqttClient)new MqttClientFactory().CreateManagedMqttClient();
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ConnectedAsync += _ =>
        {
            connected.TrySetResult();
            return Task.CompletedTask;
        };
        var storage = new BlockingStorage();
        var options = new ManagedMqttClientOptionsBuilder()
            .WithClientOptions(new MqttClientOptionsBuilder().WithTcpServer("localhost", port).Build())
            .WithStorage(storage).Build();
        Task? stopping = null;
        try
        {
            await client.StartAsync(options);
            await connected.Task.WaitAsync(TestTimeout);
            await client.EnqueueAsync(new MqttApplicationMessageBuilder().WithTopic("lifecycle/storage")
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce).Build());
            await storage.Removing.Task.WaitAsync(TestTimeout);

            stopping = client.StopAsync();
            await Task.WhenAny(stopping, Task.Delay(200));
            Assert.IsFalse(stopping.IsCompleted);
            Assert.AreEqual(1, storage.SavedCount);
        }
        finally
        {
            storage.Release.TrySetResult();
            await (stopping ?? client.StopAsync()).WaitAsync(TestTimeout);
            await server.StopAsync();
        }

        Assert.AreEqual(0, storage.SavedCount);
        Assert.AreEqual(0, client.PendingApplicationMessagesCount);
    }

    [TestMethod]
    public async Task StopAsync_DrainsPublishingInterceptorBeforeReleasingStorage()
    {
        var port = GetAvailablePort();
        using var server = new MqttServerFactory().CreateMqttServer(new MqttServerOptionsBuilder()
            .WithDefaultEndpoint().WithDefaultEndpointPort(port).Build());
        await server.StartAsync();
        using var client = (ManagedMqttClient)new MqttClientFactory().CreateManagedMqttClient();
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var intercepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ConnectedAsync += _ =>
        {
            connected.TrySetResult();
            return Task.CompletedTask;
        };
        client.InterceptPublishMessageAsync += async args =>
        {
            intercepted.TrySetResult();
            await release.Task;
            args.AcceptPublish = false;
        };
        var storage = new BlockingStorage();
        storage.Release.SetResult();
        var options = new ManagedMqttClientOptionsBuilder()
            .WithClientOptions(new MqttClientOptionsBuilder().WithTcpServer("localhost", port).Build())
            .WithStorage(storage).Build();
        Task? stopping = null;
        try
        {
            await client.StartAsync(options);
            await connected.Task.WaitAsync(TestTimeout);
            await client.EnqueueAsync(new MqttApplicationMessage { Topic = "lifecycle/intercept" });
            await intercepted.Task.WaitAsync(TestTimeout);

            stopping = client.StopAsync();
            await Task.WhenAny(stopping, Task.Delay(200));
            Assert.IsFalse(stopping.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
            await (stopping ?? client.StopAsync()).WaitAsync(TestTimeout);
            await server.StopAsync();
        }

        Assert.AreEqual(0, storage.SavedCount);
    }

    [TestMethod]
    public async Task Restart_FirstRetryDoesNotInheritPreviousFailureBackoff()
    {
        using var client = (ManagedMqttClient)new MqttClientFactory().CreateManagedMqttClient();
        var options = new ManagedMqttClientOptionsBuilder()
            .WithClientOptions(new MqttClientOptionsBuilder().WithTcpServer("localhost", GetAvailablePort()).Build())
            .WithAutoReconnectDelay(TimeSpan.FromMilliseconds(1)).Build();
        await client.StartAsync(options);
        await client.StopAsync();

        // Seed a long previous outage without spending minutes waiting for it.
        typeof(ManagedMqttClient).GetField("_reconnectFailureCount", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(client, 100);
        var retried = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failures = 0;
        client.ConnectingFailedAsync += _ =>
        {
            if (Interlocked.Increment(ref failures) == 2)
            {
                retried.TrySetResult();
            }

            return Task.CompletedTask;
        };

        try
        {
            await client.StartAsync(options);
            await retried.Task.WaitAsync(TimeSpan.FromSeconds(4));
        }
        finally
        {
            await client.StopAsync();
        }
    }

    [TestMethod]
    [DataRow(500d)]
    [DataRow(8_589_934_590d)]
    public async Task StartAsync_RejectsReconnectCapOutsideSupportedRange(double milliseconds)
    {
        using var client = (ManagedMqttClient)new MqttClientFactory().CreateManagedMqttClient();
        var options = new ManagedMqttClientOptionsBuilder()
            .WithClientOptions(new MqttClientOptionsBuilder().WithTcpServer("localhost").Build()).Build();
        options.MaxReconnectDelay = TimeSpan.FromMilliseconds(milliseconds);

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => client.StartAsync(options));
        Assert.IsFalse(client.IsStarted);
    }
}

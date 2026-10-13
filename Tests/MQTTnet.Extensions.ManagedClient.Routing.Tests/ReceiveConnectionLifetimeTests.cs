using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MQTTnet.Extensions.ManagedClient.Routing.Extensions;
using MQTTnet.Extensions.ManagedClient.Routing.ManagedClient;
using MQTTnet.Extensions.ManagedClient.Routing.Routing;
using MQTTnet.Packets;
using MQTTnet.Protocol;

namespace MQTTnet.Extensions.ManagedClient.Routing.Tests;

[TestClass]
public class ReceiveConnectionLifetimeTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(8);

    // Drive the transport callback boundary directly: MQTTnet's own DisconnectAsync
    // waits for receive subscribers, while a custom IMqttClient can overlap them.
    private static Task InvokeTransportCallback(ManagedMqttClient client, string method, EventArgs args) =>
        (Task)typeof(ManagedMqttClient).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(client, [args])!;

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task HeldReceiveSubscriber_CannotRebindDeliveryAfterReconnect(bool useApplicationBuilder, bool newFinishesFirst)
    {
        using var client = (ManagedMqttClient)new MqttClientFactory().CreateManagedMqttClient();
        var state = new DispatchState();
        state.Probes[1] = new DispatchProbe();
        state.Probes[2] = new DispatchProbe();
        var services = new ServiceCollection().AddLogging().AddSingleton(state)
            .AddScoped<DispatchResource>().AddSingleton<IManagedMqttClient>(client);
        services.AddMqttControllers(options =>
        {
            options.FromAssemblies = [typeof(DispatchLifecycleController).Assembly];
            options.WithMaxConcurrentDispatches(2);
        });
        await using var sp = services.BuildServiceProvider();
        var subscriberStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSubscriber = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var newAcknowledged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldAcknowledgements = 0;
        client.ApplicationMessageReceivedAsync += async args =>
        {
            if (args.ApplicationMessage.Topic == "lifecycle/async/1")
            {
                subscriberStarted.TrySetResult();
                await releaseSubscriber.Task;
            }
        };
        if (useApplicationBuilder)
        {
            new ApplicationBuilder(sp).UseAttributeRouting();
        }
        else
        {
            client.WithAttributeRouting(sp);
        }

        static MqttApplicationMessageReceivedEventArgs Delivery(int id, Func<MqttApplicationMessageReceivedEventArgs, CancellationToken, Task> acknowledge)
        {
            var topic = $"lifecycle/async/{id}";
            return new MqttApplicationMessageReceivedEventArgs("receive-probe", new MqttApplicationMessage { Topic = topic },
                new MqttPublishPacket { Topic = topic, PacketIdentifier = 1, QualityOfServiceLevel = MqttQualityOfServiceLevel.AtLeastOnce }, acknowledge);
        }

        var oldDelivery = Delivery(1, (_, _) =>
        {
            Interlocked.Increment(ref oldAcknowledgements);
            return Task.CompletedTask;
        });
        var newDelivery = Delivery(2, (_, _) =>
        {
            newAcknowledged.TrySetResult();
            return Task.CompletedTask;
        });
        var connecting = new MqttClientConnectingEventArgs(new MqttClientOptionsBuilder().WithTcpServer("localhost").Build());
        await InvokeTransportCallback(client, "OnConnectingAsync", connecting);
        var oldReceive = InvokeTransportCallback(client, "OnApplicationMessageReceivedAsync", oldDelivery);
        try
        {
            await subscriberStarted.Task.WaitAsync(Deadline);
            // A replacement connection cancels the old lifetime and reuses packet ID 1.
            await InvokeTransportCallback(client, "OnConnectingAsync", connecting);
            await InvokeTransportCallback(client, "OnApplicationMessageReceivedAsync", newDelivery).WaitAsync(Deadline);
            await state.Probes[2].Started.Task.WaitAsync(Deadline);
            if (newFinishesFirst)
            {
                state.Probes[2].Release.TrySetResult();
                await newAcknowledged.Task.WaitAsync(Deadline);
            }

            releaseSubscriber.TrySetResult();
            await oldReceive.WaitAsync(Deadline);
            Assert.IsTrue(oldDelivery.ProcessingFailed, "The old delivery must be rejected before dispatch.");
            Assert.IsFalse(state.Probes[1].Started.Task.IsCompleted);
            Assert.AreEqual(0, Volatile.Read(ref oldAcknowledgements));
            Assert.IsFalse(state.Probes[2].Canceled.Task.IsCompleted, "The replacement delivery must keep its own lifetime.");

            state.Probes[2].Release.TrySetResult();
            await newAcknowledged.Task.WaitAsync(Deadline);
            Assert.IsFalse(newDelivery.ProcessingFailed);
        }
        finally
        {
            releaseSubscriber.TrySetResult();
            foreach (var probe in state.Probes.Values)
            {
                probe.Release.TrySetResult();
            }

            await oldReceive.WaitAsync(Deadline);
            await sp.GetRequiredService<MqttRouter>().DisposeAsync().AsTask().WaitAsync(Deadline);
        }

        Assert.AreEqual(0, Volatile.Read(ref oldAcknowledgements));
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using MQTTnet.Extensions.ManagedClient.Routing.ManagedClient;

namespace MQTTnet.Extensions.ManagedClient.Routing.Tests;

class FakeManagedMqttClient : IManagedMqttClient
{
    public MqttApplicationMessage LastMessage;
    public IList<MqttTopicFilter> LastSubscriptions;
    public IList<string> LastUnsubscriptions;

    public Task EnqueueAsync(MqttApplicationMessage applicationMessage)
    {
        LastMessage = applicationMessage;
        return Task.CompletedTask;
    }

    public Task EnqueueAsync(ManagedMqttApplicationMessage applicationMessage)
    {
        LastMessage = applicationMessage.ApplicationMessage;
        return Task.CompletedTask;
    }

    public Task SubscribeAsync(IEnumerable<MqttTopicFilter> topicFilters)
    {
        LastSubscriptions = topicFilters.ToList();
        return Task.CompletedTask;
    }

    public Task UnsubscribeAsync(IEnumerable<string> topics)
    {
        LastUnsubscriptions = topics.ToList();
        return Task.CompletedTask;
    }

    public Task PingAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StartAsync(ManagedMqttClientOptions options) => Task.CompletedTask;
    public Task StopAsync(bool cleanDisconnect = true) => Task.CompletedTask;
    public void Dispose() { }

    public event Func<ApplicationMessageProcessedEventArgs, Task> ApplicationMessageProcessedAsync { add { } remove { } }
    public event Func<MqttApplicationMessageReceivedEventArgs, Task> ApplicationMessageReceivedAsync { add { } remove { } }
    public event Func<ApplicationMessageSkippedEventArgs, Task> ApplicationMessageSkippedAsync { add { } remove { } }
    public event Func<MqttClientConnectedEventArgs, Task> ConnectedAsync { add { } remove { } }
    public event Func<ConnectingFailedEventArgs, Task> ConnectingFailedAsync { add { } remove { } }
    public event Func<EventArgs, Task> ConnectionStateChangedAsync { add { } remove { } }
    public event Func<MqttClientDisconnectedEventArgs, Task> DisconnectedAsync { add { } remove { } }
    public event Func<ManagedProcessFailedEventArgs, Task> SynchronizingSubscriptionsFailedAsync { add { } remove { } }
    public event Func<SubscriptionsChangedEventArgs, Task> SubscriptionsChangedAsync { add { } remove { } }

    public IMqttClient InternalClient => null;
    public bool IsConnected => false;
    public bool IsStarted => false;
    public ManagedMqttClientOptions Options => null;
    public int PendingApplicationMessagesCount => 0;
}

[TestClass]
public class ManagedMqttClientExtensionsTests
{
    [TestMethod]
    public async Task EnqueueAsync_CreatesMessage_WithTopicAndPayload()
    {
        var client = new FakeManagedMqttClient();
        await client.EnqueueAsync("test/topic", "payload", MqttQualityOfServiceLevel.AtLeastOnce, true);

        Assert.IsNotNull(client.LastMessage);
        Assert.AreEqual("test/topic", client.LastMessage.Topic);
        Assert.AreEqual("payload", client.LastMessage.ConvertPayloadToString());
        Assert.AreEqual(MqttQualityOfServiceLevel.AtLeastOnce, client.LastMessage.QualityOfServiceLevel);
        Assert.IsTrue(client.LastMessage.Retain);
    }

    [TestMethod]
    public async Task SubscribeAsync_StringTopic_PassesTopicFilter()
    {
        var client = new FakeManagedMqttClient();
        await client.SubscribeAsync("foo/bar", MqttQualityOfServiceLevel.ExactlyOnce);

        Assert.IsNotNull(client.LastSubscriptions);
        var filter = client.LastSubscriptions.Single();
        Assert.AreEqual("foo/bar", filter.Topic);
        Assert.AreEqual(MqttQualityOfServiceLevel.ExactlyOnce, filter.QualityOfServiceLevel);
    }

    [TestMethod]
    public async Task UnsubscribeAsync_StringTopic_PassesTopic()
    {
        var client = new FakeManagedMqttClient();
        await client.UnsubscribeAsync("foo/bar");

        Assert.IsNotNull(client.LastUnsubscriptions);
        Assert.AreEqual(1, client.LastUnsubscriptions.Count);
        Assert.AreEqual("foo/bar", client.LastUnsubscriptions.Single());
    }
}


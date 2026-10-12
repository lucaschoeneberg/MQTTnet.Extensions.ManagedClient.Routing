using Microsoft.VisualStudio.TestTools.UnitTesting;
using MQTTnet.Extensions.ManagedClient.Routing.ManagedClient;

namespace MQTTnet.Extensions.ManagedClient.Routing.Tests;

[TestClass]
public class ManagedClientStabilityTests
{
    private sealed class InMemoryStorage : IManagedMqttClientStorage
    {
        private readonly IList<ManagedMqttApplicationMessage> messages;

        public InMemoryStorage(IList<ManagedMqttApplicationMessage> messages)
        {
            this.messages = messages;
        }

        public Task<IList<ManagedMqttApplicationMessage>> LoadQueuedMessagesAsync() =>
            Task.FromResult(messages);

        public Task SaveQueuedMessagesAsync(IList<ManagedMqttApplicationMessage> messages) =>
            Task.CompletedTask;
    }

    [TestMethod]
    public void DefaultOptions_BoundQueueAndBackoff()
    {
        var options = new ManagedMqttClientOptions();

        Assert.AreEqual(1000, options.MaxPendingMessages);
        Assert.AreEqual(TimeSpan.FromSeconds(5), options.AutoReconnectDelay);
        Assert.AreEqual(TimeSpan.FromMinutes(2), options.MaxReconnectDelay);
    }

    [TestMethod]
    public void ComputeReconnectDelay_GrowsExponentiallyWithJitter()
    {
        var baseDelay = TimeSpan.FromSeconds(5);
        var maxDelay = TimeSpan.FromMinutes(2);

        for (var i = 0; i < 50; i++)
        {
            var first = ManagedMqttClient.ComputeReconnectDelay(baseDelay, maxDelay, 0);
            Assert.IsTrue(first >= TimeSpan.FromSeconds(2.5) && first <= baseDelay);

            var third = ManagedMqttClient.ComputeReconnectDelay(baseDelay, maxDelay, 3);
            Assert.IsTrue(third >= TimeSpan.FromSeconds(20) && third <= TimeSpan.FromSeconds(40));
        }
    }

    [TestMethod]
    public void ComputeReconnectDelay_RespectsCapAndFloor()
    {
        var capped = ManagedMqttClient.ComputeReconnectDelay(
            TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(2), 100);
        Assert.IsTrue(capped <= TimeSpan.FromMinutes(2));

        var floored = ManagedMqttClient.ComputeReconnectDelay(
            TimeSpan.Zero, TimeSpan.FromMinutes(2), 0);
        Assert.IsTrue(floored >= TimeSpan.FromSeconds(1));

        var negativeFailures = ManagedMqttClient.ComputeReconnectDelay(
            TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(2), -1);
        Assert.IsTrue(negativeFailures >= TimeSpan.FromSeconds(2.5));
    }

    [TestMethod]
    public async Task LoadQueuedMessages_ReturnsCopy()
    {
        var stored = new List<ManagedMqttApplicationMessage> { new() };
        using var manager = new ManagedMqttClientStorageManager(new InMemoryStorage(stored));

        var first = await manager.LoadQueuedMessagesAsync();
        var second = await manager.LoadQueuedMessagesAsync();

        Assert.AreNotSame(first, second);
        Assert.AreEqual(1, first.Count);
    }
}

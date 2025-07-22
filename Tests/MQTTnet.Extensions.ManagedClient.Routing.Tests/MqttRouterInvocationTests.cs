using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MQTTnet;
using MQTTnet.Packets;
using MQTTnet.Extensions.ManagedClient.Routing.Attributes;
using MQTTnet.Extensions.ManagedClient.Routing.Extensions;
using MQTTnet.Extensions.ManagedClient.Routing.Routing;

namespace MQTTnet.Extensions.ManagedClient.Routing.Tests;

[MqttRoute("test")]
public class TestController : MqttBaseController
{
    public static int Calls;
    public static int LastId;
    public static TestPayload LastPayload;

    [MqttRoute("action/{id}")]
    public void Action(int id, [FromPayload] TestPayload payload)
    {
        Calls++;
        LastId = id;
        LastPayload = payload;
    }

    public static void Reset()
    {
        Calls = 0;
        LastId = 0;
        LastPayload = null;
    }
}

public class TestPayload
{
    public string Name { get; set; }
}

[TestClass]
public class MqttRouterInvocationTests
{
    private static MqttRouter CreateRouter(ServiceProvider sp)
    {
        var table = MqttRouteTableFactory.Create(new[] { typeof(TestController).Assembly });
        var logger = sp.GetRequiredService<ILogger<MqttRouter>>();
        return new MqttRouter(logger, table, new TypeActivatorCache());
    }

    [TestMethod]
    public async Task Matched_route_invokes_controller_and_deserializes_payload()
    {
        // Arrange
        TestController.Reset();
        var services = new ServiceCollection();
        var options = new MqttRoutingOptions();
        options.WithJsonSerializerOptions();
        services.AddLogging();
        services.AddSingleton(options);
        var sp = services.BuildServiceProvider();
        var router = CreateRouter(sp);
        var payload = new TestPayload { Name = "foo" };
        var json = JsonSerializer.Serialize(payload, options.SerializerOptions);
        var msg = new MqttApplicationMessage { Topic = "test/action/5", PayloadSegment = Encoding.UTF8.GetBytes(json) };
        var packet = new MqttPublishPacket { Topic = msg.Topic, PayloadSegment = Encoding.UTF8.GetBytes(json) };
        var args = new MqttApplicationMessageReceivedEventArgs("client", msg, packet, (_, _) => Task.CompletedTask);

        // Act
        await router.OnIncomingApplicationMessage(sp, args, false);

        // Assert
        Assert.AreEqual(1, TestController.Calls);
        Assert.AreEqual(5, TestController.LastId);
        Assert.IsNotNull(TestController.LastPayload);
        Assert.AreEqual("foo", TestController.LastPayload.Name);
        Assert.IsFalse(args.ProcessingFailed);
    }

    [TestMethod]
    public async Task Unmatched_route_sets_processing_failed()
    {
        // Arrange
        TestController.Reset();
        var services = new ServiceCollection();
        var options = new MqttRoutingOptions();
        options.WithJsonSerializerOptions();
        services.AddLogging();
        services.AddSingleton(options);
        var sp = services.BuildServiceProvider();
        var router = CreateRouter(sp);
        var msg = new MqttApplicationMessage { Topic = "unknown/topic" };
        var packet = new MqttPublishPacket { Topic = msg.Topic };
        var args = new MqttApplicationMessageReceivedEventArgs("client", msg, packet, (_, _) => Task.CompletedTask);

        // Act
        await router.OnIncomingApplicationMessage(sp, args, false);

        // Assert
        Assert.AreEqual(0, TestController.Calls);
        Assert.IsTrue(args.ProcessingFailed);
    }
}


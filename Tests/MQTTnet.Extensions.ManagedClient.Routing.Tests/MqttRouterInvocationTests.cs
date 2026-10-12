using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MQTTnet;
using MQTTnet.Extensions.ManagedClient.Routing.Attributes;
using MQTTnet.Extensions.ManagedClient.Routing.Extensions;
using MQTTnet.Extensions.ManagedClient.Routing.Routing;
using MQTTnet.Packets;

namespace MQTTnet.Extensions.ManagedClient.Routing.Tests;

[MqttRoute("test")]
public class TestController : MqttBaseController
{
    public static int Calls { get; set; }
    public static int LastId { get; set; }
    public static TestPayload? LastPayload { get; set; }

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

[MqttRoute("async")]
public class AsyncTestController : MqttBaseController
{
    public static int Calls { get; set; }
    public static int LastId { get; set; }
    public static bool TokenWasNone { get; set; }

    [MqttRoute("task/{id}")]
    public async Task TaskAction(int id, CancellationToken cancellationToken)
    {
        Calls++;
        LastId = id;
        TokenWasNone = cancellationToken == CancellationToken.None;
        await Task.Yield();
    }

    [MqttRoute("generic/{id}")]
    public async Task<int> GenericTaskAction(int id)
    {
        Calls++;
        await Task.Yield();
        return id;
    }

    [MqttRoute("valuetask/{id}")]
    public async ValueTask ValueTaskAction(int id)
    {
        Calls++;
        LastId = id;
        await Task.Yield();
    }

    [MqttRoute("genericvaluetask/{id}")]
    public async ValueTask<int> GenericValueTaskAction(int id)
    {
        Calls++;
        await Task.Yield();
        return id;
    }

    public static void Reset()
    {
        Calls = 0;
        LastId = 0;
        TokenWasNone = false;
    }
}

[MqttRoute("defaults")]
public class DefaultRouteController : MqttBaseController
{
    public static int Calls { get; set; }

    [MqttRoute]
    public Task Fallback()
    {
        Calls++;
        return Task.CompletedTask;
    }

    public static void Reset()
    {
        Calls = 0;
    }
}

public class TestPayload
{
    public string Name { get; set; } = string.Empty;
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

    private static (ServiceProvider sp, MqttRouter router) CreateServiceRouter()
    {
        var services = new ServiceCollection();
        var options = new MqttRoutingOptions();
        options.WithJsonSerializerOptions();
        services.AddLogging();
        services.AddSingleton(options);
        var sp = services.BuildServiceProvider();
        return (sp, CreateRouter(sp));
    }

    private static MqttApplicationMessageReceivedEventArgs CreateArgs(string topic, string? json = null)
    {
        var payload = json is null ? null : Encoding.UTF8.GetBytes(json);
        var msg = new MqttApplicationMessage { Topic = topic };
        if (payload != null)
        {
            msg.PayloadSegment = payload;
        }
        var packet = new MqttPublishPacket { Topic = msg.Topic };
        if (payload != null)
        {
            packet.PayloadSegment = payload;
        }
        return new MqttApplicationMessageReceivedEventArgs("client", msg, packet, (_, _) => Task.CompletedTask);
    }

    [TestMethod]
    public async Task Matched_route_invokes_controller_and_deserializes_payload()
    {
        // Arrange
        TestController.Reset();
        var (sp, router) = CreateServiceRouter();
        var payload = new TestPayload { Name = "foo" };
        var json = JsonSerializer.Serialize(payload);
        var args = CreateArgs("test/action/5", json);

        // Act
        await router.OnIncomingApplicationMessage(sp, args, false);

        // Assert
        Assert.AreEqual(1, TestController.Calls);
        Assert.AreEqual(5, TestController.LastId);
        Assert.IsNotNull(TestController.LastPayload);
        Assert.AreEqual("foo", TestController.LastPayload!.Name);
        Assert.IsFalse(args.ProcessingFailed);
    }

    [TestMethod]
    public async Task Unmatched_route_sets_processing_failed()
    {
        // Arrange
        TestController.Reset();
        var (sp, router) = CreateServiceRouter();
        var args = CreateArgs("unknown/topic");

        // Act
        await router.OnIncomingApplicationMessage(sp, args, false);

        // Assert
        Assert.AreEqual(0, TestController.Calls);
        Assert.IsTrue(args.ProcessingFailed);
    }

    [TestMethod]
    public async Task Task_action_with_cancellation_token_is_invoked()
    {
        // Arrange
        AsyncTestController.Reset();
        var (sp, router) = CreateServiceRouter();
        var args = CreateArgs("async/task/7");

        // Act
        await router.OnIncomingApplicationMessage(sp, args, false);

        // Assert
        Assert.AreEqual(1, AsyncTestController.Calls);
        Assert.AreEqual(7, AsyncTestController.LastId);
        Assert.IsFalse(AsyncTestController.TokenWasNone);
        Assert.IsFalse(args.ProcessingFailed);
    }

    [TestMethod]
    public async Task Generic_task_action_result_is_awaited()
    {
        // Arrange
        AsyncTestController.Reset();
        var (sp, router) = CreateServiceRouter();
        var args = CreateArgs("async/generic/3");

        // Act
        await router.OnIncomingApplicationMessage(sp, args, false);

        // Assert
        Assert.AreEqual(1, AsyncTestController.Calls);
        Assert.IsFalse(args.ProcessingFailed);
    }

    [TestMethod]
    public async Task Valuetask_actions_are_invoked()
    {
        // Arrange
        AsyncTestController.Reset();
        var (sp, router) = CreateServiceRouter();

        // Act
        await router.OnIncomingApplicationMessage(sp, CreateArgs("async/valuetask/9"), false);
        await router.OnIncomingApplicationMessage(sp, CreateArgs("async/genericvaluetask/11"), false);

        // Assert
        Assert.AreEqual(2, AsyncTestController.Calls);
        Assert.AreEqual(9, AsyncTestController.LastId);
    }

    [TestMethod]
    public async Task Parameterless_route_falls_back_to_action_name()
    {
        // Arrange
        DefaultRouteController.Reset();
        var (sp, router) = CreateServiceRouter();
        var args = CreateArgs("defaults/Fallback");

        // Act
        await router.OnIncomingApplicationMessage(sp, args, false);

        // Assert
        Assert.AreEqual(1, DefaultRouteController.Calls);
        Assert.IsFalse(args.ProcessingFailed);
    }
}

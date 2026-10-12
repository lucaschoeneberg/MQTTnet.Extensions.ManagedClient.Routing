using Microsoft.VisualStudio.TestTools.UnitTesting;
using MQTTnet;
using MQTTnet.Extensions.ManagedClient.Routing.Routing;
using MQTTnet.Packets;

namespace MQTTnet.Extensions.ManagedClient.Routing.Tests;

[TestClass]
public class MqttBaseControllerTests
{
    private sealed class StubController : MqttBaseController
    {
    }

    [TestMethod]
    public async Task Ok_Accepted_BadMessage_CompleteSuccessfully()
    {
        var controller = new StubController();

        await controller.Ok();
        await controller.Accepted();
        await controller.BadMessage();
    }

    [TestMethod]
    public void Message_Reflects_ControllerContext()
    {
        var controller = new StubController();
        var msg = new MqttApplicationMessage { Topic = "a/b" };
        var packet = new MqttPublishPacket { Topic = "a/b" };
        var args = new MqttApplicationMessageReceivedEventArgs("client", msg, packet, (_, _) => Task.CompletedTask);
        controller.ControllerContext = new MqttControllerContext { MqttContext = args };

        Assert.AreSame(args, controller.MqttContext);
        Assert.AreSame(msg, controller.Message);
    }
}

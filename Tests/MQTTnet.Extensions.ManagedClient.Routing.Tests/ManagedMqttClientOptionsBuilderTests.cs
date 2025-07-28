using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient.Routing.ManagedClient;

namespace MQTTnet.Extensions.ManagedClient.Routing.Tests;

[TestClass]
public class ManagedMqttClientOptionsBuilderTests
{
    [TestMethod]
    public void Build_WithoutClientOptions_Throws()
    {
        var builder = new ManagedMqttClientOptionsBuilder();
        Assert.ThrowsException<InvalidOperationException>(() => builder.Build());
    }

    [TestMethod]
    public void Build_WithClientOptions_ReturnsOptions()
    {
        var mqttOptions = new MqttClientOptionsBuilder().WithTcpServer("localhost").Build();
        var options = new ManagedMqttClientOptionsBuilder().WithClientOptions(mqttOptions).Build();

        Assert.AreSame(mqttOptions, options.ClientOptions);
    }

    [TestMethod]
    public void Build_WithClientOptionsBuilder_CreatesClientOptions()
    {
        var options = new ManagedMqttClientOptionsBuilder()
            .WithClientOptions(b => b.WithTcpServer("localhost"))
            .Build();

        Assert.IsNotNull(options.ClientOptions);
    }
}


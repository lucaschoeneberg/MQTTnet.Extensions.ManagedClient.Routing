using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MQTTnet.Extensions.ManagedClient.Routing.Routing;

namespace MQTTnet.Extensions.ManagedClient.Routing.Tests;

[TestClass]
public class MqttRouteTableFactoryCachingTests
{
    [TestMethod]
    public void Create_ReturnsSameInstance_ForSameAssemblies()
    {
        var asm = new[] { typeof(MqttRouteTableFactoryCachingTests).Assembly };
        var table1 = MqttRouteTableFactory.Create(asm);
        var table2 = MqttRouteTableFactory.Create(new[] { typeof(MqttRouteTableFactoryCachingTests).Assembly });

        Assert.AreSame(table1, table2);
    }

    [TestMethod]
    public void Create_ReturnsDifferentInstances_ForDifferentAssemblies()
    {
        var table1 = MqttRouteTableFactory.Create(new[] { typeof(MqttRouteTableFactoryCachingTests).Assembly });
        var table2 = MqttRouteTableFactory.Create(new[] { typeof(System.Net.Http.HttpClient).Assembly });

        Assert.AreNotSame(table1, table2);
    }
}

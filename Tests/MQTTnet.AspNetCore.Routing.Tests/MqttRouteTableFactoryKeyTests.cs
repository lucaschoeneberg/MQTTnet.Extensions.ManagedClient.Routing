using System;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MQTTnet.Extensions.ManagedClient.Routing.Routing;

namespace MQTTnet.AspNetCore.Routing.Tests;

[TestClass]
public class MqttRouteTableFactoryKeyTests
{
    private static Type GetKeyType()
    {
        return typeof(MqttRouteTableFactory).GetNestedType("Key", BindingFlags.NonPublic);
    }

    private static object CreateKey(Assembly[] assemblies)
    {
        var keyType = GetKeyType();
        var ctor = keyType.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null, [typeof(Assembly[])], null);
        return ctor.Invoke([assemblies]);
    }

    private static bool InvokeEquals(object key1, object key2)
    {
        var keyType = GetKeyType();
        var method = keyType.GetMethod("Equals", new[] { keyType });
        return (bool)method.Invoke(key1, new[] { key2 });
    }

    [TestMethod]
    public void Equals_ReturnsTrue_ForSameAssemblies()
    {
        var asm = new[] { typeof(MqttRouteTableFactoryKeyTests).Assembly };
        var key1 = CreateKey(asm);
        var key2 = CreateKey(new[] { typeof(MqttRouteTableFactoryKeyTests).Assembly });

        Assert.IsTrue(InvokeEquals(key1, key2));
    }

    [TestMethod]
    public void Equals_ReturnsFalse_ForDifferentAssemblies()
    {
        var key1 = CreateKey([typeof(string).Assembly]);
        var key2 = CreateKey([typeof(int).Assembly]);

        Assert.IsFalse(InvokeEquals(key1, key2));
    }
}
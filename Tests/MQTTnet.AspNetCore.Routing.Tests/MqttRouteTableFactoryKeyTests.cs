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
        var key1 = CreateKey([typeof(MqttRouteTableFactoryKeyTests).Assembly]);
        var key2 = CreateKey([typeof(System.Net.Http.HttpClient).Assembly]);

        Assert.IsFalse(InvokeEquals(key1, key2));
    }

    [TestMethod]
    public void Test_Key_Equals_Implementation()
    {
        var assembly1 = typeof(MqttRouteTableFactoryKeyTests).Assembly;
        var assembly2 = typeof(System.Xml.XmlDocument).Assembly;

        var keyType = GetKeyType();
        var ctor = keyType.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null,
            [typeof(Assembly[])], null);

        Assert.IsNotNull(ctor);

        var key1 = ctor.Invoke([new[] { assembly1 }]);
        var key2 = ctor.Invoke([new[] { assembly2 }]);

        // Aufruf der Equals-Methode über Reflection
        var equalsMethod = keyType.GetMethod("Equals", [typeof(object)]);
        var result = equalsMethod != null && (bool)equalsMethod.Invoke(key1, [key2])!;

        Assert.IsFalse(result, "Keys mit unterschiedlichen Assemblies sollten nicht gleich sein");
    }
}
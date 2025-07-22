using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MQTTnet.Extensions.ManagedClient.Routing.Routing;

namespace MQTTnet.AspNetCore.Routing.Tests;

[TestClass]
public class ReplaceTokensTests
{
    private static MethodInfo GetReplaceTokensMethod()
    {
        return typeof(MqttRouteTableFactory).GetMethod("ReplaceTokens", BindingFlags.NonPublic | BindingFlags.Static);
    }

    [TestMethod]
    public void ReplaceTokens_EscapedTokensPreserved()
    {
        var method = GetReplaceTokensMethod();
        var result = (string)method.Invoke(null, ["api/[[controller]]/[[action]]", "FooController", "Bar"]);
        Assert.AreEqual("api/[controller]/[action]", result);
    }

    [TestMethod]
    public void ReplaceTokens_MixedTokensReplaced()
    {
        var method = GetReplaceTokensMethod();
        var result = (string)method.Invoke(null, ["api/[[controller]]/[action]", "SampleController", "Index"]);
        Assert.AreEqual("api/[controller]/Index", result);
    }
}
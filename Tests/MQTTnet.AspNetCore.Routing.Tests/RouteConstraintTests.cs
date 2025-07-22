using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MQTTnet.Extensions.ManagedClient.Routing.Constraints;

namespace MQTTnet.AspNetCore.Routing.Tests;

[TestClass]
public class RouteConstraintTests
{
    [TestMethod]
    public void IntConstraint_Match_ValidAndInvalid()
    {
        var constraint = RouteConstraint.Parse("template", "id:int", "int");

        Assert.IsTrue(constraint.Match("42", out var value));
        Assert.AreEqual(42, value);

        Assert.IsFalse(constraint.Match("foo", out value));
        Assert.IsNull(value);
    }

    [TestMethod]
    public void FloatConstraint_Match_ValidAndInvalid()
    {
        var constraint = RouteConstraint.Parse("template", "value:float", "float");

        Assert.IsTrue(constraint.Match("3.14", out var value));
        Assert.AreEqual(3.14f, (float)value, 0.0001f);

        Assert.IsFalse(constraint.Match("bar", out value));
        Assert.IsNull(value);
    }

    [TestMethod]
    public void GuidConstraint_Match_ValidAndInvalid()
    {
        var constraint = RouteConstraint.Parse("template", "id:guid", "guid");
        var guid = Guid.NewGuid();

        Assert.IsTrue(constraint.Match(guid.ToString(), out var value));
        Assert.AreEqual(guid, value);

        Assert.IsFalse(constraint.Match("not-a-guid", out value));
        Assert.IsNull(value);
    }
}

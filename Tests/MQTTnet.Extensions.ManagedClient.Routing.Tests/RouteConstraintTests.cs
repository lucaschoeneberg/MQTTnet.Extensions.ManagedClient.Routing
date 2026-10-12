using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MQTTnet.Extensions.ManagedClient.Routing.Constraints;

namespace MQTTnet.Extensions.ManagedClient.Routing.Tests;

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
        Assert.AreEqual(3.14f, (float)value!, 0.0001f);

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

    [TestMethod]
    public void BoolConstraint_Match_ValidAndInvalid()
    {
        var constraint = RouteConstraint.Parse("template", "flag:bool", "bool");

        Assert.IsTrue(constraint.Match("True", out var value));
        Assert.AreEqual(true, value);

        Assert.IsFalse(constraint.Match("yes", out value));
        Assert.IsNull(value);
    }

    [TestMethod]
    public void OptionalIntConstraint_Match_MissingValidAndInvalid()
    {
        var constraint = RouteConstraint.Parse("template", "id:int?", "int?");

        Assert.IsTrue(constraint.Match(null, out var value));
        Assert.IsNull(value);

        Assert.IsTrue(constraint.Match("5", out value));
        Assert.AreEqual(5, value);

        Assert.IsFalse(constraint.Match("x", out value));
        Assert.IsNull(value);
    }

    [TestMethod]
    public void NumericConstraints_Match_ValidValues()
    {
        Assert.AreEqual(5L, RouteConstraint.Parse("t", "v:long", "long").Match("5", out var longValue) ? longValue : null);
        Assert.AreEqual(3.14, RouteConstraint.Parse("t", "v:double", "double").Match("3.14", out var doubleValue) ? doubleValue : null);
        Assert.AreEqual(1.5m, RouteConstraint.Parse("t", "v:decimal", "decimal").Match("1.5", out var decimalValue) ? decimalValue : null);
    }

    [TestMethod]
    public void DateTimeConstraint_Match_ValidAndInvalid()
    {
        var constraint = RouteConstraint.Parse("template", "when:datetime", "datetime");

        Assert.IsTrue(constraint.Match("2026-10-11", out _));
        Assert.IsFalse(constraint.Match("not-a-date", out var value));
        Assert.IsNull(value);
    }

    [TestMethod]
    public void Parse_UnsupportedOrEmpty_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => RouteConstraint.Parse("template", "s", "bogus"));
        Assert.ThrowsExactly<ArgumentException>(() => RouteConstraint.Parse("template", "s", ""));
    }
}

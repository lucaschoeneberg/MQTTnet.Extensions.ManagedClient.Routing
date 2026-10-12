using Microsoft.VisualStudio.TestTools.UnitTesting;
using MQTTnet.Extensions.ManagedClient.Routing.Templates;

namespace MQTTnet.Extensions.ManagedClient.Routing.Tests;

[TestClass]
public class TemplateSegmentTests
{
    [TestMethod]
    public void Literal_Matches_CaseInsensitive()
    {
        var segment = new TemplateSegment("t", "Cool", false);

        Assert.IsTrue(segment.Match("cool", out var value));
        Assert.IsNull(value);
        Assert.IsTrue(segment.Match("COOL", out _));
        Assert.IsFalse(segment.Match("other", out _));
    }

    [TestMethod]
    public void Parameter_Captures_Raw_Value()
    {
        var segment = new TemplateSegment("t", "id", true);

        Assert.IsTrue(segment.Match("42", out var value));
        Assert.AreEqual("42", value);
    }

    [TestMethod]
    public void Constraint_Mismatch_Does_Not_Match()
    {
        var segment = new TemplateSegment("t", "id:int", true);

        Assert.IsFalse(segment.Match("nope", out var value));
        Assert.IsNull(value);
        Assert.IsTrue(segment.Match("7", out value));
        Assert.AreEqual(7, value);
    }

    [TestMethod]
    public void Misplaced_Optional_Marker_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new TemplateSegment("t", "a?b", true));
    }

    [TestMethod]
    public void Multiple_CatchAll_Markers_Throw()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => new TemplateSegment("t", "*a*b", true));
    }

    [TestMethod]
    public void Equals_Compares_Shape_And_Constraints()
    {
        var left = new TemplateSegment("t", "id:int", true);
        var same = new TemplateSegment("other", "id:int", true);
        var differentConstraint = new TemplateSegment("t", "id:long", true);
        var literal = new TemplateSegment("t", "id", false);

        Assert.IsTrue(left.Equals(same));
        Assert.AreEqual(left.GetHashCode(), same.GetHashCode());
        Assert.IsFalse(left.Equals(differentConstraint));
        Assert.IsFalse(left.Equals(literal));
        Assert.IsFalse(left.Equals(null));
        Assert.IsFalse(left.Equals("id:int"));
        Assert.IsTrue(left.Equals((object)same));
    }
}

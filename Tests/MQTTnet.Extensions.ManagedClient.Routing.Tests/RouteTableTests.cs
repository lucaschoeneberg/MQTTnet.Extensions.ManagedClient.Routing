﻿using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MQTTnet.Extensions.ManagedClient.Routing.Routing;
using MQTTnet.Extensions.ManagedClient.Routing.Templates;

namespace MQTTnet.Extensions.ManagedClient.Routing.Tests;

[TestClass]
public class RouteTableTests
{
    [TestMethod]
    public void Route_Constructor()
    {
        // Arrange
        var routes = new[]
        {
            "super/awesome",
            "super/cool",
            "other/route"
        };

        var MockRoutes = new[]
        {
            new MqttRoute(
                new RouteTemplate(routes[0], new List<TemplateSegment>
                {
                    new(routes[0], "super", false),
                    new(routes[0], "awesome", false)
                }), null, new string[] { }),
            new MqttRoute(
                new RouteTemplate(routes[1], new List<TemplateSegment>
                {
                    new(routes[1], "super", false),
                    new(routes[1], "cool", false)
                }), null, new string[] { }),
            new MqttRoute(
                new RouteTemplate(routes[2], new List<TemplateSegment>
                {
                    new(routes[2], "other", false),
                    new(routes[2], "route", false)
                }), null, new string[] { })
        };

        // Act
        var MockTable = new MqttRouteTable(MockRoutes);

        // Assert
        CollectionAssert.AreEqual(MockRoutes, MockTable.Routes);
    }

    [TestMethod]
    public void Route_Match()
    {
        // Arrange
        var routes = new[]
        {
            "super/awesome",
            "super/cool",
            "other/route"
        };

        var mockMethod = Type.GetType("MQTTnet.Extensions.ManagedClient.Routing.Tests.RouteTableTests")?.GetMethod("Route_Match");
        var mockMethod2 = Type.GetType("MQTTnet.Extensions.ManagedClient.Routing.Tests.RouteTableTests")
            ?.GetMethod("Route_Constructor");
        var mockRoutes = new[]
        {
            new MqttRoute(
                new RouteTemplate(routes[0], new List<TemplateSegment>
                {
                    new(routes[0], "super", false),
                    new(routes[0], "awesome", false)
                }), mockMethod, new string[] { }),
            new MqttRoute(
                new RouteTemplate(routes[1], new List<TemplateSegment>
                {
                    new(routes[1], "super", false),
                    new(routes[1], "cool", false)
                }), mockMethod2, new string[] { }),
            new MqttRoute(
                new RouteTemplate(routes[2], new List<TemplateSegment>
                {
                    new(routes[2], "other", false),
                    new(routes[2], "route", false)
                }), mockMethod2, new string[] { })
        };
        var context = new MqttRouteContext("super/awesome");

        // Act
        var MockTable = new MqttRouteTable(mockRoutes);

        MockTable.Route(context);

        // Assert
        Assert.IsNotNull(mockMethod);
        Assert.IsNotNull(mockMethod2);
        Assert.AreNotSame(mockMethod, mockMethod2);
        Assert.AreSame(context.Handler, mockMethod);
    }

    [TestMethod]
    public void Route_MatchWildcard()
    {
        // Arrange
        var routes = new[]
        {
            "{*path}",
            "super/cool",
            "other/route"
        };

        var MockMethod = Type.GetType("MQTTnet.Extensions.ManagedClient.Routing.Tests.RouteTableTests").GetMethod("Route_Match");
        var MockMethod2 = Type.GetType("MQTTnet.Extensions.ManagedClient.Routing.Tests.RouteTableTests")
            .GetMethod("Route_Constructor");
        var MockRoutes = new[]
        {
            new MqttRoute(
                new RouteTemplate(routes[0], new List<TemplateSegment>
                {
                    new(routes[0], "*path", true)
                }), MockMethod, new string[] { }),
            new MqttRoute(
                new RouteTemplate(routes[1], new List<TemplateSegment>
                {
                    new(routes[1], "super", false),
                    new(routes[1], "cool", false)
                }), MockMethod2, new string[] { }),
            new MqttRoute(
                new RouteTemplate(routes[2], new List<TemplateSegment>
                {
                    new(routes[2], "other", false),
                    new(routes[2], "route", false)
                }), MockMethod2, new string[] { })
        };

        var context = new MqttRouteContext("super/duper");

        // Act
        var MockTable = new MqttRouteTable(MockRoutes);

        MockTable.Route(context);

        // Assert
        Assert.IsNotNull(MockMethod);
        Assert.IsNotNull(MockMethod2);
        Assert.AreNotSame(MockMethod, MockMethod2);
        Assert.AreSame(context.Handler, MockMethod);
    }

    [TestMethod]
    public void Route_MatchWildcardOrder()
    {
        // Arrange
        var routes = new[]
        {
            "{*path}",
            "super/cool",
            "other/route"
        };

        var MockMethod = Type.GetType("MQTTnet.Extensions.ManagedClient.Routing.Tests.RouteTableTests").GetMethod("Route_Match");
        var MockMethod2 = Type.GetType("MQTTnet.Extensions.ManagedClient.Routing.Tests.RouteTableTests")
            .GetMethod("Route_Constructor");
        var MockRoutes = new[]
        {
            new MqttRoute(
                new RouteTemplate(routes[1], new List<TemplateSegment>
                {
                    new(routes[1], "super", false),
                    new(routes[1], "cool", false)
                }), MockMethod, new string[] { }),
            new MqttRoute(
                new RouteTemplate(routes[2], new List<TemplateSegment>
                {
                    new(routes[2], "other", false),
                    new(routes[2], "route", false)
                }), MockMethod2, new string[] { }),
            new MqttRoute(
                new RouteTemplate(routes[0], new List<TemplateSegment>
                {
                    new(routes[0], "*path", true)
                }), MockMethod2, new string[] { })
        };

        var context = new MqttRouteContext("super/cool");

        // Act
        var MockTable = new MqttRouteTable(MockRoutes);

        MockTable.Route(context);

        // Assert
        Assert.IsNotNull(MockMethod);
        Assert.IsNotNull(MockMethod2);
        Assert.AreNotSame(MockMethod, MockMethod2);
        Assert.AreSame(context.Handler, MockMethod);
    }

    [TestMethod]
    public void Route_Miss()
    {
        // Arrange
        var routes = new[]
        {
            "super/awesome",
            "super/cool",
            "other/route"
        };

        var MockMethod = Type.GetType("MQTTnet.Extensions.ManagedClient.Routing.Tests.RouteTableTests").GetMethod("Route_Match");
        var MockMethod2 = Type.GetType("MQTTnet.Extensions.ManagedClient.Routing.Tests.RouteTableTests")
            .GetMethod("Route_Constructor");
        var MockRoutes = new[]
        {
            new MqttRoute(
                new RouteTemplate(routes[0], new List<TemplateSegment>
                {
                    new(routes[0], "super", false),
                    new(routes[0], "awesome", false)
                }), MockMethod, new string[] { }),
            new MqttRoute(
                new RouteTemplate(routes[1], new List<TemplateSegment>
                {
                    new(routes[1], "super", false),
                    new(routes[1], "cool", false)
                }), MockMethod2, new string[] { }),
            new MqttRoute(
                new RouteTemplate(routes[2], new List<TemplateSegment>
                {
                    new(routes[2], "other", false),
                    new(routes[2], "route", false)
                }), MockMethod2, new string[] { })
        };
        var context = new MqttRouteContext("super/miss");

        // Act
        var MockTable = new MqttRouteTable(MockRoutes);

        MockTable.Route(context);

        // Assert
        Assert.IsNotNull(MockMethod);
        Assert.IsNotNull(MockMethod2);
        Assert.AreNotSame(MockMethod, MockMethod2);
        Assert.IsNull(context.Handler);
    }
}
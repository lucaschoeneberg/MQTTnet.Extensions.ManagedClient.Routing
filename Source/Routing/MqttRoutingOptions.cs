using MQTTnet.AspNetCore.Routing.Routing;
using System;
using System.Reflection;
using System.Text.Json;

namespace MQTTnet.AspNetCore.Routing;

public class MqttRoutingOptions
{
    public JsonSerializerOptions SerializerOptions { get;internal set; }
    public Assembly[] FromAssemblies { get; internal set; }
    public Type RouteInvocationInterceptor { get; internal set; }
}
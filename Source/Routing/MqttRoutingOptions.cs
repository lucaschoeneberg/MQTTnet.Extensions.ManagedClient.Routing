using System.Reflection;
using System.Text.Json;

namespace MQTTnet.Extensions.ManagedClient.Routing.Routing;

public class MqttRoutingOptions
{
    public JsonSerializerOptions? SerializerOptions { get; internal set; }
    public Assembly[]? FromAssemblies { get; internal set; }
    public Type? RouteInvocationInterceptor { get; internal set; }

    /// <summary>
    /// Execution deadline for a controller action, including synchronous work.
    /// Must be positive and at most 4,294,967,294 milliseconds (the .NET timer
    /// maximum) when set; null (default) disables the deadline. Actions
    /// receive a token canceled at the deadline, connection closure, or routing
    /// shutdown, and canceled processing is marked failed.
    /// Cancellation is cooperative: the scope and concurrency slot are retained
    /// until the action actually terminates, even if it ignores the token.
    /// </summary>
    public TimeSpan? MaxActionExecutionTime { get; internal set; }

    /// <summary>
    /// Maximum number of messages dispatched concurrently. 1 (default)
    /// preserves message order; higher values allow independent messages to
    /// overlap while routing keeps acknowledgements in receive order. Must be
    /// at least 1. Routing acknowledges successful messages only after the action and interceptor
    /// finish, unless automatic acknowledgement was already disabled by the caller.
    /// </summary>
    public int MaxConcurrentDispatches { get; internal set; } = 1;
}

using System.Reflection;
using System.Text.Json;

namespace MQTTnet.Extensions.ManagedClient.Routing.Routing;

public class MqttRoutingOptions
{
    public JsonSerializerOptions? SerializerOptions { get; internal set; }
    public Assembly[]? FromAssemblies { get; internal set; }
    public Type? RouteInvocationInterceptor { get; internal set; }

    /// <summary>
    /// Maximum time a controller action may run. When set, actions receive a
    /// cancellable token and overruns mark the message as failed instead of
    /// blocking dispatch forever. Null (default) means no limit.
    /// </summary>
    public TimeSpan? MaxActionExecutionTime { get; internal set; }

    /// <summary>
    /// Maximum number of messages dispatched concurrently. 1 (default)
    /// preserves message order and acknowledgement semantics; higher values
    /// trade ordering for throughput (independent messages only).
    /// </summary>
    public int MaxConcurrentDispatches { get; internal set; } = 1;
}

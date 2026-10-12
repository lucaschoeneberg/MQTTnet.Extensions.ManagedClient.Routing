using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Extensions.ManagedClient.Routing.Routing;

namespace MqttRoutingSample.Infrastructure;

public sealed class LoggingInterceptor(ILogger<LoggingInterceptor> logger) : IRouteInvocationInterceptor
{
    public Task<object?> RouteExecuting(MqttApplicationMessageReceivedEventArgs messageReceivedEventArgs)
    {
        logger.LogDebug("Routing message on {Topic}", messageReceivedEventArgs.ApplicationMessage.Topic);
        return Task.FromResult<object?>(null);
    }

    public Task RouteExecuted(object? correlationObject,
        MqttApplicationMessageReceivedEventArgs messageReceivedEventArgs, Exception? ex)
    {
        if (ex == null)
        {
            logger.LogDebug("Routed message on {Topic}", messageReceivedEventArgs.ApplicationMessage.Topic);
        }
        else
        {
            logger.LogWarning(ex, "Routing failed on {Topic}", messageReceivedEventArgs.ApplicationMessage.Topic);
        }

        return Task.CompletedTask;
    }
}

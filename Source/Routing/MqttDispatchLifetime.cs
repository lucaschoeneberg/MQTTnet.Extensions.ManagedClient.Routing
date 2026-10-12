namespace MQTTnet.Extensions.ManagedClient.Routing.Routing;

internal readonly record struct MqttDispatchLifetime(
    object Owner,
    Func<IDisposable> EnterCallbackScope,
    CancellationToken ConnectionToken);

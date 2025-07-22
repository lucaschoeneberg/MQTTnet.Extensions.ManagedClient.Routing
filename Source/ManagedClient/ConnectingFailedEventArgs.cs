using System;

namespace MQTTnet.Extensions.ManagedClient.Routing.ManagedClient
{
    public sealed class ConnectingFailedEventArgs(MqttClientConnectResult connectResult, Exception exception)
        : EventArgs
    {
        /// <summary>
        /// This is null when the connection was failing and the server was not reachable.
        /// </summary>
        public MqttClientConnectResult ConnectResult { get; } = connectResult;

        public Exception Exception { get; } = exception;
    }
}
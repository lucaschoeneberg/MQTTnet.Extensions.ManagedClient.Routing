// Vendored MQTTnet ManagedClient fork - excluded from nullable analysis (see Directory.Build.props).
#nullable disable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet.Packets;

namespace MQTTnet.Extensions.ManagedClient.Routing.ManagedClient
{
    public interface IManagedMqttClient : IDisposable
    {
        event Func<ApplicationMessageProcessedEventArgs, Task> ApplicationMessageProcessedAsync;

        event Func<MqttApplicationMessageReceivedEventArgs, Task> ApplicationMessageReceivedAsync;

        event Func<ApplicationMessageSkippedEventArgs, Task> ApplicationMessageSkippedAsync;

        event Func<MqttClientConnectedEventArgs, Task> ConnectedAsync;

        event Func<ConnectingFailedEventArgs, Task> ConnectingFailedAsync;

        event Func<EventArgs, Task> ConnectionStateChangedAsync;

        event Func<MqttClientDisconnectedEventArgs, Task> DisconnectedAsync;

        event Func<ManagedProcessFailedEventArgs, Task> SynchronizingSubscriptionsFailedAsync;

        event Func<SubscriptionsChangedEventArgs, Task> SubscriptionsChangedAsync;

        IMqttClient InternalClient { get; }

        bool IsConnected { get; }

        bool IsStarted { get; }

        ManagedMqttClientOptions Options { get; }

        int PendingApplicationMessagesCount { get; }

        Task EnqueueAsync(MqttApplicationMessage applicationMessage);

        Task EnqueueAsync(ManagedMqttApplicationMessage applicationMessage);

        Task PingAsync(CancellationToken cancellationToken = default);

        /// <summary>Starts a new managed session and resets its reconnect backoff.</summary>
        /// <param name="options">Client options with MaxReconnectDelay between one second and the supported timer maximum (4,294,967,294 milliseconds).</param>
        /// <exception cref="ArgumentException">Client options are missing or MaxReconnectDelay is outside its supported range.</exception>
        /// <exception cref="InvalidOperationException">The client is already started.</exception>
        Task StartAsync(ManagedMqttClientOptions options);

        /// <summary>Stops the session and drains the publishing worker before releasing its storage.</summary>
        /// <param name="cleanDisconnect">Whether to send a clean disconnect to the broker.</param>
        /// <remarks>Completion waits for in-flight publishing interceptors and storage writes. These must eventually complete.</remarks>
        Task StopAsync(bool cleanDisconnect = true);

        Task SubscribeAsync(IEnumerable<MqttTopicFilter> topicFilters);

        Task UnsubscribeAsync(IEnumerable<string> topics);
    }
}

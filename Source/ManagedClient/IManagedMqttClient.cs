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
        /// <exception cref="InvalidOperationException">The client is already started or its previous shutdown has not completed.</exception>
        Task StartAsync(ManagedMqttClientOptions options);

        /// <summary>Stops the session and drains publishing and registered routing before releasing storage.</summary>
        /// <param name="cleanDisconnect">Whether to send a clean disconnect to the broker.</param>
        /// <remarks>
        /// External callers await in-flight callbacks, routed actions, and storage writes.
        /// Actions receive connection cancellation, but shutdown still waits for them to terminate.
        /// Inside a managed-client callback or routed action, this method requests shutdown and
        /// returns without joining its own processing task. A subsequent call outside the callback
        /// awaits full completion. StartAsync is rejected while shutdown is in progress.
        /// </remarks>
        Task StopAsync(bool cleanDisconnect = true);

        Task SubscribeAsync(IEnumerable<MqttTopicFilter> topicFilters);

        Task UnsubscribeAsync(IEnumerable<string> topics);
    }
}

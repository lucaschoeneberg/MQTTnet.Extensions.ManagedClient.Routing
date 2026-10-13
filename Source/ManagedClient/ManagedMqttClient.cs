// Vendored MQTTnet ManagedClient fork - excluded from nullable analysis (see Directory.Build.props).
#nullable disable

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet.Diagnostics.Logger;
using MQTTnet.Exceptions;
using MQTTnet.Internal;
using MQTTnet.Packets;
using MQTTnet.Protocol;
using MQTTnet.Server;

namespace MQTTnet.Extensions.ManagedClient.Routing.ManagedClient
{
    public sealed class ManagedMqttClient : Disposable, IManagedMqttClient
    {
        readonly MqttNetSourceLogger _logger;
        readonly AsyncEvent<MqttApplicationMessageReceivedEventArgs> _applicationMessageReceivedEvent = new();
        readonly AsyncEvent<MqttClientConnectedEventArgs> _connectedEvent = new();
        readonly AsyncEvent<MqttClientDisconnectedEventArgs> _disconnectedEvent = new();
        readonly AsyncEvent<EventArgs> _stoppingEvent = new();
        readonly object _connectionLifetimeLock = new();
        readonly object _stopLock = new();
        readonly AsyncLocal<ClientCallbackScope> _callbackScope = new();
        CancellationTokenSource _incomingConnectionLifetime;
        Task _stopTask = Task.CompletedTask;

        sealed class ClientCallbackScope : IDisposable
        {
            readonly ManagedMqttClient _owner;
            readonly ClientCallbackScope _previous;
            public bool Active { get; private set; } = true;
            public CancellationToken? ReceiveConnectionToken { get; }

            public ClientCallbackScope(ManagedMqttClient owner, CancellationToken? receiveConnectionToken = null)
            {
                _owner = owner;
                _previous = owner._callbackScope.Value;
                ReceiveConnectionToken = receiveConnectionToken ?? _previous?.ReceiveConnectionToken;
                owner._callbackScope.Value = this;
            }

            public void Dispose()
            {
                Active = false;
                _owner._callbackScope.Value = _previous;
            }
        }

        internal IDisposable EnterCallbackScope() => new ClientCallbackScope(this);

        // Never substitute a later connection when routing resumes after another subscriber.
        internal CancellationToken IncomingMessageConnectionToken =>
            _callbackScope.Value?.ReceiveConnectionToken ?? new CancellationToken(true);

        internal event Func<EventArgs, Task> StoppingAsync
        {
            add => _stoppingEvent.AddHandler(value);
            remove => _stoppingEvent.RemoveHandler(value);
        }

        readonly AsyncEvent<InterceptingPublishMessageEventArgs> _interceptingPublishMessageEvent = new();
        readonly AsyncEvent<ApplicationMessageProcessedEventArgs> _applicationMessageProcessedEvent = new();
        readonly AsyncEvent<ApplicationMessageSkippedEventArgs> _applicationMessageSkippedEvent = new();
        readonly AsyncEvent<ConnectingFailedEventArgs> _connectingFailedEvent = new();
        readonly AsyncEvent<EventArgs> _connectionStateChangedEvent = new();
        readonly AsyncEvent<ManagedProcessFailedEventArgs> _synchronizingSubscriptionsFailedEvent = new();
        readonly AsyncEvent<SubscriptionsChangedEventArgs> _subscriptionsChangedEvent = new();

        readonly BlockingQueue<ManagedMqttApplicationMessage> _messageQueue = new();
        readonly AsyncLock _messageQueueLock = new();

        /// <summary>
        ///     The subscriptions are managed in 2 separate buckets:
        ///     <see
        ///         cref="_subscriptions" />
        ///     and
        ///     <see
        ///         cref="_unsubscriptions" />
        ///     are processed during normal operation
        ///     and are moved to the
        ///     <see
        ///         cref="_reconnectSubscriptions" />
        ///     when they get processed. They can be accessed by
        ///     any thread and are therefore mutex'ed.
        ///     <see
        ///         cref="_reconnectSubscriptions" />
        ///     get sent to the broker
        ///     at reconnect and are solely owned by
        ///     <see
        ///         cref="MaintainConnectionAsync" />
        ///     .
        /// </summary>
        readonly Dictionary<string, MqttTopicFilter> _reconnectSubscriptions = new();

        readonly Dictionary<string, MqttTopicFilter> _subscriptions = new();
        readonly SemaphoreSlim _subscriptionsQueuedSignal = new(0);
        readonly HashSet<string> _unsubscriptions = new();

        CancellationTokenSource _connectionCancellationToken;
        Task _maintainConnectionTask;
        Task _publishingTask;
        CancellationTokenSource _publishingCancellationToken;

        int _reconnectFailureCount;

        ManagedMqttClientStorageManager _storageManager;
        bool _isCleanDisconnect;

        public ManagedMqttClient(IMqttClient mqttClient, IMqttNetLogger logger)
        {
            InternalClient = mqttClient ?? throw new ArgumentNullException(nameof(mqttClient));

            ArgumentNullException.ThrowIfNull(logger);

            _logger = logger.WithSource(nameof(ManagedMqttClient));
            InternalClient.ConnectingAsync += OnConnectingAsync;
            InternalClient.ConnectedAsync += OnConnectedAsync;
            InternalClient.DisconnectedAsync += OnDisconnectedAsync;
            InternalClient.ApplicationMessageReceivedAsync += OnApplicationMessageReceivedAsync;
        }

        Task OnConnectingAsync(MqttClientConnectingEventArgs args)
        {
            lock (_connectionLifetimeLock)
            {
                CancelIncomingConnection();
                _incomingConnectionLifetime?.Dispose();
                _incomingConnectionLifetime = new CancellationTokenSource();
            }

            return Task.CompletedTask;
        }

        Task OnConnectedAsync(MqttClientConnectedEventArgs args) => InvokeClientCallbackAsync(_connectedEvent, args);

        Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
        {
            CancelIncomingConnection();
            return InvokeClientCallbackAsync(_disconnectedEvent, args);
        }

        async Task OnApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
        {
            CancellationToken connectionToken;
            lock (_connectionLifetimeLock)
            {
                connectionToken = _incomingConnectionLifetime?.Token ?? new CancellationToken(true);
            }

            using (new ClientCallbackScope(this, connectionToken))
            {
                await _applicationMessageReceivedEvent.InvokeAsync(args).ConfigureAwait(false);
            }
        }

        async Task InvokeClientCallbackAsync<T>(AsyncEvent<T> clientEvent, T args) where T : EventArgs
        {
            using (EnterCallbackScope())
            {
                await clientEvent.InvokeAsync(args).ConfigureAwait(false);
            }
        }

        void CancelIncomingConnection()
        {
            lock (_connectionLifetimeLock)
            {
                try
                {
                    _incomingConnectionLifetime?.Cancel();
                }
                catch (AggregateException ex)
                {
                    _logger.Error(ex, "MQTT action cancellation callback failed.");
                }
            }
        }

        public event Func<ApplicationMessageSkippedEventArgs, Task> ApplicationMessageSkippedAsync
        {
            add => _applicationMessageSkippedEvent.AddHandler(value);
            remove => _applicationMessageSkippedEvent.RemoveHandler(value);
        }

        public event Func<ApplicationMessageProcessedEventArgs, Task> ApplicationMessageProcessedAsync
        {
            add => _applicationMessageProcessedEvent.AddHandler(value);
            remove => _applicationMessageProcessedEvent.RemoveHandler(value);
        }

        public event Func<InterceptingPublishMessageEventArgs, Task> InterceptPublishMessageAsync
        {
            add => _interceptingPublishMessageEvent.AddHandler(value);
            remove => _interceptingPublishMessageEvent.RemoveHandler(value);
        }

        public event Func<MqttApplicationMessageReceivedEventArgs, Task> ApplicationMessageReceivedAsync
        {
            add => _applicationMessageReceivedEvent.AddHandler(value);
            remove => _applicationMessageReceivedEvent.RemoveHandler(value);
        }

        public event Func<MqttClientConnectedEventArgs, Task> ConnectedAsync
        {
            add => _connectedEvent.AddHandler(value);
            remove => _connectedEvent.RemoveHandler(value);
        }

        public event Func<ConnectingFailedEventArgs, Task> ConnectingFailedAsync
        {
            add => _connectingFailedEvent.AddHandler(value);
            remove => _connectingFailedEvent.RemoveHandler(value);
        }

        public event Func<EventArgs, Task> ConnectionStateChangedAsync
        {
            add => _connectionStateChangedEvent.AddHandler(value);
            remove => _connectionStateChangedEvent.RemoveHandler(value);
        }

        public event Func<MqttClientDisconnectedEventArgs, Task> DisconnectedAsync
        {
            add => _disconnectedEvent.AddHandler(value);
            remove => _disconnectedEvent.RemoveHandler(value);
        }

        public event Func<ManagedProcessFailedEventArgs, Task> SynchronizingSubscriptionsFailedAsync
        {
            add => _synchronizingSubscriptionsFailedEvent.AddHandler(value);
            remove => _synchronizingSubscriptionsFailedEvent.RemoveHandler(value);
        }

        public event Func<SubscriptionsChangedEventArgs, Task> SubscriptionsChangedAsync
        {
            add => _subscriptionsChangedEvent.AddHandler(value);
            remove => _subscriptionsChangedEvent.RemoveHandler(value);
        }

        public IMqttClient InternalClient { get; }

        public bool IsConnected => InternalClient.IsConnected;

        public bool IsStarted => _connectionCancellationToken != null;

        public ManagedMqttClientOptions Options { get; private set; }

        public int PendingApplicationMessagesCount => _messageQueue.Count;

        public async Task EnqueueAsync(MqttApplicationMessage applicationMessage)
        {
            ThrowIfDisposed();

            ArgumentNullException.ThrowIfNull(applicationMessage);

            var managedMqttApplicationMessage = new ManagedMqttApplicationMessageBuilder().WithApplicationMessage(applicationMessage);
            await EnqueueAsync(managedMqttApplicationMessage.Build()).ConfigureAwait(false);
        }

        public async Task EnqueueAsync(ManagedMqttApplicationMessage applicationMessage)
        {
            ThrowIfDisposed();

            ArgumentNullException.ThrowIfNull(applicationMessage);

            if (Options == null)
            {
                throw new InvalidOperationException("call StartAsync before publishing messages");
            }

            MqttTopicValidator.ThrowIfInvalid(applicationMessage.ApplicationMessage);

            ManagedMqttApplicationMessage removedMessage = null;
            ApplicationMessageSkippedEventArgs applicationMessageSkippedEventArgs = null;

            try
            {
                using (await _messageQueueLock.EnterAsync().ConfigureAwait(false))
                {
                    if (_messageQueue.Count >= Options.MaxPendingMessages)
                    {
                        switch (Options.PendingMessagesOverflowStrategy)
                        {
                            case MqttPendingMessagesOverflowStrategy.DropNewMessage:
                                _logger.Verbose("Skipping publish of new application message because internal queue is full.");
                                applicationMessageSkippedEventArgs = new ApplicationMessageSkippedEventArgs(applicationMessage);
                                return;
                            case MqttPendingMessagesOverflowStrategy.DropOldestQueuedMessage:
                                removedMessage = _messageQueue.RemoveFirst();
                                _logger.Verbose("Removed oldest application message from internal queue because it is full.");
                                applicationMessageSkippedEventArgs = new ApplicationMessageSkippedEventArgs(removedMessage);
                                break;
                        }
                    }

                    _messageQueue.Enqueue(applicationMessage);

                    if (_storageManager != null)
                    {
                        if (removedMessage != null)
                        {
                            await _storageManager.RemoveAsync(removedMessage).ConfigureAwait(false);
                        }

                        await _storageManager.AddAsync(applicationMessage).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                if (applicationMessageSkippedEventArgs != null && _applicationMessageSkippedEvent.HasHandlers)
                {
                    await _applicationMessageSkippedEvent.InvokeAsync(applicationMessageSkippedEventArgs).ConfigureAwait(false);
                }
            }
        }

        public Task PingAsync(CancellationToken cancellationToken = default)
        {
            return InternalClient.PingAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task StartAsync(ManagedMqttClientOptions options)
        {
            ThrowIfDisposed();

            ArgumentNullException.ThrowIfNull(options);

            if (options.ClientOptions == null)
            {
                throw new ArgumentException("The client options are not set.", nameof(options));
            }

            if (!_stopTask.IsCompleted || (!_maintainConnectionTask?.IsCompleted ?? false))
            {
                throw new InvalidOperationException("The managed client is already started or is still stopping.");
            }

            if (options.MaxReconnectDelay < TimeSpan.FromSeconds(1) ||
                options.MaxReconnectDelay.TotalMilliseconds > uint.MaxValue - 1)
            {
                throw new ArgumentException("MaxReconnectDelay must be at least one second and within the supported timer range.", nameof(options));
            }

            _reconnectFailureCount = 0;
            Options = options;

            if (options.Storage != null)
            {
                _storageManager = new ManagedMqttClientStorageManager(options.Storage);
                var messages = await _storageManager.LoadQueuedMessagesAsync().ConfigureAwait(false);

                foreach (var message in messages)
                {
                    _messageQueue.Enqueue(message);
                }
            }

            var cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = cancellationTokenSource.Token;
            _connectionCancellationToken = cancellationTokenSource;

            _maintainConnectionTask = Task.Run(() => MaintainConnectionAsync(cancellationToken), cancellationToken);
            _maintainConnectionTask.RunInBackground(_logger);

            _logger.Info("Started");
        }

        /// <inheritdoc />
        public Task StopAsync(bool cleanDisconnect = true)
        {
            ThrowIfDisposed();
            var stopped = RequestStop(cleanDisconnect);
            // A callback cannot join the worker which is awaiting that callback.
            // Signal shutdown now; external callers still await full completion.
            return _callbackScope.Value?.Active == true ? Task.CompletedTask : stopped;
        }

        Task RequestStop(bool cleanDisconnect)
        {
            TaskCompletionSource completion;
            lock (_stopLock)
            {
                if (!_stopTask.IsCompleted)
                {
                    return _stopTask;
                }

                completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _stopTask = completion.Task;
                _stopTask.RunInBackground(_logger);
                _isCleanDisconnect = cleanDisconnect;
            }

            CancelPublishing();
            StopMaintainingConnection();
            CancelIncomingConnection();
            _ = Task.Run(() => CompleteStopAsync(completion));
            return completion.Task;
        }

        async Task CompleteStopAsync(TaskCompletionSource completion)
        {
            try
            {
                if (_maintainConnectionTask != null)
                {
                    await Task.WhenAny(_maintainConnectionTask).ConfigureAwait(false);
                    _maintainConnectionTask = null;
                }

                await StopPublishingAsync().ConfigureAwait(false);
                await _stoppingEvent.InvokeAsync(EventArgs.Empty).ConfigureAwait(false);
                using (await _messageQueueLock.EnterAsync().ConfigureAwait(false))
                {
                    _messageQueue.Clear();
                    _storageManager?.Dispose();
                    _storageManager = null;
                }

                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }

        public Task SubscribeAsync(IEnumerable<MqttTopicFilter> topicFilters)
        {
            ThrowIfDisposed();

            ArgumentNullException.ThrowIfNull(topicFilters);

            foreach (var topicFilter in topicFilters)
            {
                MqttTopicValidator.ThrowIfInvalidSubscribe(topicFilter.Topic);
            }

            lock (_subscriptions)
            {
                foreach (var topicFilter in topicFilters)
                {
                    _subscriptions[topicFilter.Topic] = topicFilter;
                    _unsubscriptions.Remove(topicFilter.Topic);
                }
            }

            _subscriptionsQueuedSignal.Release();

            return CompletedTask.Instance;
        }

        public Task UnsubscribeAsync(IEnumerable<string> topics)
        {
            ThrowIfDisposed();

            ArgumentNullException.ThrowIfNull(topics);

            lock (_subscriptions)
            {
                foreach (var topic in topics)
                {
                    _subscriptions.Remove(topic);
                    _unsubscriptions.Add(topic);
                }
            }

            _subscriptionsQueuedSignal.Release();

            return CompletedTask.Instance;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                RequestStop(cleanDisconnect: false).GetAwaiter().GetResult();
                lock (_connectionLifetimeLock)
                {
                    _incomingConnectionLifetime?.Dispose();
                    _incomingConnectionLifetime = null;
                }

                _messageQueue.Dispose();
                _messageQueueLock.Dispose();
                InternalClient.Dispose();
                _subscriptionsQueuedSignal.Dispose();
                _storageManager?.Dispose();
                _storageManager = null;
            }

            base.Dispose(disposing);
        }

        static TimeSpan GetRemainingTime(DateTime endTime)
        {
            var remainingTime = endTime - DateTime.UtcNow;
            return remainingTime < TimeSpan.Zero ? TimeSpan.Zero : remainingTime;
        }

        /// <summary>
        /// Exponential backoff with equal jitter: doubles <paramref name="baseDelay"/>
        /// per consecutive failure, capped at <paramref name="maxDelay"/>, then
        /// randomized into [50%, 100%] so fleet clients do not retry in lockstep.
        /// Never below one second (a zero delay would hot-spin the loop).
        /// </summary>
        internal static TimeSpan ComputeReconnectDelay(TimeSpan baseDelay, TimeSpan maxDelay, int consecutiveFailures)
        {
            var exponential = Math.ScaleB(Math.Max(baseDelay.TotalMilliseconds, 1), Math.Max(0, consecutiveFailures));
            var capped = Math.Min(exponential, Math.Max(maxDelay.TotalMilliseconds, 0));
            var jittered = capped * (0.5 + Random.Shared.NextDouble() * 0.5);
            return TimeSpan.FromMilliseconds(Math.Max(jittered, TimeSpan.FromSeconds(1).TotalMilliseconds));
        }

        CancellationTokenSource NewTimeoutToken(CancellationToken linkedToken)
        {
            var newTimeoutToken = CancellationTokenSource.CreateLinkedTokenSource(linkedToken);
            newTimeoutToken.CancelAfter(Options.ClientOptions.Timeout);
            return newTimeoutToken;
        }

        async Task HandleSubscriptionExceptionAsync(Exception exception, List<MqttTopicFilter> addedSubscriptions, List<string> removedSubscriptions)
        {
            _logger.Warning(exception, "Synchronizing subscriptions failed.");

            if (_synchronizingSubscriptionsFailedEvent.HasHandlers)
            {
                await InvokeClientCallbackAsync(_synchronizingSubscriptionsFailedEvent, new ManagedProcessFailedEventArgs(exception, addedSubscriptions, removedSubscriptions)).ConfigureAwait(false);
            }
        }

        async Task HandleSubscriptionsResultAsync(SendSubscribeUnsubscribeResult subscribeUnsubscribeResult)
        {
            if (_subscriptionsChangedEvent.HasHandlers)
            {
                await InvokeClientCallbackAsync(_subscriptionsChangedEvent, new SubscriptionsChangedEventArgs(subscribeUnsubscribeResult.SubscribeResults, subscribeUnsubscribeResult.UnsubscribeResults)).ConfigureAwait(false);
            }
        }

        async Task MaintainConnectionAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await TryMaintainConnectionAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Error exception while maintaining connection.");
            }
            finally
            {
                if (!IsDisposed)
                {
                    try
                    {
                        if (_isCleanDisconnect)
                        {
                            using var disconnectTimeout = NewTimeoutToken(CancellationToken.None);
                            await InternalClient.DisconnectAsync(new MqttClientDisconnectOptions(), disconnectTimeout.Token).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.Warning("Timeout while sending DISCONNECT packet.");
                    }
                    catch (Exception exception)
                    {
                        _logger.Error(exception, "Error while disconnecting.");
                    }

                    _logger.Info("Stopped");
                }

                _reconnectSubscriptions.Clear();

                lock (_subscriptions)
                {
                    _subscriptions.Clear();
                    _unsubscriptions.Clear();
                }
            }
        }

        async Task PublishQueuedMessagesAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && InternalClient.IsConnected)
                {
                    // Peek at the message without dequeueing in order to prevent the
                    // possibility of the queue growing beyond the configured cap.
                    // Previously, messages could be re-enqueued if there was an
                    // exception, and this re-enqueueing did not honor the cap.
                    // Furthermore, because re-enqueueing would shuffle the order
                    // of the messages, the DropOldestQueuedMessage strategy would
                    // be unable to know which message is actually the oldest and would
                    // instead drop the first item in the queue.
                    var message = _messageQueue.PeekAndWait(cancellationToken);
                    if (message == null)
                    {
                        continue;
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    await TryPublishQueuedMessageAsync(message, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Error while publishing queued application messages.");
            }
            finally
            {
                _logger.Verbose("Stopped publishing messages.");
            }
        }

        async Task PublishReconnectSubscriptionsAsync(CancellationToken cancellationToken)
        {
            _logger.Info("Publishing subscriptions at reconnect");

            List<MqttTopicFilter> topicFilters = null;

            try
            {
                if (_reconnectSubscriptions.Any())
                {
                    topicFilters = new List<MqttTopicFilter>();
                    SendSubscribeUnsubscribeResult subscribeUnsubscribeResult;

                    foreach (var sub in _reconnectSubscriptions)
                    {
                        topicFilters.Add(sub.Value);

                        if (topicFilters.Count == Options.MaxTopicFiltersInSubscribeUnsubscribePackets)
                        {
                            subscribeUnsubscribeResult = await SendSubscribeUnsubscribe(topicFilters, null, cancellationToken).ConfigureAwait(false);
                            topicFilters.Clear();
                            await HandleSubscriptionsResultAsync(subscribeUnsubscribeResult).ConfigureAwait(false);
                        }
                    }

                    subscribeUnsubscribeResult = await SendSubscribeUnsubscribe(topicFilters, null, cancellationToken).ConfigureAwait(false);
                    await HandleSubscriptionsResultAsync(subscribeUnsubscribeResult).ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                await HandleSubscriptionExceptionAsync(exception, topicFilters, null).ConfigureAwait(false);
            }
        }

        async Task PublishSubscriptionsAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            var endTime = DateTime.UtcNow + timeout;

            while (await _subscriptionsQueuedSignal.WaitAsync(GetRemainingTime(endTime), cancellationToken).ConfigureAwait(false))
            {
                List<MqttTopicFilter> subscriptions;
                SendSubscribeUnsubscribeResult subscribeUnsubscribeResult;
                HashSet<string> unsubscriptions;

                lock (_subscriptions)
                {
                    subscriptions = _subscriptions.Values.ToList();
                    _subscriptions.Clear();

                    unsubscriptions = new HashSet<string>(_unsubscriptions);
                    _unsubscriptions.Clear();
                }

                if (!subscriptions.Any() && !unsubscriptions.Any())
                {
                    continue;
                }

                _logger.Verbose("Publishing {0} added and {1} removed subscriptions", subscriptions.Count, unsubscriptions.Count);

                foreach (var unsubscription in unsubscriptions)
                {
                    _reconnectSubscriptions.Remove(unsubscription);
                }

                foreach (var subscription in subscriptions)
                {
                    _reconnectSubscriptions[subscription.Topic] = subscription;
                }

                var addedTopicFilters = new List<MqttTopicFilter>();
                foreach (var subscription in subscriptions)
                {
                    addedTopicFilters.Add(subscription);

                    if (addedTopicFilters.Count == Options.MaxTopicFiltersInSubscribeUnsubscribePackets)
                    {
                        subscribeUnsubscribeResult = await SendSubscribeUnsubscribe(addedTopicFilters, null, cancellationToken).ConfigureAwait(false);
                        addedTopicFilters.Clear();
                        await HandleSubscriptionsResultAsync(subscribeUnsubscribeResult).ConfigureAwait(false);
                    }
                }

                subscribeUnsubscribeResult = await SendSubscribeUnsubscribe(addedTopicFilters, null, cancellationToken).ConfigureAwait(false);
                await HandleSubscriptionsResultAsync(subscribeUnsubscribeResult).ConfigureAwait(false);

                var removedTopicFilters = new List<string>();
                foreach (var unSub in unsubscriptions)
                {
                    removedTopicFilters.Add(unSub);

                    if (removedTopicFilters.Count == Options.MaxTopicFiltersInSubscribeUnsubscribePackets)
                    {
                        subscribeUnsubscribeResult = await SendSubscribeUnsubscribe(null, removedTopicFilters, cancellationToken).ConfigureAwait(false);
                        removedTopicFilters.Clear();
                        await HandleSubscriptionsResultAsync(subscribeUnsubscribeResult).ConfigureAwait(false);
                    }
                }

                subscribeUnsubscribeResult = await SendSubscribeUnsubscribe(null, removedTopicFilters, cancellationToken).ConfigureAwait(false);
                await HandleSubscriptionsResultAsync(subscribeUnsubscribeResult).ConfigureAwait(false);
            }
        }

        async Task<ReconnectionResult> ReconnectIfRequiredAsync(CancellationToken cancellationToken)
        {
            if (InternalClient.IsConnected)
            {
                return ReconnectionResult.StillConnected;
            }

            MqttClientConnectResult connectResult = null;
            try
            {
                using (var connectTimeout = NewTimeoutToken(cancellationToken))
                {
                    connectResult = await InternalClient.ConnectAsync(Options.ClientOptions, connectTimeout.Token).ConfigureAwait(false);
                }

                if (connectResult.ResultCode != MqttClientConnectResultCode.Success)
                {
                    throw new MqttCommunicationException($"Client connected but server denied connection with reason '{connectResult.ResultCode}'.");
                }

                return connectResult.IsSessionPresent ? ReconnectionResult.Recovered : ReconnectionResult.Reconnected;
            }
            catch (Exception exception)
            {
                await InvokeClientCallbackAsync(_connectingFailedEvent, new ConnectingFailedEventArgs(connectResult, exception)).ConfigureAwait(false);
                return ReconnectionResult.NotConnected;
            }
        }

        async Task<SendSubscribeUnsubscribeResult> SendSubscribeUnsubscribe(List<MqttTopicFilter> addedSubscriptions, List<string> removedSubscriptions, CancellationToken cancellationToken)
        {
            var subscribeResults = new List<MqttClientSubscribeResult>();
            var unsubscribeResults = new List<MqttClientUnsubscribeResult>();
            try
            {
                if (removedSubscriptions != null && removedSubscriptions.Any())
                {
                    var unsubscribeOptionsBuilder = new MqttClientUnsubscribeOptionsBuilder();

                    foreach (var removedSubscription in removedSubscriptions)
                    {
                        unsubscribeOptionsBuilder.WithTopicFilter(removedSubscription);
                    }

                    using (var unsubscribeTimeout = NewTimeoutToken(cancellationToken))
                    {
                        var unsubscribeResult = await InternalClient.UnsubscribeAsync(unsubscribeOptionsBuilder.Build(), unsubscribeTimeout.Token).ConfigureAwait(false);
                        unsubscribeResults.Add(unsubscribeResult);
                    }

                    //clear because these worked, maybe the subscribe below will fail, only report those
                    removedSubscriptions.Clear();
                }

                if (addedSubscriptions != null && addedSubscriptions.Any())
                {
                    var subscribeOptionsBuilder = new MqttClientSubscribeOptionsBuilder();

                    foreach (var addedSubscription in addedSubscriptions)
                    {
                        subscribeOptionsBuilder.WithTopicFilter(addedSubscription);
                    }

                    using var subscribeTimeout = NewTimeoutToken(cancellationToken);
                    var subscribeResult = await InternalClient.SubscribeAsync(subscribeOptionsBuilder.Build(), subscribeTimeout.Token).ConfigureAwait(false);
                    subscribeResults.Add(subscribeResult);
                }
            }
            catch (Exception exception)
            {
                await HandleSubscriptionExceptionAsync(exception, addedSubscriptions, removedSubscriptions).ConfigureAwait(false);
            }

            return new SendSubscribeUnsubscribeResult(subscribeResults, unsubscribeResults);
        }

        async Task StartPublishingAsync()
        {
            await StopPublishingAsync().ConfigureAwait(false);

            var cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = cancellationTokenSource.Token;
            _publishingCancellationToken = cancellationTokenSource;

            _publishingTask = Task.Run(() => PublishQueuedMessagesAsync(cancellationToken), cancellationToken);
            _publishingTask.RunInBackground(_logger);
        }

        void StopMaintainingConnection()
        {
            try
            {
                _connectionCancellationToken?.Cancel(false);
            }
            finally
            {
                _connectionCancellationToken?.Dispose();
                _connectionCancellationToken = null;
            }
        }

        void CancelPublishing()
        {
            try
            {
                _publishingCancellationToken?.Cancel(false);
            }
            finally
            {
                _publishingCancellationToken?.Dispose();
                _publishingCancellationToken = null;
            }
        }

        async Task StopPublishingAsync()
        {
            CancelPublishing();
            if (_publishingTask != null)
            {
                // A canceled worker may still be inside an interceptor or storage
                // write. Drain it before replacing the worker or disposing storage.
                try
                {
                    await _publishingTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }

                _publishingTask = null;
            }
        }

        async Task TryMaintainConnectionAsync(CancellationToken cancellationToken)
        {
            try
            {
                var oldConnectionState = InternalClient.IsConnected;
                var connectionState = await ReconnectIfRequiredAsync(cancellationToken).ConfigureAwait(false);

                switch (connectionState)
                {
                    case ReconnectionResult.NotConnected:
                        await StopPublishingAsync().ConfigureAwait(false);
                        await Task.Delay(ComputeReconnectDelay(Options.AutoReconnectDelay, Options.MaxReconnectDelay, _reconnectFailureCount), cancellationToken).ConfigureAwait(false);
                        if (_reconnectFailureCount < int.MaxValue)
                        {
                            _reconnectFailureCount++;
                        }
                        break;
                    case ReconnectionResult.Reconnected:
                        _reconnectFailureCount = 0;
                        await PublishReconnectSubscriptionsAsync(cancellationToken).ConfigureAwait(false);
                        await StartPublishingAsync().ConfigureAwait(false);
                        break;
                    case ReconnectionResult.Recovered:
                        _reconnectFailureCount = 0;
                        await StartPublishingAsync().ConfigureAwait(false);
                        break;
                    case ReconnectionResult.StillConnected:
                        await PublishSubscriptionsAsync(Options.ConnectionCheckInterval, cancellationToken).ConfigureAwait(false);
                        break;
                }

                if (oldConnectionState != InternalClient.IsConnected)
                {
                    await InvokeClientCallbackAsync(_connectionStateChangedEvent, EventArgs.Empty).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (MqttCommunicationException exception)
            {
                _logger.Warning(exception, "Communication error while maintaining connection.");
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Error exception while maintaining connection.");
            }
        }

        async Task TryPublishQueuedMessageAsync(ManagedMqttApplicationMessage message, CancellationToken cancellationToken)
        {
            Exception transmitException = null;
            bool acceptPublish = true;
            try
            {
                if (_interceptingPublishMessageEvent.HasHandlers)
                {
                    var interceptEventArgs = new InterceptingPublishMessageEventArgs(message);
                    await InvokeClientCallbackAsync(_interceptingPublishMessageEvent, interceptEventArgs).ConfigureAwait(false);
                    acceptPublish = interceptEventArgs.AcceptPublish;
                }

                if (acceptPublish)
                {
                    using var publishTimeout = NewTimeoutToken(cancellationToken);
                    await InternalClient.PublishAsync(message.ApplicationMessage, publishTimeout.Token).ConfigureAwait(false);
                }

                using (await _messageQueueLock.EnterAsync().ConfigureAwait(false)) // Complete storage cleanup even when publishing has been stopped.
                {
                    // While publishing this message, this.PublishAsync could have booted this
                    // message off the queue to make room for another (when using a cap
                    // with the DropOldestQueuedMessage strategy).  If the first item
                    // in the queue is equal to this message, then it's safe to remove
                    // it from the queue.  If not, that means this.PublishAsync has already
                    // removed it, in which case we don't want to do anything.
                    _messageQueue.RemoveFirst(i => i.Id.Equals(message.Id));

                    if (_storageManager != null)
                    {
                        await _storageManager.RemoveAsync(message).ConfigureAwait(false);
                    }
                }
            }
            catch (MqttCommunicationException exception)
            {
                transmitException = exception;

                _logger.Warning(exception, "Publishing application message ({0}) failed.", message.Id);

                if (message.ApplicationMessage.QualityOfServiceLevel == MqttQualityOfServiceLevel.AtMostOnce)
                {
                    //If QoS 0, we don't want this message to stay on the queue.
                    //If QoS 1 or 2, it's possible that, when using a cap, this message
                    //has been booted off the queue by this.PublishAsync, in which case this
                    //thread will not continue to try to publish it. While this does
                    //contradict the expected behavior of QoS 1 and 2, that's also true
                    //for the usage of a message queue cap, so it's still consistent
                    //with prior behavior in that way.
                    using (await _messageQueueLock.EnterAsync().ConfigureAwait(false)) // Complete storage cleanup even when publishing has been stopped.
                    {
                        _messageQueue.RemoveFirst(i => i.Id.Equals(message.Id));

                        if (_storageManager != null)
                        {
                            await _storageManager.RemoveAsync(message).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                transmitException = exception;
                _logger.Error(exception, "Error while publishing application message ({0}).", message.Id);
            }
            finally
            {
                if (_applicationMessageProcessedEvent.HasHandlers)
                {
                    var eventArgs = new ApplicationMessageProcessedEventArgs(message, transmitException);
                    await InvokeClientCallbackAsync(_applicationMessageProcessedEvent, eventArgs).ConfigureAwait(false);
                }
            }
        }
    }
}

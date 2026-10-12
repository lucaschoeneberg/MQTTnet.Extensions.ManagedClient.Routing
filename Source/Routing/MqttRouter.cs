// Copyright (c) Atlas Lift Tech Inc. All rights reserved.

using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MQTTnet.Extensions.ManagedClient.Routing.Attributes;

namespace MQTTnet.Extensions.ManagedClient.Routing.Routing
{
    internal sealed class MqttRouter : IDisposable, IAsyncDisposable
    {
        private readonly ILogger<MqttRouter> logger;
        private readonly MqttRouteTable routeTable;
        private readonly ITypeActivatorCache typeActivator;
        private readonly MqttRoutingOptions? options;
        private readonly SemaphoreSlim dispatchGate;
        private readonly object dispatchLock = new();
        private int pendingDispatches;
        private bool disposed;
        private readonly CancellationTokenSource shutdown = new();
        private readonly Dictionary<CancellationToken, DispatchGroup> dispatchGroups = new();
        private TaskCompletionSource dispatchesDrained = CompletedSignal();
        private bool resourcesDisposed;

        private sealed class DispatchGroup(object? owner)
        {
            public object? Owner { get; } = owner;
            public int Count { get; set; }
            public Task AcknowledgementTail { get; set; } = Task.CompletedTask;
            public TaskCompletionSource Drained { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private static TaskCompletionSource CompletedSignal()
        {
            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            signal.SetResult();
            return signal;
        }

        public MqttRouter(ILogger<MqttRouter> logger, MqttRouteTable routeTable, ITypeActivatorCache typeActivator,
            MqttRoutingOptions? options = null)
        {
            this.logger = logger;
            this.routeTable = routeTable;
            this.typeActivator = typeActivator;
            this.options = options;
            dispatchGate = new SemaphoreSlim(Math.Max(1, options?.MaxConcurrentDispatches ?? 1));
        }

        public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

        public async ValueTask DisposeAsync()
        {
            Task drained;
            bool cancel;
            lock (dispatchLock)
            {
                cancel = !disposed;
                disposed = true;
                drained = dispatchesDrained.Task;
            }

            if (cancel)
            {
                try
                {
                    await shutdown.CancelAsync().ConfigureAwait(false);
                }
                catch (AggregateException ex)
                {
                    logger.LogError(ex, "MQTT action cancellation callback failed.");
                }
            }

            await drained.ConfigureAwait(false);
            lock (dispatchLock)
            {
                if (!resourcesDisposed)
                {
                    dispatchGate.Dispose();
                    shutdown.Dispose();
                    resourcesDisposed = true;
                }
            }

            GC.SuppressFinalize(this);
        }

        internal Task DrainAsync(object owner)
        {
            lock (dispatchLock)
            {
                return Task.WhenAll(dispatchGroups.Values
                    .Where(group => ReferenceEquals(group.Owner, owner))
                    .Select(group => group.Drained.Task));
            }
        }

        internal async Task OnIncomingApplicationMessage(IServiceProvider svcProvider,
            MqttApplicationMessageReceivedEventArgs context, bool allowUnmatchedRoutes,
            IRouteInvocationInterceptor? interceptor = null, bool dispatchInBackground = false,
            MqttDispatchLifetime? lifetime = null)
        {
            var connectionToken = lifetime?.ConnectionToken ?? CancellationToken.None;
            DispatchGroup group;
            lock (dispatchLock)
            {
                if (disposed || connectionToken.IsCancellationRequested)
                {
                    context.ProcessingFailed = true;
                    return;
                }

                if (pendingDispatches == 0)
                {
                    dispatchesDrained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                }

                if (!dispatchGroups.TryGetValue(connectionToken, out group!))
                {
                    group = new DispatchGroup(lifetime?.Owner);
                    dispatchGroups.Add(connectionToken, group);
                }

                pendingDispatches++;
                group.Count++;
            }

            using var admission = CancellationTokenSource.CreateLinkedTokenSource(connectionToken, shutdown.Token);
            try
            {
                await dispatchGate.WaitAsync(admission.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                context.ProcessingFailed = true;
                ReleaseDispatch(group, false, connectionToken);
                return;
            }

            if (dispatchInBackground && (options?.MaxConcurrentDispatches ?? 1) > 1)
            {
                var acknowledge = context.AutoAcknowledge;
                context.AutoAcknowledge = false;
                Task previousAcknowledgement;
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                lock (dispatchLock)
                {
                    previousAcknowledgement = group.AcknowledgementTail;
                    group.AcknowledgementTail = completion.Task;
                }

                _ = Task.Run(() => DispatchMessageAsync(svcProvider, context,
                    allowUnmatchedRoutes, interceptor, acknowledge, lifetime, group, previousAcknowledgement, completion));
                return;
            }

            await DispatchMessageAsync(svcProvider, context, allowUnmatchedRoutes,
                interceptor, false, lifetime, group).ConfigureAwait(false);
        }

        private void ReleaseDispatch(DispatchGroup group, bool enteredGate, CancellationToken connectionToken)
        {
            lock (dispatchLock)
            {
                if (enteredGate)
                {
                    dispatchGate.Release();
                }

                group.Count--;
                pendingDispatches--;
                if (group.Count == 0)
                {
                    dispatchGroups.Remove(connectionToken);
                    group.Drained.TrySetResult();
                }

                if (pendingDispatches == 0)
                {
                    dispatchesDrained.TrySetResult();
                }
            }
        }

        private async Task DispatchMessageAsync(IServiceProvider svcProvider,
            MqttApplicationMessageReceivedEventArgs context, bool allowUnmatchedRoutes,
            IRouteInvocationInterceptor? interceptor, bool acknowledge,
            MqttDispatchLifetime? lifetime, DispatchGroup group,
            Task? previousAcknowledgement = null, TaskCompletionSource? completion = null)
        {
            var connectionToken = lifetime?.ConnectionToken ?? CancellationToken.None;
            using var clientCallback = lifetime?.EnterCallbackScope.Invoke();
            try
            {
                object? correlationObject = null;
                Exception? failure;
                try
                {
                    if (interceptor != null)
                    {
                        correlationObject = await interceptor.RouteExecuting(context).ConfigureAwait(false);
                    }

                    failure = await RouteMessageAsync(svcProvider, context, allowUnmatchedRoutes, connectionToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    context.ProcessingFailed = true;
                    failure = ex;
                    logger.LogError(ex, "Unable to dispatch MQTT message.");
                }

                if (interceptor != null)
                {
                    try
                    {
                        await interceptor.RouteExecuted(correlationObject, context, failure).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        context.ProcessingFailed = true;
                        logger.LogError(ex, "RouteExecuted interceptor failed.");
                    }
                }

                if (previousAcknowledgement != null)
                {
                    // MQTT requires PUBACK/PUBREC to follow receive order even
                    // when independent controller actions finish out of order.
                    await previousAcknowledgement.ConfigureAwait(false);
                }

                if (acknowledge && !context.ProcessingFailed && !connectionToken.IsCancellationRequested && !shutdown.IsCancellationRequested)
                {
                    await context.AcknowledgeAsync(connectionToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                context.ProcessingFailed = true;
                logger.LogError(ex, "Unable to acknowledge MQTT message.");
            }
            finally
            {
                completion?.TrySetResult();
                ReleaseDispatch(group, true, connectionToken);
            }
        }

        private async Task<Exception?> RouteMessageAsync(IServiceProvider svcProvider,
            MqttApplicationMessageReceivedEventArgs context, bool allowUnmatchedRoutes, CancellationToken connectionToken)
        {
            var topic = context.ApplicationMessage?.Topic;
            if (string.IsNullOrEmpty(topic))
            {
                logger.LogWarning("Received message without topic; marking as failed.");
                context.ProcessingFailed = true;
                return null;
            }

            var routeContext = new MqttRouteContext(topic);

            routeTable.Route(routeContext);

            if (routeContext.Handler == null)
            {
                // Route not found
                if (!allowUnmatchedRoutes)
                {
                    logger.LogDebug("No route matched for topic '{Topic}'", topic);
                }

                context.ProcessingFailed = !allowUnmatchedRoutes;
                return null;
            }

            logger.LogDebug("Route matched for topic '{Topic}'", topic);

            return await DispatchCoreAsync(svcProvider, context, routeContext, connectionToken).ConfigureAwait(false);
        }

        private async Task<Exception?> DispatchCoreAsync(IServiceProvider svcProvider,
            MqttApplicationMessageReceivedEventArgs context, MqttRouteContext routeContext, CancellationToken connectionToken)
        {
            // Ensure message is marked as accepted when the route is
            // processed successfully.
            context.ProcessingFailed = false;

            using var actionCts = CancellationTokenSource.CreateLinkedTokenSource(connectionToken, shutdown.Token);
            if (options?.MaxActionExecutionTime is { } maxExecutionTime)
            {
                actionCts.CancelAfter(maxExecutionTime);
            }

            var actionToken = actionCts.Token;

            try
            {
                await using (var scope = svcProvider.CreateAsyncScope())
                {
                    var declaringType = routeContext.Handler!.DeclaringType;

                    if (declaringType == null)
                    {
                        throw new InvalidOperationException($"{routeContext.Handler} must have a declaring type.");
                    }

                    var classInstance = typeActivator.CreateInstance<object>(scope.ServiceProvider, declaringType);

                    try
                    {
                        // Potential perf improvement is to cache this reflection work in the future.
                        var activateProperties = declaringType.GetRuntimeProperties()
                            .Where((property) => property.IsDefined(typeof(MqttControllerContextAttribute)) &&
                                                 property.GetIndexParameters().Length == 0 &&
                                                 property.SetMethod != null &&
                                                 !property.SetMethod.IsStatic)
                            .ToArray();

                        if (activateProperties.Length == 0)
                        {
                            logger.LogDebug(
                                "MqttController '{Controller}' does not have a property that can accept a controller context. You may want to add a [{ContextAttribute}] to a public property.",
                                declaringType.FullName, nameof(MqttControllerContextAttribute));
                        }

                        var controllerContext = new MqttControllerContext()
                        {
                            MqttContext = context
                        };

                        for (int i = 0; i < activateProperties.Length; i++)
                        {
                            PropertyInfo property = activateProperties[i];
                            property.SetValue(classInstance, controllerContext);
                        }

                        if (routeContext.HaveControllerParameter && routeContext.ControllerTemplate != null)
                        {
                            var tmpx = routeContext.ControllerTemplate;
                            tmpx.Segments.Where(p => p.IsParameter).ToList().ForEach(ts =>
                            {
                                var pro = declaringType.GetRuntimeProperty(ts.Value);
                                if (pro == null) return;
                                if (routeContext.Parameters?.TryGetValue(ts.Value, out object? pvalue) == true)
                                {
                                    pro.SetValue(classInstance, pvalue);
                                }
                            });
                        }

                        ParameterInfo[] parameters = routeContext.Handler.GetParameters();

                        if (parameters.Length == 0)
                        {
                            await InvokeActionAsync(routeContext.Handler, classInstance, null, context, actionToken, connectionToken).ConfigureAwait(false);
                        }
                        else
                        {
                            var paramArray = parameters.Select(p =>
                                    MatchParameterOrThrow(p, routeContext.Parameters, controllerContext, svcProvider, actionToken)
                                )
                                .ToArray();

                            await InvokeActionAsync(routeContext.Handler, classInstance, paramArray, context, actionToken, connectionToken).ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        if (classInstance is IAsyncDisposable asyncDisposable)
                        {
                            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                        }
                        else if (classInstance is IDisposable disposable)
                        {
                            disposable.Dispose();
                        }
                    }
                }
            }
            catch (OperationCanceledException ex) when (connectionToken.IsCancellationRequested || shutdown.IsCancellationRequested)
            {
                context.ProcessingFailed = true;
                logger.LogDebug(ex, "MQTT action canceled during connection or router shutdown.");
                return ex;
            }
            catch (TimeoutException ex)
            {
                logger.LogWarning(ex,
                    "MQTT action exceeded the configured execution time. See inner exception for details.");

                context.ProcessingFailed = true;
                return ex;
            }
            catch (ArgumentException ex)
            {
                logger.LogError(ex,
                    "Unable to match route parameters to all arguments. See inner exception for details.");

                // Parameter matching failed; reject the message.
                context.ProcessingFailed = true;
                return ex;
            }
            catch (TargetInvocationException ex)
            {
                logger.LogError(ex.InnerException,
                    "Unhandled MQTT action exception. See inner exception for details.");

                // This is an unhandled exception from the invoked action
                context.ProcessingFailed = true;
                return ex;
            }
            catch (Exception ex)
            {
                // Catches activation, binding and invocation failures so a
                // single bad message never faults the MQTT dispatch loop.
                logger.LogError(ex, "Unable to invoke Mqtt Action. See inner exception for details.");

                context.ProcessingFailed = true;
                return ex;
            }

            return null;
        }

        private async Task InvokeActionAsync(MethodInfo method, object instance, object?[]? parameters,
            MqttApplicationMessageReceivedEventArgs context, CancellationToken actionToken, CancellationToken connectionToken)
        {
            // Include synchronous invocation and the synchronous prefix of async
            // methods in the deadline, without blocking the receive callback.
            var invocation = Task.Run(() => HandlerInvoker(method, instance, parameters));
            try
            {
                await invocation.WaitAsync(actionToken).ConfigureAwait(false);
                actionToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (actionToken.IsCancellationRequested)
            {
                context.ProcessingFailed = true;
                // Cancellation cannot stop arbitrary user code. Keep its scope and
                // dispatch slot alive, and observe any late exception before returning.
                try
                {
                    await invocation.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "MQTT action terminated after its execution deadline.");
                }

                if (connectionToken.IsCancellationRequested || shutdown.IsCancellationRequested)
                {
                    throw new OperationCanceledException("MQTT action canceled during shutdown.", actionToken);
                }

                throw new TimeoutException("MQTT action exceeded the configured execution time.");
            }
        }

        private static async Task HandlerInvoker(MethodInfo method, object instance, object?[]? parameters)
        {
            if (method.ReturnType == typeof(void))
            {
                method.Invoke(instance, parameters);

                return;
            }

            var result = method.Invoke(instance, parameters);
            if (result is null)
            {
                throw new InvalidOperationException(
                    $"{method.DeclaringType?.FullName}.{method.Name} returned null instead of an awaitable.");
            }

            // Task and Task<T> (Task<T> derives from Task).
            if (result is Task task)
            {
                await task.ConfigureAwait(false);
                return;
            }

            if (result is ValueTask valueTask)
            {
                await valueTask.ConfigureAwait(false);
                return;
            }

            var resultType = result.GetType();
            if (resultType.IsGenericType && resultType.GetGenericTypeDefinition() == typeof(ValueTask<>))
            {
                var asTask = (Task?)resultType.GetMethod("AsTask")?.Invoke(result, null);
                if (asTask is null)
                {
                    throw new InvalidOperationException(
                        $"{method.DeclaringType?.FullName}.{method.Name} returned an unawaitable ValueTask<T>.");
                }

                await asTask.ConfigureAwait(false);
                return;
            }

            throw new InvalidOperationException(
                $"Unsupported action return type \"{method.ReturnType}\" on method {method.DeclaringType?.FullName}.{method.Name}. Only void, Task, Task<T>, ValueTask and ValueTask<T> are allowed.");
        }

        private static object? MatchParameterOrThrow(ParameterInfo param,
            IReadOnlyDictionary<string, object?>? availableParameters, MqttControllerContext controllerContext,
            IServiceProvider serviceProvider, CancellationToken actionToken)
        {
            if (param.ParameterType == typeof(CancellationToken))
            {
                // The MQTT dispatch pipeline is event based and has no ambient
                // cancellation token, so actions receive CancellationToken.None
                // unless a MaxActionExecutionTime timeout is configured.
                return actionToken;
            }

            if (param.IsDefined(typeof(FromPayloadAttribute), false))
            {
                JsonSerializerOptions? defaultOptions =
                    serviceProvider.GetService<MqttRoutingOptions>()?.SerializerOptions;
                if (controllerContext.MqttContext?.ApplicationMessage?.Payload == null)
                {
                    return param.ParameterType.IsValueType ? Activator.CreateInstance(param.ParameterType) : null;
                }

                try
                {
                    var jsonString = Encoding.UTF8.GetString(controllerContext.MqttContext.ApplicationMessage.Payload);
                    return JsonSerializer.Deserialize(jsonString, param.ParameterType, defaultOptions ?? new JsonSerializerOptions());
                }
                catch (JsonException ex)
                {
                    throw new ArgumentException($"Failed to deserialize the payload to type {param.ParameterType.Name}", ex);
                }
            }

            if (availableParameters == null || param.Name == null || !availableParameters.TryGetValue(param.Name, out object? value))
            {
                if (param.IsOptional)
                {
                    return null;
                }

                throw new ArgumentException(
                    $"No matching route parameter for \"{param.ParameterType.Name} {param.Name}\"", param.Name);
            }

            if (param.ParameterType.IsInstanceOfType(value)) return value;
            try
            {
                value = Convert.ChangeType(value, param.ParameterType, CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                throw new ArgumentException(
                    $"Cannot assign type \"{value?.GetType().ToString() ?? "null"}\" to parameter \"{param.ParameterType.Name} {param.Name}\"",
                    param.Name, ex);
            }

            return value;
        }
    }
}

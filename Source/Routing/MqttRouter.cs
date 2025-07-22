// Copyright (c) Atlas Lift Tech Inc. All rights reserved.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MQTTnet.Extensions.ManagedClient.Routing.Attributes;

namespace MQTTnet.Extensions.ManagedClient.Routing.Routing
{
    internal class MqttRouter
    {
        private readonly ILogger<MqttRouter> logger;
        private readonly MqttRouteTable routeTable;
        private readonly ITypeActivatorCache typeActivator;

        public MqttRouter(ILogger<MqttRouter> logger, MqttRouteTable routeTable, ITypeActivatorCache typeActivator)
        {
            this.logger = logger;
            this.routeTable = routeTable;
            this.typeActivator = typeActivator;
        }

        internal async Task OnIncomingApplicationMessage(IServiceProvider svcProvider,
            MqttApplicationMessageReceivedEventArgs context, bool allowUnmatchedRoutes)
        {
            var routeContext = new MqttRouteContext(context.ApplicationMessage.Topic);

            routeTable.Route(routeContext);

            if (routeContext.Handler == null)
            {
                // Route not found
                if (!allowUnmatchedRoutes)
                {
                    logger.LogDebug($"No route matched for topic '{context.ApplicationMessage.Topic}'");
                }

                // In newer versions the application message is accepted via
                // the AcceptPublish flag rather than the deprecated
                // ProcessingFailed property.
                context.ProcessingFailed = !allowUnmatchedRoutes;
            }
            else
            {
                logger.LogDebug($"Route matched for topic '{context.ApplicationMessage.Topic}'");
                using (var scope = svcProvider.CreateScope())
                {
                    var declaringType = routeContext.Handler.DeclaringType;

                    if (declaringType == null)
                    {
                        throw new InvalidOperationException($"{routeContext.Handler} must have a declaring type.");
                    }

                    var classInstance = typeActivator.CreateInstance<object>(scope.ServiceProvider, declaringType);

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
                            $"MqttController '{declaringType.FullName}' does not have a property that can accept a controller context.  You may want to add a [{nameof(MqttControllerContextAttribute)}] to a public property.");
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

                    if (routeContext.HaveControllerParameter)
                    {
                        var tmpx = routeContext.ControllerTemplate;
                        tmpx.Segments.Where(p => p.IsParameter).ToList().ForEach(ts =>
                        {
                            var pro = declaringType.GetRuntimeProperty(ts.Value);
                            if (pro == null) return;
                            if (routeContext.Parameters.TryGetValue(ts.Value, out object pvalue))
                            {
                                pro.SetValue(classInstance, pvalue);
                            }
                        });
                    }

                    ParameterInfo[] parameters = routeContext.Handler.GetParameters();

                    // Ensure message is marked as accepted when the route is
                    // processed successfully.
                    context.ProcessingFailed = false;
    
                    if (parameters.Length == 0)
                    {
                        await HandlerInvoker(routeContext.Handler, classInstance, null).ConfigureAwait(false);
                    }
                    else
                    {
                        object?[] paramArray;

                        try
                        {
                            paramArray = parameters.Select(p =>
                                    MatchParameterOrThrow(p, routeContext.Parameters, controllerContext, svcProvider)
                                )
                                .ToArray();

                            await HandlerInvoker(routeContext.Handler, classInstance, paramArray).ConfigureAwait(false);
                        }
                        catch (ArgumentException ex)
                        {
                            logger.LogError(ex,
                                $"Unable to match route parameters to all arguments. See inner exception for details.");

                            // Parameter matching failed; reject the message.
                            context.ProcessingFailed = true;
                        }
                        catch (TargetInvocationException ex)
                        {
                            logger.LogError(ex.InnerException,
                                $"Unhandled MQTT action exception. See inner exception for details.");

                            // This is an unhandled exception from the invoked action
                            context.ProcessingFailed = true;
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Unable to invoke Mqtt Action.  See inner exception for details.");

                            // Unexpected error; reject the message.
                            context.ProcessingFailed = true;
                        }
                    }
                }
            }
        }

        private static Task HandlerInvoker(MethodInfo method, object instance, object?[]? parameters)
        {
            if (method.ReturnType == typeof(void))
            {
                method.Invoke(instance, parameters);

                return Task.CompletedTask;
            }

            if (method.ReturnType == typeof(Task))
            {
                var result = (Task?)method.Invoke(instance, parameters);

                if (result == null)
                {
                    throw new NullReferenceException(
                        $"{method.DeclaringType.FullName}.{method.Name} returned null instead of Task");
                }

                return result;
            }

            throw new InvalidOperationException(
                $"Unsupported Action return type \"{method.ReturnType}\" on method {method.DeclaringType.FullName}.{method.Name}. Only void and {nameof(Task)} are allowed.");
        }

        private static object? MatchParameterOrThrow(ParameterInfo param,
            IReadOnlyDictionary<string, object> availableParameters, MqttControllerContext controllerContext,
            IServiceProvider serviceProvider)
        {
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
                    var jsonString = System.Text.Encoding.UTF8.GetString(controllerContext.MqttContext.ApplicationMessage.Payload);
                    return JsonSerializer.Deserialize(jsonString, param.ParameterType, defaultOptions ?? new JsonSerializerOptions());
                }
                catch (JsonException ex)
                {
                    throw new ArgumentException($"Fehler bei der Deserialisierung des Payloads zum Typ {param.ParameterType.Name}", ex);
                }
            }

            if (!availableParameters.TryGetValue(param.Name, out object? value))
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
                    $"Cannot assign type \"{value.GetType()}\" to parameter \"{param.ParameterType.Name} {param.Name}\"",
                    param.Name, ex);
            }

            return value;
        }
    }
}
// Copyright (c) Atlas Lift Tech Inc. All rights reserved.

using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MQTTnet.Extensions.ManagedClient.Routing.ManagedClient;
using MQTTnet.Extensions.ManagedClient.Routing.Routing;

namespace MQTTnet.Extensions.ManagedClient.Routing.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddMqttControllers(this IServiceCollection services, Assembly[] fromAssemblies)
        {
            return services.AddMqttControllers(opt => opt.FromAssemblies = fromAssemblies);
        }

        public static IServiceCollection AddMqttControllers(this IServiceCollection services)
        {
            return services.AddMqttControllers(opt => { });
        }

        public static IServiceCollection AddMqttControllers(this IServiceCollection services,
            Action<MqttRoutingOptions> options)
        {
            var opt = new MqttRoutingOptions();
            opt.WithJsonSerializerOptions();
            opt.FromAssemblies = null;
            opt.RouteInvocationInterceptor = null;
            options?.Invoke(opt);

            if (opt.MaxConcurrentDispatches < 1)
            {
                throw new InvalidOperationException(
                    "MqttRoutingOptions.MaxConcurrentDispatches must be at least 1.");
            }

            if (opt.MaxActionExecutionTime <= TimeSpan.Zero)
            {
                throw new InvalidOperationException(
                    "MqttRoutingOptions.MaxActionExecutionTime must be positive when set.");
            }

            services.AddSingleton(opt);
            services.AddSingleton(_ =>
            {
                var fromAssemblies = opt.FromAssemblies;
                if (fromAssemblies != null && fromAssemblies.Length == 0)
                {
                    throw new ArgumentException(
                        "'FromAssemblies' cannot be an empty array. Pass null or a collection of 1 or more assemblies.",
                        nameof(options));
                }

                var entryAssembly = Assembly.GetEntryAssembly();
                var assemblies = fromAssemblies ?? (entryAssembly is not null
                    ? [entryAssembly]
                    : throw new InvalidOperationException(
                        "Unable to determine the entry assembly. Specify the controller assemblies explicitly via AddMqttControllers(services, assemblies)."));

                return MqttRouteTableFactory.Create(assemblies);
            });

            services.AddSingleton<ITypeActivatorCache>(new TypeActivatorCache());
            // Stateless: safe to share across all dispatched messages.
            services.AddSingleton<MqttRouter>();
            if (opt.RouteInvocationInterceptor != null)
            {
                services.AddSingleton(typeof(IRouteInvocationInterceptor), opt.RouteInvocationInterceptor);
            }

            return services;
        }

        public static void WithRouteInvocationInterceptor<T>(this MqttRoutingOptions opt)
            where T : IRouteInvocationInterceptor
        {
            opt.RouteInvocationInterceptor = typeof(T);
        }

        public static MqttRoutingOptions WithMaxActionExecutionTime(this MqttRoutingOptions opt, TimeSpan timeout)
        {
            opt.MaxActionExecutionTime = timeout;
            return opt;
        }

        public static MqttRoutingOptions WithMaxConcurrentDispatches(this MqttRoutingOptions opt, int maxConcurrentDispatches)
        {
            opt.MaxConcurrentDispatches = maxConcurrentDispatches;
            return opt;
        }

        public static MqttRoutingOptions WithJsonSerializerOptions(this MqttRoutingOptions opt)
        {
            var jopt = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
            };
            opt.SerializerOptions = jopt;
            return opt;
        }

        public static MqttRoutingOptions WithJsonSerializerOptions(this MqttRoutingOptions opt,
            JsonSerializerOptions options)
        {
            opt.SerializerOptions = options;
            return opt;
        }

        public static IApplicationBuilder UseAttributeRouting(this IApplicationBuilder app,
            bool allowUnmatchedRoutes = false)
        {
            var router = app.ApplicationServices.GetRequiredService<MqttRouter>();
            var client = app.ApplicationServices.GetRequiredService<IManagedMqttClient>();
            var interceptor = app.ApplicationServices.GetService<IRouteInvocationInterceptor>();

            client.ApplicationMessageReceivedAsync += async args =>
                await HandleMqttMessage(router, interceptor, app.ApplicationServices, args, allowUnmatchedRoutes);

            return app;
        }

        public static void WithAttributeRouting(this ManagedMqttClient client, IServiceProvider svcProvider,
            bool allowUnmatchedRoutes = false)
        {
            var router = svcProvider.GetRequiredService<MqttRouter>();
            var interceptor = svcProvider.GetService<IRouteInvocationInterceptor>();

            client.ApplicationMessageReceivedAsync += async args =>
                await HandleMqttMessage(router, interceptor, svcProvider, args, allowUnmatchedRoutes);
        }

        internal static async Task HandleMqttMessage(
            MqttRouter router,
            IRouteInvocationInterceptor? interceptor,
            IServiceProvider serviceProvider,
            MqttApplicationMessageReceivedEventArgs args,
            bool allowUnmatchedRoutes)
        {
            object? correlationObject = null;
            var logger = serviceProvider.GetService<ILogger<MqttRouter>>();
            if (interceptor != null)
            {
                try
                {
                    correlationObject = await interceptor.RouteExecuting(args);
                }
                catch (Exception ex)
                {
                    // A failing interceptor must never fault the MQTT dispatch
                    // loop; report through RouteExecuted and mark as failed.
                    logger?.LogError(ex, "RouteExecuting interceptor failed.");
                    await TryRouteExecutedAsync(interceptor, logger, null, args, ex);
                    args.ProcessingFailed = true;
                    return;
                }
            }

            try
            {
                await router.OnIncomingApplicationMessage(serviceProvider, args, allowUnmatchedRoutes);

                if (interceptor != null)
                {
                    await interceptor.RouteExecuted(correlationObject, args, null);
                }
            }
            catch (Exception ex)
            {
                if (interceptor != null)
                {
                    await TryRouteExecutedAsync(interceptor, logger, correlationObject, args, ex);
                }

                args.ProcessingFailed = true;
            }
        }

        private static async Task TryRouteExecutedAsync(IRouteInvocationInterceptor interceptor,
            ILogger? logger, object? correlationObject, MqttApplicationMessageReceivedEventArgs args, Exception ex)
        {
            try
            {
                await interceptor.RouteExecuted(correlationObject, args, ex);
            }
            catch (Exception inner)
            {
                // Interceptor failures are observed here so they can never
                // fault the MQTT dispatch loop or mask the original error.
                logger?.LogError(inner, "RouteExecuted interceptor failed.");
            }
        }
    }
}

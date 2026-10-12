// Copyright (c) Atlas Lift Tech Inc. All rights reserved.

using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
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

            if (opt.MaxActionExecutionTime <= TimeSpan.Zero ||
                opt.MaxActionExecutionTime?.TotalMilliseconds > uint.MaxValue - 1)
            {
                throw new InvalidOperationException(
                    "MqttRoutingOptions.MaxActionExecutionTime must be positive and within the supported timer range when set.");
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
            // Share the dispatch gate across all clients using this service provider.
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

        /// <summary>Configures a cooperative action deadline, including synchronous invocation.</summary>
        /// <param name="opt">The routing options being configured.</param>
        /// <param name="timeout">A positive deadline within the supported timer range. Overruns fail the message; cleanup waits for the action to terminate.</param>
        /// <returns>The same options instance.</returns>
        /// <remarks>Invalid deadlines are rejected by <see cref="AddMqttControllers(IServiceCollection, Action{MqttRoutingOptions})"/>.</remarks>
        public static MqttRoutingOptions WithMaxActionExecutionTime(this MqttRoutingOptions opt, TimeSpan timeout)
        {
            opt.MaxActionExecutionTime = timeout;
            return opt;
        }

        /// <summary>Configures bounded dispatch concurrency while deferring acknowledgements until processing finishes.</summary>
        /// <param name="opt">The routing options being configured.</param>
        /// <param name="maxConcurrentDispatches">At least 1. The default is 1; higher values may reorder independent messages.</param>
        /// <returns>The same options instance.</returns>
        /// <remarks>Invalid limits are rejected by <see cref="AddMqttControllers(IServiceCollection, Action{MqttRoutingOptions})"/>.</remarks>
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
                await router.OnIncomingApplicationMessage(app.ApplicationServices, args, allowUnmatchedRoutes,
                    interceptor, dispatchInBackground: true);

            return app;
        }

        public static void WithAttributeRouting(this ManagedMqttClient client, IServiceProvider svcProvider,
            bool allowUnmatchedRoutes = false)
        {
            var router = svcProvider.GetRequiredService<MqttRouter>();
            var interceptor = svcProvider.GetService<IRouteInvocationInterceptor>();

            client.ApplicationMessageReceivedAsync += async args =>
                await router.OnIncomingApplicationMessage(svcProvider, args, allowUnmatchedRoutes,
                    interceptor, dispatchInBackground: true);
        }
    }
}

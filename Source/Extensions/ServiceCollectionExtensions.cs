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
            services.AddTransient<MqttRouter>();
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

        private static async Task HandleMqttMessage(
            MqttRouter router,
            IRouteInvocationInterceptor? interceptor,
            IServiceProvider serviceProvider,
            MqttApplicationMessageReceivedEventArgs args,
            bool allowUnmatchedRoutes)
        {
            object? correlationObject = null;
            if (interceptor != null)
            {
                correlationObject = await interceptor.RouteExecuting(args);
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
                    await interceptor.RouteExecuted(correlationObject, args, ex);
                }

                throw;
            }
        }
    }
}

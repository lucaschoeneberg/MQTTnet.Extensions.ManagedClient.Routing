// Copyright (c) Atlas Lift Tech Inc. All rights reserved.

using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using MQTTnet.Extensions.ManagedClient.Routing.ManagedClient;
using MQTTnet.Extensions.ManagedClient.Routing.Routing;

// This is needed to make internal classes visible to UnitTesting projects
[assembly: InternalsVisibleTo("MQTTnet.AspNetCore.Routing.Tests, PublicKey=00240000048000009" +
                              "4000000060200000024000052534131000400000100010089369e254b2bf47119265eb7514c522350b2e61beda20ccc9" +
                              "a9ddc3f8dab153d59d23011476cc939860d9ae7d09d1bade2915961d01f9ec1f1852265e4d54b090f4c427756f7044e8" +
                              "65ffcd47bf99f18af6361de42003808f7323d20d5d2c66fe494852b5e2438db793ec9fd845b80e1ce5c9b17ff053f386" +
                              "bc0f06080e9d0ba")]

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
                        "'fromAssemblies' cannot be an empty array. Pass null or a collection of 1 or more assemblies.",
                        nameof(fromAssemblies));
                }

                var assemblies = fromAssemblies ?? [Assembly.GetEntryAssembly()];

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
            IRouteInvocationInterceptor interceptor,
            IServiceProvider serviceProvider,
            MqttApplicationMessageReceivedEventArgs args,
            bool allowUnmatchedRoutes)
        {
            object correlationObject = null;
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
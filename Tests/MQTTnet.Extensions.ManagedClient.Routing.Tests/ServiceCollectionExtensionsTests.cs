using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MQTTnet;
using MQTTnet.Extensions.ManagedClient.Routing.Extensions;
using MQTTnet.Extensions.ManagedClient.Routing.Routing;

namespace MQTTnet.Extensions.ManagedClient.Routing.Tests;

[TestClass]
public class ServiceCollectionExtensionsTests
{
    private sealed class TestInterceptor : IRouteInvocationInterceptor
    {
        public Task<object?> RouteExecuting(MqttApplicationMessageReceivedEventArgs messageReceivedEventArgs) =>
            Task.FromResult<object?>(null);

        public Task RouteExecuted(object? correlationObject,
            MqttApplicationMessageReceivedEventArgs messageReceivedEventArgs, Exception? ex) =>
            Task.CompletedTask;
    }

    [TestMethod]
    public void AddMqttControllers_WithAssemblies_RegistersRouteTable()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMqttControllers([typeof(TestController).Assembly]);

        var table = services.BuildServiceProvider().GetService<MqttRouteTable>();

        Assert.IsNotNull(table);
        Assert.IsTrue(table!.Routes.Length > 0);
    }

    [TestMethod]
    public void AddMqttControllers_EmptyAssemblies_ThrowsOnResolve()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMqttControllers(Array.Empty<Assembly>());
        var sp = services.BuildServiceProvider();

        Assert.ThrowsExactly<ArgumentException>(() => sp.GetRequiredService<MqttRouteTable>());
    }

    [TestMethod]
    public void AddMqttControllers_WithInterceptor_RegistersInterceptor()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMqttControllers(opt => opt.WithRouteInvocationInterceptor<TestInterceptor>());

        var interceptor = services.BuildServiceProvider().GetService<IRouteInvocationInterceptor>();

        Assert.IsNotNull(interceptor);
        Assert.IsInstanceOfType<TestInterceptor>(interceptor);
    }

    [TestMethod]
    public void WithJsonSerializerOptions_SetsOptions()
    {
        var options = new MqttRoutingOptions();
        var json = new JsonSerializerOptions();
        options.WithJsonSerializerOptions(json);

        Assert.AreSame(json, options.SerializerOptions);

        options.WithJsonSerializerOptions();

        Assert.IsNotNull(options.SerializerOptions);
    }

    [TestMethod]
    public void AddMqttControllers_InvalidConcurrency_Throws()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            services.AddMqttControllers(opt => opt.WithMaxConcurrentDispatches(0)));
    }

    [TestMethod]
    public void AddMqttControllers_InvalidTimeout_Throws()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            services.AddMqttControllers(opt => opt.WithMaxActionExecutionTime(TimeSpan.Zero)));
    }
}

using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MQTTnet.Extensions.ManagedClient.Routing.Attributes;
using MQTTnet.Extensions.ManagedClient.Routing.Extensions;
using MQTTnet.Extensions.ManagedClient.Routing.ManagedClient;
using MQTTnet.Extensions.ManagedClient.Routing.Routing;
using MQTTnet.Extensions.ManagedClient.Routing.Templates;
using MQTTnet.Packets;
using MQTTnet.Protocol;
using MQTTnet.Server;

namespace MQTTnet.Extensions.ManagedClient.Routing.Tests;

public sealed class ResourceLifecycleState
{
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool ControllerDisposed { get; set; }
    public bool DependencyDisposed { get; set; }
    public bool UsedDisposedDependency { get; set; }
    public bool ActionRan { get; set; }
    public bool AsyncControllerDisposed { get; set; }
    public bool ControllerDisposedBeforeDependency { get; set; }
    public bool CallbackStopCompleted { get; set; }
}

public sealed class AsyncOnlyDependency(ResourceLifecycleState state) : IAsyncDisposable
{
    public ValueTask DisposeAsync()
    {
        state.DependencyDisposed = true;
        return ValueTask.CompletedTask;
    }
}

public sealed class LifetimeSingleton(ResourceLifecycleState state) : IDisposable
{
    public void Dispose() => state.DependencyDisposed = true;
}

[MqttRoute("further-owned")]
public sealed class DisposableLifecycleController(ResourceLifecycleState state) : MqttBaseController, IDisposable
{
    [MqttRoute("run")]
    public void Run() => state.ActionRan = true;

    [NonAction]
    public void Dispose() => state.ControllerDisposed = true;
}

[MqttRoute("further-async")]
public sealed class AsyncDependencyController(ResourceLifecycleState state, AsyncOnlyDependency dependency) : MqttBaseController
{
    [MqttRoute("run")]
    public Task Run()
    {
        GC.KeepAlive(dependency);
        state.ActionRan = true;
        return Task.CompletedTask;
    }
}

[MqttRoute("further-shutdown")]
public sealed class ShutdownLifecycleController(ResourceLifecycleState state, LifetimeSingleton dependency) : MqttBaseController
{
    [MqttRoute("run")]
    public async Task Run()
    {
        GC.KeepAlive(dependency);
        state.Started.SetResult();
        await state.Release.Task;
        state.UsedDisposedDependency = state.DependencyDisposed;
        state.Finished.SetResult();
    }
}

[MqttRoute("async-controller")]
public sealed class AsyncDisposableLifecycleController(ResourceLifecycleState state, AsyncOnlyDependency dependency)
    : MqttBaseController, IDisposable, IAsyncDisposable
{
    [MqttRoute("run")]
    public Task Run()
    {
        GC.KeepAlive(dependency);
        state.Started.TrySetResult();
        return Task.CompletedTask;
    }

    [NonAction]
    public void Dispose() => throw new InvalidOperationException("Async cleanup must take precedence.");

    [NonAction]
    public async ValueTask DisposeAsync()
    {
        await state.Release.Task;
        state.AsyncControllerDisposed = true;
        state.ControllerDisposedBeforeDependency = !state.DependencyDisposed;
    }
}

[MqttRoute("stop-from-route")]
public sealed class StopFromRouteController(IManagedMqttClient client, ResourceLifecycleState state) : MqttBaseController
{
    [MqttRoute("run")]
    public async Task Run()
    {
        var stopping = client.StopAsync();
        state.Started.TrySetResult();
        await Task.WhenAny(stopping, state.Release.Task);
        state.CallbackStopCompleted = stopping.IsCompleted;
    }
}

[TestClass]
public class RoutingRegressionTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(8);

    private static int Port()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static MqttApplicationMessageReceivedEventArgs Args(string topic) =>
        new("probe", new MqttApplicationMessage { Topic = topic }, new MqttPublishPacket { Topic = topic },
            (_, _) => Task.CompletedTask);

    private static MqttRouter Router(ServiceProvider sp, Type controller, int concurrency = 1)
    {
        var table = MqttRouteTableFactory.Create([controller.GetMethod("Run")!]);
        return new MqttRouter(sp.GetRequiredService<ILogger<MqttRouter>>(), table, new TypeActivatorCache(),
            new MqttRoutingOptions().WithMaxConcurrentDispatches(concurrency));
    }

    [TestMethod]
    [DataRow("devices/{*rest}", "unrelated/command")]
    [DataRow("devices/{id:int}/{*rest}", "devices/invalid/command")]
    [DataRow("devices/sub/{*rest}", "devices/other")]
    public void CatchAll_MustRejectAnUnmatchedPrefix(string template, string topic)
    {
        var route = new MqttRoute(TemplateParser.ParseTemplate(template),
            typeof(DisposableLifecycleController).GetMethod("Run")!, []);
        var context = new MqttRouteContext(topic);
        route.Match(context);
        Assert.IsNull(context.Handler);
    }

    [TestMethod]
    public async Task ControllerInstance_MustBeDisposedAfterDispatch()
    {
        var state = new ResourceLifecycleState();
        using var sp = new ServiceCollection().AddLogging().AddSingleton(state).BuildServiceProvider();
        using var router = Router(sp, typeof(DisposableLifecycleController));
        await router.OnIncomingApplicationMessage(sp, Args("further-owned/run"), false);
        Assert.IsTrue(state.ActionRan);
        Assert.IsTrue(state.ControllerDisposed);
    }

    [TestMethod]
    public async Task AsyncOnlyScopedDependency_MustNotTurnSuccessfulActionIntoFailure()
    {
        var state = new ResourceLifecycleState();
        using var sp = new ServiceCollection().AddLogging().AddSingleton(state)
            .AddScoped<AsyncOnlyDependency>().BuildServiceProvider();
        using var router = Router(sp, typeof(AsyncDependencyController));
        var args = Args("further-async/run");
        await router.OnIncomingApplicationMessage(sp, args, false);
        Assert.IsTrue(state.ActionRan);
        Assert.IsFalse(args.ProcessingFailed);
        Assert.IsTrue(state.DependencyDisposed);
    }

    [TestMethod]
    public async Task AsyncControllerCleanup_IsAwaitedBeforeItsDependenciesAreDisposed()
    {
        var state = new ResourceLifecycleState();
        await using var sp = new ServiceCollection().AddLogging().AddSingleton(state)
            .AddScoped<AsyncOnlyDependency>().BuildServiceProvider();
        await using var router = Router(sp, typeof(AsyncDisposableLifecycleController));
        var args = Args("async-controller/run");
        var dispatch = router.OnIncomingApplicationMessage(sp, args, false);
        try
        {
            await state.Started.Task.WaitAsync(Deadline);
            Assert.IsFalse(dispatch.IsCompleted);
            Assert.IsFalse(state.DependencyDisposed);
        }
        finally
        {
            state.Release.TrySetResult();
            await dispatch.WaitAsync(Deadline);
        }

        Assert.IsFalse(args.ProcessingFailed);
        Assert.IsTrue(state.AsyncControllerDisposed);
        Assert.IsTrue(state.ControllerDisposedBeforeDependency);
        Assert.IsTrue(state.DependencyDisposed);
    }

    [TestMethod]
    public async Task HostedShutdown_DrainsActionsBeforeContainerDisposal()
    {
        var state = new ResourceLifecycleState();
        using var host = Host.CreateDefaultBuilder().ConfigureServices(services =>
        {
            services.AddSingleton(state);
            services.AddSingleton<LifetimeSingleton>();
            services.AddMqttControllers(options =>
            {
                options.FromAssemblies = [typeof(ShutdownLifecycleController).Assembly];
                options.WithMaxConcurrentDispatches(2);
            });
        }).Build();
        await host.StartAsync();
        Task? stopping = null;
        try
        {
            var router = host.Services.GetRequiredService<MqttRouter>();
            await router.OnIncomingApplicationMessage(host.Services, Args("further-shutdown/run"), false,
                dispatchInBackground: true);
            await state.Started.Task.WaitAsync(Deadline);

            stopping = host.StopAsync();
            Assert.IsFalse(stopping.IsCompleted);
            Assert.IsFalse(state.DependencyDisposed);
        }
        finally
        {
            state.Release.TrySetResult();
            await (stopping ?? host.StopAsync()).WaitAsync(Deadline);
        }

        Assert.IsFalse(state.UsedDisposedDependency);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ManagedStop_DrainsIncomingActionsAndSupportsStopFromInsideAction(bool stopInsideAction)
    {
        var port = Port();
        using var server = new MqttServerFactory().CreateMqttServer(new MqttServerOptionsBuilder()
            .WithDefaultEndpoint().WithDefaultEndpointPort(port).Build());
        await server.StartAsync();
        using var client = (ManagedMqttClient)new MqttClientFactory().CreateManagedMqttClient();
        var state = new ResourceLifecycleState();
        var services = new ServiceCollection().AddLogging().AddSingleton(state)
            .AddSingleton<IManagedMqttClient>(client).AddSingleton<LifetimeSingleton>();
        services.AddMqttControllers(options =>
        {
            options.FromAssemblies = [typeof(ShutdownLifecycleController).Assembly];
            options.WithMaxConcurrentDispatches(2);
        });
        await using var sp = services.BuildServiceProvider();
        client.WithAttributeRouting(sp);
        var subscribed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.SubscriptionsChangedAsync += _ => { subscribed.TrySetResult(); return Task.CompletedTask; };
        client.DisconnectedAsync += _ => { disconnected.TrySetResult(); return Task.CompletedTask; };
        var topic = stopInsideAction ? "stop-from-route/run" : "further-shutdown/run";
        Task? stopping = null;
        try
        {
            await client.SubscribeAsync([new MqttTopicFilter { Topic = "#", QualityOfServiceLevel = MqttQualityOfServiceLevel.AtLeastOnce }]);
            await client.StartAsync(new ManagedMqttClientOptionsBuilder().WithClientOptions(
                new MqttClientOptionsBuilder().WithTcpServer("localhost", port).Build()).Build());
            await subscribed.Task.WaitAsync(Deadline);
            await server.InjectApplicationMessage(new InjectedMqttApplicationMessage(new MqttApplicationMessageBuilder()
                .WithTopic(topic).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce).Build()));
            await state.Started.Task.WaitAsync(Deadline);

            stopping = client.StopAsync();
            if (!stopInsideAction)
            {
                await disconnected.Task.WaitAsync(Deadline);
                Assert.IsFalse(stopping.IsCompleted);
                Assert.IsFalse(state.DependencyDisposed);
            }
            else
            {
                await stopping.WaitAsync(Deadline);
                Assert.IsTrue(state.CallbackStopCompleted);
            }
        }
        finally
        {
            state.Release.TrySetResult();
            await (stopping ?? client.StopAsync()).WaitAsync(Deadline);
            await server.StopAsync();
        }

        Assert.IsFalse(state.UsedDisposedDependency);
    }

    [TestMethod]
    public async Task BackgroundShutdown_MustDrainBeforeSingletonDisposal()
    {
        var state = new ResourceLifecycleState();
        var sp = new ServiceCollection().AddLogging().AddSingleton(state)
            .AddSingleton<LifetimeSingleton>().BuildServiceProvider();
        using var router = Router(sp, typeof(ShutdownLifecycleController), 2);
        try
        {
            await router.OnIncomingApplicationMessage(sp, Args("further-shutdown/run"), false, dispatchInBackground: true);
            await state.Started.Task.WaitAsync(Deadline);
            var disposing = router.DisposeAsync().AsTask();
            Assert.IsFalse(disposing.IsCompleted);
            Assert.IsFalse(state.DependencyDisposed);
            state.Release.SetResult();
            await disposing.WaitAsync(Deadline);
            await sp.DisposeAsync();
            await state.Finished.Task.WaitAsync(Deadline);
            Assert.IsFalse(state.UsedDisposedDependency);
        }
        finally
        {
            state.Release.TrySetResult();
            await sp.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task Shutdown_CancelsQueuedAdmissionAndDrainsTheActiveAction()
    {
        var state = new ResourceLifecycleState();
        await using var sp = new ServiceCollection().AddLogging().AddSingleton(state)
            .AddSingleton<LifetimeSingleton>().BuildServiceProvider();
        await using var router = Router(sp, typeof(ShutdownLifecycleController));
        var active = router.OnIncomingApplicationMessage(sp, Args("further-shutdown/run"), false);
        Task? queued = null;
        Task? disposing = null;
        var queuedArgs = Args("further-shutdown/run");
        try
        {
            await state.Started.Task.WaitAsync(Deadline);
            queued = router.OnIncomingApplicationMessage(sp, queuedArgs, false);
            Assert.IsFalse(queued.IsCompleted);
            disposing = router.DisposeAsync().AsTask();
            await queued.WaitAsync(Deadline);
            Assert.IsTrue(queuedArgs.ProcessingFailed);
            Assert.IsFalse(disposing.IsCompleted);
            Assert.IsFalse(state.DependencyDisposed);
        }
        finally
        {
            state.Release.TrySetResult();
            await active.WaitAsync(Deadline);
            if (queued != null)
            {
                await queued.WaitAsync(Deadline);
            }

            if (disposing != null)
            {
                await disposing.WaitAsync(Deadline);
            }
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task StopAsync_FromPublishingCallback_MustNotAwaitItsOwnWorker(bool intercept)
    {
        var port = Port();
        using var server = new MqttServerFactory().CreateMqttServer(new MqttServerOptionsBuilder()
            .WithDefaultEndpoint().WithDefaultEndpointPort(port).Build());
        await server.StartAsync();
        using var client = (ManagedMqttClient)new MqttClientFactory().CreateManagedMqttClient();
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopping = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ConnectedAsync += _ => { connected.TrySetResult(); return Task.CompletedTask; };
        async Task RequestStopFromCallback()
        {
            var stop = client.StopAsync();
            stopping.TrySetResult(stop);
            // Allow a failed regression test to unwind its callback for cleanup.
            await Task.WhenAny(stop, release.Task);
        }

        if (intercept)
        {
            client.InterceptPublishMessageAsync += async args =>
            {
                args.AcceptPublish = false;
                await RequestStopFromCallback();
            };
        }
        else
        {
            client.ApplicationMessageProcessedAsync += _ => RequestStopFromCallback();
        }
        Task? pendingStop = null;
        try
        {
            await client.StartAsync(new ManagedMqttClientOptionsBuilder().WithClientOptions(
                new MqttClientOptionsBuilder().WithTcpServer("localhost", port).Build()).Build());
            await connected.Task.WaitAsync(Deadline);
            await client.EnqueueAsync(new MqttApplicationMessage { Topic = "probe/stop" });
            pendingStop = await stopping.Task.WaitAsync(Deadline);
            await pendingStop.WaitAsync(Deadline);
            await client.StopAsync().WaitAsync(Deadline);
        }
        finally
        {
            release.TrySetResult();
            await (pendingStop ?? client.StopAsync()).WaitAsync(Deadline);
            await server.StopAsync();
        }
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task OldDelivery_MustNotAcknowledgeNewSessionPacketWithSameId(bool oldFinishesFirst)
    {
        var port = Port();
        using var server = new MqttServerFactory().CreateMqttServer(new MqttServerOptionsBuilder()
            .WithDefaultEndpoint().WithDefaultEndpointPort(port).Build());
        await server.StartAsync();
        var state = new DispatchState();
        state.Probes[1] = new DispatchProbe();
        state.Probes[2] = new DispatchProbe();
        var services = new ServiceCollection().AddLogging().AddSingleton(state).AddScoped<DispatchResource>();
        services.AddMqttControllers(options =>
        {
            options.FromAssemblies = [typeof(DispatchLifecycleController).Assembly];
            options.WithMaxConcurrentDispatches(2);
        });
        using var sp = services.BuildServiceProvider();
        using var client = (ManagedMqttClient)new MqttClientFactory().CreateManagedMqttClient();
        var firstReceived = new TaskCompletionSource<ushort>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondReceived = new TaskCompletionSource<ushort>(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscribed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondAcknowledged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ApplicationMessageReceivedAsync += args =>
        {
            (args.ApplicationMessage.Topic.EndsWith("/1", StringComparison.Ordinal) ? firstReceived : secondReceived)
                .TrySetResult(args.PacketIdentifier);
            return Task.CompletedTask;
        };
        client.WithAttributeRouting(sp);
        client.SubscriptionsChangedAsync += _ => { subscribed.TrySetResult(); return Task.CompletedTask; };
        server.ClientAcknowledgedPublishPacketAsync += args =>
        {
            if (args.PublishPacket.Topic == "lifecycle/async/2")
            {
                secondAcknowledged.TrySetResult();
            }

            return Task.CompletedTask;
        };
        var options = new ManagedMqttClientOptionsBuilder().WithClientOptions(new MqttClientOptionsBuilder()
            .WithTcpServer("localhost", port).WithClientId("reconnect-probe").WithCleanSession().Build()).Build();
        try
        {
            await client.SubscribeAsync([new MqttTopicFilter { Topic = "lifecycle/#", QualityOfServiceLevel = MqttQualityOfServiceLevel.AtLeastOnce }]);
            await client.StartAsync(options);
            await subscribed.Task.WaitAsync(Deadline);
            await server.InjectApplicationMessage(new InjectedMqttApplicationMessage(new MqttApplicationMessageBuilder()
                .WithTopic("lifecycle/async/1").WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce).Build()));
            await state.Probes[1].Started.Task.WaitAsync(Deadline);
            var oldId = await firstReceived.Task.WaitAsync(Deadline);

            subscribed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await client.InternalClient.DisconnectAsync(new MqttClientDisconnectOptions());
            await subscribed.Task.WaitAsync(Deadline);
            await server.InjectApplicationMessage(new InjectedMqttApplicationMessage(new MqttApplicationMessageBuilder()
                .WithTopic("lifecycle/async/2").WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce).Build()));
            await state.Probes[2].Started.Task.WaitAsync(Deadline);
            var newId = await secondReceived.Task.WaitAsync(Deadline);
            Assert.AreEqual(oldId, newId, "Probe requires id reuse across clean sessions.");

            if (oldFinishesFirst)
            {
                state.Probes[1].Release.SetResult();
                var outcome = await Task.WhenAny(secondAcknowledged.Task, Task.Delay(150));
                Assert.AreNotSame(secondAcknowledged.Task, outcome);
                state.Probes[2].Release.SetResult();
                await secondAcknowledged.Task.WaitAsync(Deadline);
            }
            else
            {
                state.Probes[2].Release.SetResult();
                await secondAcknowledged.Task.WaitAsync(Deadline);
                Assert.IsFalse(state.Probes[1].ScopeDisposed);
                state.Probes[1].Release.SetResult();
            }
        }
        finally
        {
            state.Probes[1].Release.TrySetResult();
            state.Probes[2].Release.TrySetResult();
            await client.StopAsync().WaitAsync(Deadline);
            await server.StopAsync();
        }
    }
}

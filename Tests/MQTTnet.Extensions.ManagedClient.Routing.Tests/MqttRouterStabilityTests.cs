using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MQTTnet;
using MQTTnet.Extensions.ManagedClient.Routing.Attributes;
using MQTTnet.Extensions.ManagedClient.Routing.Extensions;
using MQTTnet.Extensions.ManagedClient.Routing.Routing;
using MQTTnet.Packets;

namespace MQTTnet.Extensions.ManagedClient.Routing.Tests;

[MqttRoute("stab")]
public sealed class BrokenController : MqttBaseController
{
    public BrokenController(IMissingService missing)
    {
    }

    [MqttRoute("broken")]
    public void Broken()
    {
    }
}

public interface IMissingService
{
}

[MqttRoute("stab")]
public sealed class SlowController : MqttBaseController
{
    public static int Calls { get; set; }

    [MqttRoute("slow")]
    public async Task Slow(CancellationToken cancellationToken)
    {
        await Task.Delay(5000, cancellationToken);
        Calls++;
    }

    [MqttRoute("fast")]
    public Task Fast()
    {
        Calls++;
        return Task.CompletedTask;
    }

    public static void Reset()
    {
        Calls = 0;
    }
}

[MqttRoute("conc")]
public sealed class ConcurrencyController : MqttBaseController
{
    public static int Calls => calls;
    private static int calls;
    public static int MaxObserved => maxObserved;

    private static int current;
    private static int maxObserved;

    [MqttRoute("work")]
    public async Task Work()
    {
        var snapshot = Interlocked.Increment(ref current);
        try
        {
            int observed;
            do
            {
                observed = maxObserved;
            }
            while (observed < snapshot &&
                Interlocked.CompareExchange(ref maxObserved, snapshot, observed) != observed);

            await Task.Delay(200);
            Interlocked.Increment(ref calls);
        }
        finally
        {
            Interlocked.Decrement(ref current);
        }
    }

    public static void Reset()
    {
        calls = 0;
        current = 0;
        maxObserved = 0;
    }
}

[TestClass]
public class MqttRouterStabilityTests
{
    private static (ServiceProvider sp, MqttRouter router) CreateRouter(
        MethodInfo action, MqttRoutingOptions? options = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var sp = services.BuildServiceProvider();
        var table = MqttRouteTableFactory.Create([action]);
        var logger = sp.GetRequiredService<ILogger<MqttRouter>>();
        return (sp, new MqttRouter(logger, table, new TypeActivatorCache(), options));
    }

    private static MqttApplicationMessageReceivedEventArgs CreateArgs(string? topic)
    {
        var msg = new MqttApplicationMessage { Topic = topic! };
        var packet = new MqttPublishPacket { Topic = topic! };
        return new MqttApplicationMessageReceivedEventArgs("client", msg, packet, (_, _) => Task.CompletedTask);
    }

    private static MqttRoutingOptions TimeoutOptions() =>
        new MqttRoutingOptions().WithJsonSerializerOptions()
            .WithMaxActionExecutionTime(TimeSpan.FromMilliseconds(150));

    [TestMethod]
    public async Task BrokenControllerActivation_MarksFailedWithoutThrowing()
    {
        var (sp, router) = CreateRouter(typeof(BrokenController).GetMethod("Broken")!);
        var args = CreateArgs("stab/broken");

        await router.OnIncomingApplicationMessage(sp, args, false);

        Assert.IsTrue(args.ProcessingFailed);
    }

    [TestMethod]
    public async Task MissingTopic_MarksFailedWithoutThrowing()
    {
        var (sp, router) = CreateRouter(typeof(SlowController).GetMethod("Fast")!);
        var args = CreateArgs(null);

        await router.OnIncomingApplicationMessage(sp, args, false);

        Assert.IsTrue(args.ProcessingFailed);
    }

    [TestMethod]
    public async Task SlowAction_WithTimeout_MarksFailedPromptly()
    {
        SlowController.Reset();
        var (sp, router) = CreateRouter(typeof(SlowController).GetMethod("Slow")!, TimeoutOptions());
        var args = CreateArgs("stab/slow");

        var sw = Stopwatch.StartNew();
        await router.OnIncomingApplicationMessage(sp, args, false);
        sw.Stop();

        Assert.IsTrue(args.ProcessingFailed);
        Assert.AreEqual(0, SlowController.Calls);
        Assert.IsTrue(sw.Elapsed < TimeSpan.FromSeconds(3));
    }

    [TestMethod]
    public async Task FastAction_WithTimeout_Succeeds()
    {
        SlowController.Reset();
        var (sp, router) = CreateRouter(typeof(SlowController).GetMethod("Fast")!, TimeoutOptions());
        var args = CreateArgs("stab/fast");

        await router.OnIncomingApplicationMessage(sp, args, false);

        Assert.IsFalse(args.ProcessingFailed);
        Assert.AreEqual(1, SlowController.Calls);
    }

    [TestMethod]
    public async Task ParallelDispatch_HandlesConcurrentMessages()
    {
        ConcurrencyController.Reset();
        var options = new MqttRoutingOptions().WithJsonSerializerOptions()
            .WithMaxConcurrentDispatches(4);
        var (sp, router) = CreateRouter(typeof(ConcurrencyController).GetMethod("Work")!, options);

        var sw = Stopwatch.StartNew();
        await Task.WhenAll(
            router.OnIncomingApplicationMessage(sp, CreateArgs("conc/work"), false),
            router.OnIncomingApplicationMessage(sp, CreateArgs("conc/work"), false),
            router.OnIncomingApplicationMessage(sp, CreateArgs("conc/work"), false),
            router.OnIncomingApplicationMessage(sp, CreateArgs("conc/work"), false));

        sw.Stop();

        Assert.AreEqual(4, ConcurrencyController.Calls);
        Assert.IsTrue(ConcurrencyController.MaxObserved >= 2);
        Assert.IsTrue(sw.Elapsed < TimeSpan.FromSeconds(10));
    }

    private sealed class ThrowingInterceptor : IRouteInvocationInterceptor
    {
        private readonly bool throwOnExecuting;
        private readonly bool throwOnExecuted;

        public ThrowingInterceptor(bool throwOnExecuting = true, bool throwOnExecuted = false)
        {
            this.throwOnExecuting = throwOnExecuting;
            this.throwOnExecuted = throwOnExecuted;
        }

        public Task<object?> RouteExecuting(MqttApplicationMessageReceivedEventArgs messageReceivedEventArgs) =>
            throwOnExecuting
                ? throw new InvalidOperationException("boom")
                : Task.FromResult<object?>(null);

        public Task RouteExecuted(object? correlationObject,
            MqttApplicationMessageReceivedEventArgs messageReceivedEventArgs, Exception? ex) =>
            throwOnExecuted
                ? throw new InvalidOperationException("boom")
                : Task.CompletedTask;
    }

    [TestMethod]
    public async Task ThrowingInterceptor_DoesNotPropagate()
    {
        var (sp, router) = CreateRouter(typeof(SlowController).GetMethod("Fast")!);
        var args = CreateArgs("stab/fast");

        await router.OnIncomingApplicationMessage(sp, args, false, new ThrowingInterceptor());

        Assert.IsTrue(args.ProcessingFailed);
    }

    [TestMethod]
    public async Task ThrowingRouteExecuted_DoesNotPropagate()
    {
        SlowController.Reset();
        var (sp, router) = CreateRouter(typeof(SlowController).GetMethod("Fast")!);
        var args = CreateArgs("stab/fast");

        await router.OnIncomingApplicationMessage(
            sp, args, false, new ThrowingInterceptor(throwOnExecuting: false, throwOnExecuted: true));

        Assert.AreEqual(1, SlowController.Calls);
    }
}

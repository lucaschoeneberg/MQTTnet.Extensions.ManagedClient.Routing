using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MQTTnet.Extensions.ManagedClient.Routing.Attributes;
using MQTTnet.Extensions.ManagedClient.Routing.Extensions;
using MQTTnet.Extensions.ManagedClient.Routing.Routing;
using MQTTnet.Packets;

namespace MQTTnet.Extensions.ManagedClient.Routing.Tests;

public sealed class DispatchProbe
{
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool ScopeDisposed { get; set; }
    public bool UsedDisposedScope { get; set; }
    public Exception? Failure { get; set; }
}

public sealed class DispatchState
{
    public ConcurrentDictionary<int, DispatchProbe> Probes { get; } = new();
}

public sealed class DispatchResource : IDisposable
{
    public DispatchProbe? Probe { get; set; }
    public bool IsDisposed { get; private set; }

    public void Dispose()
    {
        IsDisposed = true;
        if (Probe != null)
        {
            Probe.ScopeDisposed = true;
        }
    }
}

[MqttRoute("lifecycle")]
public sealed class DispatchLifecycleController(DispatchState state, DispatchResource resource) : MqttBaseController
{
    [MqttRoute("async/{id:int}")]
    public async Task Async(int id, CancellationToken cancellationToken)
    {
        var probe = Begin(id);
        using var registration = cancellationToken.Register(() => probe.Canceled.TrySetResult());
        await probe.Release.Task;
        Finish(probe);
    }

    [MqttRoute("async-no-token/{id:int}")]
    public Task AsyncWithoutToken(int id) => Async(id, CancellationToken.None);

    [MqttRoute("sync/{id:int}")]
    public void Sync(int id, CancellationToken cancellationToken)
    {
        var probe = Begin(id);
        using var registration = cancellationToken.Register(() => probe.Canceled.TrySetResult());
        probe.Release.Task.GetAwaiter().GetResult();
        Finish(probe);
    }

    [MqttRoute("sync-no-token/{id:int}")]
    public void SyncWithoutToken(int id) => Sync(id, CancellationToken.None);

    [MqttRoute("prefix/{id:int}")]
    public Task Prefix(int id, CancellationToken cancellationToken)
    {
        Sync(id, cancellationToken);
        return Task.CompletedTask;
    }

    [MqttRoute("value/{id:int}")]
    public async ValueTask Value(int id, CancellationToken cancellationToken) => await Async(id, cancellationToken);

    [MqttRoute("generic-value/{id:int}")]
    public async ValueTask<int> GenericValue(int id, CancellationToken cancellationToken)
    {
        await Async(id, cancellationToken);
        return id;
    }

    private DispatchProbe Begin(int id)
    {
        var probe = state.Probes[id];
        resource.Probe = probe;
        probe.Started.TrySetResult();
        return probe;
    }

    private void Finish(DispatchProbe probe)
    {
        probe.UsedDisposedScope = resource.IsDisposed;
        if (probe.Failure != null)
        {
            throw probe.Failure;
        }
    }
}

[TestClass]
public class MqttDispatchLifecycleTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private sealed class RecordingInterceptor : IRouteInvocationInterceptor
    {
        public TaskCompletionSource<Exception?> Executed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ExecutedCount { get; private set; }
        public bool ThrowOnExecuted { get; init; }
        public object Correlation { get; } = new();

        public Task<object?> RouteExecuting(MqttApplicationMessageReceivedEventArgs args) => Task.FromResult<object?>(Correlation);

        public Task RouteExecuted(object? correlationObject, MqttApplicationMessageReceivedEventArgs args, Exception? ex)
        {
            Assert.AreSame(Correlation, correlationObject);
            ExecutedCount++;
            Executed.TrySetResult(ex);
            if (ThrowOnExecuted)
            {
                throw new InvalidOperationException("interceptor failed");
            }

            return Task.CompletedTask;
        }
    }

    private static ServiceProvider CreateServices(DispatchState state, int concurrency, bool timeout = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(state);
        services.AddScoped<DispatchResource>();
        services.AddMqttControllers(opt =>
        {
            opt.FromAssemblies = [typeof(DispatchLifecycleController).Assembly];
            opt.WithMaxConcurrentDispatches(concurrency);
            if (timeout)
            {
                opt.WithMaxActionExecutionTime(TimeSpan.FromMilliseconds(150));
            }
        });
        return services.BuildServiceProvider();
    }

    private static MqttApplicationMessageReceivedEventArgs CreateArgs(string topic, Action? acknowledge = null) =>
        new("client", new MqttApplicationMessage { Topic = topic }, new MqttPublishPacket { Topic = topic },
            (_, _) =>
            {
                acknowledge?.Invoke();
                return Task.CompletedTask;
            });

    [TestMethod]
    public async Task ParallelDispatch_AwaitsActionAndReportsFailureToInterceptor()
    {
        var probe = new DispatchProbe { Failure = new InvalidOperationException("action failed") };
        var state = new DispatchState();
        state.Probes[1] = probe;
        using var sp = CreateServices(state, 2);
        var router = sp.GetRequiredService<MqttRouter>();
        var interceptor = new RecordingInterceptor();
        var args = CreateArgs("lifecycle/async/1");
        var dispatch = router.OnIncomingApplicationMessage(sp, args, false, interceptor);
        try
        {
            await probe.Started.Task.WaitAsync(TestTimeout);
            Assert.IsFalse(dispatch.IsCompleted);
            Assert.IsFalse(interceptor.Executed.Task.IsCompleted);
        }
        finally
        {
            probe.Release.TrySetResult();
            await dispatch.WaitAsync(TestTimeout);
        }

        Assert.IsTrue(args.ProcessingFailed);
        Assert.AreSame(probe.Failure, await interceptor.Executed.Task.WaitAsync(TestTimeout));
        Assert.IsTrue(probe.ScopeDisposed);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task BackgroundDispatch_AcknowledgesOnlyAfterSuccessfulCompletion(bool fail)
    {
        var probe = new DispatchProbe { Failure = fail ? new InvalidOperationException("action failed") : null };
        var state = new DispatchState();
        state.Probes[1] = probe;
        using var sp = CreateServices(state, 2);
        var router = sp.GetRequiredService<MqttRouter>();
        var interceptor = new RecordingInterceptor();
        var acknowledged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var args = CreateArgs("lifecycle/async/1", () => acknowledged.TrySetResult());
        try
        {
            await router.OnIncomingApplicationMessage(sp, args, false, interceptor, dispatchInBackground: true);
            await probe.Started.Task.WaitAsync(TestTimeout);
            Assert.IsFalse(args.AutoAcknowledge);
            Assert.IsFalse(acknowledged.Task.IsCompleted);
            Assert.IsFalse(interceptor.Executed.Task.IsCompleted);
        }
        finally
        {
            probe.Release.TrySetResult();
            await interceptor.Executed.Task.WaitAsync(TestTimeout);
        }

        if (fail)
        {
            Assert.IsTrue(args.ProcessingFailed);
            await Assert.ThrowsExactlyAsync<TimeoutException>(() =>
                acknowledged.Task.WaitAsync(TimeSpan.FromMilliseconds(150)));
        }
        else
        {
            await acknowledged.Task.WaitAsync(TestTimeout);
            Assert.IsFalse(args.ProcessingFailed);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task BackgroundDispatch_PreservesAcknowledgementOrderAndSkipsFailures(bool firstFails)
    {
        var state = new DispatchState();
        state.Probes[1] = new DispatchProbe { Failure = firstFails ? new InvalidOperationException("failed") : null };
        state.Probes[2] = new DispatchProbe();
        using var sp = CreateServices(state, 2);
        var router = sp.GetRequiredService<MqttRouter>();
        var acknowledgements = new ConcurrentQueue<int>();
        var acknowledged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondInterceptor = new RecordingInterceptor();
        try
        {
            await router.OnIncomingApplicationMessage(sp, CreateArgs("lifecycle/async/1", () => acknowledgements.Enqueue(1)),
                false, dispatchInBackground: true);
            await router.OnIncomingApplicationMessage(sp, CreateArgs("lifecycle/async/2", () =>
            {
                acknowledgements.Enqueue(2);
                acknowledged.TrySetResult();
            }), false, secondInterceptor, dispatchInBackground: true);
            await state.Probes[1].Started.Task.WaitAsync(TestTimeout);
            await state.Probes[2].Started.Task.WaitAsync(TestTimeout);

            state.Probes[2].Release.SetResult();
            await secondInterceptor.Executed.Task.WaitAsync(TestTimeout);
            Assert.IsTrue(acknowledgements.IsEmpty);
        }
        finally
        {
            state.Probes[1].Release.TrySetResult();
            state.Probes[2].Release.TrySetResult();
            await acknowledged.Task.WaitAsync(TestTimeout);
        }

        CollectionAssert.AreEqual(firstFails ? new[] { 2 } : new[] { 1, 2 }, acknowledgements.ToArray());
    }

    [TestMethod]
    public async Task BackgroundDispatch_PreservesCallerOwnedManualAcknowledgement()
    {
        var probe = new DispatchProbe();
        probe.Release.SetResult();
        var state = new DispatchState();
        state.Probes[1] = probe;
        using var sp = CreateServices(state, 2);
        var interceptor = new RecordingInterceptor();
        var calls = 0;
        var args = CreateArgs("lifecycle/async/1", () => Interlocked.Increment(ref calls));
        args.AutoAcknowledge = false;

        await sp.GetRequiredService<MqttRouter>().OnIncomingApplicationMessage(sp, args, false, interceptor,
            dispatchInBackground: true);
        await interceptor.Executed.Task.WaitAsync(TestTimeout);

        Assert.AreEqual(0, calls);
        await args.AcknowledgeAsync(CancellationToken.None);
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    [DataRow("async", 1)]
    [DataRow("async", 2)]
    [DataRow("async-no-token", 2)]
    [DataRow("sync", 1)]
    [DataRow("sync-no-token", 1)]
    [DataRow("prefix", 1)]
    [DataRow("value", 1)]
    [DataRow("generic-value", 1)]
    public async Task Timeout_KeepsScopeAndDispatchSlotUntilActionTerminates(string action, int concurrency)
    {
        var state = new DispatchState();
        for (var i = 0; i <= concurrency; i++)
        {
            state.Probes[i] = new DispatchProbe
            {
                Failure = i == 0 ? new InvalidOperationException("late failure after timeout") : null
            };
        }

        using var sp = CreateServices(state, concurrency, timeout: true);
        var router = sp.GetRequiredService<MqttRouter>();
        var args = Enumerable.Range(0, concurrency + 1).Select(i => CreateArgs($"lifecycle/{action}/{i}")).ToArray();
        var interceptors = Enumerable.Range(0, concurrency + 1).Select(_ => new RecordingInterceptor()).ToArray();
        var dispatches = Enumerable.Range(0, concurrency)
            .Select(i => router.OnIncomingApplicationMessage(sp, args[i], false, interceptors[i])).ToList();
        try
        {
            for (var i = 0; i < concurrency; i++)
            {
                await state.Probes[i].Started.Task.WaitAsync(TestTimeout);
                using var deadline = new CancellationTokenSource(TestTimeout);
                while (!args[i].ProcessingFailed)
                {
                    await Task.Delay(10, deadline.Token);
                }

                Assert.IsFalse(state.Probes[i].ScopeDisposed);
                Assert.IsFalse(dispatches[i].IsCompleted);
                if (!action.EndsWith("-no-token", StringComparison.Ordinal))
                {
                    await state.Probes[i].Canceled.Task.WaitAsync(TestTimeout);
                }
            }

            dispatches.Add(router.OnIncomingApplicationMessage(sp, args[concurrency], false));
            Assert.IsFalse(state.Probes[concurrency].Started.Task.IsCompleted);
        }
        finally
        {
            foreach (var probe in state.Probes.Values)
            {
                probe.Release.TrySetResult();
            }

            await Task.WhenAll(dispatches).WaitAsync(TestTimeout);
        }

        for (var i = 0; i < concurrency; i++)
        {
            Assert.IsTrue(args[i].ProcessingFailed);
            Assert.IsTrue(state.Probes[i].ScopeDisposed);
            Assert.IsFalse(state.Probes[i].UsedDisposedScope);
            Assert.IsInstanceOfType<TimeoutException>(await interceptors[i].Executed.Task.WaitAsync(TestTimeout));
            Assert.AreEqual(1, interceptors[i].ExecutedCount);
        }
    }

    [TestMethod]
    public async Task ThrowingCompletionInterceptor_IsCalledOnceAndMarksMessageFailed()
    {
        var probe = new DispatchProbe();
        probe.Release.SetResult();
        var state = new DispatchState();
        state.Probes[1] = probe;
        using var sp = CreateServices(state, 1);
        var interceptor = new RecordingInterceptor { ThrowOnExecuted = true };
        var args = CreateArgs("lifecycle/async/1");

        await sp.GetRequiredService<MqttRouter>().OnIncomingApplicationMessage(sp, args, false, interceptor);

        Assert.IsTrue(args.ProcessingFailed);
        Assert.AreEqual(1, interceptor.ExecutedCount);
    }
}

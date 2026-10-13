[![NuGet](https://img.shields.io/nuget/v/MQTTnet.Extensions.ManagedClient.Routing)](https://www.nuget.org/packages/MQTTnet.Extensions.ManagedClient.Routing/)
[![.NET CI](https://github.com/lucaschoeneberg/MQTTnet.Extensions.ManagedClient.Routing/actions/workflows/dotnet.yml/badge.svg)](https://github.com/lucaschoeneberg/MQTTnet.Extensions.ManagedClient.Routing/actions/workflows/dotnet.yml)
[![Security](https://github.com/lucaschoeneberg/MQTTnet.Extensions.ManagedClient.Routing/actions/workflows/security.yml/badge.svg)](https://github.com/lucaschoeneberg/MQTTnet.Extensions.ManagedClient.Routing/actions/workflows/security.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-brightgreen.svg)](./LICENSE)

# MQTTnet Extensions ManagedClient Routing

MQTTnet Extension ManagedClient Routing is a fork of https://github.com/IoTSharp/MQTTnet.Extensions.ManagedClient.Routing

This addon to MQTTnet provides the ability to define controllers and use attribute-based routing against message topics in a manner that is very similar to AspNet Core.

## Overview

MQTTnet.Extensions.ManagedClient.Routing extends MQTTnet's `ManagedMqttClient` with controller based routing. Use it when you want to organize MQTT handlers using MVC style controllers and attribute routes.

## Installation

```bash
dotnet add package MQTTnet.Extensions.ManagedClient.Routing
```

## Quickstart

A runnable end-to-end example lives in [`Samples/MqttRoutingSample`](Samples/MqttRoutingSample). It starts an in-process broker, routes two messages through a controller and exits:

```bash
dotnet run --project Samples/MqttRoutingSample
```

## Usage

Register your MQTT controllers in the DI container and enable routing:

```csharp
builder.Services.AddMqttControllers();

app.UseAttributeRouting(); // or ((ManagedMqttClient)managedClient).WithAttributeRouting(app.Services);
```

`AddMqttControllers` scans an assembly for controllers. By default the entry
assembly is used; pass assemblies explicitly (e.g. from tests or libraries):

```csharp
builder.Services.AddMqttControllers([typeof(TelemetryController).Assembly]);
```

### Example controller

```csharp
public class TelemetryController : MqttBaseController
{
    [MqttRoute("telemetry/{deviceId:int}")]
    public async Task OnTemperature(int deviceId, [FromPayload] Reading reading)
    {
        Console.WriteLine($"Device {deviceId}: {reading.Celsius} °C");
        await Ok();
    }
}
```

### Routes

A route template is a `/`-separated topic pattern. Literals match
case-insensitively, `{name}` captures a segment, `{name:type}` validates and
converts it, `{name?}` / `{name:type?}` are optional, `{*rest}` catches all
remaining segments. Supported constraints: `bool`, `datetime`, `decimal`,
`double`, `float`, `guid`, `int`, `long` (each optionally nullable with `?`).

`[controller]` and `[action]` tokens are replaced with the controller (minus
`Controller` suffix) and action names; escape them with double brackets
(`[[controller]]`). A parameterless `[MqttRoute]` on an action falls back to
the action name; on a controller it adds no route prefix.

### Action signatures

Actions support `void`, `Task`, `Task<T>`, `ValueTask` and `ValueTask<T>`
return types. Returned values are awaited and then discarded; throw to mark
processing as failed. Parameters are bound from route values, from the JSON
payload via `[FromPayload]`, or from DI context:

- `CancellationToken` tracks routing shutdown and, for `ManagedMqttClient`,
  the connection that delivered the message. `MaxActionExecutionTime` adds an execution deadline.
  This token is cancellable even without an action deadline.
- The current message is available via `MqttBaseController.Message` /
  `MqttContext`, or a `[MqttControllerContext]` property on custom
  controllers.

```csharp
[MqttRoute("telemetry/{deviceId}")]
public async Task<int> OnTelemetry(int deviceId, [FromPayload] Reading reading, CancellationToken ct)
{
    await _store.SaveAsync(deviceId, reading, ct);
    return deviceId;
}
```

### Interceptors

To execute code before and after each controller action, implement
`IRouteInvocationInterceptor` and hook it up when adding the controllers:

```csharp
builder.Services.AddMqttControllers(opt =>
    opt.WithRouteInvocationInterceptor<MyInterceptor>());
```

`RouteExecuting` is called before the handler runs and `RouteExecuted`
afterwards (with the correlation object and any exception). See the
[architecture overview](docs/architecture.md#extension-points) for details.

### Failure handling

Each message is attempted once: handler, binding and interceptor failures
mark the message as failed (`ProcessingFailed`) and are logged, but never
fault the MQTT dispatch loop. There is no built-in retry or dead-letter
queue — count failures in an `IRouteInvocationInterceptor` and forward
poison messages to a dead-letter topic from there if needed.

Bound the blast radius of slow actions with
`opt.WithMaxActionExecutionTime(...)` and, for independent messages only,
raise `opt.WithMaxConcurrentDispatches(...)` (default 1, ordered).

The deadline also covers synchronous actions and synchronous work before an
async action returns its task. On expiry the message is marked failed and
the action token is canceled. Cancellation is cooperative: routing retains
the DI scope and concurrency slot until the action actually terminates.
An action that ignores cancellation can therefore hold a slot indefinitely.

In parallel mode routing disables MQTTnet's automatic acknowledgement and
acknowledges successful messages after the action and `RouteExecuted` finish,
in receive order within the original connection. Old deliveries cannot
acknowledge packets on a replacement connection after reconnect.
The connection lifetime is captured before any managed receive subscriber runs;
a delivery held by an earlier subscriber across reconnect is rejected by routing.
Failed QoS 1/2 deliveries remain unacknowledged; broker redelivery depends on
the MQTT session. If automatic acknowledgement was already disabled before
routing, the caller retains responsibility for acknowledging the message.
Other receive handlers must not acknowledge a routed message prematurely or
modify its processing result after handing it to background routing.

### Configuration

`MqttRoutingOptions` (registered as singleton by `AddMqttControllers`):

- `SerializerOptions`: JSON options for `[FromPayload]` deserialization
  (defaults to web defaults with full Unicode support; override via
  `opt.WithJsonSerializerOptions(...)`).
- `FromAssemblies`: assemblies scanned for controllers (set via the
  `AddMqttControllers` overloads).
- `RouteInvocationInterceptor`: interceptor type (set via
  `WithRouteInvocationInterceptor<T>()`).
- `MaxActionExecutionTime`: optional positive action deadline (default
  `null`), up to the .NET timer maximum of 4,294,967,294 milliseconds;
  set via `WithMaxActionExecutionTime`. Invalid values throw
  `InvalidOperationException` during `AddMqttControllers`.
- `MaxConcurrentDispatches`: maximum active dispatches, including timed-out
  actions still running (default `1`, minimum `1`); set via
  `WithMaxConcurrentDispatches`. Invalid values throw
  `InvalidOperationException` during `AddMqttControllers`.

Managed client reconnects grow exponentially with equal jitter (50–100% of
the capped delay), with a one-second floor. `MaxReconnectDelay` defaults to
two minutes and must be between one second and the .NET timer maximum of
4,294,967,294 milliseconds; values outside this range throw
`ArgumentException` during `StartAsync`. Even very small base delays
eventually reach this cap. Every new `StartAsync` resets the failure count.
`StopAsync` called outside a callback waits for publishing, registered routed
actions, and storage writes before releasing storage. Inside a managed-client
callback or routed action it requests shutdown and returns without waiting
for its own task; a later external call awaits full completion. `StartAsync`
throws `InvalidOperationException` while shutdown is still in progress.

### Resource ownership and shutdown

Routing awaits controller cleanup before disposing its asynchronous DI scope.
Controllers and dependencies may implement `IDisposable`, `IAsyncDisposable`,
or both; asynchronous cleanup takes precedence. Mark public cleanup methods
with `[NonAction]` so controller discovery does not expose them as routes.

`AddMqttControllers` registers a hosted service that drains accepted dispatches
during `Host.StopAsync`. For standalone clients, await `client.StopAsync()`
outside a callback before disposing the service provider. Shutdown cancels
action tokens and waits for actual termination, so handlers and cleanup
operations must eventually complete. Disposing a container alone does not
replace this shutdown sequence.

## Versioning

Versions follow [Semantic Versioning](https://semver.org/) and are derived
from `v*` git tags via MinVer. See [CHANGELOG.md](CHANGELOG.md).

## MIT License
This project is released under the [MIT License](./LICENSE).

## Contributing
See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines on how to open issues, submit pull requests and run the test suite.

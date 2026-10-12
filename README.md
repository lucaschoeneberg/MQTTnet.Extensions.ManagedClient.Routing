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

- `CancellationToken` is bound to `CancellationToken.None` (the MQTT dispatch
  pipeline is event based and has no ambient token).
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

### Configuration

`MqttRoutingOptions` (registered as singleton by `AddMqttControllers`):

- `SerializerOptions`: JSON options for `[FromPayload]` deserialization
  (defaults to web defaults with full Unicode support; override via
  `opt.WithJsonSerializerOptions(...)`).
- `FromAssemblies`: assemblies scanned for controllers (set via the
  `AddMqttControllers` overloads).
- `RouteInvocationInterceptor`: interceptor type (set via
  `WithRouteInvocationInterceptor<T>()`).

## Versioning

Versions follow [Semantic Versioning](https://semver.org/) and are derived
from `v*` git tags via MinVer. See [CHANGELOG.md](CHANGELOG.md).

## MIT License
This project is released under the [MIT License](./LICENSE).

## Contributing
See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines on how to open issues, submit pull requests and run the test suite.

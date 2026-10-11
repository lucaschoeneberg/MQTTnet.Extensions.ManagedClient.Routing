[![NuGet Badge](https://buildstats.info/nuget/MQTTnet.Extensions.ManagedClient.Routing)]([https://www.nuget.org/packages/MQTTnet.Extensions.ManagedClient.Routing](https://www.nuget.org/packages/MQTTnet.Extensions.ManagedClient.Routing/))
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

## Usage

Register your MQTT controllers in the DI container and enable routing:

```csharp
builder.Services.AddMqttControllers();

app.UseAttributeRouting(); // or managedClient.WithAttributeRouting(app.Services);
```

To execute code before and after each controller action you can register an
`IRouteInvocationInterceptor`. Implement the interface and hook it up when
adding the controllers:

```csharp
builder.Services.AddMqttControllers(opt =>
    opt.WithRouteInvocationInterceptor<MyInterceptor>());
```

`RouteExecuting` is called before the handler runs and `RouteExecuted` afterwards.
See the [architecture overview](docs/architecture.md#extension-points) for
details.

### Example controller

```csharp
public class TelemetryController : MqttBaseController
{
    [MqttRoute("telemetry/temperature")]
    public Task OnTemperature(string payload)
    {
        Console.WriteLine($"Temp: {payload}");
        return Ok();
    }
}
```

### Action signatures

Actions support `void`, `Task`, `Task<T>`, `ValueTask` and `ValueTask<T>`
return types. Returned values are awaited and then discarded; throw to mark
processing as failed. A `CancellationToken` parameter is bound to
`CancellationToken.None` (the MQTT dispatch pipeline is event based and has
no ambient token):

```csharp
[MqttRoute("telemetry/{deviceId}")]
public async Task<int> OnTelemetry(int deviceId, [FromPayload] Reading reading, CancellationToken ct)
{
    await _store.SaveAsync(deviceId, reading, ct);
    return deviceId;
}
```

A parameterless `[MqttRoute]` on an action falls back to the action name;
on a controller it adds no route prefix.

## MIT License
This project is released under the [MIT License](./LICENSE).

## Contributing
See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines on how to open issues, submit pull requests and run the test suite.
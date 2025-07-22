[![NuGet Badge](https://buildstats.info/nuget/MQTTnet.Extensions.ManagedClient.Routing)]([https://www.nuget.org/packages/MQTTnet.AspNetCore.Routing](https://www.nuget.org/packages/MQTTnet.Extensions.ManagedClient.Routing/))
[![License: MIT](https://img.shields.io/badge/License-MIT-brightgreen.svg)](./LICENSE)

# MQTTnet AspNetCore Routing

MQTTnet Extension ManagedClient Routing is a fork of https://github.com/IoTSharp/MQTTnet.AspNetCore.Routing

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

## MIT License
This project is released under the [MIT License](./LICENSE).

## Contributing
See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines on how to open issues, submit pull requests and run the test suite.
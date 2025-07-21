[![NuGet Badge](https://buildstats.info/nuget/MQTTnet.Extensions.ManagedClient.Routing)](https://www.nuget.org/packages/MQTTnet.Extensions.ManagedClient.Routing/)
[![License: MIT](https://img.shields.io/badge/License-MIT-brightgreen.svg)](https://github.com/lucaschoeneberg/MQTTnet.Extensions.ManagedClient.Routing/LICENSE)

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

See https://github.com/IoTSharp/MQTTnet.AspNetCore.Routing/LICENSE.

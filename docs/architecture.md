# Architecture Overview

This project adds ASP.NET‑style routing on top of MQTTnet's managed client. Controllers are discovered at startup and incoming messages are dispatched to matching controller actions.

## Routing Flow

1. **Route table creation** – `MqttRouteTableFactory` scans the configured assemblies for classes decorated with [`MqttControllerAttribute`](../Source/Attributes/MqttControllerAttribute.cs). It reads the [`MqttRouteAttribute`](../Source/Attributes/MqttRouteAttribute.cs) from controllers and action methods to build an in‑memory `MqttRouteTable`.
2. **Message dispatch** – `MqttRouter` receives `MqttApplicationMessageReceivedEventArgs` from the managed client. The topic is matched against the `MqttRouteTable`. When a match is found a controller instance is created through dependency injection and the action method is invoked.
3. **Interception** – If an implementation of `IRouteInvocationInterceptor` is registered it is called before and after the controller action, allowing custom processing or logging.

## Key Classes

### MqttBaseController

`MqttBaseController` provides common conveniences for controllers. It exposes the current `MqttApplicationMessageReceivedEventArgs` via `MqttContext`, the incoming message via `Message`, and helper methods like `Ok()` and `BadMessage()` for signalling processing results.

### Routing attributes

- **`MqttControllerAttribute`** – Marks a class as a controller that can contain routed actions.
- **`MqttRouteAttribute`** – Specifies the topic template for a controller or action. Tokens such as `[controller]` and `[action]` are replaced with the controller and method names. Use double brackets like `[[controller]]` or `[[action]]` to include the literal token text.
- **`FromPayloadAttribute`** – Applied to method parameters to deserialize the message payload with the configured JSON options.
- **`MqttControllerContextAttribute`** – Applied to a property when using custom controllers so that the routing infrastructure can set the `MqttControllerContext` instance.

### Route constraints

Route templates may include type constraints like `{id:int}` or optional parameters `{value:float?}`. These are parsed into `RouteConstraint` instances (`TypeRouteConstraint`, `OptionalTypeRouteConstraint`) which validate and convert segment values before invoking the action.

## Extension Points

The `IRouteInvocationInterceptor` interface allows hooking into the routing pipeline. Implementations can inspect or modify the message before an action executes and observe the result or exceptions after execution.

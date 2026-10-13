# Architecture Overview

This project adds ASP.NET‑style routing on top of MQTTnet's managed client. Controllers are discovered at startup and incoming messages are dispatched to matching controller actions.

## Routing Flow

1. **Route table creation** – `MqttRouteTableFactory` scans the configured assemblies for classes decorated with [`MqttControllerAttribute`](../Source/Attributes/MqttControllerAttribute.cs). It reads the [`MqttRouteAttribute`](../Source/Attributes/MqttRouteAttribute.cs) from controllers and action methods to build an in‑memory `MqttRouteTable`. Routes are ordered most-specific-first (literals beat parameters, constrained beats plain, optional and catch-all go last); ambiguous pairs throw at startup.
2. **Message dispatch** – `MqttRouter` receives `MqttApplicationMessageReceivedEventArgs` from the managed client. The topic is matched against the `MqttRouteTable`. When a match is found a controller instance is created in a fresh DI scope and the action method is invoked. Unmatched topics set `ProcessingFailed` unless `allowUnmatchedRoutes` is enabled.
3. **Interception** – If an implementation of `IRouteInvocationInterceptor` is registered it is called before and after the controller action, allowing custom processing or logging.

## Action invocation

Supported action return types are `void`, `Task`, `Task<T>`, `ValueTask` and `ValueTask<T>`. Results are awaited and discarded; exceptions mark the message as failed (`ProcessingFailed = true`).

Parameters are bound in order: `CancellationToken` receives a routing lifetime token linked to the message's original managed-client connection and, when configured, its action deadline; `[FromPayload]` parameters are JSON-deserialized with the configured `MqttRoutingOptions.SerializerOptions`; everything else is matched against route values (with invariant-culture conversion as fallback).

The singleton router owns a semaphore shared by its receive callbacks. Direct dispatch calls await processing. For MQTTnet's serialized receive pipeline, an opt-in parallel callback first reserves a slot and disables automatic acknowledgement, then processes the action and both interceptor hooks in the background. Acknowledgement chains belong to individual connection lifetimes, preserving receive order as required by [MQTT 3.1.1 §4.6](https://docs.oasis-open.org/mqtt/mqtt/v3.1.1/os/mqtt-v3.1.1-os.html). Closing a connection cancels its token, which also fences old acknowledgements; a replacement connection gets a fresh token and chain. Pre-existing manual acknowledgement ownership is preserved.

The managed client captures the connection token at the receive callback boundary, before invoking any receive subscribers. The existing asynchronous callback scope carries that immutable token to routing, including across subscriber awaits. Routing rejects a delivery whose captured connection has closed, rather than reading the replacement connection's token; overlapping receive flows each retain their own token.

With `MaxActionExecutionTime`, invocation runs on a worker so the deadline also covers `void` actions and synchronous prefixes of async actions. At the deadline the message fails and the token is canceled. The router drains the underlying invocation before disposing its scope or releasing its slot; arbitrary .NET code cannot be forcibly stopped safely. Late exceptions are observed, and `RouteExecuted` receives the timeout after the action terminates. Cooperative actions should pass the token to their asynchronous operations.

Controller instances created by the activator are owned by routing and disposed explicitly, preferring `IAsyncDisposable`. Their asynchronous DI scopes are disposed afterwards. The dispatch slot remains reserved until invocation, controller cleanup, scope cleanup, interception, and acknowledgement have finished.

The managed client tracks and drains its outbound publishing worker and all registered dispatch groups owned by that client before releasing storage. Callback scopes make StopAsync reentrant: a call inside a managed callback or routed action requests shutdown without joining itself, while external callers await the shared shutdown task. Restart is rejected until that task completes. Successful publishes finish queue and storage cleanup even if shutdown canceled the worker. Reconnect delays grow to the configured cap without a fixed exponent limit, and each new session resets the consecutive-failure count.

For a running host, the registered routing hosted service drains the router during Host.StopAsync, before DI teardown. Standalone callers must await managed-client StopAsync before disposing the service provider. Router disposal stops admission, cancels action tokens and waits for its accepted dispatches; cooperative cancellation cannot forcibly stop arbitrary user code.

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

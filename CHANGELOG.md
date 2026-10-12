# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Public API tracking (`Source/PublicAPI.Shipped.txt`): new APIs must go
  to `PublicAPI.Unshipped.txt` first; CI fails otherwise.
- Symbol packages (`.snupkg`) are built and published alongside the NuGet
  package.
- First-party line coverage gate (75%, vendored fork excluded) enforced in
  CI; release notes are now drafted automatically per merged PR.
- `ManagedMqttClientOptions.MaxReconnectDelay`: reconnect delay now grows
  exponentially with jitter (fleet-safe) instead of hammering at a fixed
  interval.
- `MqttRoutingOptions.MaxActionExecutionTime`: cooperative deadlines cover
  synchronous and asynchronous actions; overruns fail the message while
  retaining its scope and dispatch slot until the action terminates.
- `MqttRoutingOptions.MaxConcurrentDispatches`: opt-in bounded parallel
  dispatch for independent messages (default 1, ordered).
- `ManagedMqttClientStorageManager` is now disposable.

### Changed

- Package and assembly versions are now derived from `v*` git tags via
  MinVer instead of hardcoded csproj values.
- `ManagedMqttClientOptions.MaxPendingMessages` defaults to 1000 instead of
  unbounded, bounding offline memory usage.
- Handler, binding and interceptor failures no longer propagate into the
  MQTT dispatch loop; messages are marked failed and logged instead.

### Fixed

- Parallel routing defers acknowledgement until the action and interceptor
  finish, and reports action failures to the interceptor.
- Timed-out actions retain their DI scope and concurrency slot; synchronous
  invocations are included in the deadline and late exceptions are observed.
- Publishing workers are drained before storage disposal and complete
  post-publish storage cleanup during shutdown.
- Reconnect backoff grows to the configured maximum even for tiny base
  delays, and resets when starting a new managed session.

## [1.2.0] - 2026-10-11

### Added

- Action return types `Task<T>`, `ValueTask` and `ValueTask<T>` are now
  supported. Results are awaited and then discarded; throw to mark message
  processing as failed.
- `CancellationToken` action parameters are now bound (to
  `CancellationToken.None`, as the event-based dispatch pipeline has no
  ambient token).
- A parameterless `[MqttRoute]` on an action now falls back to the action
  name instead of failing at startup; on a controller it adds no prefix.
- `CHANGELOG.md` (this file) following Keep a Changelog and Semantic
  Versioning.
- Repository foundation: `global.json` SDK pin, `Directory.Build.props`
  (analyzers, nullable, NuGet audit, SourceLink, deterministic builds),
  `Directory.Packages.props` (central package management), `.editorconfig`,
  `lefthook.yml` (gitleaks + format pre-commit gates), `.gitleaks.toml`,
  `.semgrepignore`, Dependabot (NuGet + GitHub Actions).
- CI: hardened `.NET CI` workflow (pinned actions, TruffleHog secret scan,
  NuGet audit restore gates, test coverage summary) and new `Security`
  workflow (Trivy FS/config, transitive vulnerability audit, blocking
  Semgrep SAST).

### Changed

- Target framework `net9.0` -> `net10.0` (.NET 8/9 reach end of support on
  2026-11-10).
- `MQTTnet` / `MQTTnet.AspNetCore` `5.0.1.1416` -> `5.2.0.1603`.
- Test stack: `Microsoft.NET.Test.Sdk` -> `18.10.1`, `MSTest.*` -> `4.5.1`,
  `coverlet.collector` for coverage.
- Nullable reference types and .NET analyzers are now enabled for all
  first-party code; the vendored `Source/ManagedClient` fork is explicitly
  excluded via `#nullable disable` (fixes belong upstream).
- Resource-leak analyzer gates (`CA1001`, `CA2000`, `CA2213`, plus
  `CA1063`/`CA1816`/`CA2012`/`CA1821`/`CA1849`) warn locally and fail CI.
- Logging uses structured message templates instead of interpolation.

### Fixed

- `AddMqttControllers()` without explicit assemblies threw an obscure
  `NullReferenceException` when no entry assembly exists; it now throws an
  `InvalidOperationException` with guidance.
- `MqttRouteAttribute.Name` is now nullable and documented; broken `cref`
  doc references fixed.
- `MqttBaseController.Ok()`/`Accepted()`/`BadMessage()` XML docs now state
  honestly that they complete without effect (no reply is published).
- Test suite: `tcs.Task.Result` blocking call replaced with `await`,
  undisposed test clients/servers now disposed, assertion argument order
  corrected.

### Security

- `Microsoft.SourceLink.GitHub` -> `10.0.401` (transitive
  `Microsoft.Build.Tasks.Git 8.0.0` had a known moderate vulnerability,
  [GHSA-23fw-v26w-5fgq](https://github.com/advisories/GHSA-23fw-v26w-5fgq)).
- No vulnerable packages (direct or transitive) per `dotnet list package
  --vulnerable`.

## [1.1.0] - 2025-07-22

- Package renamed to `MQTTnet.Extensions.ManagedClient.Routing`.
- Repository URLs and README packaging updated.
- Managed client extension and options builder tests added.

## [1.0.0] - 2024-12-05

- Initial release as a fork of
  `IoTSharp/MQTTnet.Extensions.ManagedClient.Routing`: attribute-based
  MQTT topic routing with MVC-style controllers on top of MQTTnet's managed
  client.

[Unreleased]: https://github.com/lucaschoeneberg/MQTTnet.Extensions.ManagedClient.Routing/compare/v1.2.0...HEAD
[1.2.0]: https://github.com/lucaschoeneberg/MQTTnet.Extensions.ManagedClient.Routing/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/lucaschoeneberg/MQTTnet.Extensions.ManagedClient.Routing/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/lucaschoeneberg/MQTTnet.Extensions.ManagedClient.Routing/releases/tag/v1.0.0

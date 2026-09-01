# Phase 3 — Observability: Detailed Implementation Plan

## 1. Goal / why

Sox currently has exactly one observability surface: five public C# events (`OnConnection`, `OnDisconnection`, `OnTextMessage`, `OnBinaryMessage`, `OnError`, `OnFrame`) that a consumer must wire up by hand, in-process, while the app is running. There is no way to:

- See what Sox is doing *without writing code* — no console/file/structured logs a consumer can point `Serilog`/`Seq`/`ELK` at.
- Ask "how many connections are open right now, how many messages/bytes have flowed, what's the error rate, what close codes are we seeing" without hooking every event and hand-rolling counters.
- Attach `dotnet-counters`/`dotnet-trace`/OpenTelemetry to a running Sox server the way you can with literally every other piece of the modern .NET stack (Kestrel, HttpClient, SqlClient, gRPC all ship `ILogger` + `Meter`).

You can't debug what you can't see — right now the only way to find out a production Sox server is silently closing connections with `MessageTooBig` or hitting the `MaxFrameBytes` ceiling is to have manually wired `OnError`/`OnDisconnection` in advance and be tailing console output. This phase adds the two de-facto-standard .NET observability primitives (`ILogger` for structured logs, `Meter` for metrics) **additively** — nothing about the existing event-based API changes, and both are fully optional/zero-cost-by-default for existing consumers.

This phase is explicitly scoped as additive/library-internal instrumentation only. It does not touch Phase 1 (protocol correctness) or Phase 2 (robustness) logic — it just adds visibility into the code paths those phases already exercise (and every log/metric call site below doubles as a natural insertion point for Phase 2's keepalive-timeout/idle-timeout/concurrency-limit code, so landing this in parallel with or after Phase 2 makes those additions cheaper).

**Cross-cutting note (see `plan/00-overview.md` finding 3):** Phase 2 introduces an options-object API pattern (`WebSocketServerOptions`/`ConnectionOptions`). This phase's `ILogger` should become a property on that same options object rather than a competing flat constructor parameter — land Phase 2 first, or coordinate directly if landing in parallel.

## 2. NuGet packages to add to `Sox/Sox.csproj`

Current `Sox.csproj` targets `netstandard2.1`, `LangVersion 9.0`, and only references `System.Threading.Channels 5.0.0` and `System.IO.Pipelines 9.0.4`. Add:

```xml
<ItemGroup>
  <PackageReference Include="System.Threading.Channels" Version="5.0.0" />
  <PackageReference Include="System.IO.Pipelines" Version="9.0.4" />
  <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.2" />
  <PackageReference Include="System.Diagnostics.DiagnosticSource" Version="8.0.1" />
</ItemGroup>
```

Verification notes:

- **`Microsoft.Extensions.Logging.Abstractions`**: multi-targets `netstandard2.0`/`netstandard2.1`/`net6.0`+ starting well before 8.x. Pick the `8.0.x` line (matches the LTS most consuming apps will already have in their graph, minimizing version-conflict warnings via NuGet's lowest-applicable-version resolution) rather than reaching for a `9.x`/`10.x` version the library doesn't need — `Abstractions` package surface (`ILogger`, `ILogger<T>`, `LogLevel`, `[LoggerMessage]`) has been stable since 6.0. **`[LoggerMessage]` requires `Microsoft.Extensions.Logging.Abstractions >= 6.0.0`** (it shipped with .NET 6) — 8.0.2 satisfies that.
- **`System.Diagnostics.DiagnosticSource`**: this needs explicit verification — on `netstandard2.1`, `System.Diagnostics.Metrics.Meter`/`Counter<T>`/`Histogram<T>`/`UpDownCounter<T>` are **not** part of the netstandard2.1 reference assembly's BCL surface; they only became part of the BCL starting with `net6.0`. For any TFM below net6.0 (including netstandard2.1, which Sox targets), you must add an explicit `System.Diagnostics.DiagnosticSource` package reference — that package ships a netstandard2.0-compatible build containing `System.Diagnostics.Metrics.*` as "polyfill" types that forward/interop correctly when the consuming app itself runs on a modern .NET runtime. Use `8.0.1` (or the latest 8.0.x patch) to match the Logging.Abstractions major version and avoid dragging in a `9.x` dependency chain unnecessarily. This is a **required, non-optional** addition — without it, `new Meter(...)` will not compile on netstandard2.1.
- Both packages are widely present transitively already in almost any modern .NET app, so this is very unlikely to create version conflicts for consumers.
- After adding, run `dotnet build Sox/Sox.csproj` and check for any `NU1605`/downgrade warnings given `System.IO.Pipelines 9.0.4` is already newer than net8 baseline — expect none, but verify.

## 3. `ILogger` integration design

### 3.1 How `WebSocketServer`/`Connection` accept an `ILogger`

Add an optional constructor parameter to both, defaulting to `NullLogger<T>.Instance` so it's fully non-breaking:

```csharp
// WebSocketServer.cs
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

private readonly ILogger<WebSocketServer> _logger;

public WebSocketServer(IPAddress ipAddress,
    int port,
    int? maxMessageBytes = default,
    X509Certificate2 x509Certificate = default,
    int connectionReadTimeoutMs = 5000,
    ILogger<WebSocketServer> logger = default)
{
    ...
    _logger = logger ?? NullLogger<WebSocketServer>.Instance;
}
```

```csharp
// Connection.cs
private readonly ILogger _logger; // ILogger, not ILogger<Connection> - Connection is created
                                   // internally by WebSocketServer per-accept, not by a DI
                                   // container, so a plain ILogger passed down from the server
                                   // (server's own ILogger<WebSocketServer>) avoids
                                   // manufacturing a fake generic-category logger per connection.

public Connection(Stream stream, int maxMessageBytes, ILogger logger = default)
{
    ...
    _logger = logger ?? NullLogger.Instance;
}
```

Design decision: **do not** require `ILoggerFactory` — just thread a single `ILogger` down from `WebSocketServer` into each `Connection` it constructs (`new Connection(stream, MaxMessageBytes, _logger)` in `ProcessHandshake`). This keeps the surface area minimal and matches how a small, dependency-light library like Sox is already built (no DI container assumption anywhere in the codebase today). Every log entry should include the connection Id as a scope/structured field (`_logger.BeginScope` or just interpolate `connection.Id` into the message args) so logs from concurrent connections are attributable.

### 3.2 Log call sites (exact location, level, message)

All in `WebSocketServer.cs` unless noted. Level rationale: `Trace` for high-frequency per-frame chatter, `Debug` for per-connection lifecycle/handshake detail, `Information` reserved for server-level start/stop (low-frequency, operationally significant), `Warning` for recoverable/expected-bad-input conditions (protocol violations, oversized messages, malformed handshakes — the peer's fault, not a Sox bug), `Error` for unexpected exceptions.

| # | Location | Level | Message |
|---|---|---|---|
| 1 | `Start()`, right after `_server.Start()` | Information | `"WebSocketServer listening on {Protocol}://{IpAddress}:{Port}"` |
| 2 | `Start()` catch block (accept-loop exception) | Error (with exception) | `"Unhandled exception in accept loop"` |
| 3 | `Stop()`, before cancel | Information | `"WebSocketServer stopping, draining {ConnectionCount} connection(s)"` |
| 4 | `Stop()`, after `Task.WhenAll` | Information | `"WebSocketServer stopped"` |
| 5 | `HandleHttpUpgrade`, before `AuthenticateAsServerAsync` (only if `X509Certificate != null`) | Trace | `"Starting TLS handshake for {RemoteEndPoint}"` |
| 6 | `HandleHttpUpgrade`, non-websocket-upgrade branch (ties into Phase 1's 400/426 response work) | Warning | `"Rejected non-websocket-upgrade HTTP request from {RemoteEndPoint}"` |
| 7 | `HandleHttpUpgrade` catch block | Warning if it's a benign network-reset/`IOException`/`SocketException` (client disconnected mid-handshake — routine), Error otherwise (with exception) | `"Handshake failed for {RemoteEndPoint}"` |
| 8 | `ProcessHandshake`, after `connection.State = ConnectionState.Open` | Debug | `"Connection {ConnectionId} accepted, handshake complete"` |
| 9 | `StartClientHandler` catch block, before `OnError?.Invoke` | Error (with exception) | `"Unhandled exception reading frames for connection {ConnectionId}"` |
| 10 | `HandleFrame`, `!frame.Headers.ShouldMask` branch | Warning | `"Connection {ConnectionId} sent an unmasked frame, closing per RFC6455 section 5.1"` |
| 11 | `HandleFrame` default branch (unrecognized/reserved opcode) | Warning | `"Connection {ConnectionId} sent frame with unexpected OpCode {OpCode} in state {State}, closing"` |
| 12 | `Connection.TryCompleteMessage`, size-rejection branch | Warning | `"Connection {ConnectionId} exceeded max message size, closing with MessageTooBig"` |
| 13 | `HandleDataFrame` default branch (unexpected `message.Type`) | Warning | `"Connection {ConnectionId} produced message of unexpected type {MessageType}, closing"` |
| 14 | `HandleCloseFrame` | Debug | `"Connection {ConnectionId} sent Close frame"` |
| 15 | `CloseConnection` | Debug | `"Closing connection {ConnectionId} with reason {CloseStatusCode}"` |
| 16 | `HandlePongFrame` (or inside `Connection.UpdateLastPong`) | Trace | `"Connection {ConnectionId} pong received"` |

In `Connection.cs`:

| # | Location | Level | Message |
|---|---|---|---|
| 17 | `Send(string)`/`Send(byte[])`, on entry or exit | Trace | `"Connection {ConnectionId} sending {ByteCount} byte(s)"` |
| 18 | `Close(CloseStatusCode)`, entry | Debug | `"Connection {ConnectionId} closing with {Reason}"` |
| 19 | `TryCompleteMessage`, `!_messageAssembler.TryAppend` branch | Warning | `"Connection {ConnectionId} message exceeded max size, closing"` |
| 20 | `Ping(...)` catch `(IOException)` block | Debug | `"Connection {ConnectionId} ping failed, disposing"` (this is routine — a dead peer, not a bug) |
| 21 | `Dispose(bool)` | Trace | `"Connection {ConnectionId} disposed"` |

### 3.3 `[LoggerMessage]` source-generated logging

**Yes, use it, and it's fully compatible.** `[LoggerMessage]` needs:
- `Microsoft.Extensions.Logging.Abstractions >= 6.0.0` for the attribute type — satisfied by the `8.0.2` reference above.
- A Roslyn/SDK version that ships the source generator — this is a build-time-only requirement on whichever SDK compiles the project, **not** a runtime/TFM requirement.
- C# 9+ for `partial` methods with logging attributes — the project's `LangVersion` is already `9.0`. Confirmed compatible.

Recommended pattern — a static partial `Log` class colocated per type, using the `[LoggerMessage]` attribute for every call site in the tables above (avoids boxing/allocation and the `IsEnabled` check duplication):

```csharp
internal static partial class WebSocketServerLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "WebSocketServer listening on {Protocol}://{IpAddress}:{Port}")]
    public static partial void Listening(ILogger logger, Protocol protocol, IPAddress ipAddress, int port);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error,
        Message = "Unhandled exception in accept loop")]
    public static partial void AcceptLoopException(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 10, Level = LogLevel.Warning,
        Message = "Connection {ConnectionId} sent an unmasked frame, closing per RFC6455 section 5.1")]
    public static partial void UnmaskedFrame(ILogger logger, string connectionId);

    // ... one partial method per row in the tables above, unique sequential EventId per call site
}
```

Assign a stable, unique `EventId` per call site (start at 1, increment) so structured-log backends (Seq, Application Insights) can filter/alert on specific Sox conditions by ID rather than message-text matching. Put one such static class per source file (`WebSocketServerLog`, `ConnectionLog`) beside the type it logs for, marked `internal`.

## 4. Metrics design

### 4.1 Meter

```csharp
private static readonly Meter Meter = new("Sox.WebSocketServer", "1.0.0");
```

Single static `Meter` shared by all `WebSocketServer`/`Connection` instances in the process (matches how `System.Net.Http`'s `Meter` — `"System.Net.Http"` — works: one Meter, instance identity carried via tags, not via separate Meters per server). Version string `"1.0.0"` is the *meter's* version, independent of the NuGet package version — bump only if instrument shape changes in a breaking way.

Place this in a new internal static class, e.g. `Sox/Server/Diagnostics/SoxMetrics.cs`, so both `WebSocketServer.cs` and `Connection.cs` reference the same instruments without a circular/awkward dependency, and so `Sox.Tests` can access it via `InternalsVisibleTo` (already configured for `Sox.Tests` in the csproj) for the `MeterListener`-based unit tests in section 6.

### 4.2 Instruments

| Instrument | Type | Name | Unit | Description | Tags |
|---|---|---|---|---|---|
| Connections opened | `Counter<long>` | `sox.connections.opened` | `{connection}` | Total connections successfully upgraded to WebSocket | none |
| Connections closed | `Counter<long>` | `sox.connections.closed` | `{connection}` | Total connections closed, tagged by reason | `close.code`, `close.reason` |
| Connections active | `UpDownCounter<long>` | `sox.connections.active` | `{connection}` | Currently open connections (increments on open, decrements on close) | none |
| Messages sent | `Counter<long>` | `sox.messages.sent` | `{message}` | Messages sent to clients | `message.type` (`text`/`binary`) |
| Messages received | `Counter<long>` | `sox.messages.received` | `{message}` | Messages received from clients | `message.type` (`text`/`binary`) |
| Bytes sent | `Counter<long>` | `sox.bytes.sent` | `By` | Bytes written to connection streams | none |
| Bytes received | `Counter<long>` | `sox.bytes.received` | `By` | Bytes read off connection streams | none |
| Errors | `Counter<long>` | `sox.errors` | `{error}` | Unhandled/handled-and-logged errors | `error.type`, `phase` |
| Message size (stretch, optional) | `Histogram<double>` | `sox.message.size` | `By` | Distribution of received message sizes — useful for tuning `MaxMessageBytes`/`MaxFrameBytes` | `message.type` |
| Handshake duration (stretch, optional) | `Histogram<double>` | `sox.handshake.duration` | `ms` | Time from TCP accept to handshake completion | none |

Rationale for folding close-code distribution into the `sox.connections.closed` counter's `close.code`/`close.reason` tags rather than a standalone instrument: `dotnet-counters`/OTel already aggregate a tagged counter into per-tag-value rates (this is exactly the pattern `Microsoft.AspNetCore.Hosting` uses for HTTP status code counts), so a second instrument would be redundant.

Use `Instrument.Unit` values matching the conventions .NET's own instruments use: `"By"` for bytes, `"{connection}"`/`"{message}"`/`"{error}"` for unit-less counts.

### 4.3 Exact call sites in existing code

In `WebSocketServer.cs`:

- **`ProcessHandshake`**, right after `OnConnection?.Invoke(...)`:
  ```csharp
  SoxMetrics.ConnectionsOpened.Add(1);
  SoxMetrics.ConnectionsActive.Add(1);
  ```
- **`CloseConnection`**, right after `OnDisconnection?.Invoke(...)`:
  ```csharp
  SoxMetrics.ConnectionsClosed.Add(1,
      new KeyValuePair<string, object>("close.code", (int)reason),
      new KeyValuePair<string, object>("close.reason", reason.ToString()));
  SoxMetrics.ConnectionsActive.Add(-1);
  ```
- **`HandleDataFrame`**, right after `OnTextMessage?.Invoke`/`OnBinaryMessage?.Invoke` fires (both branches):
  ```csharp
  SoxMetrics.MessagesReceived.Add(1,
      new KeyValuePair<string, object>("message.type", message.Type == MessageType.Text ? "text" : "binary"));
  SoxMetrics.BytesReceived.Add(message.Data.Length);
  ```
- **`Start()` catch block** and **`StartClientHandler` catch block** and **`HandleHttpUpgrade` catch block** (all three existing `OnError?.Invoke(...)` call sites): add
  ```csharp
  SoxMetrics.Errors.Add(1,
      new KeyValuePair<string, object>("error.type", ex.GetType().Name),
      new KeyValuePair<string, object>("phase", "accept" /* or "handshake" / "read" per site */));
  ```

In `Connection.cs`:

- **`Send(string data)`/`Send(byte[] data)`**, after the `foreach (var frame in message.Pack(...))` loop completes:
  ```csharp
  SoxMetrics.MessagesSent.Add(1,
      new KeyValuePair<string, object>("message.type", data is string ? "text" : "binary"));
  ```
  Compute the byte count from `WebSocketMessage.Data.Length` (which the class already knows) rather than re-deriving it from `data`.
- **`DequeueAsync`**, after `await _stream.WriteAndFlushAsync(frame)`: this is the single best place for `BytesSent` (it sees literal wire bytes for every frame — pings/pongs/close frames included, not just data messages), while `MessagesSent` stays at the `Send(string)`/`Send(byte[])` level (message-level, not frame-level) so fragmented sends count as one message, not N frames:
  ```csharp
  SoxMetrics.BytesSent.Add(frame.Length);
  ```

## 5. `EventSource` — recommendation: skip it (for now)

Assessment: **not worth doing in this phase, and likely never needed as a separate artifact.**

Reasoning:
- `System.Diagnostics.Metrics.Meter` (added in .NET 6, which is what this phase is already adding via the `System.Diagnostics.DiagnosticSource` package) is *already* the low-overhead, ETW/EventPipe-native mechanism `dotnet-counters`/`dotnet-trace`/`dotnet-monitor` use for exactly this purpose — `Meter` instruments are published over `System.Diagnostics.Metrics`, which itself is implemented via `EventSource`/EventPipe under the hood (specifically `System.Diagnostics.Metrics.MetricsEventSource`, an internal, framework-owned `EventSource` that both `dotnet-counters monitor` and `dotnet-trace` already know how to listen to). A hand-rolled Sox-specific `EventSource` would be a **second, parallel, hand-maintained channel** publishing largely the same information (connection open/close, message counts) that the Meter is already exposing for free, with none of the tag/dimension richness a `Counter<long>` gets.
- The only scenario where a bespoke `EventSource` earns its keep is if you need *event-shaped* (not metric-shaped) low-overhead tracing — e.g. "emit an ETW event with the full frame header on every single frame, filterable/enabled only when a trace session is attached." Sox doesn't have that need today: the `Trace`-level `ILogger` calls above already cover per-frame diagnostics for anyone who explicitly opts in (and `ILogger`'s `IsEnabled(LogLevel.Trace)` check means zero cost when trace logging isn't configured, same as `EventSource.IsEnabled()`).
- Adding one now would mean maintaining three parallel observability surfaces (events, logs, metrics, *and* EventSource) for a young library still working through Phase 1/2 protocol-correctness gaps — overhead disproportionate to the payoff.

**Recommendation: do not implement an `EventSource` in this phase.** `Meter`-based metrics already deliver the `dotnet-trace`/ETW/`dotnet-counters` visibility this would chase; revisit only if a concrete future need for raw per-frame ETW events (not per-connection/message aggregates) surfaces.

## 6. Test coverage

**Unit-testable now (add to `Sox.Tests`):**

1. **`ILogger` capture test** — implement a minimal fake `ILogger`/`ILogger<T>` (a small `TestLogger : ILogger` capturing `(LogLevel, EventId, string message, Exception)` tuples into a `List<>`, `BeginScope` returning a no-op `IDisposable`). Construct a `WebSocketServer`/`Connection` with it injected, drive a scenario (e.g. directly call `HandleFrame` with an unmasked frame via reflection/internal visibility, or construct a `Connection` over an in-memory `Pipe`-backed `Stream` and call `TryCompleteMessage` with an oversized frame) and assert the expected log entry (level + `EventId` + that the connection id appears in the state/message) was captured. Given `Sox.Tests` already has `InternalsVisibleTo`, internal methods like `Connection.TryCompleteMessage`/`ReadFramesAsync` are directly testable without going through a real socket.
2. **`MeterListener` capture test** — `System.Diagnostics.Metrics.MeterListener` (also from the `System.Diagnostics.DiagnosticSource` package, netstandard2.1-safe) can subscribe to `SoxMetrics.Meter` by name (`"Sox.WebSocketServer"`), record every `Measurement<long>`/`Measurement<double>` via `SetMeasurementEventCallback`, and assert exact `Add()`/`Record()` calls and tag values happened for a given internal-method invocation. This is the standard, documented .NET pattern for testing custom `Meter` instrumentation.
3. Verify `NullLogger`/no-`Meter`-listener defaults don't throw and add no measurable overhead path issues (basic sanity: server/connection work identically with no logger/no listener attached, which is most existing tests today since none currently pass a logger).

**Needs live/manual verification (not practically unit-testable):**

- Actual `dotnet-counters monitor` output formatting/aggregation — that's EventPipe/host tooling behavior, not Sox code.
- Real console/structured-log sink formatting — that's `Microsoft.Extensions.Logging.Console`'s concern.
- End-to-end confirmation that logs/metrics correlate correctly across concurrent real-socket connections under load — covered better by Phase 4's integration-test work than by anything in this phase.

## 7. Verification steps

**Build/test:**
```bash
dotnet build Sox/Sox.csproj                 # confirms new PackageReferences resolve on netstandard2.1
dotnet build                                 # whole solution
dotnet test Sox.Tests/Sox.Tests.csproj       # existing + new logger/meter unit tests green
```

**Manual/consumer-facing verification via `Sox.EchoServer`:**

1. Add `Microsoft.Extensions.Logging.Console` to `Sox.EchoServer.csproj` (a sample app, no netstandard2.1-compatibility concern) and wire an `ILoggerFactory` in `Program.cs`. Run `dotnet run --project Sox.EchoServer`, connect with `wscat`/browser dev tools, and confirm structured log lines appear for accept/handshake/message events at the levels designed above.
2. Verify `dotnet-counters` visibility:
   ```bash
   dotnet tool install -g dotnet-counters
   dotnet run --project Sox.EchoServer &
   dotnet-counters monitor -n Sox.EchoServer --counters Sox.WebSocketServer
   ```
   Drive traffic and confirm `sox.connections.opened`, `sox.connections.active`, `sox.messages.sent`/`received`, `sox.bytes.sent`/`received`, and `sox.connections.closed` (with `close.code` broken out) all show live, updating values.
3. Optionally, `dotnet-trace collect -n Sox.EchoServer --providers Sox.WebSocketServer` to confirm the Meter's EventSource-backed transport is traceable too.

### Critical Files for Implementation
- /Users/daniel/_/sox/Sox/Server/WebSocketServer.cs
- /Users/daniel/_/sox/Sox/Server/State/Connection.cs
- /Users/daniel/_/sox/Sox/Sox.csproj
- /Users/daniel/_/sox/Sox.EchoServer/Program.cs
- /Users/daniel/_/sox/Sox.Tests/Sox.Tests.csproj

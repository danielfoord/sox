# Phase 5 Implementation Plan — `Sox.AspNetCore` (ASP.NET Core integration track)

## 1. Recap: goal and why

Today Sox owns the entire connection lifecycle itself: a `TcpListener`, its own HTTP/1.1 handshake parser (`Sox/Http/HttpRequest.cs`), optional `SslStream` for TLS, and a hand-rolled `PipeReader`-based frame/message pipeline (`Sox/Server/State/Connection.cs`, `Sox/Websocket/Rfc6455/*`). That's a legitimate, dependency-light mode, but it means Sox has to re-solve problems (TLS, HTTP/2, connection limits, SNI, header-size limits) that Kestrel already solves well.

Phase 5 adds a second, additive way to consume Sox: let **Kestrel own the HTTP handshake/upgrade and the transport** (via `HttpContext.WebSockets.AcceptWebSocketAsync()`), and have a thin `Sox.AspNetCore` adapter bridge the resulting `System.Net.WebSockets.WebSocket` into Sox's existing message-level abstractions. This is purely additive — it doesn't touch `Sox.csproj`'s standalone server code path — and it inherits Kestrel's connection limits, TLS/SNI, and HTTP/2 support "for free."

## 2. THE key design decision — what actually gets reused

Here's the honest accounting of what `System.Net.WebSockets.WebSocket` (the type Kestrel hands you) already does versus what Sox does:

**Already fully handled by Kestrel + the BCL `WebSocket`, redundant in this mode:**
- HTTP upgrade handshake, `Sec-WebSocket-Key`/`Accept` computation, `Sec-WebSocket-Version` validation, 4xx/426 responses — all Kestrel middleware, not `WebSocketServer.HandleHttpUpgrade`/`ProcessHandshake`.
- TLS/SNI, HTTP/2, connection limits, header-size limits — all Kestrel.
- **Raw frame parsing and masking**: `FrameHeaders.TryParse`, `WebSocketFrame.TryParse`, the `Mask()`/`AsMutableSpan` trick, `FrameHeaders.WriteTo`/`WebSocketFrame.Pack()`. There is no raw byte stream to parse in this mode — `WebSocket.ReceiveAsync` already returns decoded, unmasked payload bytes plus a `WebSocketReceiveResult`/`ValueWebSocketReceiveResult` with `MessageType` (`Text`/`Binary`/`Close` only — **note: `WebSocketMessageType` has no `Ping`/`Pong` value**) and `EndOfMessage`. `SendAsync` does its own outgoing framing (and per RFC6455, server→client frames aren't masked anyway).
- **Ping/Pong and Close protocol mechanics**: the managed `WebSocket` implementation answers incoming Pings automatically and never surfaces Ping/Pong to the caller at all; `CloseAsync`/`CloseOutputAsync` implement the close handshake, and Kestrel's `WebSocketAcceptContext`/`WebSocketOptions.KeepAliveInterval` gives you an equivalent to Sox's ping-timer/keepalive, configured through ASP.NET Core, not Sox.
- Net effect: **all of `Sox.Websocket.Rfc6455.Framing` (`WebSocketFrame`, `FrameHeaders`, masking, `Pack()`) is redundant in this mode.** Don't reuse it for wire I/O in either direction.

**What's actually worth reusing — the message layer:**
- `MessageAssembler` (`Sox/Websocket/Rfc6455/Messaging/MessageAssembler.cs`) owns real, non-trivial logic that's independent of *how* frames were parsed: fragment reassembly, `ArrayPool<byte>`-backed growth, and max-message-size enforcement (`TryAppend` returning `false` past `maxMessageBytes`). This is exactly the semantic Phase 1/Phase 2 protocol-correctness work (message size caps, and wherever UTF-8 validation for Text ends up landing) targets, so reusing it means those fixes benefit *both* consumption modes automatically.
- `WebSocketMessage` (`Sox/Websocket/Rfc6455/Messaging/WebSocketMessage.cs`) is a fine, already-public DTO (`Type`, `Data`, `IDisposable`) to keep as the shared vocabulary for both event surfaces.

**The catch**: `MessageAssembler.TryAppend` takes `in WebSocketFrame frame`, and `WebSocketFrame`'s only constructors are `internal`. To reuse the reassembly logic without reimplementing it, the adapter must build a **synthetic `WebSocketFrame`** per BCL receive-chunk purely as an in-memory DTO — never parsed off a wire, never masked:

```csharp
var headers = new FrameHeaders(          // public ctor - no access change needed
    isFinal: result.EndOfMessage,
    rsv1: false, rsv2: false, rsv3: false,
    opCode: isContinuation ? OpCode.Continuation
          : result.MessageType == WebSocketMessageType.Text ? OpCode.Text : OpCode.Binary,
    shouldMask: false,
    payloadLength: bytesRead);

var frame = new WebSocketFrame(headers, ReadOnlyMemory<byte>.Empty, buffer.AsMemory(0, bytesRead)); // needs internal ctor
_messageAssembler.TryAppend(frame, out var message);   // MessageAssembler + TryAppend are internal
```

**Recommendation**: do this. It's a few lines of adapter glue, and it means Sox has exactly one implementation of "how do I reassemble fragments and enforce a message-size cap," so any future Phase 1/2/4 fix to that logic benefits `Sox.AspNetCore` with zero extra work. The alternative — hand-rolling a second, parallel reassembly loop directly against `Memory<byte>`/`bool EndOfMessage` — duplicates ~40 lines of pooling logic and creates a second place to introduce bugs or protocol drift between the two modes. It is *not* worth extracting a frame-agnostic interface out of `MessageAssembler` for this (that's a real core-`Sox` refactor with its own risk); the synthetic-`WebSocketFrame` shim is a self-contained, adapter-only cost.

**Be explicit with the team**: outside of `MessageAssembler`/`WebSocketMessage` reuse, **Phase 5 is mostly new code**. It does not reuse `Sox.Websocket.Rfc6455.Framing` in any real sense (only borrows the public `FrameHeaders` constructor and public `OpCode` enum as a bridging vocabulary) — that's expected and correct, since Kestrel has already solved the framing problem this layer exists to solve.

## 3. Public API design for `Sox.AspNetCore`

Minimal-API-first, matching modern ASP.NET Core idiom:

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30)
});

app.MapSoxWebSocket("/ws", connection =>
{
    connection.OnTextMessage += async (_, e) =>
        await connection.Send($"echo: {e.GetString()}");

    connection.OnBinaryMessage += (_, e) =>
    {
        // e.Payload: ReadOnlyMemory<byte>
    };

    connection.OnError += (_, e) =>
        app.Logger.LogError(e.Exception, "Sox connection {Id} errored", connection.Id);

    connection.OnDisconnection += (_, _) =>
        app.Logger.LogInformation("Sox connection {Id} closed", connection.Id);
});

app.Run();
```

`MapSoxWebSocket` signature:

```csharp
namespace Sox.AspNetCore;

public static class SoxWebSocketEndpointRouteBuilderExtensions
{
    public static IEndpointConventionBuilder MapSoxWebSocket(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        Action<Connection> onConnected,
        SoxWebSocketOptions? options = null) =>
        endpoints.Map(pattern, async (HttpContext context) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            var socket = await context.WebSockets.AcceptWebSocketAsync();
            var logger = context.RequestServices.GetService<ILogger<Connection>>();

            using var connection = new Connection(socket, options ?? SoxWebSocketOptions.Default, logger);
            onConnected(connection);
            await connection.RunAsync(context.RequestAborted);
        });
}
```

`Connection` (namespace `Sox.AspNetCore.State`, deliberately mirroring `Sox.Server.State.Connection`'s name/shape for a consistent DX — flag to the team: this creates a same-name-different-namespace type, so a consumer using both modes in one file needs `using Sox.Server.State.Connection = ...;`-style aliasing; call it `SoxWebSocketConnection` instead if that ambiguity is judged worse than the parity win — see `plan/00-overview.md` finding 5):

```csharp
public sealed class Connection : IDisposable
{
    public string Id { get; }
    public Task Send(string data);
    public Task Send(byte[] data);
    public Task Close(CloseStatusCode reason = CloseStatusCode.Normal);   // reuses Sox's public enum

    public event EventHandler<OnTextMessageEventArgs>? OnTextMessage;
    public event EventHandler<OnBinaryMessageEventArgs>? OnBinaryMessage;
    public event EventHandler<OnErrorEventArgs>? OnError;
    public event EventHandler<OnDisconnectionEventArgs>? OnDisconnection;
    // Deliberately NO OnConnection (the Map callback already only fires post-accept)
    // and NO OnFrame (WebSocketMessageType has no Ping/Pong - nothing to surface)
}
```

`SoxWebSocketOptions`: `int MaxMessageBytes = 10 * 1024 * 1024` (matches `WebSocketServer`'s default), `int ReceiveBufferSize = 4096` (matches `Connection.MaxFrameBytes`).

Optional/secondary pattern worth documenting but not building first: an injectable singleton "hub" (`services.AddSoxWebSocket()` + a `SoxWebSocketHub` with the same 4 events aggregated across all connections) for teams that want 1:1 parity with the standalone `WebSocketServer`'s global event surface rather than a per-route callback. Note it as a fast-follow, not required for the first cut.

## 4. Accessibility changes needed on core `Sox`

| Type | Current accessibility | Needed for Phase 5 |
|---|---|---|
| `WebSocketFrame` (struct) | `public` | OK as-is |
| `WebSocketFrame(FrameHeaders, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>)` ctor | `internal` | **Needs internal access** |
| `FrameHeaders` (struct) + its `(bool,bool,bool,bool,OpCode,bool,int)` ctor | `public` | OK as-is, no change |
| `OpCode` enum | `public` | OK as-is |
| `MessageAssembler` class | `internal sealed` | **Needs internal access** |
| `MessageAssembler(int)` ctor, `TryAppend`, `Dispose` | `internal` | **Needs internal access** |
| `WebSocketMessage` class | `public sealed` | OK as-is |
| `WebSocketMessage(MessageType, ReadOnlyMemory<byte>, byte[])` ctor | `internal` | **Needs internal access** (required for zero-copy Text/Binary message construction from raw BCL bytes; the public `string`/`byte[]` ctors would force an extra decode/copy round trip for Text) |

**Recommendation**: add `Sox.AspNetCore` to `Sox/Sox.csproj`'s existing `InternalsVisibleTo` `ItemGroup`, exactly the same mechanism already used for `Sox.Tests`:

```xml
<ItemGroup>
  <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo">
    <_Parameter1>Sox.Tests</_Parameter1>
  </AssemblyAttribute>
  <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo">
    <_Parameter1>Sox.AspNetCore</_Parameter1>
  </AssemblyAttribute>
</ItemGroup>
```

Do **not** flip these types to `public` — they're wire-encoding/reassembly internals that regular consumers of the standalone `WebSocketServer` never touch directly today (the public `OnFrame` event already exposes the public parts of `WebSocketFrame` without needing its constructors public), and making them public would be a real, permanent public-API surface expansion of core `Sox` for the sake of one internal consumer. `InternalsVisibleTo` is the lower-risk, precedented choice.

## 5. New project layout: `Sox.AspNetCore.csproj`

```
Sox.AspNetCore/
  Sox.AspNetCore.csproj
  SoxWebSocketOptions.cs
  State/
    Connection.cs
  Events/
    OnTextMessageEventArgs.cs
    OnBinaryMessageEventArgs.cs
    OnErrorEventArgs.cs
    OnDisconnectionEventArgs.cs
  Extensions/
    SoxWebSocketEndpointRouteBuilderExtensions.cs   // MapSoxWebSocket
```

These `Events/*` classes are **new code**, not reused — core `Sox`'s `OnTextMessageEventArgs`/etc. are hard-wired to `Sox.Server.State.Connection` in their constructors, so this project needs its own small parallel set referencing `Sox.AspNetCore.State.Connection`, following the same doc-comment/shape conventions for consistency.

`.csproj` (project type — library, not `Sdk.Web`, since it's not itself a runnable web app):

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>9.0</LangVersion>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Sox\Sox.csproj" />
  </ItemGroup>

</Project>
```

**TFM tradeoff, addressed explicitly**: core `Sox` targets `netstandard2.1` for maximum reach. `Sox.AspNetCore` *cannot* follow that — `Microsoft.AspNetCore.App` is only available as a `FrameworkReference` on real .NET Core/.NET 5+ TFMs, never on `netstandard2.x`. So this project is unavoidably pinned to a "real" .NET TFM.

Which one: the repo's `Sox.EchoServer`/`Sox.Tests` currently target `net9.0`, and CI pins SDK `9.0.303`. However (see `plan/00-overview.md` finding 6), **.NET 9 is an STS release and has already reached end-of-support**, while **.NET 8 is LTS (still supported)** and **.NET 10 (LTS) has since shipped**. For a new, forward-facing project, target `net8.0` as the floor (still-supported LTS, widest current ASP.NET Core compatibility) rather than perpetuating the already-EOL `net9.0`. If the team wants zero churn with the rest of the solution right now, multi-targeting `net8.0;net9.0` is the safe middle ground. This is a good moment to flag — separately from this phase — that the whole repo's `net9.0` pin in `Sox.Tests`/`Sox.EchoServer`/CI is stale and worth a follow-up bump to `net8.0`/`net10.0`; don't silently couple that unrelated bump into this PR.

Add to `Sox.sln` following the exact pattern of the 3 existing project entries (same project-type GUID `{9A19103F-16F7-4668-BE54-9A1E7A4F7556}` used for the other C# projects, a fresh project GUID, and matching `Debug|Any CPU`/`Release|Any CPU` config lines in both `SolutionConfigurationPlatforms` and `ProjectConfigurationPlatforms`).

## 6. Sample project: `Sox.AspNetCoreSample`

Mirror `Sox.EchoServer`'s role and simplicity:

```
Sox.AspNetCoreSample/
  Sox.AspNetCoreSample.csproj   (Sdk.Web, OutputType Exe implicit, TargetFramework matching Sox.AspNetCore)
  Program.cs
  Properties/launchSettings.json
```

`Program.cs` — a genuine minimal API app (not a console host like `Sox.EchoServer`'s manual `TcpListener`/`ManualResetEventSlim` loop, since Kestrel's own hosting model replaces all of that):

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseWebSockets();

app.MapSoxWebSocket("/ws", connection =>
{
    app.Logger.LogInformation("{Id} connected", connection.Id);

    connection.OnTextMessage += async (_, e) =>
        await connection.Send($"{connection.Id} sent {e.GetString()}");

    connection.OnDisconnection += (_, _) =>
        app.Logger.LogInformation("{Id} disconnected", connection.Id);

    connection.OnError += (_, e) =>
        app.Logger.LogError(e.Exception, "{Id} errored", connection.Id);
});

app.Run();
```

Project reference: `Sox.AspNetCore.csproj` only (not `Sox.csproj` directly — go through the adapter's public surface, same as `Sox.EchoServer` never reaches into `Sox.Websocket.Rfc6455` directly). Add a `Properties/launchSettings.json` with a distinct port (e.g. `5080`/`5443`) so it can run side-by-side with `Sox.EchoServer`'s `8888`.

## 7. Test plan

Add a new test project `Sox.AspNetCore.Tests` (don't fold into `Sox.Tests`, which is wired only to core `Sox` — keeps the dependency graph clean). Use the same test stack the repo already standardizes on (NUnit + `Microsoft.NET.Test.Sdk`) plus `Microsoft.AspNetCore.Mvc.Testing` (or, more precisely for WebSocket testing, `Microsoft.AspNetCore.TestHost`, which ships `TestServer.CreateWebSocketClient()` — an in-memory `WebSocketClient` that avoids binding a real socket/port).

```csharp
[TestFixture]
public class SoxWebSocketEndpointTests
{
    private WebApplicationFactory<Program> _factory;

    [SetUp]
    public void SetUp() => _factory = new WebApplicationFactory<Program>();

    [Test]
    public async Task EchoesTextMessage()
    {
        var wsClient = _factory.Server.CreateWebSocketClient();
        var socket = await wsClient.ConnectAsync(new Uri(_factory.Server.BaseAddress, "ws"), CancellationToken.None);

        var payload = "hello"u8.ToArray();
        await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);

        var buffer = new byte[1024];
        var result = await socket.ReceiveAsync(buffer, CancellationToken.None);

        Assert.That(Encoding.UTF8.GetString(buffer, 0, result.Count), Does.Contain("hello"));
    }
}
```

This requires a `public partial class Program { }` marker in the sample app (standard `WebApplicationFactory<TEntryPoint>` requirement for top-level-statement `Program.cs`), so `Sox.AspNetCoreSample` doubles as the test host — the sample app itself gets exercised by CI, not just hand-run.

Scenarios to cover (mirroring what Phase 4 does for the standalone server, adapted to this transport):
- Single-frame text/binary echo round trip.
- Fragmented message (`ClientWebSocket.SendAsync(..., endOfMessage:false)` across two calls) reassembled correctly via `MessageAssembler`.
- Message exceeding `SoxWebSocketOptions.MaxMessageBytes` → connection closed with an appropriate status (verify via `socket.CloseStatus`).
- Non-WebSocket request to `/ws` → 400.
- Multiple concurrent connections to the same endpoint, verifying independent per-connection state (no cross-talk) — cheap to add, catches any accidental static/shared mutable state in the adapter.
- A real end-to-end variant using `WebApplicationFactory.CreateClient()`'s Kestrel test server + real `ClientWebSocket` over `ws://localhost:<port>`, as a smoke test that Kestrel's actual accept path works (in-memory `TestServer` bypasses some real-socket behavior).

## 8. Risks / open questions

- **No `OnConnection`/`OnFrame` equivalents by design** — `WebSocketMessageType` has no Ping/Pong, so there's structurally nothing to put in an `OnFrame` event. `OnConnection` is redundant since the `MapSoxWebSocket` callback itself only runs post-accept — but this should be called out prominently in the README so users don't go looking for it.
- **Keepalive/idle-timeout is not Sox's responsibility here** — it's `WebSocketOptions.KeepAliveInterval` (set on `app.UseWebSockets(...)`) and Kestrel's own request/connection timeouts. Phase 2's `PongRecieved`/keepalive-timeout work in `Connection.cs` has **no analog to port** into this mode; document that explicitly rather than silently having weaker behavior here.
- **Buffer lifetime for the per-chunk receive buffer feeding the synthetic `WebSocketFrame`.** For a single, unfragmented message, `MessageAssembler.TryAppend` returns a `WebSocketMessage` that *borrows* the frame's `Data` rather than copying it (same zero-copy behavior as the standalone path) — so whatever buffer backs that `ReadOnlyMemory<byte>` must stay valid until the dispatched `OnTextMessage`/`OnBinaryMessage` handler and its `using`/`Dispose()` complete, and must not be reused for the next `ReceiveAsync` call until then. The receive loop must await the handler synchronously per iteration (same discipline as `Connection.ReadFramesAsync`'s doc comment already requires) before looping. Simplest safe first cut: allocate a fresh buffer per receive call rather than pooling it (small, defensible perf cost); pooling with explicit return-after-dispatch is a valid later optimization once correctness is proven by the test suite above.
- **DI/logging integration** — since this project is hosted inside ASP.NET Core, take `ILogger<Connection>` via `context.RequestServices` naturally (as sketched in section 3), which is a nicer, more idiomatic home for the observability goals of Phase 3 than a hand-wired `ILogger` field — worth noting this phase can partially satisfy Phase 3's "plug into normal .NET production monitoring" goal for free, for this consumption mode.
- **`CloseStatusCode` enum mismatch** — Sox's `CloseStatusCode` (16 values, including `ServiceRestart`/`TryAgainLater`/`BadGateway`/`TlsHandshakeFail`) doesn't map 1:1 onto `System.Net.WebSockets.WebSocketCloseStatus` (11 values). Casting a raw `ushort`/int through is fine (`WebSocketCloseStatus` is just an int enum), but this needs an explicit, tested mapping function rather than an implicit cast sprinkled around.
- **Naming collision** — `Sox.AspNetCore.State.Connection` vs `Sox.Server.State.Connection` (see section 3) is a real ergonomic tradeoff; decide before writing code, since renaming later is a breaking API change.

## 9. How to verify this phase

- `dotnet build` (solution-wide) green, including the new `Sox.AspNetCore` and `Sox.AspNetCoreSample` projects, on both `.github/workflows/linux.yml` and `windows.yml`.
- `dotnet test Sox.AspNetCore.Tests` green, covering the scenarios in section 7.
- Manually run `Sox.AspNetCoreSample` (`dotnet run --project Sox.AspNetCoreSample`) and connect with any WS client (e.g. `wscat -c ws://localhost:5080/ws`) to confirm the echo sample works end-to-end, matching the existing manual-verification bar set by `Sox.EchoServer`.
- README gets a new `## ASP.NET Core integration` section (parallel to the existing "Simple example") showing the `Program.cs` snippet from section 3, plus a one-line callout of what's *not* available in this mode (`OnFrame`, `OnConnection`, Sox-level keepalive) so users pick the right mode deliberately.
- Confirm no regressions to the standalone path: `dotnet test Sox.Tests` still green — this phase should touch `Sox.csproj` only via the `InternalsVisibleTo` addition, nothing else in core `Sox`.

### Critical Files for Implementation
- /Users/daniel/_/sox/Sox/Sox.csproj
- /Users/daniel/_/sox/Sox/Websocket/Rfc6455/Messaging/MessageAssembler.cs
- /Users/daniel/_/sox/Sox/Websocket/Rfc6455/Messaging/WebSocketMessage.cs
- /Users/daniel/_/sox/Sox/Websocket/Rfc6455/Framing/WebSocketFrame.cs
- /Users/daniel/_/sox/Sox/Server/State/Connection.cs
- /Users/daniel/_/sox/Sox.sln

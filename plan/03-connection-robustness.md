# Phase 2 — Connection robustness & security hardening: Implementation Plan

## 1. Goal / why

Sox's README says "DO NOT USE IN PRODUCTION," and Phase 2 is one of the two phases (with Phase 1) that actually earns removing that banner. Right now the server has no admission control and no way to shed a client that goes idle, stops ponging, or lies about payload size:

- `Connection.PongRecieved` (`Sox/Server/State/Connection.cs:37,217-220`) is written by `WebSocketServer.HandlePongFrame` but never read — the `_pinger` (`Connection.cs:79-86`, `Connection.Ping` at `Connection.cs:244-254`) only ever sends pings, so an unresponsive peer is never dropped.
- `ReadFramesAsync` (`Connection.cs:171-190`) loops on `_pipeReader.ReadAsync(cancellationToken)` with no per-iteration deadline — only `WebSocketServer.ConnectionReadTimeoutMs` (`WebSocketServer.cs:106,231`) applies, and only during the handshake `Stream.ReadTimeout`, before `Connection` even exists.
- `HttpRequest.ReadAsync`'s header `while` loop (`Sox/Http/HttpRequest.cs:54-61`) has no bound on header count or line length.
- `WebSocketServer._connections` (`WebSocketServer.cs:112`) is an unbounded `ConcurrentDictionary` with no admission control in `Start()` (`WebSocketServer.cs:154-177`).
- `WebSocketFrame.TryParse` (`Sox/Websocket/Rfc6455/Framing/WebSocketFrame.cs:252-285`) trusts `FrameHeaders.PayloadLength` (up to `int.MaxValue`, per `FrameHeaders.TryParse` at `Sox/Websocket/Rfc6455/Framing/FrameHeaders.cs:107-120`) and hands it straight to `TryReadContiguous`, which will let the underlying Pipe accumulate that many bytes *before* `MessageAssembler.TryAppend` (`Sox/Websocket/Rfc6455/Messaging/MessageAssembler.cs:46-62`) ever gets a chance to reject it via `MaxMessageBytes`.
- `Connection.MaxFrameBytes`, `PingIntervalMs`, `StreamWriteTimeoutMs` are `const`/hardcoded (`Connection.cs:50,53,59`) — no per-instance tuning.
- TLS setup (`WebSocketServer.cs:219-229`) is a single fixed cert with `clientCertificateRequired: false` — no SNI-based cert selection, no mTLS.

**Cross-cutting notes (see `plan/00-overview.md`):**
- Land Phase 1 before this phase — both restructure `HandleHttpUpgrade` (finding 4).
- This phase introduces the options-object API pattern (`WebSocketServerOptions`/`ConnectionOptions`); Phase 3's `ILogger` addition should extend it rather than adding a competing flat parameter (finding 3).
- This phase's design surfaces a real concurrency bug in `Connection.Close()` (finding 2) — the fix is designed in §6 and §3 below, land it as part of items 1+2.

## 2. Per-item design

### Item 1 — Wire `PongRecieved` into a real keepalive timeout

**Files:** `Sox/Server/State/Connection.cs`

**Mechanism:** Reuse the *existing* `_pinger` timer instead of adding a second one. Today `Ping(object, ElapsedEventArgs)` (`Connection.cs:244-254`) unconditionally sends a ping. Change it to check staleness first:

```csharp
private async void Ping(object sender, ElapsedEventArgs e)
{
    try
    {
        if (DateTime.UtcNow - PongRecieved > TimeSpan.FromMilliseconds((double)PingIntervalMs * PongTimeoutMultiplier))
        {
            await Close(CloseStatusCode.PolicyViolation);
            return;
        }
        await EnqueueAsync(WebSocketFrame.CreatePing());
    }
    catch (IOException)
    {
        Dispose();
    }
}
```

Rejected alternative: a second dedicated "pong watchdog" `Timer`. The `_pinger` already ticks at exactly the cadence needed to double as the check point, and adding a second concurrently-firing timer only widens the `Close()`/`Dispose()` race surface discussed in §6.

**Concrete edge case to fix:** `PongRecieved` defaults to `default(DateTime)` == `DateTime.MinValue`. On the very first tick, `DateTime.UtcNow - PongRecieved` is decades, so a naive implementation closes the connection after one `PingIntervalMs`, before any pong could possibly have arrived. Fix: initialize `PongRecieved = DateTime.UtcNow` in the constructor (also switch from `DateTime.Now` to `DateTime.UtcNow` everywhere it's touched — the existing code uses `DateTime.Now` in `WebSocketServer.HandlePongFrame`, which is wrong across DST transitions).

**New API on `Connection`:** `PongTimeoutMultiplier` (int, default `3` — i.e. drop after 3 missed ping intervals, a conventional keepalive multiplier), added via the options object described in §5.

### Item 2 — Genuine post-handshake idle/read timeout on `ReadFramesAsync`

**Files:** `Sox/Server/State/Connection.cs` (`ReadFramesAsync`, `Connection.cs:171-190`)

**Mechanism:** A `CancellationTokenSource` linked to the caller's token, with `CancelAfter` re-armed on every successful read:

```csharp
internal async IAsyncEnumerable<WebSocketFrame> ReadFramesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
{
    using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    while (!idleCts.IsCancellationRequested)
    {
        if (_idleReadTimeoutMs != Timeout.Infinite)
        {
            idleCts.CancelAfter(_idleReadTimeoutMs);
        }

        ReadResult result;
        try
        {
            result = await _pipeReader.ReadAsync(idleCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ConnectionIdleTimeoutException();
        }

        var buffer = result.Buffer;
        while (WebSocketFrame.TryParse(ref buffer, out var frame))
        {
            yield return frame;
        }
        _pipeReader.AdvanceTo(buffer.Start, buffer.End);
        if (result.IsCompleted || result.IsCanceled) yield break;
    }
}
```

**Why this over the alternatives:**
- `Task.WhenAny(pipeReader.ReadAsync().AsTask(), Task.Delay(timeout))` — the "losing" `ReadAsync` task keeps running after `WhenAny` returns; you'd still need a linked `CancellationToken` to actually free it, so you end up needing the CTS approach anyway, plus an extra `Task` allocation per loop iteration for no benefit, since `PipeReader.ReadAsync` already accepts a `CancellationToken` natively.
- A separate `Timer` (mirroring `_pinger`) calling `Close()` on elapse — this is a second independent path that can call `Close()`/`Dispose()` concurrently with the loop's own natural exit and with item 1's ping-timeout path, which is exactly the race called out in §6. Reusing the token the loop already awaits keeps "what can end this connection" to a single signal.

`ConnectionIdleTimeoutException` is a small new type under `Sox.Exceptions`, mirroring `HttpRequestParseException`'s style. It propagates out of `ReadFramesAsync` into `WebSocketServer.StartClientHandler`'s existing `catch (Exception ex)` block (`WebSocketServer.cs:305-312`); add a specific `catch (ConnectionIdleTimeoutException)` above the generic one that calls `CloseConnection(connection, CloseStatusCode.GoingAway)` (1001 fits "the peer trickled bytes and we're done waiting" better than the generic `ProtocolError` the fallback path uses).

**New API:** `Connection.IdleReadTimeoutMs` (int, default `Timeout.Infinite` = `-1`, i.e. **disabled** — preserves today's "never times out post-handshake" behavior exactly).

### Item 3 — Cap handshake header count and per-header/total size

**Files:** `Sox/Http/HttpRequest.cs` (`ReadAsync`, lines 33-116)

**Mechanism:** Add three counters to the existing `while (!string.IsNullOrWhiteSpace(line = await sr.ReadLineAsync()))` loop (`HttpRequest.cs:54-61`):

```csharp
internal static async Task<HttpRequest> ReadAsync(Stream stream,
    int maxHeaderCount = 100,
    int maxHeaderBytes = 8 * 1024,
    int maxTotalHeaderBytes = 16 * 1024)
{
    ...
    var headerCount = 0;
    var totalHeaderBytes = 0;
    while (!string.IsNullOrWhiteSpace((line = await sr.ReadLineAsync())))
    {
        if (line.Length > maxHeaderBytes)
        {
            throw new HttpRequestParseException($"Header line exceeds the {maxHeaderBytes}-byte limit");
        }
        totalHeaderBytes += line.Length;
        if (totalHeaderBytes > maxTotalHeaderBytes)
        {
            throw new HttpRequestParseException($"Total header size exceeds the {maxTotalHeaderBytes}-byte limit");
        }
        if (++headerCount > maxHeaderCount)
        {
            throw new HttpRequestParseException($"Header count exceeds the {maxHeaderCount} limit");
        }
        ...
    }
    ...
}
```

Reuse `HttpRequestParseException` — no new exception type needed. It's already thrown for other malformed-request cases in this exact method (content-length validation, `HttpRequest.cs:70-83`) and already flows correctly through `WebSocketServer.HandleHttpUpgrade`'s catch-all (`WebSocketServer.cs:246-254`) and through `HttpRequest.TryParse`'s existing `catch (HttpRequestParseException)` (`HttpRequest.cs:120-130`) — and, once Phase 1 lands, through the new `TryGetUpgradeRejection`/400-response path.

**Known limitation to document, not fix in this phase:** `StreamReader.ReadLineAsync()` has no built-in max-length guard *while reading* — the `line.Length > maxHeaderBytes` check only fires *after* the full line (however long) has already been buffered into a `string`. A client trickling one byte every few hundred ms with no `\n` could still accumulate an arbitrarily long in-memory line before the check ever runs, and the existing handshake-phase `stream.ReadTimeout = ConnectionReadTimeoutMs` (`WebSocketServer.cs:231`) only bounds a *single* `Read` call, not this cumulative trickle. A fully robust fix needs a custom byte-level bounded line reader replacing `StreamReader` for the header-reading phase — call this out as a known residual gap / good Phase-2-followup, not required for this pass, since the count+size checks already close the more common "thousands of small headers" and "one huge header value" cases.

**New API:** `maxHeaderCount`, `maxHeaderBytes`, `maxTotalHeaderBytes` optional params on `HttpRequest.ReadAsync`, with the defaults above (chosen to comfortably exceed every existing test fixture in `HttpRequestTests.cs`, which uses ≤6 headers of a few dozen bytes each). `WebSocketServer.HandleHttpUpgrade` (`WebSocketServer.cs:233`, `await HttpRequest.ReadAsync(stream)`) passes its own configured values (see §5).

### Item 4 — Configurable max-concurrent-connections limit

**Files:** `Sox/Server/WebSocketServer.cs` (`Start`, lines 154-177)

**Mechanism:** Check admission *before* spawning per-client work, reusing the already-existing `_clientTasks` dictionary (`WebSocketServer.cs:118`, currently used only for graceful-drain tracking in `Stop()`) as the live in-flight count — it already covers a client mid-handshake that hasn't reached `_connections` yet, which a check against `_connections.Count` alone would miss:

```csharp
client = await _server.AcceptTcpClientAsync();
if (_clientTasks.Count >= MaxConcurrentConnections)
{
    client.Close();
    continue;
}
TrackClientTask(Task.Run(() => HandleHttpUpgrade(client)));
```

This rejects the rawest, cheapest way possible (before TLS, before any bytes are read) — see §6 for why this ordering is deliberate and how it should evolve alongside Phase 1.

**New API:** `WebSocketServer.MaxConcurrentConnections` (int, default `int.MaxValue` — i.e. **no limit**, identical to today's behavior).

### Item 5 — Configurable per-frame payload-length ceiling, independent of `MaxMessageBytes`

**Files:** `Sox/Websocket/Rfc6455/Framing/WebSocketFrame.cs` (`TryParse`, lines 252-285), `Sox/Server/State/Connection.cs` (`ReadFramesAsync`)

**Mechanism:** The check must happen right after `FrameHeaders.TryParse` returns — at that point `headers.PayloadLength` is known, but no payload bytes have been read/buffered yet (`TryReadContiguous` at `WebSocketFrame.cs:271` hasn't run). Add an optional parameter to `TryParse` (default keeps existing call sites, including `WebSocketFrameTests.Unpack`, compiling unchanged):

```csharp
internal static bool TryParse(ref ReadOnlySequence<byte> buffer, out WebSocketFrame frame, int maxPayloadLength = int.MaxValue)
{
    frame = default;
    var reader = new SequenceReader<byte>(buffer);
    if (!FrameHeaders.TryParse(ref reader, out var headers)) return false;

    if (headers.PayloadLength > maxPayloadLength)
    {
        throw new WebSocketFrameParseException(
            $"Frame payload length {headers.PayloadLength} exceeds the {maxPayloadLength}-byte limit");
    }
    ...
}
```

It **must throw**, not return `false` — `false` means "not enough data yet, wait for more," which is exactly the failure mode being defended against (the caller would keep buffering toward the declared length forever). New exception type `WebSocketFrameParseException` under `Sox.Exceptions`, mirroring `HttpRequestParseException`.

`Connection.ReadFramesAsync` passes its configured ceiling: `WebSocketFrame.TryParse(ref buffer, out var frame, _maxFramePayloadBytes)`. In `WebSocketServer.StartClientHandler` (`WebSocketServer.cs:290-313`), add `catch (WebSocketFrameParseException) { await CloseConnection(connection, CloseStatusCode.MessageTooBig); }` above the existing generic catch, so this closes with the RFC-appropriate 1009 rather than falling through to `ProtocolError` — matching `MessageAssembler`'s existing behavior on oversized *messages* (`Connection.TryCompleteMessage`, `Connection.cs:202-211`, closes with `CloseStatusCode.MessageTooBig`).

**New API / default:** `Connection.MaxFramePayloadBytes` (int, default = **the connection's own `maxMessageBytes`**, not `int.MaxValue`). This default is deliberately behavior-preserving for legitimate traffic: no single RFC6455 frame's payload can legitimately exceed the total message-size cap anyway (a fragment is always ≤ the whole message), so defaulting to `maxMessageBytes` changes nothing for well-behaved clients while closing the DoS gap — a hostile frame claiming, say, 2 GB now fails immediately instead of causing the Pipe to buffer toward that length first. Still independently overridable (tighter or looser) per the requirement.

### Item 6 — Promote hardcoded internals to per-instance configuration

**Files:** `Sox/Server/State/Connection.cs` (`MaxFrameBytes`, `PingIntervalMs`, `StreamWriteTimeoutMs`, lines 50/53/59)

**Mechanism:** Turn the three `const`/hardcoded fields into instance fields set from the constructor (see §5's options object), keeping today's literal values (`4096`, `60000`, `2000`) as the defaults so nothing changes for a caller that doesn't set them. `MaxFrameBytes` continues to be used exactly where it is today — `Send(string)`/`Send(byte[])`'s `message.Pack(MaxFrameBytes)` (`Connection.cs:121,135`), i.e. it governs **outbound** fragmentation chunk size, not inbound frame acceptance. This is worth calling out explicitly because item 5's new `MaxFramePayloadBytes` is easy to conflate with it — they are two different knobs (outbound chunking vs. inbound acceptance ceiling) that happen to share a similar name.

### Item 7 — TLS: `ServerCertificateSelectionCallback`/SNI, opt-in mTLS

**Files:** `Sox/Server/WebSocketServer.cs` (`HandleHttpUpgrade`, lines 213-255)

**Mechanism:** `SslStream.AuthenticateAsServerAsync(SslServerAuthenticationOptions, CancellationToken)` (available since .NET Core 2.1 / present in the `netstandard2.1` reference surface this project targets) exposes both `ServerCertificateSelectionCallback` (`Func<object, string, X509Certificate>`, `(sender, sniHostName) => cert`) and `ClientCertificateRequired`. Replace the current fixed-cert overload call:

```csharp
if (stream is SslStream sslStream)
{
    var options = new SslServerAuthenticationOptions
    {
        ServerCertificate = ServerCertificateSelectionCallback == null ? X509Certificate : null,
        ServerCertificateSelectionCallback = ServerCertificateSelectionCallback,
        ClientCertificateRequired = ClientCertificateRequired,
        EnabledSslProtocols = SslProtocols.Tls12 | Tls13,
        CertificateRevocationCheckMode = X509RevocationMode.Online // preserves checkCertificateRevocation: true
    };
    await sslStream.AuthenticateAsServerAsync(options, CancellationToken.None);
}
```

(`ServerCertificate` and `ServerCertificateSelectionCallback` are mutually exclusive on `SslServerAuthenticationOptions` — set only one, based on whether a callback was supplied.)

**New API:** `WebSocketServer.ServerCertificateSelectionCallback` (`Func<object, string, X509Certificate>`, default `null` — preserves today's single-fixed-cert behavior exactly), `WebSocketServer.ClientCertificateRequired` (bool, default `false` — preserves today's `clientCertificateRequired: false`).

## 3. Recommended ordering

```
Item 6 (config plumbing)  --+-- Item 1 (pong keepalive, needs PingIntervalMs as instance state)
                             +-- Item 2 (idle timeout, shares Connection ctor changes with 1/6)
                             +-- Item 5 (per-frame ceiling, shares Connection ctor changes with 6)

Item 3 (header cap)  -- fully independent, different file (Sox/Http/HttpRequest.cs), no shared code with 5
Item 4 (max concurrent connections)  -- independent, WebSocketServer.Start()/HandleHttpUpgrade only
Item 7 (TLS SNI/mTLS)  -- fully independent, isolated to HandleHttpUpgrade's SslStream setup
```

- Land **item 6 first** (mechanical: turn 3 consts into instance fields, no new behavior) — it touches `Connection`'s constructor once, and items 1/2/5 all need to add fields to that same constructor. Doing 6 first means 1/2/5 are then purely additive on top of an already-parameterized `Connection`, instead of three separate PRs each fighting over the same constructor signature.
- **Item 5 does *not* need to land before or after item 3** — they're unrelated: item 3 caps the *HTTP text header* loop in `HttpRequest.ReadAsync` (pre-handshake, line-based `StreamReader`), while item 5 caps the *binary WebSocket frame header* in `WebSocketFrame.TryParse` (post-handshake, `SequenceReader<byte>`/`Pipe`-based). No shared types, no shared call path. They can be built in either order or in parallel by two different people.
- **Items 1 and 2** should land together or with item 1 immediately preceding item 2, specifically *because* of the `Close()`/`Dispose()` idempotency risk in §6 — whoever implements the idle-timeout's cancellation-triggered close path should also be the one adding the `Interlocked`-based close-guard, since both new timeout paths need it.
- **Item 4** is best sequenced with an eye toward Phase 1 (see §6) but has no *code* dependency on any other Phase 2 item — it can land anytime.
- **Item 7** is fully isolated and can land whenever; recommend last only because it's the least urgent from a "stop calling this production-unsafe" standpoint (Phase 1's protocol-correctness items and items 1-5 here are the ones actually cited as exploitable gaps in the roadmap).

## 4. Unit tests

**Unit-testable now:**

- **Item 3 (header cap)** — add to `Sox.Tests/Http/HttpRequestTests.cs`:
  - `Parse_Throws_When_Header_Count_Exceeds_Limit` — build a raw request with `maxHeaderCount + 1` distinct headers, assert `HttpRequestParseException`.
  - `Parse_Throws_When_Header_Line_Exceeds_MaxHeaderBytes` — one header whose value is `maxHeaderBytes + 1` bytes long, assert `HttpRequestParseException`.
  - `Parse_Throws_When_Total_Header_Bytes_Exceeds_Limit` — many headers each under `maxHeaderBytes` but summing past `maxTotalHeaderBytes`, assert `HttpRequestParseException`.
  - `Parse_Succeeds_At_Exactly_The_Header_Count_Limit` / `_Byte_Limit` — boundary (`== limit`, not `> limit`) cases, both directions.
  - Existing `Parse_Can_Parse_Headers` (6 headers) must still pass unmodified against the new defaults — a direct regression guard that the defaults don't break current behavior.

- **Item 5 (per-frame ceiling)** — add to `Sox.Tests/Websocket/Rfc6455/Framing/WebSocketFrameTests.cs`:
  - `TryParse_Throws_When_PayloadLength_Exceeds_MaxPayloadLength` — hand-pack a frame header (e.g. via `WebSocketFrame.CreateBinary(...).Pack()`, or a raw byte array with the 16-bit extended-length form) claiming a length above a small test ceiling, assert `WebSocketFrameParseException`.
  - `TryParse_Succeeds_At_Exactly_MaxPayloadLength` — boundary case.
  - `TryParse_Uses_IntMaxValue_Default_When_MaxPayloadLength_Not_Specified` — confirms the existing `Unpack` helper (no ceiling passed) keeps working, i.e. no regression for every other test in this file.

- **Item 1's edge-case arithmetic (recommended, low cost, high value):** extract the staleness check into a pure, static, directly-testable method:
  ```csharp
  internal static bool IsPongStale(DateTime pongReceived, DateTime now, int pingIntervalMs, int pongTimeoutMultiplier)
      => now - pongReceived > TimeSpan.FromMilliseconds((double)pingIntervalMs * pongTimeoutMultiplier);
  ```
  Add `ConnectionTests.cs` (new file — `Connection`/`WebSocketServer` currently have zero tests at all) with cases covering: comfortably-fresh pong (false), exactly-at-the-multiplier boundary, and — critically — the `DateTime.MinValue`-at-construction edge case that motivated initializing `PongRecieved` in the constructor. This doesn't prove the *timer* fires correctly, but it does pin down the arithmetic bug class without needing real elapsed time.

**NOT reasonably unit-testable — defer to Phase 4's integration suite:**

- **Item 1 end-to-end** — that the `_pinger`'s `Elapsed` event actually results in a real socket being closed after real wall-clock time requires a live `WebSocketServer` + `ClientWebSocket` (or a raw socket that never responds to pings) and a real, if short, `PingIntervalMs`. `System.Timers.Timer` isn't mockable without introducing a clock/timer abstraction, which is out of scope here.
- **Item 2 end-to-end** — same reasoning: proving `ReadFramesAsync`'s `CancelAfter`-driven timeout actually severs a real connection that goes quiet post-handshake needs a real socket and real elapsed time.
- **Item 4** — the admission-control *decision* (`_clientTasks.Count >= MaxConcurrentConnections`) is trivial and arguably not worth a dedicated unit test, but proving that the *(N+1)*th real `TcpClient.ConnectAsync()` against a live loopback `WebSocketServer` is actually refused (connection reset/refused, not silently hung) is inherently a real-socket test.
- **Item 7** — proving SNI-based `ServerCertificateSelectionCallback` actually gets invoked with the right hostname, and that a real TLS handshake actually fails when `ClientCertificateRequired = true` and no client cert is presented, needs a real `SslStream`/certificate pair. (Phase 4 already calls for a "TLS/wss integration test" — extend it to cover SNI dispatch and the mTLS-rejection case specifically, rather than treating it as a separate effort.) If wiring is factored into a small pure `BuildSslServerAuthenticationOptions()` helper on `WebSocketServer`, that helper's *construction* logic (does it null out `ServerCertificate` when a callback is set, does it thread `ClientCertificateRequired` through) can be unit-tested without a socket — recommended as a cheap addition alongside the real integration test.

## 5. Backward compatibility

**Will new constructor parameters break existing consumers?** Both `Connection` (`public Connection(Stream stream, int maxMessageBytes)`) and `WebSocketServer` (5 params, all with defaults, `WebSocketServer.cs:130-134`) are public types with public constructors, and the only in-repo consumer, `Sox.EchoServer/Program.cs:65-70`, already calls `WebSocketServer` exclusively with **named** arguments (`ipAddress:`, `port:`, `x509Certificate:`), which is *source*-compatible with any number of newly appended optional parameters regardless of order.

The real question is *design*, not compatibility risk, given Sox has never published a NuGet package (Phase 6 is explicitly parked, "no packaging story") and is flagged pre-production — there are no compiled-against-the-old-IL external consumers to protect via strict binary compatibility (C# bakes optional-argument defaults into caller IL at compile time, so appending optional params is source-compatible but not binary-compatible; that distinction only matters once real prebuilt consumers exist).

Given the item list adds up to ~7 new knobs (`PongTimeoutMultiplier`, `IdleReadTimeoutMs`, `MaxFramePayloadBytes`, `MaxConcurrentConnections`, header-cap x3, `ServerCertificateSelectionCallback`, `ClientCertificateRequired`) on top of `WebSocketServer`'s *already* 5-parameter constructor, **recommend an options object over more flat optional parameters**:

```csharp
public sealed class WebSocketServerOptions
{
    public int MaxConcurrentConnections { get; init; } = int.MaxValue;
    public int? MaxFramePayloadBytes { get; init; }          // null => defaults to maxMessageBytes at construction
    public int MaxFrameBytes { get; init; } = 4096;
    public int PingIntervalMs { get; init; } = 60000;
    public int PongTimeoutMultiplier { get; init; } = 3;
    public int IdleReadTimeoutMs { get; init; } = Timeout.Infinite;
    public int StreamWriteTimeoutMs { get; init; } = 2000;
    public int MaxHeaderCount { get; init; } = 100;
    public int MaxHeaderBytes { get; init; } = 8 * 1024;
    public int MaxTotalHeaderBytes { get; init; } = 16 * 1024;
    public Func<object, string, X509Certificate> ServerCertificateSelectionCallback { get; init; }
    public bool ClientCertificateRequired { get; init; } = false;
}

public WebSocketServer(IPAddress ipAddress, int port, int? maxMessageBytes = default,
    X509Certificate2 x509Certificate = default, int connectionReadTimeoutMs = 5000,
    WebSocketServerOptions options = null)
```

with an analogous `ConnectionOptions` threaded into a **new overload** `Connection(Stream stream, int maxMessageBytes, ConnectionOptions options = null)`, and the existing 2-arg `Connection(Stream, int)` kept verbatim, delegating (`: this(stream, maxMessageBytes, null)`). Every property default above reproduces today's exact hardcoded behavior when `options` is omitted, so `Sox.EchoServer`'s existing calls need zero changes. This also avoids the awkwardness of a 13-parameter flat constructor, and gives room to grow (Phase 3's observability additions will likely want the same options-object treatment rather than more constructor params — see `plan/00-overview.md` finding 3). Recommend switching `WebSocketServer`'s constructor to *require* an options-object-first shape (breaking) only once Phase 6 actually ships a v1.0 package — not now.

## 6. Risks / edge cases

- **Idle timeout racing the ping/pong keepalive — real, needs a fix, not just documentation.** `Connection.Close(CloseStatusCode)` (`Connection.cs:102-111`) guards re-entry with `if (State == ConnectionState.Open || State == ConnectionState.Connecting)`, but that check-then-set is not atomic against `State` being merely `volatile`. Once item 1 (ping-timeout path) and item 2 (idle-timeout path) can both independently call `Close()`/trigger connection teardown, two callers can both observe `State == Open` before either flips it to `Closing`, both proceed into `EnqueueAsync(WebSocketFrame.CreateClose(...))`, double-writing/double-disposing. **Recommended fix, in scope for this phase**: add a single-shot guard, e.g. `private int _closeInitiated; ... if (Interlocked.CompareExchange(ref _closeInitiated, 1, 0) != 0) return;` at the top of `Close()`, making it safe to call concurrently from the ping-timeout path, the idle-timeout path, `HandleCloseFrame`, and `WebSocketServer.Stop()`. This is exactly why §3 recommends landing items 1 and 2 together — whoever writes the idle-timeout close path should add this guard once, not have two people add divergent partial fixes.
- **Max-concurrent-connections rejection vs. `HandleHttpUpgrade`/Phase 1.** The design in §2 item 4 rejects at the rawest, cheapest point (right after `AcceptTcpClientAsync`, before TLS, before any HTTP parsing) by closing the raw `TcpClient` with no response — this is consistent with *today's* behavior for the "not a websocket request"/"malformed request" stubs, tracked as Phase 1's open issue #6. Once Phase 1 lands real 400/426 responses for those paths, revisit this: for a **plain `ws`** server it becomes cheap and correct to write a real `503 Service Unavailable` before closing (no TLS cost to pay first); for a **`wss`** server, sending a real 503 requires completing the TLS handshake first (a plaintext response is meaningless to a client expecting TLS), which is exactly the CPU cost admission control exists to avoid — recommend keeping the silent-close-before-TLS behavior for `wss` even after Phase 1, and only adding a real response for the pre-TLS `ws` case. Document this as a deliberate asymmetry, not an oversight.
- **`PongRecieved` initial-value bug** (§2 item 1) — must initialize to `DateTime.UtcNow` at construction, not leave at `default`, or every connection gets closed on its first ping tick. Covered by the recommended `IsPongStale` unit tests in §4.
- **`DateTime.Now` vs `DateTime.UtcNow`** — `WebSocketServer.HandlePongFrame` (`WebSocketServer.cs:383-386`) currently calls `connection.UpdateLastPong(DateTime.Now)`. Switch to `UtcNow` as part of this work so a DST transition mid-connection can't cause a spurious keepalive-timeout false-positive (or false-negative).
- **Header-cap byte-level gap** (§2 item 3) — documented above: the per-line check only fires after `ReadLineAsync` returns, so a slow-trickle single oversized line isn't fully bounded by this phase's fix alone. Acceptable for this phase given the existing `ConnectionReadTimeoutMs` provides partial mitigation and the more common attack shapes (many headers, one large-but-eventually-terminated header) are fully closed; flag as a known residual gap in the PR description.
- **`Connection`'s constructor is `public`**, not `internal`, even though only `WebSocketServer.ProcessHandshake` (`WebSocketServer.cs:276`) is expected to construct one. Out of scope to change here (would be a real breaking change and isn't asked for), but worth a one-line note in the PR: any external code directly `new Connection(...)`-ing today would need to be aware of the new overload once `ConnectionOptions` lands.
- **Duplicate HTTP headers** — `HttpRequestHeaders` (`Dictionary<string,string>`) `.Add()` throws `ArgumentException` on a duplicate key today; a malicious client sending the same header twice already crashes the parse path (caught by the generic `catch (Exception)` in `HandleHttpUpgrade`, so not a server-down bug, just a silently-dropped connection). Not part of this phase's charter (it's a Phase-1-flavored correctness issue, not a new-hardening item), but noting it here since header-cap work (item 3) touches the same loop.

## 7. Verification

**Automated (mirrors existing CI, `.github/workflows/linux.yml`/`windows.yml`):**
```
dotnet build --configuration Release
dotnet test -v n
```
Run from repo root. All new unit tests from §4 should be added to `Sox.Tests` and pass here; this is necessary but not sufficient for the timing-sensitive items.

**Manual / live verification (needed because several items are inherently about real elapsed time over a real socket, which `dotnet test` alone won't prove):**
- Run `Sox.EchoServer` (`dotnet run --project Sox.EchoServer`) configured with a short `PingIntervalMs` (e.g. 2000ms) and default `PongTimeoutMultiplier`; connect a real `ClientWebSocket` from a scratch console app or `wscat`, then simulate an unresponsive peer (a raw `TcpClient` that completes the handshake and never answers pings, since `ClientWebSocket` auto-pongs and can't be told not to) — confirm `OnDisconnection` fires within ~`PingIntervalMs * PongTimeoutMultiplier`.
- Same setup with a short `IdleReadTimeoutMs`; connect, send nothing post-handshake, confirm disconnection within that window.
- Set `MaxConcurrentConnections` to a small number (e.g. 2) and open N `ClientWebSocket` connections in a loop; confirm the (N+1)th fails to connect (connection reset, not a hang) once the cap is hit.
- For item 5, since `ClientWebSocket` won't let you construct an invalid/oversized frame declaration, use a raw `TcpClient`/`NetworkStream` test client that completes the HTTP handshake by hand and then writes a hand-crafted frame header claiming a huge payload length (e.g. the 64-bit extended-length form) — confirm the server closes the connection immediately (via `CloseStatusCode.MessageTooBig`) rather than hanging or growing memory while waiting for that many bytes.
- Recommend that Phase 4's integration-test suite formalize exactly these four scenarios as automated (not manual) tests once it exists — the most direct way to "verify" items 1, 2, and 4 for good is to write these as the first real integration tests against a live loopback `WebSocketServer`, rather than treating them as one-off manual checks.

### Critical Files for Implementation
- /Users/daniel/_/sox/Sox/Server/State/Connection.cs
- /Users/daniel/_/sox/Sox/Server/WebSocketServer.cs
- /Users/daniel/_/sox/Sox/Http/HttpRequest.cs
- /Users/daniel/_/sox/Sox/Websocket/Rfc6455/Framing/WebSocketFrame.cs
- /Users/daniel/_/sox/Sox/Websocket/Rfc6455/Framing/FrameHeaders.cs

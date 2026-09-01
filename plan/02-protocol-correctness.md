# Implementation Plan: Phase 1 — RFC6455 Protocol Correctness

## 1. Goal / Why

Sox's README says outright **"DO NOT USE IN PRODUCTION."** Phase 1 is the first of two phases (with Phase 2, connection robustness) that actually earn removing that banner, by closing five concrete correctness gaps in the RFC6455 implementation: version negotiation, HTTP error responses on bad/non-upgrade requests (GitHub issue #6), Close-frame status code validation, Text-frame UTF-8 validation, and RSV bit enforcement. All five gaps live in `Sox/Server/WebSocketServer.cs` plus small, cleanly-separable pieces of logic that can be pulled out into pure, directly unit-testable helpers — which matters because Phase 4 (real socket-based integration tests) hasn't landed yet, so this phase has to be provable by unit tests alone.

Key facts confirmed from the real code:
- `Sox/Sox.csproj` targets **netstandard2.1** — this rules out any .NET 8+-only API (e.g. `System.Text.Unicode.Utf8.IsValid`) for the UTF-8 validation step. `Sox.Tests` targets net9.0, but the implementation itself must build for netstandard2.1.
- `WebSocketServer.HandleHttpUpgrade` currently has a literal `// TODO: Not websocket request, return HTTP response (https://github.com/danielfoord/sox/issues/6)` comment and does nothing but drop the connection; the "malformed request" path is an uncaught-and-swallowed `HttpRequestParseException` from `HttpRequest.ReadAsync`, caught by the same generic `catch (Exception ex)` that also handles TLS/socket errors.
- `HttpRequestHeaders.IsWebSocketUpgrade` checks `SecWebSocketVersion != null` but never checks its *value*.
- `HandleCloseFrame(Connection connection)` **doesn't even take the frame as a parameter** — it can't inspect the received code at all today; it unconditionally calls `CloseConnection(connection, CloseStatusCode.Normal)`.
- `FrameHeaders` already parses `Rsv1`/`Rsv2`/`Rsv3` as public readonly bools but `HandleFrame` never reads them.
- The U+FFFD substitution happens in `OnTextMessageEventArgs.GetString()` → `ByteArrayExtensions.GetString()` → `Encoding.UTF8.GetString(bytes)`, which uses the default replacement-fallback decoder. `WebSocketServer.HandleDataFrame` fires `OnTextMessage` with the raw, unvalidated bytes regardless of validity.
- `HttpResponse.ToString()` **does not emit `Body`** at all (only status line + headers + blank line) — a pre-existing gap that affects how error responses can carry a message body.

**Cross-cutting note (see `plan/00-overview.md` finding 4): land this phase before Phase 2** — both restructure `HandleHttpUpgrade`, and this phase's split is the more invasive one.

## 2. The five items — exact changes

### Item A — `Sec-WebSocket-Version` validation (426) + Item B — issue #6's 400/426 response paths

These two are one unit of work: the version check literally *is* one of the two response paths issue #6 asks for, and both touch the same method.

**Files:** `Sox/Server/WebSocketServer.cs`

**Refactor for testability (do this first, before wiring the checks in):** `HandleHttpUpgrade(TcpClient client)` currently does socket/TLS setup *and* protocol decision-making in one method, which makes it untestable without a real socket. Split it:

```csharp
private async Task HandleHttpUpgrade(TcpClient client)
{
    Stream stream = null;
    try
    {
        stream = X509Certificate != null ? new SslStream(client.GetStream()) : client.GetStream();
        if (stream is SslStream sslStream)
        {
            await sslStream.AuthenticateAsServerAsync(...); // unchanged
        }
        stream.ReadTimeout = ConnectionReadTimeoutMs;

        await HandleHttpUpgrade(stream); // new internal overload, the testable part
    }
    catch (Exception ex)
    {
        OnError?.Invoke(this, new OnErrorEventArgs(null, ex));
        if (stream != null) { stream.Close(); await stream.DisposeAsync(); }
    }
}

internal async Task HandleHttpUpgrade(Stream stream)
{
    HttpRequest httpRequest;
    try
    {
        httpRequest = await HttpRequest.ReadAsync(stream);
    }
    catch (HttpRequestParseException ex)
    {
        OnError?.Invoke(this, new OnErrorEventArgs(null, ex));
        await WriteErrorResponseAsync(stream, HttpStatusCode.BadRequest);
        stream.Close();
        return;
    }

    if (httpRequest == null)
    {
        // Peer disconnected before sending anything readable - nothing to reply to.
        stream.Close();
        return;
    }

    if (TryGetUpgradeRejection(httpRequest.Headers, out var rejectStatus, out var extraHeaders))
    {
        await WriteErrorResponseAsync(stream, rejectStatus, extraHeaders);
        stream.Close();
        return;
    }

    await ProcessHandshake(stream, httpRequest);
}
```

Pull the *decision* out into a pure, IO-free, directly-unit-testable function (this is the key move — it needs zero stream/socket setup to test):

```csharp
// Pure decision logic - no I/O - fully unit-testable with a hand-built HttpRequestHeaders.
internal static bool TryGetUpgradeRejection(
    HttpRequestHeaders headers,
    out HttpStatusCode statusCode,
    out IReadOnlyDictionary<string, string> extraHeaders)
{
    if (!headers.IsWebSocketUpgrade)
    {
        statusCode = HttpStatusCode.BadRequest;
        extraHeaders = null;
        return true;
    }

    if (headers.SecWebSocketVersion != "13")
    {
        statusCode = HttpStatusCode.UpgradeRequired;
        extraHeaders = new Dictionary<string, string> { ["Sec-WebSocket-Version"] = "13" };
        return true;
    }

    statusCode = default;
    extraHeaders = null;
    return false;
}
```

**Important subtlety to note explicitly:** `IsWebSocketUpgrade` already requires `SecWebSocketVersion != null`. So a request *missing* `Sec-WebSocket-Version` entirely falls into the generic 400 branch, not 426 — the 426 branch is only reachable when the header is present but its value isn't `"13"`. That matches RFC6455 §4.4 ("If this version does not match a version understood by the server, the server MUST... send an appropriate HTTP error code (such as 426 Upgrade Required) and a `Sec-WebSocket-Version` header field indicating the version(s) the server is capable of understanding") — 426 is specifically for a version mismatch, not for a malformed/incomplete handshake in general.

The response-writer, also `internal static` for direct testing with a `MemoryStream` (the same test-double pattern already used in `Sox.Tests/Extensions/StreamExtensionsTests.cs`):

```csharp
internal static async Task WriteErrorResponseAsync(
    Stream stream, HttpStatusCode statusCode, IReadOnlyDictionary<string, string> extraHeaders = null)
{
    var response = new HttpResponse
    {
        StatusCode = statusCode,
        Headers = new HttpResponseHeaders { { "Connection", "close" } }
    };

    if (extraHeaders != null)
    {
        foreach (var (key, value) in extraHeaders)
        {
            response.Headers[key] = value;
        }
    }

    await stream.WriteAndFlushAsync(response.ToString().GetBytes());
}
```

**Note the `HttpResponse.ToString()` gap:** it never emits `Body`, regardless of what's assigned to it. Recommendation: don't set `Body` on these error responses (a status-line-plus-headers response with no body is a perfectly valid, complete HTTP response, and matches how the existing 101 handshake response is already sent). Fixing `ToString()` to append `Body` is a legitimate one-line drive-by fix but is out of scope here — call it out in the PR description as a follow-up rather than silently bundling it in.

Delete the `// TODO: ... issues/6` comment as part of this change (that's what "closes the TODO for real" means).

**Spec citations:** RFC6455 §4.2.1 (client MUST include `Sec-WebSocket-Version: 13`); §4.4 (server's required 426 behavior on mismatch).

---

### Item C — Close frame status code parsing/validation

**Files:** `Sox/Server/WebSocketServer.cs` (`HandleFrame`, `HandleCloseFrame`), new file `Sox/Websocket/Rfc6455/CloseStatusCodeExtensions.cs` (or inline in `CloseStatusCode.cs`).

`HandleCloseFrame` must take the frame:

```csharp
// call site in HandleFrame:
case OpCode.Close:
    await HandleCloseFrame(connection, frame);
    break;
```

```csharp
private async Task HandleCloseFrame(Connection connection, WebSocketFrame frame)
{
    if (_cancellationTokenSource.IsCancellationRequested)
    {
        return;
    }

    var closeCode = CloseStatusCode.Normal; // used when no code was sent at all - see below

    if (frame.PayloadLength == 1)
    {
        // A close body must be absent, or at least a 2-byte status code. One stray byte is malformed.
        closeCode = CloseStatusCode.ProtocolError;
    }
    else if (frame.PayloadLength >= 2)
    {
        var receivedCode = BinaryPrimitives.ReadUInt16BigEndian(frame.Data.Span);
        closeCode = receivedCode.IsValidReceivedCloseCode()
            ? (CloseStatusCode)receivedCode
            : CloseStatusCode.ProtocolError;
    }
    // frame.PayloadLength == 0: no status code was sent at all; RFC6455 designates 1005 for
    // this internally but 1005 MUST NOT ever be put on the wire, so we fall back to Normal.

    await CloseConnection(connection, closeCode);
}
```

Needs `using System.Buffers.Binary;` added to `WebSocketServer.cs` (already used the same way in `FrameHeaders.cs`). `frame.Data` is already unmasked by the time it reaches here — `WebSocketFrame.TryParse` applies `Mask()` during parsing before the frame is ever handed off — so no extra unmasking step is needed.

The range-validator, built directly from the **actual `CloseStatusCode` enum shape** already in the repo (`Sox/Websocket/Rfc6455/CloseStatusCode.cs`):

```csharp
internal static bool IsValidReceivedCloseCode(this ushort code)
{
    if (code < 1000) return false;                      // section 7.4.1: 0-999 not used

    if (code <= 2999)
    {
        // Explicitly reserved - MUST NOT appear on the wire (section 7.4.1)
        if (code is 1004 or 1005 or 1006 or 1015) return false;
        // Everything else defined in this range is an actual member of CloseStatusCode
        // (1000-1003, 1007-1014) - NoStatusSet/AbnormalClosure/Reserved are excluded above.
        return Enum.IsDefined(typeof(CloseStatusCode), (int)code);
    }

    return code <= 4999; // section 7.4.1: 3000-3999 libraries/apps, 4000-4999 private use - both open ranges
}
```

**Design decision worth flagging explicitly:** `CloseStatusCode` already defines `ServiceRestart=1012`, `TryAgainLater=1013`, `BadGateway=1014` as real enum members (these are IANA-registered extension codes, registered after RFC6455 was published). This treats them as valid-to-receive since the codebase's own type already commits to them being meaningful. `Enum.IsDefined` correctly excludes `Reserved=1004`, `NoStatusSet=1005`, `AbnormalClosure=1006` from that automatic pass **only** because they're filtered out by the explicit `is 1004 or 1005 or 1006 or 1015` check first.

**Spec citations:** RFC6455 §7.4.1 (status code ranges; 1005/1006/1015 "MUST NOT be set as a status code in a Close control frame by an endpoint"), §5.5.1 (Close frame body structure — a 1-byte body is invalid, no body means no status code was given).

---

### Item D — Text frame UTF-8 validation

**Files:** `Sox/Extensions/ByteArrayExtensions.cs` (add validator), `Sox/Server/WebSocketServer.cs` (`HandleDataFrame`).

Since `netstandard2.1` rules out `System.Text.Unicode.Utf8.IsValid`, use a strict `UTF8Encoding` decoder (available since netstandard2.0/.NET Framework — safe choice):

```csharp
// Sox/Extensions/ByteArrayExtensions.cs
private static readonly UTF8Encoding StrictUtf8 =
    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

internal static bool IsValidUtf8(this ReadOnlySpan<byte> bytes)
{
    if (bytes.IsEmpty) return true;
    try
    {
        StrictUtf8.GetCharCount(bytes); // Encoding.GetCharCount(ReadOnlySpan<byte>) exists in netstandard2.1
        return true;
    }
    catch (DecoderFallbackException)
    {
        return false;
    }
}

internal static bool IsValidUtf8(this ReadOnlyMemory<byte> bytes) => bytes.Span.IsValidUtf8();
```

Wire it into `HandleDataFrame`, **after** the message has been fully reassembled (see risk discussion below for why this ordering matters):

```csharp
private async Task HandleDataFrame(WebSocketFrame frame, Connection connection)
{
    using var message = await connection.TryCompleteMessage(frame);
    if (message == null) return;

    switch (message.Type)
    {
        case MessageType.Binary:
            OnBinaryMessage?.Invoke(this, new OnBinaryMessageEventArgs(connection, message.Data));
            break;
        case MessageType.Text:
            if (!message.Data.IsValidUtf8())
            {
                await CloseConnection(connection, CloseStatusCode.NotConsistent);
                return;
            }
            OnTextMessage?.Invoke(this, new OnTextMessageEventArgs(connection, message.Data));
            break;
        default:
            await CloseConnection(connection, CloseStatusCode.ProtocolError);
            break;
    }
}
```

**Spec citations:** RFC6455 §5.6 (Text frame payload is defined as UTF-8), §8.1 ("...the endpoint MUST close the connection using status code 1007 (as defined in Section 7.4.1)" for invalid UTF-8 in a Text message).

---

### Item E — RSV1-3 enforcement

**Files:** `Sox/Websocket/Rfc6455/Framing/FrameHeaders.cs` (or a new extension file next to it), `Sox/Server/WebSocketServer.cs` (`HandleFrame`).

Given the real `FrameHeaders` shape (`public readonly bool Rsv1/Rsv2/Rsv3`), add a small pure predicate — testable in isolation without any server/connection machinery:

```csharp
// e.g. as an extension method, or a property on FrameHeaders itself
internal static bool HasUnsupportedRsvBits(this FrameHeaders headers) =>
    headers.Rsv1 || headers.Rsv2 || headers.Rsv3;
```

Wire into `HandleFrame`, right after the existing mask check (before the opcode switch — RSV validity is unconditional for every frame type, not just data frames):

```csharp
private async Task HandleFrame(Connection connection, WebSocketFrame frame)
{
    if (!frame.Headers.ShouldMask)
    {
        await CloseConnection(connection, CloseStatusCode.ProtocolError);
        return;
    }

    if (frame.Headers.HasUnsupportedRsvBits())
    {
        await CloseConnection(connection, CloseStatusCode.ProtocolError);
        return;
    }

    switch (frame.OpCode) { ... } // unchanged
}
```

Since Sox parses no `Sec-WebSocket-Extensions` header anywhere today (confirmed — no such property exists on `HttpRequestHeaders` or `HttpHeaders`), "no extension has been negotiated" is unconditionally true for every current connection, so this can be a blanket, unconditional check with no per-connection state. **Flag explicitly for future readers:** once an extension (e.g. permessage-deflate) is actually negotiated, this must become conditional on that connection's negotiated extension set — today's blanket check is only correct *because* nothing is ever negotiated.

**Spec citation:** RFC6455 §5.2 ("RSV1, RSV2, RSV3: ... MUST be 0 unless an extension is negotiated that defines meanings for non-zero values. If a nonzero value is received and none of the negotiated extensions defines the meaning of such a nonzero value, the receiving endpoint MUST _Fail the WebSocket Connection_.").

---

### Stretch (not designed in detail): `Sec-WebSocket-Protocol`

`HttpRequestHeaders.SecWebSocketProtocol` already exists as a read-only accessor, but nothing reads it in `ProcessHandshake`, and there's no way for a consumer of `WebSocketServer` to declare which subprotocols it supports or to have the negotiated choice echoed back via a `Sec-WebSocket-Protocol` response header. Real implementation would need: a new `WebSocketServer` constructor parameter (e.g. `IEnumerable<string> supportedSubProtocols`), negotiation logic in `ProcessHandshake` picking the first mutually-supported value, and echoing it in the 101 response's `HttpResponseHeaders`. Left as a stretch goal — noted, not designed further here.

## 3. Recommended ordering

1. **RSV1-3 enforcement (Item E)** — smallest, a single boolean predicate, zero I/O, trivially unit-testable, touches only one `if` in `HandleFrame`. Do this first to bank a safe win and validate the general "small pure predicate + one-line wiring" pattern used again below.
2. **Close frame status code validation (Item C)** — still pure logic (no I/O), but has a wider test matrix and one real judgment call (the 1012-1014 ambiguity) that needs a citation and explicit tests to defend. Slightly riskier than Item E because `HandleCloseFrame`'s signature changes (adds a parameter), so double-check the one call site.
3. **`Sec-WebSocket-Version` + issue #6 responses (Items A+B together)** — more moving parts (stream lifecycle, refactor of `HandleHttpUpgrade` into IO/non-IO halves, response header construction), but still contained to one file. Do this after 1 and 2 so the "pure decision function + IO writer" testability pattern is already proven out.
4. **Text frame UTF-8 validation (Item D)** — last, and give it the broadest test coverage. The validator itself is a simple pure function, but it sits on the hottest code path (every Text message, including large fragmented ones near `MaxMessageBytes`), and its correctness *depends on* the assemble-then-validate ordering being right (see risks below) rather than being self-evidently safe like the others.
5. Subprotocol negotiation — note only, no code, at the very end of the phase writeup.

## 4. New/updated unit tests

Following the repo's existing conventions (`[TestFixture]`, NUnit `[Test]`/`[TestCase]`, Arrange/Act/Assert comments, `Assert.Multiple` for grouped assertions, `MemoryStream` as the I/O test-double as already used in `Sox.Tests/Extensions/StreamExtensionsTests.cs`):

**RSV bits — new file `Sox.Tests/Websocket/Rfc6455/Framing/FrameHeadersTests.cs`:**
```csharp
[TestCase(true, false, false)]
[TestCase(false, true, false)]
[TestCase(false, false, true)]
[TestCase(true, true, true)]
public void HasUnsupportedRsvBits_Returns_True_When_Any_Rsv_Bit_Set(bool rsv1, bool rsv2, bool rsv3)

[Test]
public void HasUnsupportedRsvBits_Returns_False_When_No_Rsv_Bits_Set()
```
Also add to `WebSocketFrameTests.cs` (currently only tests RSV bits being *false*, via `CreateText`'s hardcoded `rsv1: false`): a round-trip test constructing a frame via the `internal` `WebSocketFrame` constructor with `rsv1: true` directly (accessible from `Sox.Tests` via the existing `InternalsVisibleTo`), packing and unpacking it, asserting the bit survives the wire round-trip — there's currently zero coverage of RSV bits ever being `true`.

**Close code validation — new file `Sox.Tests/Websocket/Rfc6455/CloseStatusCodeExtensionsTests.cs`:**
```csharp
[TestCase((ushort)0, false)]
[TestCase((ushort)999, false)]
[TestCase((ushort)1000, true)]
[TestCase((ushort)1001, true)]
[TestCase((ushort)1002, true)]
[TestCase((ushort)1003, true)]
[TestCase((ushort)1004, false)]
[TestCase((ushort)1005, false)]
[TestCase((ushort)1006, false)]
[TestCase((ushort)1007, true)]
[TestCase((ushort)1011, true)]
[TestCase((ushort)1015, false)]
[TestCase((ushort)1016, false)]
[TestCase((ushort)2999, false)]
[TestCase((ushort)3000, true)]
[TestCase((ushort)4999, true)]
[TestCase((ushort)5000, false)]
[TestCase(ushort.MaxValue, false)]
public void IsValidReceivedCloseCode_Returns_Expected(ushort code, bool expectedValid)
```
Plus, in `WebSocketServer` tests (see below), a couple of `HandleCloseFrame` behavior tests once a `Connection`-level test seam exists (may land as Phase-4-adjacent if a live `Connection` is required — see risks).

**HTTP upgrade validation — new file `Sox.Tests/Server/WebSocketServerTests.cs`** (first test file ever for this class):
```csharp
[Test]
public void TryGetUpgradeRejection_Returns_False_For_Valid_Version_13_Request()

[Test]
public void TryGetUpgradeRejection_Returns_BadRequest_When_Not_A_Websocket_Upgrade()

[TestCase("8")]
[TestCase("7")]
[TestCase("")]
public void TryGetUpgradeRejection_Returns_UpgradeRequired_With_Version_13_Header_When_Version_Mismatched(string version)

[Test]
public async Task WriteErrorResponseAsync_Writes_Status_Line_And_Headers()
// Arrange: new MemoryStream(); Act: WebSocketServer.WriteErrorResponseAsync(stream, HttpStatusCode.BadRequest);
// Assert: Encoding.UTF8.GetString(stream.ToArray()) starts with "HTTP/1.1 400 Bad Request"

[Test]
public async Task WriteErrorResponseAsync_Includes_Extra_Headers()
// Assert the response text contains "Sec-WebSocket-Version: 13"

[Test]
public async Task HandleHttpUpgrade_Writes_400_On_Malformed_Request_Line()
// Arrange a MemoryStream seeded with an invalid request line (mirrors HttpRequestTests'
// "GET / HTTP/1.1 abc\r\n" cases), call the internal HandleHttpUpgrade(Stream) overload directly
// Assert the stream now contains "400" / "Bad Request"

[Test]
public async Task HandleHttpUpgrade_Writes_400_For_Non_Websocket_Request()
// A plain "GET / HTTP/1.1\r\nHost: x\r\n\r\n" with no Upgrade header

[Test]
public async Task HandleHttpUpgrade_Writes_426_For_Wrong_Version()
// A full, otherwise-valid handshake request with Sec-WebSocket-Version: 8
```

**UTF-8 validation — new file `Sox.Tests/Extensions/ByteArrayExtensionsTests.cs`:**
```csharp
[Test] public void IsValidUtf8_Returns_True_For_Empty_Span()
[Test] public void IsValidUtf8_Returns_True_For_Ascii_Text()
[Test] public void IsValidUtf8_Returns_True_For_Multibyte_Text() // "héllo wörld 日本語".GetBytes()
[Test] public void IsValidUtf8_Returns_False_For_Truncated_Multibyte_Sequence() // { 0xE2, 0x82 }
[Test] public void IsValidUtf8_Returns_False_For_Overlong_Encoding() // { 0xC0, 0x80 }
[Test] public void IsValidUtf8_Returns_False_For_Lone_Surrogate_Encoding() // { 0xED, 0xA0, 0x80 }
[Test] public void IsValidUtf8_Returns_False_For_Standalone_Continuation_Byte() // { 0x80 }
```

**UTF-8 + fragmentation interaction — add to `Sox.Tests/Websocket/Rfc6455/Messaging/MessageAssemblerTests.cs`:**
```csharp
[Test]
public void Assembled_Message_Spanning_A_Split_Multibyte_Codepoint_Is_Valid_Utf8()
// Arrange: encode "héllo" (é = 0xC3 0xA9), split so the 2-byte sequence for 'é' straddles
// two frames (initial + continuation), feed both through MessageAssembler.TryAppend
// Assert: the completed message.Data.IsValidUtf8() is true - proves whole-message
// (post-assembly) validation is the correct place for this check, not per-frame.
```

## 5. Risks / edge cases

- **Split multi-byte codepoints across fragmented frames — the big one.** `MessageAssembler.TryAppend` only returns a non-null `WebSocketMessage` once the *final* fragment has arrived; all continuation bytes are copied into one contiguous buffer first. By validating `message.Data` in `HandleDataFrame` *after* `TryCompleteMessage` returns (i.e., on the fully-assembled message, never on an individual frame's bytes), the classic "3-byte UTF-8 sequence split 1+2 bytes across a frame boundary" bug is naturally avoided. **Do not** move this validation to be per-frame as a "streaming" optimization without also switching to a stateful `Decoder.Convert(..., flush: false)` loop that carries partial-sequence state between calls — validating each frame's raw bytes independently would produce false positives at every fragment boundary.
- **Cost of validate-after-buffer-everything.** A hostile client could send a large, fragmented, *valid* UTF-8 message up to `MaxMessageBytes`, only to have the very last byte make it invalid — the server has already buffered the whole thing before discovering that. This is spec-legal (RFC6455 §8.1 explicitly allows validating "on the concatenation of all the fragments received" rather than incrementally) and isn't a *new* resource-exhaustion vector since it's already bounded by the existing `MaxMessageBytes` cap — just worth knowing it's a full buffer-then-check design, not early-rejection.
- **Never let a "no code" close (0-byte body) leak `NoStatusSet`(1005) onto the wire.** `CloseStatusCode.NoStatusSet` and `AbnormalClosure` are explicitly commented "Do not send over the wire" in `CloseStatusCode.cs`, and nothing today stops a future caller from doing `CloseConnection(connection, CloseStatusCode.Reserved)` by mistake — `Connection.Close`/`WebSocketFrame.CreateClose(CloseStatusCode)` will happily pack and send it. Not fixing this in Phase 1 (it's a defensive nice-to-have, not one of the five listed items), but worth flagging as a follow-up guard (e.g. an assertion in `WebSocketFrame.CreateClose`) since Item C's fix makes this exact bug easier to accidentally introduce elsewhere.
- **1-byte close body must not reach `BinaryPrimitives.ReadUInt16BigEndian` un-guarded** — it throws `ArgumentOutOfRangeException` on a span shorter than 2 bytes. The `frame.PayloadLength == 1` branch in Item C must be checked *before* the `>= 2` branch, not folded into it.
- **`HttpResponse.ToString()` silently drops `Body`** regardless of what's assigned — a real, pre-existing bug independent of this phase. The plan above sidesteps it by never setting `Body` on the new error responses; flag it in the PR rather than silently fixing it as a drive-by (scope creep risk) unless the reviewer explicitly wants it bundled.
- **`MemoryStream` timeout limitation** — `Stream.ReadTimeout`/`WriteTimeout` setters throw `InvalidOperationException` on a plain `MemoryStream` (it doesn't override `CanTimeout`). This is exactly why the `HandleHttpUpgrade` split keeps `stream.ReadTimeout = ConnectionReadTimeoutMs;` only in the outer, `TcpClient`-based overload — the new `internal Task HandleHttpUpgrade(Stream stream)` overload never touches timeout properties, so it can be exercised directly against a `MemoryStream` in tests.
- **Behavior change for existing consumers:** any current consumer relying on `OnTextMessage` firing even for slightly-malformed UTF-8 (previously silently replaced with U+FFFD) will now instead see the connection closed with 1007 and get *no* event at all for that message. This is the spec-correct behavior, but it is a real, user-visible breaking change worth calling out explicitly in the PR description (even though packaging/CHANGELOG is deferred to Phase 6).
- **RSV enforcement is only correct because nothing is ever negotiated today.** If/when an extension (permessage-deflate) is added later, this blanket check must become per-connection/conditional — noted in-code as a comment, not just in this plan, so it isn't missed.

## 6. End-to-end verification for this phase

Build/test (matches the existing CI in `.github/workflows/linux.yml`, which runs `dotnet build --configuration Release` then `dotnet test -v n`):
```
dotnet build --configuration Release
dotnet test Sox.Tests -v n
```
Focused runs while iterating:
```
dotnet test Sox.Tests --filter "FullyQualifiedName~WebSocketServerTests"
dotnet test Sox.Tests --filter "FullyQualifiedName~CloseStatusCodeExtensionsTests|FullyQualifiedName~FrameHeadersTests"
dotnet test Sox.Tests --filter "FullyQualifiedName~ByteArrayExtensionsTests|FullyQualifiedName~MessageAssemblerTests"
```

Since Phase 4's real socket-driven integration tests don't exist yet, this phase's proof rests on: (a) the pure-logic unit tests above covering every new decision function in isolation (`TryGetUpgradeRejection`, `IsValidReceivedCloseCode`, `HasUnsupportedRsvBits`, `IsValidUtf8`) with no I/O involved, and (b) the `MemoryStream`-based tests against the newly-split `HandleHttpUpgrade(Stream)`/`WriteErrorResponseAsync` for the HTTP-response paths, which exercise the *real* code path end-to-end (not a mock) short of an actual TCP socket.

For a manual smoke test beyond unit tests, `Sox.EchoServer` (in `/Users/daniel/_/sox/Sox.EchoServer/Program.cs`) already exists and starts a real `ws://127.0.0.1:8888` server:
```
dotnet run --project Sox.EchoServer
```
Then, from another terminal:
- `curl -i http://127.0.0.1:8888/` (no upgrade headers) → expect `400 Bad Request` instead of a dropped connection.
- A raw handshake request with `Sec-WebSocket-Version: 8` via `nc`/`curl --http1.1 -H ...` → expect `426 Upgrade Required` with a `Sec-WebSocket-Version: 13` response header.
- A normal client (`wscat -c ws://127.0.0.1:8888`, or a throwaway `System.Net.WebSockets.ClientWebSocket` script) → confirm the handshake and echo still work unchanged.
- A small script sending a raw, intentionally-invalid-UTF-8 Text frame (e.g. Python `websockets`/raw socket, bypassing a conformant client library that would refuse to send bad UTF-8 itself) → confirm the server closes with code `1007`.

### Critical Files for Implementation
- /Users/daniel/_/sox/Sox/Server/WebSocketServer.cs
- /Users/daniel/_/sox/Sox/Websocket/Rfc6455/CloseStatusCode.cs
- /Users/daniel/_/sox/Sox/Extensions/ByteArrayExtensions.cs
- /Users/daniel/_/sox/Sox/Websocket/Rfc6455/Framing/FrameHeaders.cs
- /Users/daniel/_/sox/Sox/Http/HttpRequestHeaders.cs
- /Users/daniel/_/sox/Sox/Websocket/Rfc6455/Messaging/MessageAssembler.cs

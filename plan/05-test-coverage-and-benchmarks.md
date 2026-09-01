# Phase 4 Implementation Plan — Test coverage & performance proof

## 1. Recap: goal and why

`Sox` currently has 5 test files, all pure unit tests of isolated parsing logic (`WebSocketFrame`, `MessageAssembler`, `HttpRequest`, a stream extension). `WebSocketServer.cs` and `Connection.cs` — the two files that actually own the socket lifecycle, the handshake, the channel-based write queue, and the ping/pong/close state machine — have **zero** coverage. There is also no evidence the recent zero-copy `Rfc6455` framing rewrite actually reduced allocations (the old `Sox.Benchmarks` project was deleted in the `netcoreapp3.1`→`net9.0` upgrade, and never revived), and `coverlet.msbuild` is referenced in `Sox.Tests.csproj` but never invoked/collected in CI.

This phase closes both gaps: real end-to-end tests driving a live `WebSocketServer` over loopback TCP/TLS with `System.Net.WebSockets.ClientWebSocket`, concurrency/stress tests targeting the one piece of code most likely to have a latent bug (`Connection`'s channel write-queue trigger logic — see §5, a real race was found while researching this plan), a revived BenchmarkDotNet project targeting the *current* zero-copy API, and coverage wired into the two existing GitHub Actions workflows.

Per the roadmap's sequencing note, these tests are also the regression net for Phase 1/2 fixes landing in parallel — several scenarios below are written today against current (pre-Phase-1) behavior and are explicitly flagged where they'll need to flip to assert the *fixed* behavior once Phase 1 lands.

**Cross-cutting note (see `plan/00-overview.md` finding 1):** the `EnqueueAsync` race found in §5 below is a real, currently-shipping bug — recommend fixing it independently and immediately, not gated on this whole phase landing first.

## 2. Test project structure

**Recommendation: add a new `Sox.IntegrationTests` project, separate from `Sox.Tests`.** Put concurrency/stress tests in the *same* new project (not a third project), under their own subfolder and an NUnit `Category` for selective filtering.

Why a new project instead of `Sox.Tests/Integration/`:
- `Sox.Tests` today runs in well under a second and is what a contributor runs in their inner loop (`dotnet test Sox.Tests`). Integration tests bind real sockets, run real handshakes, and — for the wss test — do real TLS negotiation; even "fast" ones are 10-100x slower than a unit test, and stress tests are inherently slower still. Mixing them in one project means `dotnet test Sox.Tests` is no longer fast.
- Isolation of flakiness: a hung/flaky loopback-socket or timing-based test failing should be visibly distinct from a unit test regression in CI output and in local runs.
- CI simplicity is *not* meaningfully worse: `Sox.sln` already lists projects explicitly; adding `Sox.IntegrationTests` to the `.sln` means a bare `dotnet test` at the repo root (which is what `linux.yml`/`windows.yml` already run) picks it up automatically.
- Precedent exists in this repo already: `Sox.EchoServer` and `Sox.Tests` are already separate projects from `Sox` itself.

Concrete structure:
```
Sox.IntegrationTests/
  Sox.IntegrationTests.csproj   (net9.0, NUnit, same package versions as Sox.Tests + Microsoft.NET.Test.Sdk, NUnit3TestAdapter, coverlet.msbuild)
  TestSupport/
    LoopbackServerFixture.cs    (helper: allocate a free port, construct+Start() a WebSocketServer, Stop()/Dispose() in TearDown)
    SelfSignedTestCertificate.cs (in-memory cert generation, see §4)
    RawFrameTestClient.cs       (thin TcpClient/SslStream + manual HTTP upgrade, for scenarios ClientWebSocket can't drive - raw ping/pong frames, malformed handshake)
  Server/
    WebSocketServerHandshakeTests.cs
    WebSocketServerMessagingTests.cs
    WebSocketServerCloseTests.cs
    WebSocketServerTlsTests.cs
  Concurrency/
    ConnectionConcurrentSendTests.cs
    WebSocketServerConcurrentConnectionsTests.cs
```

Required one-line change to `Sox.sln`: add `Sox.IntegrationTests` under the existing `Project(...)` list and matching `ProjectConfigurationPlatforms` entries (copy the `Sox.Tests` block, new GUID).

**One necessary `Sox.csproj` change**: several scenarios (raw ping/pong frame construction, the malformed-handshake test) need `Sox`'s `internal` `WebSocketFrame` factory methods/constructor and `TryParse`, exactly like `Sox.Tests` already uses. Add a second `AssemblyAttribute` entry for `Sox.IntegrationTests` alongside the existing `Sox.Tests` one.

## 3. Integration test scenarios

All server instances bind `IPAddress.Loopback` on a port obtained via a small `TestPortFactory.GetFreePort()` helper (bind a `TcpListener` on port 0, read `((IPEndPoint)listener.LocalEndpoint).Port`, `Stop()` it, hand that port number to `new WebSocketServer(...)`) — `WebSocketServer`'s `Port` field is fixed at construction and never reports the OS-assigned port back, so this pre-allocate-then-construct pattern is required. Every test must `await server.Stop()` (or at minimum `server.Dispose()`) in teardown; `Stop()` already drains all connections and in-flight tasks, so it's the correct thing to await rather than just disposing.

Naming follows the existing convention (`MethodName_Condition_ExpectedResult`, `[TestFixture]`/`[Test]`/`[TestCase]`, `Assert.Multiple` for multi-assert cases):

1. **`ConnectAsync_ValidHandshakeRequest_ClientAndServerReachOpenState`**
   Setup: default `WebSocketServer` on loopback, `OnConnection` wired to a `TaskCompletionSource`.
   Client: `new ClientWebSocket(); await client.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);`
   Assert: `client.State == WebSocketState.Open`; the server's `OnConnection` fired exactly once; `server.ConnectionCount == 1`.

2. **`SendAsync_TextMessage_ServerRaisesOnTextMessageWithSamePayload`** (and a companion that has the server echo it back so the assertion is round-trip, matching how `Sox.EchoServer` behaves)
   Setup: server's `OnTextMessage` handler calls `connection.Send(eventArgs.GetString())` synchronously.
   Client: connects, `SendAsync(Encoding.UTF8.GetBytes("hello"), WebSocketMessageType.Text, true, ct)`, then `ReceiveAsync` into a buffer.
   Assert: received `MessageType == Text` and the decoded string equals `"hello"`.

3. **`SendAsync_BinaryMessage_ServerEchoesIdenticalBytes`**
   Same shape as #2 but `OnBinaryMessage` handler does `connection.Send(eventArgs.Payload.ToArray())` (must copy — the `ReadOnlyMemory<byte>` is only valid for the synchronous duration of the event), `WebSocketMessageType.Binary`, and assert `Assert.AreEqual(sentBytes, receivedBytes)`.

4. **`SendAsync_FragmentedMessageAcrossMultipleFrames_ServerReassemblesAndEchoesFullPayload`**
   This exercises `MessageAssembler`/`Connection.ReadFramesAsync` fragment handling end-to-end (inbound) and `WebSocketMessage.Pack`'s frame-splitting (outbound), since `Connection.MaxFrameBytes = 4096` — anything the server sends larger than 4096 bytes is itself split into multiple frames the client must reassemble.
   Client: build e.g. a 10,000-byte random binary payload, send it via three explicit `SendAsync` calls with `endOfMessage: false, false, true`.
   Server: echoes the full assembled message back via `connection.Send(byte[])`, which internally re-fragments it into 3 outbound frames (10000 / 4096 → 3 frames) that `ClientWebSocket.ReceiveAsync` transparently reassembles.
   Assert: received bytes equal the original 10,000-byte payload byte-for-byte, regardless of the fragment boundary not aligning with `MaxFrameBytes`.

5. **`Ping_RawMaskedPingFrame_ServerRespondsWithPongFrame`**
   `ClientWebSocket` has no public API to send a raw `Ping` or observe a `Pong` frame, and `Connection`'s own internal pinger fires only every `PingIntervalMs = 60000` — too slow for a test. So this test uses the `RawFrameTestClient` helper: open a raw `TcpClient`, write a valid handshake request by hand, read+discard the `101` response, then write a masked `Ping` frame built directly via the internal `WebSocketFrame` constructor (`new WebSocketFrame(isFinal: true, rsv1: false, rsv2: false, rsv3: false, opCode: OpCode.Ping, shouldMask: true, maskingKey: new byte[]{1,2,3,4}).Pack()` — note `WebSocketFrame.CreatePing()` hardcodes `shouldMask: false` and can't be used directly for a "client" frame, since `HandleFrame` closes any unmasked frame with `ProtocolError`).
   Assert: the next raw frame read off the socket parses via `WebSocketFrame.TryParse` to `OpCode.Pong`.

6. **`CloseAsync_ClientInitiatesClose_ServerRespondsAndConnectionReachesClosed`**
   Client: after connecting, `await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", ct)`.
   Assert: `client.State == WebSocketState.Closed`, `client.CloseStatus == WebSocketCloseStatus.NormalClosure`; server's `OnDisconnection` fires; `server.ConnectionCount` returns to 0 within a short timeout.

7. **`Stop_ServerInitiatesCloseWhileClientConnected_ClientObservesGoingAwayClose`**
   Client connects, then the test calls `await server.Stop()` directly (not client-initiated).
   Assert: a subsequent `client.ReceiveAsync` returns a `WebSocketMessageType.Close` message with `client.CloseStatus == WebSocketCloseStatus.EndpointUnavailable` (`CloseStatusCode.GoingAway == 1001`, which is exactly `WebSocketCloseStatus.EndpointUnavailable`'s wire value) — this directly exercises the `Stop()` shutdown path in `WebSocketServer.cs`, which today has no coverage at all. Also assert `await server.Stop()` itself completes (doesn't hang) within a timeout — this is the regression test for the drain logic.

8. **`HandleHttpUpgrade_NonWebSocketRequest_ClosesConnectionWithoutResponse`** *(Phase 1 dependency)*
   Raw client sends a plain `GET / HTTP/1.1\r\nHost: localhost\r\n\r\n` (no `Upgrade` header) via `RawFrameTestClient`.
   Assert (documenting **today's** actual, admittedly bad, behavior): the socket is simply closed with **no** bytes written back.
   **Explicit note for whoever picks up Phase 1**: once `HandleHttpUpgrade`'s TODO (issue #6) is implemented, this test must be *replaced* (not kept alongside) with `HandleHttpUpgrade_NonWebSocketRequest_Returns400BadRequest`, asserting an actual parsed HTTP response with a `400`/`426` status line and, for the version-mismatch case, a `Sec-WebSocket-Version: 13` header. Write this test now specifically so it's the forcing function/regression net Phase 1 flips green — don't defer writing it until Phase 1 lands.

9. **`ConnectAsync_Wss_ValidSelfSignedCertificateWithClientValidationCallback_HandshakeSucceeds`** — see §4/§6 below for the certificate; this is the wss-specific scenario: `WebSocketServer` constructed with `x509Certificate:` set, client is a `ClientWebSocket` with `Options.RemoteCertificateValidationCallback` set to accept only the exact known test certificate (by thumbprint — not a blanket `return true`), connecting to `wss://127.0.0.1:{port}/`. Assert connect succeeds and a text round trip (same as scenario 2) works over the TLS-wrapped connection, proving the `SslStream` path in `HandleHttpUpgrade` is exercised, not just the plaintext path.

## 4. In-process self-signed test certificate

No external script, no manual `create_https_cert.ps1`/PowerShell step (that script is Windows-only and requires interactive cert-store trust — unusable in CI on Linux or in an automated test run anyway). Generate the cert entirely in-memory in a fixture:

```csharp
internal static class SelfSignedTestCertificate
{
    internal static X509Certificate2 Create()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        req.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: false));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, critical: false)); // serverAuth

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddIpAddress(IPAddress.Loopback);
        req.CertificateExtensions.Add(sanBuilder.Build());

        using var ephemeral = req.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));

        // Re-import via PFX bytes: CreateSelfSigned's result has ephemeral-key-set quirks on
        // some platforms (notably Windows) that make it unreliable directly in an SslStream
        // server handshake; round-tripping through PKCS#12 gives a certificate with a properly
        // persisted/exportable key on every OS this repo's CI runs (linux.yml/windows.yml).
        var pfxBytes = ephemeral.Export(X509ContentType.Pfx);
        return X509CertificateLoader.LoadPkcs12(pfxBytes, password: null, X509KeyStorageFlags.Exportable);
    }
}
```

Use `X509CertificateLoader.LoadPkcs12` (not `new X509Certificate2(byte[], ...)`) to match the loader-based pattern `Sox.EchoServer.Program.CreateServer` already uses, and because the old `X509Certificate2` byte-array constructors are obsolete (`SYSLIB0057`) on the `net9.0` target this test project uses.

Lifecycle: a `[OneTimeSetUp]`/`[OneTimeTearDown]` pair in `WebSocketServerTlsTests` (or a small shared base class) calls `Create()` once and `Dispose()`s it at the end — a fresh 2048-bit RSA self-signed cert per test run is fast enough (a few ms) that per-fixture generation is fine.

Client-side validation: rather than `RemoteCertificateValidationCallback = (_, _, _, _) => true` (which would silently mask a real bug where the wrong cert is served), compare against the known cert's thumbprint:
```csharp
options.RemoteCertificateValidationCallback = (_, cert, _, _) =>
    cert is X509Certificate2 c && c.Thumbprint == testCertificate.Thumbprint;
```

## 5. Concurrency/stress test design

This is the highest-risk area, because reading `Connection.cs` closely surfaces a real, currently-latent correctness issue in the write-queue trigger logic that a naive "just don't crash" stress test would **not** catch:

```csharp
private async Task EnqueueAsync(byte[] frame)
{
    await _channel.Writer.WriteAsync(frame);
    if (_channel.Reader.Count == 1)
    {
        await DequeueAsync();
    }
}
```
This only starts a drain loop when the writer happens to observe the channel count transition to exactly `1`. With concurrent writers, two (or more) `EnqueueAsync` calls can each write before either checks `Reader.Count`, so **both** see a count `> 1` and **neither** starts `DequeueAsync` — the queued frames are not stuck forever (a subsequent unrelated write that happens to land the count back at exactly `1` will eventually trigger a drain), but they can sit unflushed for an unbounded, unpredictable amount of time under concurrent `Send()` calls, and in the worst case (e.g. a fixed, bounded number of concurrent sends with no further writer ever arriving) they never get flushed within the test's timeout at all. A stress test asserting only "no exception/no deadlock" would pass even if this bug is present or gets reintroduced later; it must assert **delivery**, not just non-crash. **This bug should be fixed independently and immediately** (see `plan/00-overview.md` finding 1) — don't wait for this whole phase to land before patching it.

Concrete assertion strategy — **prove no message is lost, corrupted, or interleaved, and that all N arrive within a bounded time**:

1. **`Send_ManyConcurrentSendCallsOnSameConnection_ClientReceivesExactlyNWellFormedMessagesWithNoLossOrCorruption`**
   Setup: one real client/server connection (via `ClientWebSocket`, not raw sockets — the full `Connection.Send` → channel → `_stream.WriteAndFlushAsync` path under test).
   Act: from the *server side*, in the test's own code, grab the `Connection` reference from `OnConnection`'s event args, then fire `N` (e.g. 200) concurrent `Task`s each calling `await connection.Send($"msg-{i}")` with a unique, self-identifying payload per task (`i` embedded in the string, e.g. `$"{i:D6}:{Guid.NewGuid()}"` so corruption/truncation/concatenation is detectable, not just "any random garbage").
   Client: loop `ReceiveAsync` until `N` complete text messages have been received or a generous timeout (e.g. 15s) elapses, collecting each into a `HashSet<string>`.
   Assert (`Assert.Multiple`):
   - Exactly `N` messages were received (catches both loss *and* the "never gets flushed" failure mode above).
   - Every received string is a well-formed `"{index}:{guid}"` — not truncated, not two payloads concatenated into one frame, not partially overwritten.
   - The set of received indices is exactly `{0, ..., N-1}` (`Assert.AreEqual(Enumerable.Range(0, N).ToHashSet(), receivedIndices)`) — proves no duplication and no loss.
   - Wall-clock time from the last `Send` task completing to the last message actually arriving is *bounded* (assert it happened before the timeout via a `Task.WhenAny(receiveLoopTask, Task.Delay(timeout))` pattern with an explicit `Assert.Fail("timed out waiting for N messages")` on the timeout branch).

2. **`ManyConcurrentConnections_AllHandshakeAndExchangeMessagesIndependently_NoCrossTalk`**
   Setup: one server, `N` (e.g. 50) concurrent `ClientWebSocket`s all `ConnectAsync` at once.
   Act: each client sends one uniquely-identifying text message and awaits its own echo.
   Assert: `server.ConnectionCount == N` while all are open; every client receives back *its own* payload; then close all clients and assert `server.ConnectionCount` returns to `0`.

3. **`Send_ConcurrentSendCallsUnderLoad_NoDeadlockOnServerStop`**
   Start `N` concurrent long-running `Send()` loops on one connection, then call `await server.Stop()` concurrently with sends still in flight. Assert `Stop()` completes within a bounded timeout (proves `Connection.Close`/`Dispose` correctly unblocks any `EnqueueAsync`/`DequeueAsync` in progress rather than deadlocking).

Use NUnit `[Category("Stress")]` on all of these so CI (per §7) can run them but treat failures/timing differently from the deterministic scenario tests in §3.

## 6. BenchmarkDotNet revival plan

New project `Sox.Benchmarks/` (sibling to `Sox`, `Sox.Tests`, `Sox.IntegrationTests`; added to `Sox.sln`), modeled on the deleted project but retargeted at `net9.0` and a current BenchmarkDotNet version (0.14.x):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="BenchmarkDotNet" Version="0.14.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Sox\Sox.csproj" />
  </ItemGroup>
</Project>
```
`Sox/Sox.csproj` needs a matching `InternalsVisibleTo` entry for `Sox.Benchmarks` — `WebSocketFrame.CreateText/CreateBinary/Pack/TryParse`, `WebSocketMessage`, and `MessageAssembler` are all `internal`.

The old `FrameBench` benchmarked the now-deleted `Frame.CreateText(...).PackAsync().Result` API. The current shapes to target instead:

`Sox.Benchmarks/Websocket/Rfc6455/Framing/FrameBench.cs`:
```csharp
[MemoryDiagnoser]
public class FrameBench
{
    private const int PayloadSize = 1000;
    private byte[] _binaryPayload;
    private string _textPayload;
    private byte[] _packedTextFrame;

    [GlobalSetup]
    public void Setup()
    {
        _binaryPayload = new byte[PayloadSize];
        new Random(42).NextBytes(_binaryPayload);
        _textPayload = new string('a', PayloadSize);
        _packedTextFrame = WebSocketFrame.CreateText(_textPayload, shouldMask: true).Pack();
    }

    [Benchmark] public byte[] Pack_TextFrame_1000Bytes() => WebSocketFrame.CreateText(_textPayload, shouldMask: true).Pack();

    [Benchmark] public byte[] Pack_BinaryFrame_1000Bytes() => WebSocketFrame.CreateBinary(_binaryPayload, shouldMask: true).Pack();

    [Benchmark]
    public bool TryParse_SingleFrame()
    {
        var buffer = new ReadOnlySequence<byte>(_packedTextFrame);
        return WebSocketFrame.TryParse(ref buffer, out _);
    }
}
```

`Sox.Benchmarks/Websocket/Rfc6455/Messaging/MessageAssemblerBench.cs` — **this is the one that actually validates the zero-copy claim**, since the doc comments on `WebSocketMessage`/`MessageAssembler` explicitly describe the zero-copy-vs-pooled-copy split (single final frame → borrows memory, no allocation; fragmented → `ArrayPool<byte>.Shared` rent + copy):
```csharp
[MemoryDiagnoser]
public class MessageAssemblerBench
{
    private WebSocketFrame _singleFrameMessage;       // one final frame, whole message
    private WebSocketFrame[] _fragmentedMessageFrames; // same total payload, split into N frames

    [Params(1, 4, 16, 64)]
    public int FragmentCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        const int totalBytes = 16 * 1024;
        var payload = new byte[totalBytes];
        new Random(42).NextBytes(payload);

        _singleFrameMessage = WebSocketFrame.CreateBinary(payload, isFinal: true);

        using var message = new WebSocketMessage(payload);
        _fragmentedMessageFrames = message.Pack(totalBytes / FragmentCount)
            .Select(Unpack).ToArray();
    }

    [Benchmark(Baseline = true)]
    public int Assemble_SingleFrameMessage_BorrowsMemory_NoAllocation()
    {
        var assembler = new MessageAssembler(int.MaxValue);
        assembler.TryAppend(_singleFrameMessage, out var message);
        using (message) { return message.Data.Length; }
    }

    [Benchmark]
    public int Assemble_FragmentedMessage_UsesArrayPoolRent()
    {
        var assembler = new MessageAssembler(int.MaxValue);
        WebSocketMessage result = null;
        foreach (var frame in _fragmentedMessageFrames)
        {
            assembler.TryAppend(frame, out var m);
            if (m != null) result = m;
        }
        using (result) { return result.Data.Length; }
    }

    private static WebSocketFrame Unpack(byte[] bytes) { var b = new ReadOnlySequence<byte>(bytes); WebSocketFrame.TryParse(ref b, out var f); return f; }
}
```
`Sox.Benchmarks/Websocket/Rfc6455/Messaging/WebSocketMessagePackBench.cs` for the outbound side (`WebSocketMessage.Pack` splitting into frames), parameterized over frame-size vs. message-size ratio to show allocation scales with fragment *count*, not payload size (each `Pack()` call currently allocates a new `byte[]` per frame via `WebSocketFrame.Pack()` — worth confirming/quantifying, not assuming zero allocation on the outbound path, since the zero-copy work is explicitly about the *parse*/*assemble* (inbound) path, not `Pack()`).

What the `[MemoryDiagnoser]` numbers actually validate:
- `Assemble_SingleFrameMessage_BorrowsMemory_NoAllocation` should report **0 (or near-0) bytes allocated** for the message itself (only the `WebSocketMessage` wrapper object, no payload copy) — this is the concrete, falsifiable claim behind "zero-copy."
- `Assemble_FragmentedMessage_UsesArrayPoolRent` should show a `Gen0`/bytes-allocated count roughly proportional to `ArrayPool` rent overhead (small, since arrays are pooled) but **not** proportional to raw payload size the way a naive `byte[] buffer = new byte[totalSize]` accumulation would.
- Running `Assemble_SingleFrameMessage` as `[Benchmark(Baseline = true)]` lets BenchmarkDotNet report a direct ratio column against the fragmented case — the "before/after" style number the roadmap asks for (framed as "single-frame zero-copy path" vs. "fragmented pooled-copy path" rather than against the old deleted `Frame` class, since that class no longer exists — comparing the two *current* code paths against each other is the meaningful, actually-executable comparison).

`Sox.Benchmarks/Program.cs`:
```csharp
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
```

Do **not** wire `Sox.Benchmarks` into `dotnet test` or CI — it's a manually-run `dotnet run -c Release --project Sox.Benchmarks` tool, not a test project (no `Microsoft.NET.Test.Sdk` reference). Results land in `Sox.Benchmarks/BenchmarkDotNet.Artifacts/results/*-report-github.md` — worth committing a fresh one after the first real run as the "before/after" evidence, in the PR description if not the repo itself.

## 7. CI wiring

Current `linux.yml`/`windows.yml` (identical apart from `runs-on`):
```yaml
    - name: Build with dotnet
      run: dotnet build --configuration Release
    - name: Test
      run : dotnet test -v n
```

Concrete change (same for both files):
```yaml
    - name: Build with dotnet
      run: dotnet build --configuration Release
    - name: Unit tests with coverage
      run: >
        dotnet test Sox.Tests --configuration Release --no-build -v n
        --collect:"XPlat Code Coverage"
        --results-directory ./coverage
    - name: Integration tests
      run: >
        dotnet test Sox.IntegrationTests --configuration Release --no-build -v n
        --filter "Category!=Stress"
        --collect:"XPlat Code Coverage"
        --results-directory ./coverage
    - name: Stress tests
      continue-on-error: true
      run: >
        dotnet test Sox.IntegrationTests --configuration Release --no-build -v n
        --filter "Category=Stress"
```
Reasoning for the 3-step split (same job, three steps — not a separate CI job):
- **Same job, not a separate workflow/job**: this repo is small (2 OS matrix legs already), and a second job would double the checkout/setup-dotnet/restore overhead for no real isolation benefit — a failing step in the same job already shows up as a clearly separate, individually-named red step in the Actions UI.
- **Stress tests as their own step with `continue-on-error: true`**: timing-based concurrency tests are the one category genuinely prone to environment-dependent flakiness. Making them non-blocking initially avoids the classic failure mode of a flaky test being disabled/deleted in frustration; revisit `continue-on-error` once they've proven stable over some weeks of real CI runs.
- **Coverage collection, not gating**: `--collect:"XPlat Code Coverage"` (the standard VSTest-coverlet-collector invocation) produces a Cobertura XML per run. Add one lightweight visibility step:
```yaml
    - name: Publish coverage summary
      uses: irongut/CodeCoverageSummary@v1.3.0
      with:
        filename: "coverage/**/coverage.cobertura.xml"
        badge: false
        format: markdown
        output: both
    - name: Add coverage PR comment
      uses: marocchino/sticky-pull-request-comment@v2
      if: github.event_name == 'pull_request'
      with:
        recreate: true
        path: code-coverage-results.md
```
This surfaces a coverage percentage on every PR without failing the build on a threshold. A hard `--threshold`/fail-under-X% gate is a reasonable follow-up once there's a baseline number to set the threshold from.

Add `Sox.IntegrationTests.csproj` (and `Sox.Tests.csproj`, which is missing it today) a `coverlet.collector` package reference alongside the existing `coverlet.msbuild`:
```xml
<PackageReference Include="coverlet.collector" Version="6.0.2">
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  <PrivateAssets>all</PrivateAssets>
</PackageReference>
```

## 8. How to verify this phase

1. `dotnet build --configuration Release` succeeds for the whole solution, including the two new projects (`Sox.IntegrationTests`, `Sox.Benchmarks`) added to `Sox.sln`.
2. `dotnet test Sox.Tests` still completes in roughly the same (sub-second-to-low-second) time as today.
3. `dotnet test Sox.IntegrationTests --filter "Category!=Stress"` passes locally on both an actual macOS/Linux dev machine and (via the CI change) both `ubuntu-latest` and `windows-latest` — the wss test in particular is worth explicitly confirming on Windows once, since TLS/cert-store behavior is the one place this repo's own history (the PowerShell-only `create_https_cert.ps1`) shows real platform sensitivity; the in-memory `CertificateRequest` + PFX-roundtrip approach in §4 is specifically designed to sidestep that.
4. `dotnet test Sox.IntegrationTests --filter "Category=Stress"` passes repeatedly (run it locally 5-10x in a loop) before merging — since these are exactly the tests susceptible to the channel-drain race described in §5, a single green run is not sufficient confidence.
5. `dotnet run -c Release --project Sox.Benchmarks` runs to completion and produces a `*-report-github.md` with `[MemoryDiagnoser]` columns showing near-zero allocation for the single-frame assembly path relative to the fragmented path.
6. CI: both `linux.yml` and `windows.yml` runs on the phase's PR are green, with the new coverage-summary PR comment showing a real (non-zero, non-100%) percentage.
7. Manually confirm (once, not automated) that intentionally reintroducing a race (e.g. wrapping `EnqueueAsync` in a `Task.Delay(1)` before the count check) causes `Send_ManyConcurrentSendCallsOnSameConnection_...` to fail — a cheap, one-time "mutation test" that proves the stress test actually detects the class of bug it's meant to catch.

### Critical Files for Implementation

- /Users/daniel/_/sox/Sox/Server/State/Connection.cs
- /Users/daniel/_/sox/Sox/Server/WebSocketServer.cs
- /Users/daniel/_/sox/Sox/Sox.csproj
- /Users/daniel/_/sox/Sox.Tests/Sox.Tests.csproj
- /Users/daniel/_/sox/.github/workflows/linux.yml
- /Users/daniel/_/sox/.github/workflows/windows.yml
- /Users/daniel/_/sox/Sox.sln

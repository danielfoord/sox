# Sox production-readiness roadmap

This is the index. Each phase below has its own detailed, directly-executable
sub-plan in this directory. This file stays the high-level "why" and
sequencing; the per-phase files carry the file-by-file changes, exact code
sketches, test lists, and risks.

## Context

Sox is currently an honest work-in-progress: the README says outright,
**"THIS IS A WORK IN PROGRESS. DO NOT USE IN PRODUCTION."** Two recent
sessions did real hardening (zero-copy Rfc6455 pipeline rewrite;
`WebSocketServer` connection-lifecycle cleanup), but a full survey of the
codebase (packaging/docs, protocol completeness, test/observability
posture) surfaced the gaps that actually justify that banner today:

- **No RFC6455 correctness in a few real spots**: no `Sec-WebSocket-Version`
  check, no UTF-8 validation on Text frames (invalid sequences silently
  become U+FFFD instead of closing with 1007), received Close codes aren't
  parsed/validated, and RSV1-3 bits are parsed but never enforced (a
  connection using an unnegotiated extension is processed as if nothing
  were set — the opposite of what the spec requires).
- **No resilience against a slow/hostile client**: `PongRecieved` is
  tracked but never read anywhere — the ping timer only ever sends, it
  never disconnects an unresponsive peer. There's no post-handshake idle
  timeout, no cap on handshake header count/size, and no concurrent
  connection limit.
- **The "not a websocket request" and "malformed request" paths are both
  stubs** — `WebSocketServer.cs`'s TODO for issue #6 is still open; both
  cases just close the socket with no HTTP status line ever written back.
- **Zero observability** beyond the public C# events — no `ILogger`, no
  metrics, nothing plugs into normal .NET production monitoring.
- **Zero end-to-end test coverage** — all 5 test files unit-test
  frame/message/HTTP-request parsing in isolation; nothing drives a real
  `WebSocketServer` over a socket. `WebSocketServer.cs` and `Connection.cs`
  have no tests at all.
- **No proof the recent zero-copy refactor actually helped** — a
  BenchmarkDotNet project existed once, was deleted in a framework
  upgrade, and was never revived; there are no before/after allocation
  numbers anywhere.
- **No packaging story** — no NuGet metadata, no version tags, no
  CHANGELOG. (Deferred — see phase 7, parked.)

Decisions made along the way:
- **Publishing to NuGet is deferred.** Code/protocol/test hardening first;
  packaging is a future phase, not sized until the rest lands.
- **Server-only.** No WebSocket client is in scope — a real follow-on
  project once the server is solid.
- **Two supported integration modes, sequenced as two phases**: keep
  hardening the existing standalone, dependency-light `TcpListener`-based
  implementation as the core/default, *and* add a separate optional
  `Sox.AspNetCore` adapter that lets an app accept a `WebSocket` via
  Kestrel/ASP.NET Core middleware and hand it to Sox's existing
  `Sox.Websocket.Rfc6455` message-assembly layer instead of Sox owning the
  socket. Same core code, two ways to consume it.

## Phases

| # | File | What |
|---|---|---|
| 0 | [`01-foundations.md`](01-foundations.md) | Low-risk hygiene: dead CI config, `.editorconfig` + analyzer baseline, Release XML docs, CONTRIBUTING + issue templates |
| 1 | [`02-protocol-correctness.md`](02-protocol-correctness.md) | RFC6455 correctness: version check, HTTP error responses (issue #6), Close-code validation, UTF-8 validation, RSV enforcement |
| 2 | [`03-connection-robustness.md`](03-connection-robustness.md) | Keepalive/idle timeouts, header/frame/connection caps, TLS SNI + mTLS, config surface |
| 3 | [`04-observability.md`](04-observability.md) | `ILogger` + `System.Diagnostics.Metrics` alongside the existing events |
| 4 | [`05-test-coverage-and-benchmarks.md`](05-test-coverage-and-benchmarks.md) | Real socket-driven integration tests, concurrency/stress tests, revived BenchmarkDotNet project, CI coverage wiring |
| 5 | [`06-aspnetcore-integration.md`](06-aspnetcore-integration.md) | `Sox.AspNetCore` — second consumption mode built on Kestrel's own WebSocket support |
| 6 | [`07-packaging-publishing.md`](07-packaging-publishing.md) | NuGet metadata, SourceLink, versioning policy, CHANGELOG, publish workflow — **parked, prepared in advance only** |

## Cross-cutting findings

Researching all 7 phases in parallel, grounded in the real code, turned up
things no single phase's plan alone would show:

1. **A real, currently-shipping concurrency bug**, found while designing
   phase 4's stress tests: `Connection.EnqueueAsync`
   (`Sox/Server/State/Connection.cs`) triggers its write-queue drain with
   `if (_channel.Reader.Count == 1)`. Under concurrent `Send()` calls, two
   writers can each observe a count `> 1` and neither starts draining —
   queued frames can sit unflushed for an unbounded time. **Recommend
   fixing this as its own small, immediate patch**, independent of phase
   ordering — it's a correctness bug in code already shipped, not new
   hardening work. Phase 4's `Send_ManyConcurrentSendCallsOnSameConnection_...`
   stress test is designed specifically to catch it and doubles as the
   regression test.

2. **A second concurrency bug that phase 2's own new work would introduce
   if not handled**: `Connection.Close()`'s `if (State == Open || State ==
   Connecting)` guard is a non-atomic check-then-set on a `volatile`
   field. Fine today (one caller); phase 2 adds two more independent
   callers (ping-timeout, idle-timeout). Phase 2's plan already designs
   the fix (`Interlocked.CompareExchange` single-shot guard) as part of
   landing those two items together — see `03-connection-robustness.md`.

3. **API shape coordination needed between phases 2 and 3**: phase 2 wants
   an options-object (`WebSocketServerOptions`/`ConnectionOptions`)
   instead of more flat constructor parameters; phase 3 wants to add an
   `ILogger`. Whichever lands first should establish the options-object
   shape; the other extends it rather than adding a competing flat
   parameter. Recommend phase 2 first (higher priority anyway), phase 3's
   `ILogger` becomes a property on the same options object.

4. **Phases 1 and 2 both restructure `HandleHttpUpgrade`.** Phase 1 splits
   it into an IO half and a testable IO-free half; phase 2 separately
   changes its TLS setup and touches `Start()`'s accept loop. **Land phase
   1 first** so phase 2 builds on the already-split method.

5. **Naming collision risk** (phase 5): a `Sox.AspNetCore.State.Connection`
   would share a bare name with `Sox.Server.State.Connection` — decide on
   `SoxWebSocketConnection` or similar before writing code.

6. **Stale TFM pin, unrelated to any single phase**: `Sox.Tests`/
   `Sox.EchoServer`/CI are pinned to `net9.0`, no longer the LTS train.
   New phase 5 projects should target `net8.0`; the repo-wide pin is a
   good separate follow-up bump, not bundled into any one phase.

7. **NuGet naming confirmed clear** (phase 6): `Sox` isn't taken on
   nuget.org — no rename needed when that phase happens.

## Sequencing

Phase 0 anytime, low risk. Fix the `EnqueueAsync` race (finding 1)
independently and immediately — no dependency on anything else. **Phase 1
before phase 2** (finding 4). Phase 3 can run in parallel with phase 2, but
should follow phase 2's options-object shape (finding 3). Phase 4's tests
should be interleaved with 1/2, not strictly after — they're the regression
net for those very fixes. Phase 5 is independent and additive. Phase 6 stays
parked until 1-4 (ideally 5) have landed.

## Verification

Each phase (or item within a phase) lands as its own scoped session/PR,
verified independently per its own sub-plan's verification section:
`dotnet build` + `dotnet test` green via the existing
`.github/workflows/linux.yml`/`windows.yml` CI.

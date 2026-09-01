# Phase 6 — Packaging/Publishing: Detailed Implementation Plan

## 1. Goal, and an explicit STOP sign

**Goal**: give Sox a real, repeatable NuGet release story — package metadata, SourceLink-enabled symbol packages, a semver starting point, a `CHANGELOG.md`, and an automated publish workflow — so that when the team decides the library is ready, shipping it is a tag push, not a scramble.

**This document is preparation, not a green light.** Per `plan/00-overview.md`, this phase is explicitly parked: *"Revisit once Phases 1-4 (and ideally 5) have landed — publishing a package before the protocol/security gaps are closed would just re-badge the current 'don't use in production' state as a real release."* Concretely, do not execute any step below until:
- Phase 1 (RFC6455 correctness: version check, UTF-8 validation, close-code validation, RSV enforcement) is merged.
- Phase 2 (keepalive timeout, idle timeout, header caps, connection limits, TLS hardening) is merged.
- Phase 4's integration tests exist and are green in CI (so a release is backed by real end-to-end coverage, not just unit tests of isolated parsers).
- Ideally Phase 3 (observability) and Phase 5 (`Sox.AspNetCore`) have landed too, since a first NuGet version's public surface is expensive to change (SemVer major bump territory) — better to publish once with both consumption modes in place than to ship v1 twice.

Nothing in this plan should be actioned as a result of this planning session. It exists so that whoever picks up Phase 6 later has an exact, repo-grounded checklist instead of starting from scratch.

## 2. Current repo state (verified)

- `/Users/daniel/_/sox/Sox/Sox.csproj` today has **zero** packaging metadata — no `PackageId`, `Version`, `Authors`, `Description`, license, repo URL, tags, symbols, or SourceLink. Only `TargetFramework=netstandard2.1`, `DocumentationFile` (Debug-only — a Phase 0 item, not this phase), `LangVersion=9.0`, and two `PackageReference`s (`System.Threading.Channels 5.0.0`, `System.IO.Pipelines 9.0.4`).
- `/Users/daniel/_/sox/LICENSE` is **MIT**, copyright "Daniel Foord", 2018 → `PackageLicenseExpression` should be `MIT`.
- `/Users/daniel/_/sox/README.md` currently has the "DO NOT USE IN PRODUCTION" banner right under the one-line description, plus a "Simple example" and local test-running instructions. No install/NuGet section exists yet since there's nothing to install.
- `git remote -v` → `origin  ssh://git@github.com/danielfoord/sox.git` → canonical HTTPS repo URL is `https://github.com/danielfoord/sox`.
- `git tag -l` → **no tags exist**. This is a from-scratch versioning start.
- `.github/workflows/` has `linux.yml` and `windows.yml` (both: `actions/checkout@v2`, `actions/setup-dotnet@v1` pinned to `dotnet-version: 9.0.303`, `dotnet build --configuration Release`, `dotnet test -v n`) and `codeql-analysis.yml`. No publish workflow exists. These are the conventions a new publish workflow should match (same checkout/setup-dotnet actions, same dotnet version pin — though bump to a maintained `actions/*` major version while at it, see §6).
- Recent history includes a `refactor(websocket)!:` breaking-change commit (renamed `Frame`→`WebSocketFrame`, `Message`→`WebSocketMessage`, changed several event-arg shapes) and a `WebSocketServer` connection-lifecycle cleanup. Both are relevant to the CHANGELOG draft below once actually committed.
- No `Directory.Build.props` existed at the time of this research (Phase 0 adds one — coordinate).
- Solution (`Sox.sln`) has 3 shippable-relevant projects: `Sox` (the library — the only one to pack), `Sox.Tests`, `Sox.EchoServer` (sample, not packed). By the time this phase executes, Phase 4/5 will have added `Sox.IntegrationTests`, `Sox.Benchmarks`, `Sox.AspNetCore`, `Sox.AspNetCoreSample` — none of those except `Sox.AspNetCore` should ever be packed.
- `nuget.org` search shows the exact `Sox` package ID is not taken (only unrelated audio-tooling packages `SoxSharp` and `ManagedBass.Sox` exist, both wrappers around the SoX *sound* tool) — see §4 for how to handle that naming adjacency.

## 3. `Sox/Sox.csproj` additions

Add a new `<PropertyGroup>` (after the existing `TargetFramework`/`GenerateAssemblyInfo` group) with concrete values for this project:

```xml
<PropertyGroup>
  <!-- NuGet package identity -->
  <PackageId>Sox</PackageId>
  <VersionPrefix>0.1.0</VersionPrefix>
  <!-- VersionSuffix left empty for stable releases; set per-build for pre-releases, e.g. -DVersionSuffix=preview.1 -->
  <Authors>Daniel Foord</Authors>
  <Company>Daniel Foord</Company>
  <Product>Sox</Product>
  <Description>A pure, dependency-light RFC 6455 WebSocket server implementation for .NET, built on TcpListener with zero-copy frame parsing.</Description>
  <Copyright>Copyright (c) 2018 Daniel Foord</Copyright>

  <!-- License / links -->
  <PackageLicenseExpression>MIT</PackageLicenseExpression>
  <PackageProjectUrl>https://github.com/danielfoord/sox</PackageProjectUrl>
  <RepositoryUrl>https://github.com/danielfoord/sox</RepositoryUrl>
  <RepositoryType>git</RepositoryType>

  <!-- Discoverability -->
  <PackageTags>websocket;websockets;rfc6455;tcp;server;networking;realtime</PackageTags>
  <PackageReadmeFile>README.md</PackageReadmeFile>
  <PackageReleaseNotes>https://github.com/danielfoord/sox/blob/master/CHANGELOG.md</PackageReleaseNotes>

  <!-- Symbols -->
  <IncludeSymbols>true</IncludeSymbols>
  <SymbolPackageFormat>snupkg</SymbolPackageFormat>

  <!-- SourceLink support -->
  <PublishRepositoryUrl>true</PublishRepositoryUrl>
  <EmbedUntrackedSources>true</EmbedUntrackedSources>
  <ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>
  <IncludeSource>false</IncludeSource> <!-- SourceLink supersedes shipping raw source -->
</PropertyGroup>

<ItemGroup>
  <None Include="..\README.md" Pack="true" PackagePath="\" />
</ItemGroup>
```

Notes on specific choices:
- **`PackageId = "Sox"`**: matches the existing root namespace/assembly name exactly (least-surprise for consumers doing `dotnet add package Sox` then `using Sox;`). The nuget.org search found no exact `Sox` package, only adjacent audio-tooling packages (`SoxSharp`, `ManagedBass.Sox`) wrapping the *different*, unrelated `SoX` "Sound eXchange" CLI tool. This is a real but soft naming-adjacency risk — not a hard collision, so keep `PackageId=Sox`, but make `PackageTags` and `Description` do the disambiguation work (lead with "WebSocket", not "Sox", in the description — done above) rather than renaming the package.
- **`VersionPrefix` not `Version`**: keeps the door open for a `VersionSuffix` passed via `-p:VersionSuffix=preview.1` on the CLI/CI without editing the csproj for pre-release builds.
- **`PackageReadmeFile` reusing the root `README.md`**: yes — reuse it, but only *after* the README edits in §7 land (banner softened, install instructions added, badges added). Packing the current README verbatim would ship "DO NOT USE IN PRODUCTION" as the front page of the NuGet listing, which is actually fine for a `0.1.0-preview` (honest) but must be revisited before a `1.0.0`.
- **`ContinuousIntegrationBuild`** gated on `$(CI)=='true'`: GitHub Actions sets `CI=true` in the environment automatically, so this activates deterministic, SourceLink-friendly builds in CI without affecting local `dotnet pack` runs.
- Leave `Sox.Tests`/`Sox.EchoServer`/`Sox.IntegrationTests`/`Sox.Benchmarks` untouched — add `<IsPackable>false</IsPackable>` to any of them explicitly if `dotnet pack` on the solution is ever run; only `Sox.csproj` (and, once it exists, `Sox.AspNetCore.csproj`) should produce a `.nupkg`.

## 4. SourceLink

Add to `Sox/Sox.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.SourceLink.GitHub" Version="10.0.301" PrivateAssets="All" />
</ItemGroup>
```

(Re-check for a newer stable version at execution time rather than trust this number blindly — SourceLink versions track .NET SDK release trains loosely, not semver-locked to the app's TFM.)

No further SourceLink-specific properties are needed beyond `PublishRepositoryUrl` and `EmbedUntrackedSources` already added in §3 — `Microsoft.SourceLink.GitHub` auto-detects the GitHub origin from the repo's `.git` metadata and wires the `SourceRoot`/commit-SHA embedding automatically as long as the build happens inside a git checkout (which CI always is).

**Repo hygiene precondition**: SourceLink requires the build be run from a `git`-tracked working tree (checked out via `actions/checkout`, not a tarball) — already true for the existing CI. `EmbedUntrackedSources=true` covers any generated file that isn't committed.

## 5. Versioning strategy

**Starting version: `0.1.0`, not `1.0.0`.**

Rationale grounded in repo history:
- No tags exist — there is no prior public contract to be compatible with, so there's no forcing reason to start at 1.0.0.
- The README's own words — "THIS IS A WORK IN PROGRESS" — describe a library that is explicitly pre-production today. SemVer's own spec (`0.y.z`) exists exactly for this: "anything may change at any time," which is an honest signal to early adopters, whereas `1.0.0` promises a stable public API the team isn't ready to commit to yet — especially since Phase 5 (`Sox.AspNetCore`) will likely add public surface later that should ideally ship as part of a single 1.0, not require an immediate 1.1/2.0 scramble.
- The already-landed `refactor(websocket)!:` breaking-change commit is a real precedent: it renamed public types and reshaped event-arg constructors — a breaking change that happened *before* any release. Under SemVer 0.y.z rules, breaking changes are absorbed by a **minor** bump (`0.1.0`→`0.2.0`), not a major one, which is exactly the flexibility the project needs while Phases 1-5 are still landing.
- Recommend **`1.0.0` as the version to cut once Phase 1+2 (ideally +3, +4, +5) are done** — i.e., 1.0.0 is the natural "we stand behind this API and these production-readiness guarantees" milestone, not something to rush to now.

**Policy going forward (once 0.1.0 ships and stabilizes toward 1.0):**
- **Patch** (`0.1.x`): bug fixes, no public API change.
- **Minor** (`0.x.0`) while pre-1.0: any change, including breaking ones (per SemVer 0.y.z semantics) — but still write it up prominently in `CHANGELOG.md`'s `[Unreleased]`→dated-version move.
- **Post-1.0**: standard SemVer — **major** only for actual breaking public-API changes; **minor** for additive, backward-compatible features (e.g., adding `Sox.AspNetCore` as a new package doesn't bump `Sox`'s version at all — it's a separate `PackageId`); **patch** for fixes.
- Use Conventional Commits' `!` suffix (already in use) as the trigger to flag "this needs at least a minor (pre-1.0) / major (post-1.0) bump" during release-prep review.

**Mechanism: manual `VersionPrefix` bump per release commit — not Nerdbank.GitVersioning or MinVer, at least initially.**

| | Manual bump | Nerdbank.GitVersioning (NBGV) | MinVer |
|---|---|---|---|
| Setup cost | Zero — just edit one line in the csproj | New `version.json`, new build-time package, learning curve | Lighter than NBGV, but still a new package + tag-parsing convention |
| Predictability | Fully explicit — version in source control history, reviewable in the PR diff | Height-based versions between tags can be surprising until the team is used to it | Similar tag-height suffixes |
| Fit for solo/small team | Good — a human decides and writes the number when cutting a release | Overkill for infrequent releases | Same |
| Risk given empty tag history | None — starting from `0.1.0` explicitly is unambiguous | First height calculation from zero tags needs care | Same bootstrapping consideration |

**Recommendation**: start manual. Bump `VersionPrefix` in `Sox/Sox.csproj` as part of the release-prep commit, tag `vX.Y.Z` to match, let the publish workflow (§6) read the version straight from the csproj via `dotnet pack` (no extra tooling needed). Revisit MinVer (lighter than NBGV) only if release cadence increases enough that manual bumps become a recurring point of friction — not a concern for a project that hasn't cut a single release yet.

## 6. `CHANGELOG.md`

Format: **Keep a Changelog** (keepachangelog.com) — de facto standard, plain Markdown, plays well with `PackageReleaseNotes` linking to it, and pairs naturally with the SemVer policy above.

Draft initial content (to be created at `/Users/daniel/_/sox/CHANGELOG.md` when this phase is executed):

```markdown
# Changelog

All notable changes to Sox will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed
- `WebSocketServer` connection lifecycle: `ConnectionCount` no longer takes an
  internal lock; graceful `Stop()` now drains every in-flight client task
  (including a connection still mid-handshake) via a tracked task set instead
  of only the ones already in `_connections`.
- Added TLS 1.3 support in the server's `SslStream` handshake path.

## [0.1.0] - YYYY-MM-DD

Initial published release.

### Changed
- **Breaking**: rewrote the Rfc6455 frame/message pipeline for zero-copy
  parsing. `Frame` is renamed `WebSocketFrame` (now a `readonly struct` that
  borrows buffer memory instead of copying); `Message` is renamed
  `WebSocketMessage` (now `IDisposable`, exposing `ReadOnlyMemory<byte> Data`
  backed by pooled buffers for fragmented messages). Event-arg types
  (`OnFrameEventArgs`, `OnTextMessageEventArgs`, `OnBinaryMessageEventArgs`)
  changed shape to match.
- `Connection.ReadFramesAsync` now reads via `PipeReader` instead of raw
  per-frame `Stream` reads.
- Fixed in passing: `CreateText` payload length previously used char count
  instead of byte count; mask-key generation previously filled all 4 bytes
  with one identical random byte instead of 4 independent random bytes.

[Unreleased]: https://github.com/danielfoord/sox/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/danielfoord/sox/releases/tag/v0.1.0
```

Notes for whoever executes this:
- The `[Unreleased]` "WebSocketServer cleanup" entry above is drafted from a diff seen during this planning pass — confirm it has actually been committed, and fold in whatever else has landed from Phases 1-5 by the time this phase is executed (the `0.1.0` entry should really summarize *everything* that shipped between "project start" and the first release — treat this draft as a seed, not the final text).
- Once real releases start happening, add each new version as its own dated section above `[Unreleased]`, with compare-link footnotes updated each time.

## 7. Publish workflow

New file: `/Users/daniel/_/sox/.github/workflows/publish.yml`, matching the existing `linux.yml`/`windows.yml` conventions (same actions, same dotnet version — bump `actions/checkout` and `actions/setup-dotnet` to `@v4` while adding this, since `@v2`/`@v1` are old; cheap to do in the same PR):

```yaml
name: Publish

on:
  release:
    types: [published]
  workflow_dispatch:
    inputs:
      version-suffix:
        description: 'Optional pre-release suffix, e.g. preview.1 (leave empty for a stable release)'
        required: false
        default: ''

jobs:
  publish:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0   # full history so SourceLink can resolve commit info correctly

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 9.0.303

      - name: Restore
        run: dotnet restore

      - name: Build
        run: dotnet build --configuration Release --no-restore

      - name: Test
        run: dotnet test --configuration Release --no-build

      - name: Pack
        run: >
          dotnet pack Sox/Sox.csproj
          --configuration Release
          --no-build
          --output ./artifacts
          -p:VersionSuffix=${{ github.event.inputs.version-suffix }}

      - name: Push to NuGet
        run: >
          dotnet nuget push "./artifacts/*.nupkg"
          --api-key ${{ secrets.NUGET_API_KEY }}
          --source https://api.nuget.org/v3/index.json
          --skip-duplicate

      - name: Upload artifacts to workflow run
        uses: actions/upload-artifact@v4
        with:
          name: nuget-packages
          path: ./artifacts/*.nupkg
```

Design decisions:
- **Trigger on `release: published`** (a maintainer manually drafts a GitHub Release, tagged `vX.Y.Z` matching `VersionPrefix`) rather than a raw tag push — a manual confirmation gate before anything reaches nuget.org, which matters given §8's irreversibility risk. Keep `workflow_dispatch` as a manual escape hatch for a pre-release push without cutting a full GitHub Release (used for the preview strategy in §8).
- **`--skip-duplicate`** on the push step: makes re-runs of a failed workflow safe/idempotent.
- Push both the `.nupkg` and the `.snupkg` symbol package — `dotnet nuget push` with a glob picks up both automatically (`IncludeSymbols`+`SymbolPackageFormat=snupkg` from §3 makes `dotnet pack` emit both).
- **Secret needed**: `NUGET_API_KEY`, a repository secret under Settings → Secrets and variables → Actions. **This requires a manual, one-time, human step this plan cannot perform**: the repo owner must create a nuget.org account (if one doesn't exist), generate an API key scoped to push new versions of the `Sox` package (or "push new packages and package versions" more broadly, since `Sox` doesn't exist on nuget.org yet), and add it as `NUGET_API_KEY` in the GitHub repo's Actions secrets. Flag this explicitly in the PR/issue that eventually executes this phase.

## 8. README changes

To `/Users/daniel/_/sox/README.md`, once this phase is actually executed (i.e., after Phase 1+2, ideally further):

1. **Add a NuGet version badge** next to the existing CI badges:
   ```markdown
   [![NuGet](https://img.shields.io/nuget/v/Sox.svg)](https://www.nuget.org/packages/Sox/)
   ```
   (only add this *after* the first real publish — a badge pointing at a nonexistent package renders as "not found.")

2. **Add install instructions**, e.g. a new section right after the one-line description:
   ```markdown
   ## Install

   ```
   dotnet add package Sox
   ```
   ```

3. **Banner revision — staged, not a single flip:**
   - **Now (do nothing)**: keep the current wording as-is. It is accurate.
   - **After Phase 1 + Phase 2 land**, but **before** a `1.0.0`/first stable NuGet publish, soften rather than remove — recommended replacement wording:
     > **This library is under active hardening and has not yet had a stable release. Core protocol correctness and connection-security fixes have landed; production use is possible but should be done with awareness that the API may still change before 1.0.** See [CHANGELOG.md](CHANGELOG.md) for release status.
   - **After a real 1.0.0 has shipped and had some real-world mileage** (recommend: at least one full patch/minor release cycle post-1.0 with no reported protocol/security regressions) — remove the banner entirely, replaced with a normal "Status: stable, SemVer" note if desired.
   - Do **not** remove the banner as part of *this* phase's own work — Phase 6 is packaging mechanics, not a readiness judgment call; the banner wording change is gated on Phase 1/2 (and later, real-world usage), tracked as a follow-up when those phases actually land, not bundled automatically into "we added a `.csproj` `PackageId`."

## 9. Risks

- **First-publish irreversibility**: nuget.org does not allow deleting a published package version outright (only "unlisting," which hides it from search/browse but does not stop existing consumers from restoring it) — publishing a `0.1.0` prematurely cannot be cleanly undone.
- **Mitigation — ship a pre-release first**: before wiring the fully automatic `release: published` → push pipeline into "production" use, do the *first* real push as a manually-triggered pre-release, e.g. `0.1.0-preview.1`, via the `workflow_dispatch` path in §7. This validates the entire pipeline against the real registry with a version string that is unambiguously "this may be broken/renamed," lowering the cost of a mistake. Only once a preview round-trips cleanly should the team cut a real `0.1.0` (or wait for `1.0.0`, per §5) through the full `release: published` trigger.
- **API surface lock-in**: once real consumers exist, renaming/removing public types becomes a breaking-change event with real external impact instead of a free pre-release cleanup — another reason 1.0.0 should wait for Phase 5 to land so the public surface is more settled before the SemVer "no more free breaking changes without a major bump" contract kicks in.
- **Secret handling**: `NUGET_API_KEY` must be scoped as narrowly as nuget.org allows (ideally package-scoped once `Sox` exists) and rotated if a workflow log ever accidentally echoes it (the `dotnet nuget push` command above passes it as a CLI arg, which GitHub Actions redacts in logs by default when sourced from `secrets.*` — do not `echo` it or pass it via a script that could print `$@`).

## 10. Verification before touching the real registry

Before ever running the workflow in §7 against `https://api.nuget.org`:

1. **Local pack smoke test**: `dotnet pack Sox/Sox.csproj --configuration Release --output ./artifacts` and inspect the resulting `.nupkg` — unzip it (a `.nupkg` is a zip) and confirm: `Sox.nuspec` contains the expected `id`/`version`/`license`/`repository` metadata; `README.md` is present at the package root; the `.snupkg` exists alongside it.
2. **Local feed push test**: create a local folder-based NuGet feed and push there instead of nuget.org:
   ```
   mkdir -p ~/local-nuget-feed
   dotnet nuget push ./artifacts/Sox.0.1.0-preview.1.nupkg --source ~/local-nuget-feed
   ```
   Then, from a throwaway sample console app, `dotnet nuget add source ~/local-nuget-feed --name local` and `dotnet add package Sox --source local` to confirm the package actually resolves, restores, and the library's public API is usable end-to-end.
3. **SourceLink verification**: after a local pack, use the `sourcelink` dotnet tool (`dotnet tool install --global sourcelink`) — `sourcelink test ./artifacts/Sox.0.1.0-preview.1.nupkg` — to confirm embedded source links resolve to the correct GitHub commit before ever pushing publicly.
4. **Real preview push**: only after 1-3 pass, run the `workflow_dispatch` trigger with `version-suffix: preview.1` against the real `https://api.nuget.org` and confirm the listing page renders correctly (README, license, tags, links) and `dotnet add package Sox --version 0.1.0-preview.1 --prerelease` works from a clean machine/CI runner.
5. **Only then** cut the first real release via a GitHub Release (`release: published` trigger) with the version decided per §5.

### Critical Files for Implementation
- /Users/daniel/_/sox/Sox/Sox.csproj
- /Users/daniel/_/sox/.github/workflows/publish.yml (new)
- /Users/daniel/_/sox/CHANGELOG.md (new)
- /Users/daniel/_/sox/README.md
- /Users/daniel/_/sox/plan/00-overview.md (source of truth for the phase gate — this phase should not start until earlier phases are marked done)

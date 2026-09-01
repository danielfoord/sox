# Phase 0 — Foundations: Implementation Plan

## 1. Recap: Goal and Why

Phase 0 is explicitly the "low-risk, anytime" phase of the roadmap — it doesn't touch protocol/security/observability code at all. Its job is to remove stale/misleading process artifacts and lay down cheap infrastructure (lint baseline, contributor docs) that later phases and any outside contributors will benefit from, without introducing any behavior change or CI-breaking risk. Every item here is additive or purely deletionary of dead config — nothing here should change `Sox.dll`'s behavior.

Verified current repo state (read directly, not assumed):
- `/Users/daniel/_/sox/.gitlab-ci.yml` really does reference `dotnet test Sox.Core.Tests`, a project that doesn't exist in `Sox.sln` (only `Sox`, `Sox.Tests`, `Sox.EchoServer` are there) and uses an ancient `microsoft/aspnetcore-build:2.0` image. It is genuinely dead — real CI is `.github/workflows/windows.yml` and `linux.yml`, both pinned to `dotnet-version: 9.0.303`, running `dotnet build --configuration Release` then `dotnet test -v n`.
- No `.editorconfig`, no `CONTRIBUTING.md`, no `.github/ISSUE_TEMPLATE/` or PR template exist anywhere in the repo today — confirmed via direct filesystem check.
- `Sox/Sox.csproj`'s `DocumentationFile` is set only inside the `Debug|AnyCPU` `PropertyGroup`; the `Release|AnyCPU` group only sets `LangVersion`.
- All three real `.csproj` files (`Sox/Sox.csproj`, `Sox.Tests/Sox.Tests.csproj`, `Sox.EchoServer/Sox.EchoServer.csproj`) have no analyzer package and no `Directory.Build.props` exists to centralize one.
- A disposable, git-ignored scratch copy of the repo was built with `Microsoft.CodeAnalysis.NetAnalyzers` 9.0.0 added, to get real (not guessed) warning data — details in section 6.

## 2. Ordered Steps

**Step 1 — Delete `.gitlab-ci.yml`**
- File: `/Users/daniel/_/sox/.gitlab-ci.yml`
- Action: `git rm .gitlab-ci.yml`. No replacement needed — GitHub Actions workflows already cover build+test on both OSes.

**Step 2 — Add root `.editorconfig`**
- File (new): `/Users/daniel/_/sox/.editorconfig`
- Set `root = true` so it isn't merged with any parent directory config.
- See exact rule shape in section 3.

**Step 3 — Add a `Directory.Build.props` to centralize the analyzer package**
- File (new): `/Users/daniel/_/sox/Directory.Build.props`
- Rationale: "across all `.csproj` files" — rather than pasting an identical `PackageReference` + settings block into `Sox.csproj`, `Sox.Tests.csproj`, and `Sox.EchoServer.csproj` (and having to remember to do it a fourth time for `Sox.AspNetCore` later), a root `Directory.Build.props` is picked up automatically by every project under the repo and is the standard MSBuild mechanism for exactly this. Content:
  ```xml
  <Project>
    <PropertyGroup>
      <EnableNETAnalyzers>true</EnableNETAnalyzers>
      <AnalysisLevel>latest</AnalysisLevel>
      <AnalysisMode>Recommended</AnalysisMode>
      <CodeAnalysisTreatWarningsAsErrors>false</CodeAnalysisTreatWarningsAsErrors>
    </PropertyGroup>
    <ItemGroup>
      <PackageReference Include="Microsoft.CodeAnalysis.NetAnalyzers" Version="9.0.0">
        <PrivateAssets>all</PrivateAssets>
        <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      </PackageReference>
    </ItemGroup>
  </Project>
  ```
  If a `Directory.Build.props` at repo root is undesirable for some reason (e.g. wanting fully explicit per-project control), the equivalent 8 lines can instead be pasted into the `<PropertyGroup>`/new `<ItemGroup>` of each of the three `.csproj` files individually — functionally identical, just 3x the maintenance surface.
- Do **not** set `TreatWarningsAsErrors` anywhere — leave existing project-level warning settings alone otherwise.

**Step 4 — Fix `Sox/Sox.csproj`'s `DocumentationFile` for Release**
- File: `/Users/daniel/_/sox/Sox/Sox.csproj`
- Current exact content of the two config-specific groups:
  ```xml
  <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' ">
    <DocumentationFile>bin\Debug\netstandard2.1\Sox.xml</DocumentationFile>
    <LangVersion>9.0</LangVersion>
  </PropertyGroup>
  <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Release|AnyCPU' ">
    <LangVersion>9.0</LangVersion>
  </PropertyGroup>
  ```
- Change: add the equivalent `DocumentationFile` line to the `Release|AnyCPU` group, pointed at the Release output path:
  ```xml
  <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Release|AnyCPU' ">
    <DocumentationFile>bin\Release\netstandard2.1\Sox.xml</DocumentationFile>
    <LangVersion>9.0</LangVersion>
  </PropertyGroup>
  ```
  (Alternative, less duplicative: move `<DocumentationFile>bin\$(Configuration)\netstandard2.1\Sox.xml</DocumentationFile>` into the top unconditioned `<PropertyGroup>` instead of duplicating it per-config — functionally equivalent and slightly cleaner; either is acceptable.)
- **This is the step that requires the most care** — see section 6, since it surfaces ~152 new compiler XML-doc warnings (not analyzer warnings) that don't exist in the current Release build.

**Step 5 — Add `CONTRIBUTING.md`**
- File (new): `/Users/daniel/_/sox/CONTRIBUTING.md`
- Outline in section 4.

**Step 6 — Add GitHub issue templates and a PR template**
- Files (new):
  - `/Users/daniel/_/sox/.github/ISSUE_TEMPLATE/bug_report.md`
  - `/Users/daniel/_/sox/.github/ISSUE_TEMPLATE/feature_request.md`
  - `/Users/daniel/_/sox/.github/ISSUE_TEMPLATE/config.yml` (optional — disables blank issues / links to CONTRIBUTING.md)
  - `/Users/daniel/_/sox/.github/PULL_REQUEST_TEMPLATE.md`
- Content shape in section 5.

**Step 7 — Verify** (see section 7).

## 3. Analyzer Package Version and `.editorconfig` Shape

**Package version: `Microsoft.CodeAnalysis.NetAnalyzers` `9.0.0`.**

Reasoning, checked against the live NuGet index and the environment's installed SDKs:
- Both CI workflows pin `dotnet-version: 9.0.303`. `9.0.0` is the last non-preview release in the analyzer package's `9.x` line (the package's own versioning re-based to match .NET SDK feature-band numbers starting with .NET 10, e.g. `10.0.100`, `10.0.101`, ... which now track monthly SDK servicing releases). Pinning `9.0.0` keeps the analyzer version aligned with the SDK actually used in CI today rather than jumping ahead to the `10.0.1xx` train.
- Roslyn analyzers run in the compiler process and analyze source per the project's `LangVersion`; `9.0.0` fully understands `LangVersion 9.0` C# syntax, and works fine against a `netstandard2.1`-targeted library — analyzer packages aren't gated by the analyzed project's TFM.
- When CI's pinned SDK is eventually bumped to .NET 10, bump this package to the matching `10.0.1xx` release at that time — not now.

**`.editorconfig` shape** — actually validated by building this repo's real source with these settings applied (see section 6), not just written from memory:

```ini
root = true

[*]
indent_style = space
indent_size = 4
end_of_line = lf
charset = utf-8
trim_trailing_whitespace = true
insert_final_newline = true

[*.cs]
indent_style = space
indent_size = 4

# Naming: match existing convention already used throughout Sox/ (private fields as _camelCase)
dotnet_naming_symbols.private_fields.applicable_kinds = field
dotnet_naming_symbols.private_fields.applicable_accessibilities = private
dotnet_naming_style.underscore_camel_case.required_prefix = _
dotnet_naming_style.underscore_camel_case.capitalization = camel_case
dotnet_naming_rule.private_fields_underscore.symbols = private_fields
dotnet_naming_rule.private_fields_underscore.style = underscore_camel_case
dotnet_naming_rule.private_fields_underscore.severity = suggestion

# --- Baseline CA rule overrides: start as warnings, don't let known/intentional patterns be noisy ---
# CA1051 (do not declare visible instance fields): the whole OnXxxEventArgs family under
# Sox/Server/Events/*.cs intentionally exposes public readonly fields as its public event-args
# API shape. Changing that is an API-breaking design decision out of scope for Phase 0 — suppress
# for now, revisit deliberately later if desired.
dotnet_diagnostic.CA1051.severity = none

# CA5350 (do not use weak cryptographic algorithms): Sox/Server/WebSocketServer.cs uses SHA1
# for the Sec-WebSocket-Accept handshake per RFC6455 section 1.3 - this is protocol-mandated,
# not a bug, and must never be "fixed" by swapping algorithms. Suppress with the paper trail
# here instead of an inline #pragma so it survives file edits.
dotnet_diagnostic.CA5350.severity = none

# Everything else stays at the analyzer's default severity (warning), not none/error.
```

Notes:
- Deliberately not enumerating every CA rule — the point of this phase is "on, as warnings, minus the two known-intentional false positives", not a fully bespoke ruleset. Rule tuning belongs to whichever phase actually touches the flagged code.
- `AnalysisMode=Recommended` (rather than leaving the SDK's implicit `Default`) is a deliberate choice: verified empirically (section 6) that the SDK's implicit default mode produces **zero** new warnings on this codebase today, which would make "add a baseline analyzer" a no-op in practice. `Recommended` is the documented, still-conservative middle tier (not `AllEnabledByDefault`, which is much noisier) and actually surfaces real, fixable signal without being alarming.

## 4. `CONTRIBUTING.md` Outline

```
# Contributing to Sox

## Before you start
- Note the README banner: Sox is pre-production; expect API/behavior changes.
- Check open issues / the roadmap doc (plan/00-overview.md) before starting large changes.

## Prerequisites
- .NET SDK matching CI: 9.0.303 (or later 9.x). `Sox` targets netstandard2.1;
  `Sox.Tests`/`Sox.EchoServer` target net9.0.

## Getting the code building
- `dotnet restore`
- `dotnet build`

## Running tests
- `dotnet test`
- Full coverage report (matches CI expectations, threshold 80% line coverage):
  `./test.sh` (requires `dotnet tool install -g dotnet-reportgenerator-globaltool` once)

## Code style
- Formatting/naming is enforced via the root `.editorconfig` — most editors/IDEs pick this
  up automatically (`dotnet format` can also be run locally).
- Static analysis: `Microsoft.CodeAnalysis.NetAnalyzers` runs as part of every build and
  surfaces warnings (not build-breaking yet). New code shouldn't introduce new warnings;
  existing warnings don't need to be fixed opportunistically inside unrelated PRs.
- Public API members should have XML doc comments (`///`) — `Sox.csproj` generates an XML
  doc file in both Debug and Release.

## Making a change
- Branch from `master`.
- Keep PRs scoped to one concern; large refactors should reference/align with
  `plan/00-overview.md`'s phases where applicable.
- Commit messages: this repo is moving toward Conventional Commits style
  (`fix:`, `feat:`, `refactor(scope)!:`, etc.) — not strictly enforced, but preferred.

## Pull requests
- Must pass CI (`Windows` and `Linux` GitHub Actions workflows: build + test).
- Describe what changed and why; link related issues.
- Add/update tests for behavior changes — see `Sox.Tests/` for existing patterns
  (NUnit-based unit tests of frame/message/HTTP parsing).

## Reporting bugs / requesting features
- Use the issue templates under `.github/ISSUE_TEMPLATE/`.
```

## 5. GitHub Issue/PR Template Types

Bare minimum, matching what a small OSS library needs — no custom forms/YAML schema complexity:

- **`bug_report.md`** — Markdown-based issue template. Sections: description, repro steps, expected vs actual behavior, environment (.NET version, OS), relevant logs/stack trace.
- **`feature_request.md`** — Sections: problem/motivation, proposed solution, alternatives considered.
- **`config.yml`** (optional, small) — sets `blank_issues_enabled: false` and optionally links to `CONTRIBUTING.md` and/or a discussions link, so people are steered toward the templates.
- **`PULL_REQUEST_TEMPLATE.md`** — a short checklist: what changed, related issue link, "tests added/updated", "docs updated if needed", "CI green".

## 6. Risks / Gotchas (grounded in actual builds of this codebase)

1. **The Release `DocumentationFile` fix (Step 4) surfaces ~152 real compiler warnings, not zero.** Verified directly by building `Sox/Sox.csproj` with `-p:GenerateDocumentationFile=true -p:DocumentationFile=...` against the real, unmodified source:
   - The overwhelming majority are `CS1591` ("Missing XML comment for publicly visible type or member") — e.g. `HttpHeaders` and its members have no XML docs at all, and there are more classes like it. These aren't dangerous, just noisy, and are compiler warnings (governed by `NoWarn`, not analyzer editorconfig severities).
   - Two are genuine, pre-existing doc bugs worth fixing as a 2-line drive-by while touching this file:
     - `Sox/Extensions/StreamExtensions.cs` — `ReadBytesAsync(this Stream stream, int bytesToRead, int bufferSize = 1024)`'s doc comment is missing a `<param name="bufferSize">` tag (`CS1573`).
     - `Sox/Extensions/StreamReaderExtensions.cs` — `ReadBytesAsync(this StreamReader sr, int bytesToRead)`'s doc comment has `<param name="stream">` but the actual parameter is named `sr` (`CS1572`/`CS1573` pair).
   - Recommendation: since Phase 0 explicitly avoids `TreatWarningsAsErrors`, these ~150 `CS1591`s won't break the build, but they will make CI logs noisy from this PR onward. Two reasonable options, pick one and say so in the PR: (a) accept the noise as a known/expected side effect of turning on Release doc generation (cheapest, matches "warnings only" intent), or (b) add `<NoWarn>$(NoWarn);CS1591</NoWarn>` alongside the `DocumentationFile` change so only genuinely broken doc comments (`CS1572`/`CS1573`, of which there are exactly 2, both fixable in the same PR) still show up. Lean toward (b) — a 1-line addition that keeps the phase's diff self-contained.

2. **CA1051 (visible instance fields) would fire on the library's entire event-args public surface if left at default severity.** `OnFrameEventArgs`, `OnBinaryMessageEventArgs`, `OnConnectionEventArgs`, `OnDisconnectionEventArgs`, `OnErrorEventArgs`, `OnTextMessageEventArgs` (all under `Sox/Server/Events/`) all use `public readonly Connection Connection;`-style fields as intentional design (not properties). This is a legitimate, deliberate API shape, not a bug — must be suppressed via `.editorconfig` (done above) rather than "fixed," since converting these to properties would be a breaking API change entirely out of scope for Phase 0.

3. **CA5350 (weak cryptographic algorithm) would fire on `WebSocketServer.cs`'s `SHA1.Create()` call**, which computes the RFC6455 `Sec-WebSocket-Accept` value. This is protocol-mandated (the spec requires SHA-1, no alternative), so it must be suppressed with a clear comment, not swapped for a "stronger" hash. Handled via `.editorconfig` above.

4. **`AnalysisMode=Recommended` will surface ~70 warnings across roughly a dozen other rule IDs** on the current codebase (verified by an actual build in a disposable scratch copy) — mainly `CA1305`/`CA1304`/`CA1310`/`CA1311` (locale-sensitive string/parse operations, e.g. `int.Parse(string)` in `HttpRequest.ParseRequestLine`), `CA2211`/`CA2201` (reserved-exception/static-field patterns), `CA1825`/`CA1835`/`CA1861`/`CA1862`/`CA1866` (minor perf suggestions like `new byte[0]` vs `Array.Empty<byte>()`), and a couple of naming ones (`CA1716`/`CA1711`). None build-breaking; fixing them is out of scope for Phase 0 — exactly the kind of "now visible, addressed opportunistically in later phases" signal.

5. **Existing `catch (Exception ex)` blocks** in `WebSocketServer.cs` (3 places) and one broad `catch (Exception)` in `HttpResponse.cs` will trip `CA1031` under `Recommended`/`AllEnabledByDefault` modes but did **not** appear in the `Recommended`-mode test run's captured warning list (only appeared under the more aggressive `AllEnabledByDefault` test) — worth a spot-check after the real PR lands.

6. **Zero uses of `ConfigureAwait(false)` anywhere in `Sox/`** (verified via grep — literal zero matches). Under `AllEnabledByDefault` this alone produced 48 `CA2007` warnings, by far the largest single category. `CA2007` is *not* part of `Recommended` mode's default rule set (confirmed empirically), so it stays silent with the settings recommended above — flagging in case a future phase decides to opt into `AllEnabledByDefault`, since that one change alone would need a real library-wide `ConfigureAwait(false)` sweep, not just a suppression.

7. **`Sox.TestWebClient`** is a static JS/HTML sample (has a `package.json`/jquery, not a `.csproj`) and isn't in `Sox.sln` — the `Directory.Build.props` in Step 3 has no effect on it and it needs no analyzer/editorconfig-for-C# changes.

8. **`obj`/`bin` build artifacts**: rerunning `dotnet build` after adding the `Directory.Build.props`/package reference requires `dotnet restore` to actually pull `Microsoft.CodeAnalysis.NetAnalyzers` — a stale `obj/project.assets.json` from before the change can make it look like the analyzer isn't running. Not a code risk, just a "restore before verifying" reminder worth putting in the PR description.

## 7. Verification

- `dotnet restore` then `dotnet build --configuration Debug` and `dotnet build --configuration Release` both from repo root — both should succeed with 0 errors; Release should now show the `CS1591`-family doc warnings (or 0 if `NoWarn` per item 1 is applied) and the `Recommended`-mode CA warnings from item 4, but no new errors.
- `dotnet test` (or `./test.sh` for the full coverage pass) — should be unaffected; no test project code changes in this phase.
- Confirm both GitHub Actions workflows (`.github/workflows/windows.yml`, `.github/workflows/linux.yml`) go green on the PR.
- Confirm `git status`/`git log` shows `.gitlab-ci.yml` removed and no other CI config references `Sox.Core.Tests` anywhere (`grep -r "Sox.Core.Tests" .` should return nothing after the deletion).
- Confirm `Sox/bin/Release/netstandard2.1/Sox.xml` is produced after a Release build (it currently is not — only the Debug one exists).
- Sanity-check the new `.editorconfig` is actually being picked up: open any `.cs` file in an IDE (or run `dotnet format --verify-no-changes`) and confirm no unexpected mass-reformat is proposed, since indent/newline settings should already match existing code style.
- Visually confirm on GitHub.com that opening "New issue" now offers the Bug Report / Feature Request template chooser, and that opening a PR pre-populates the PR template body.

### Critical Files for Implementation
- /Users/daniel/_/sox/.gitlab-ci.yml
- /Users/daniel/_/sox/Sox/Sox.csproj
- /Users/daniel/_/sox/Directory.Build.props (new)
- /Users/daniel/_/sox/.editorconfig (new)
- /Users/daniel/_/sox/CONTRIBUTING.md (new)
- /Users/daniel/_/sox/.github/ISSUE_TEMPLATE/ and /Users/daniel/_/sox/.github/PULL_REQUEST_TEMPLATE.md (new)

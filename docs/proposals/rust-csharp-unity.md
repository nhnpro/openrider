# Proposal: first-class Rust, C# and Unity support

Status: draft for discussion · 2026-11-06

## Goal

Make OpenIDEA a credible daily driver for:

- **Rust**: Cargo workspaces of any size, on par with RustRover for everyday work.
- **C# / .NET**: solutions, NuGet, tests and debugging, on par with Rider for non-Unity work.
- **Unity**: open a Unity project natively and get Rider-style Unity features:
  - asset-aware navigation ("find the prefabs and GameObjects using this script");
  - Unity-aware inspections;
  - a debugger attached to the Editor or a player;
  - control of the Editor from the IDE.

This document records what exists today (verified in code), what is missing, and an
ordered plan. Each phase is meant to ship as several small PRs, following `CONTRIBUTING.md`.

---

## 1. Where we are today

### Rust: good base, thin Cargo layer

Most Rust support comes from generic LSP, run, debug and test plumbing around
rust-analyzer and CodeLLDB.

| Area                                                           | Status      | Notes                                                                                                                                              |
| -------------------------------------------------------------- | ----------- | -------------------------------------------------------------------------------------------------------------------------------------------------- |
| Completion, navigation, rename, refactors, quick fixes         | Full        | Generic LSP over rust-analyzer (`languages/rust.toml`), installed via rustup with a VSIX fallback, waits for `experimental/serverStatus`.          |
| Semantic highlighting, clippy/check on save, build diagnostics | Full        | Settings: `ide-core/src/settings.rs:778`. Mapped to rust-analyzer in `ide-sdk/src/apply.rs:624`. Build parses rustc JSON.                          |
| Coverage                                                       | Full        | `cargo llvm-cov`, lcov (`ide-coverage/src/plan.rs`).                                                                                               |
| Cargo.toml editing                                             | Partial     | Version completion, outdated dependencies and an update fix. **No crate-name completion.**                                                         |
| Inlay hints                                                    | Partial     | Three generic toggles. rust-analyzer's own hint options are not exposed.                                                                           |
| Run / test gutter                                              | Partial     | **Regex-based** (`src/features/run/targets.ts:67`). No doc-test or bench markers.                                                                  |
| Test runner                                                    | Partial     | Parses libtest text output and nextest. Nextest is not auto-detected. No JSON reporter.                                                            |
| Run configurations                                             | Partial     | Auto-detects bins, `cargo test` and `cargo build`. No Cargo run-config type, no benches, no feature selection.                                     |
| Debugger                                                       | Partial     | CodeLLDB, cargo-artifact resolution, break on panic. No custom pretty-printers.                                                                    |
| Toolchains                                                     | Partial     | Discovers and selects rustup toolchains. Cannot install toolchains, components or targets. `rust-toolchain.toml` is not honoured.                  |
| Profiling                                                      | Partial     | `perf` on Linux only.                                                                                                                              |
| Cargo project model / tool window                              | **Missing** | `BuildTool` is only Maven or Gradle (`ide-build/src/lib.rs:33`). No members, targets or features view.                                             |
| rust-analyzer extensions                                       | **Missing** | No macro expansion, runnables, HIR/MIR, crate graph, parent module, reload workspace, rebuild proc macros, or structural search and replace (SSR). |

Two structural gaps sit under most of the Rust items:

- **No custom LSP requests.** The IDE cannot send rust-analyzer's custom (`rust-analyzer/*`, `experimental/*`) requests. There is no passthrough in `ide-lsp` or `src-tauri/src/commands/lsp.rs`, and the WIT plugin API has no LSP access either.
- **No Cargo project model.** Run targets come from regexes, and run detection reads TOML separately. `cargo metadata` is only used for External Libraries.

### C# / .NET: basic

| Area                                                                  | Status       | Notes                                                                                                                                 |
| --------------------------------------------------------------------- | ------------ | ------------------------------------------------------------------------------------------------------------------------------------- |
| Language server                                                       | Partial      | `csharp-ls` (`languages/csharp.toml`), no init options. Weaker than the Roslyn LSP in refactors, analyzers and decompiled navigation. |
| Run configurations                                                    | Full (basic) | Detector reads csproj/sln/slnx and `launchSettings.json` (`ide-run/src/detect/dotnet.rs`).                                            |
| Build                                                                 | Partial      | `dotnet build` before launch, with console links. Nothing reaches the Problems view.                                                  |
| Tests                                                                 | Partial      | `dotnet test` in the console only. No test tree, no gutter icons.                                                                     |
| Debugger                                                              | Partial      | netcoredbg, **launch only**. No `AttachKind` for .NET (`ide-dap/src/attach.rs:13`). No Intel Mac or win-arm64 build.                  |
| Solution view, NuGet, decompiler, analyzers, `.editorconfig` C# style | **Missing**  |                                                                                                                                       |
| Smoke tests and fixtures                                              | **Missing**  | No C# entry in `language_smoke.rs`, no `fixtures/csharp-*`.                                                                           |

### Unity: project hygiene done, intelligence missing

**Already done well:**

- **Detection:** `ide-workspace/src/unity.rs`.
- **Unity tree mode:** hides `Library/`, `Temp/` and `.meta` files.
- **`.meta` stays with its asset:** moves keep the GUID, copies get a fresh GUID and their internal references are rewritten, deletes remove both.
- **VCS:** new `.meta` files are staged with their assets.

**Missing:** everything Unity-specific beyond that:

- **Asset tooling:**
  - no index of `.unity`, `.prefab` or `.asset` files;
  - no "used in scenes/prefabs";
  - `.asmdef` files have no schema or navigation.
- **Code awareness:** OpenIDEA doesn't know Unity's event functions. `Update`/`Start` are not exempt from "unused" warnings, and there's no message completion.
- **Shader and UI files:** no ShaderLab or HLSL support, and no USS or UXML.
- **Editor integration:**
  - no Mono debugger, so OpenIDEA can't attach to the Editor or a player;
  - no Editor connection or Unity test runner;
  - OpenIDEA is not a Unity "External Script Editor", so the user must make Unity generate `.csproj` files through VS Code or Rider.

---

## 2. Shared foundations (do first: Rust, C# and Unity all need them)

| #   | Work                                                                                                                                                                                                                                                                                         | Why                                                                                   | Where                                                                                      |
| --- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------ |
| F1  | **Typed LSP custom-request passthrough**: `lsp_request(server, method, params)` with an allowlist per server, plus handling of server-to-client custom notifications. Expose a narrow version to plugins.                                                                                    | rust-analyzer extensions, Roslyn LSP project-loading notifications, Unity plugins.    | `crates/ide-lsp`, `src-tauri/src/commands/lsp.rs`, a new `wit/lsp.wit` (minor WIT version) |
| F2  | **Build-system model beyond Maven/Gradle**: `BuildTool::Cargo` and `BuildTool::Dotnet`, each with modules, targets and dependencies, shown in one "Build" tool window.                                                                                                                       | Cargo tool window, solution view, run configs from the real model instead of regexes. | `crates/ide-build`, `src/features/<build>`                                                 |
| F3  | **Structured test reporting**: libtest/nextest JSON (`--format json` / `--message-format libtest-json`), and TRX or the Microsoft.Testing.Platform protocol for .NET. Both produce the existing test tree.                                                                                   | Reliable test trees, rerun-failed, durations.                                         | `src/features/run/testing/`                                                                |
| F4  | **Attach-kind registry**: `AttachKind::Dotnet`, `AttachKind::UnityMono`, with process discovery per kind.                                                                                                                                                                                    | Attach to .NET, Unity Editor and players.                                             | `crates/ide-dap/src/attach.rs`, `src/features/debugger/attach.ts`                          |
| F5  | **Fixtures and smoke tests**: `fixtures/rust-workspace` (members, features, `build.rs`, proc macro), `fixtures/csharp-app` (sln, 2 projects, xUnit), `fixtures/unity-sample` (minimal project with generated csproj committed). Add them to `language_smoke`, `smoke/run` and `smoke/debug`. | Every phase below is verified against these.                                          | `fixtures/`, `crates/ide-host/tests/language_smoke.rs`, `smoke/`                           |

---

## 3. Rust plan

### R1: Cargo project model (core, on F2)

- Load the model from `cargo metadata --format-version 1` (cached, refreshed when `Cargo.toml`/`Cargo.lock` change).
  - Packages, targets (lib/bin/example/test/bench/build-script), features, dependencies.
  - Honour `rust-toolchain.toml` for the cargo/rustc used.
- Project tree: workspace members shown as modules. `target/` stays excluded, and `OUT_DIR` code generated by build scripts appears under External Libraries.
- **Cargo tool window**: members → targets → run/debug/test; a feature checklist (stored per project, passed as `--features` / `--no-default-features` to run, test, build and rust-analyzer `cargo.features`); dependencies tree with "outdated" badges (reusing `ide-workspace/src/dependencies/cargo.rs`).
- Replace regex run targets with rust-analyzer **runnables** (`experimental/runnables`, via F1). Keep the regexes as a fallback while rust-analyzer loads. This adds doc-test and bench gutter icons.

### R2: Run, test and debug

- A dedicated **Cargo run-config type**: command (run/test/bench/build/custom), package, target, features, profile, env, args.
- Auto-detect **nextest** and offer it as the test runner. Use JSON output (F3).
- Debugger: ship rustc's LLDB pretty-printer scripts from the active toolchain's sysroot (`lib/rustlib/etc/lldb_commands`). Show `Rc`, `Arc`, `RefCell`, `HashMap` and `Vec` correctly. Debug a single doc test or integration test from the gutter.
- Profiling on macOS through `samply` (cross-platform, writes Firefox-profiler format). Keep `perf` on Linux.

### R3: rust-analyzer power features (via F1)

Each is a small, separate PR with its own action id:

- Expand macro recursively, in a read-only editor tab.
- View HIR, MIR and the crate graph.
- Go to parent module; open `Cargo.toml` of the current crate.
- Reload workspace; rebuild proc macros. Proc-macro and build-script settings go on the Rust settings page.
- Structural search and replace (SSR) dialog.
- Expose all rust-analyzer inlay-hint kinds (chaining, lifetimes, closure return, discriminants) as settings.

### R4: Toolchain and editing polish

- Toolchain manager: install or update toolchains, components (clippy, rustfmt, rust-src, llvm-tools) and targets through rustup (`Method::Rustup` already exists in `ide-tools`).
- Crate-name completion in `Cargo.toml` (crates.io search API, with debouncing and caching).
- `rustfmt.toml` / `clippy.toml` schema support through taplo.

---

## 4. C# / .NET plan

### C1: Language server upgrade (**needs-decision**)

Recommendation: move to the **Roslyn language server** (`Microsoft.CodeAnalysis.LanguageServer`, MIT, from dotnet/roslyn; the server used by the VS Code C# extension). Keep csharp-ls as a fallback setting.

- **Gains:** full Roslyn refactors and code fixes, analyzers in the project and in NuGet packages (needed for Unity in U3), `.editorconfig` code style, and navigation into decompiled metadata.
- **Costs:**
  - Non-standard startup: the client must send `solution/open` or `project/open` and handle project-loading notifications, which needs F1.
  - It is distributed from a NuGet feed per runtime identifier, not nuget.org; the installer and licence need checking before we redistribute it.
- Add a smoke test (F5) before switching the default.

### C2: Solution model, build and NuGet (on F2)

- Solution view from `.sln`/`.slnx` and the csproj project graph. Edit project references and multi-targeting from the IDE.
- `dotnet build` diagnostics go to the Problems view: parse MSBuild's `file(line,col): error CS1234: …` output, or use a binary-log logger.
- **NuGet tool window**:
  - search, install, update and remove packages (`dotnet add/remove package`, nuget.org v3 API);
  - outdated badges, shared with the dependencies inspection framework;
  - central package management (`Directory.Packages.props`).

### C3: Tests and debugging

- Test tree for xUnit, NUnit and MSTest using TRX or the Microsoft.Testing.Platform JSON-RPC protocol (F3), with gutter icons.
- netcoredbg **attach** (F4), process picker filtered to .NET processes. Add Intel Mac and win-arm64 builds, from source if needed.
- Note: `vsdbg` may not be used outside Microsoft products. netcoredbg (MIT) stays the debugger.

---

## 5. Unity plan (Rider-style)

The order follows user value: first make the project work without other tools (U1), then
the feature Rider users miss most, asset-aware navigation (U2–U3), then the debugger and
Editor link (U4–U5).

### U1: OpenIDEA as Unity's external script editor

- Ship a **Unity package** `dev.prefabric.unity` (new top-level `unity/` folder, installable by git URL or OpenUPM):
  - implements `IExternalCodeEditor`, so OpenIDEA appears in _Preferences › External Tools_ and opens files at a line;
  - generates `.sln`/`.csproj` itself, replacing the "pick VS Code or Rider" step in `ProjectView.tsx:785`.
- The IDE offers to install the package when it opens a Unity project that doesn't have it (edits `Packages/manifest.json`, with confirmation).
- Language mappings:
  - `.asmdef`/`.asmref` → JSON with a schema, with go-to on assembly references.
  - `.uss` → CSS with Unity properties.
  - `.uxml` → XML with Unity's generated schemas.
  - `.unity`, `.prefab`, `.asset`, `.mat`, `.controller`, `.anim` → Unity YAML, read-only by default.

### U2: Unity asset index (core, Rust)

- New crate **`ide-unity`** (or an extension of `ide-workspace/src/unity.rs`): a streaming parser for Unity's YAML. It must handle the `--- !u!<classId> &<fileId>` documents, `stripped` objects and prefab variants/overrides.
- Index, incrementally, on the existing index worker:
  - `.meta` GUID → asset path;
  - script GUID (`m_Script: {fileID: 11500000, guid: …}`) → GameObjects/prefabs using that MonoBehaviour;
  - GameObject names and hierarchy paths in each scene or prefab;
  - serialized field values per component;
  - **UnityEvent targets** (`m_PersistentCalls` → `m_Target` + `m_MethodName`);
  - animation events.
- Parse large scenes on rayon and honour cancellation (CLAUDE.md rules). Budget: re-indexing a 50 MB scene must stay off the UI thread.

### U3: Unity code intelligence

- **Find Usages includes assets**: for a MonoBehaviour or ScriptableObject class, list the scenes, prefabs and GameObjects using it. Methods include UnityEvent or animation-event bindings; serialized fields include overridden values in prefabs.
- **Code Vision lines** above classes and methods ("used in 3 prefabs", "event handler in Main.unity"), and gutter icons on event functions (Update, Awake, OnTriggerEnter…).
- **Search Everywhere › Unity tab**: find a GameObject by name in all scenes and prefabs, or a prefab or asset by name. Opening a result reveals it in the hierarchy-path view and, with U5, selects it in the Editor.
- **Inspections**: bundle **Microsoft.Unity.Analyzers** (MIT, the analyzers used by Visual Studio Tools for Unity), injected into the generated csproj (U1) and run by the Roslyn LSP (C1).
  - Gives: event functions not reported as unused, `[SerializeField]` fields not reported as never assigned, and many Unity-specific diagnostics and fixes.
  - Message completion (type `OnTrig…` → full signature) is done by an OpenIDEA completion provider that reads a generated list of Unity messages.
- "Rename" of a serialized field offers to add `[FormerlySerializedAs("old")]`.

### U4: Unity debugger (**needs-decision**: build our own vs fork)

- Unity uses the **Mono soft debugger protocol**, which netcoredbg does not support. Plan:
  - Write a DAP adapter over **Mono.Debugger.Soft** (MIT, mono/debugger-libs), as a self-contained .NET tool installed by `ide-tools`. Prior art: the archived MIT `vscode-unity-debug`.
  - Add `AttachKind::UnityMono` (F4) with discovery of:
    - **Editors**: listening on port `56000 + pid % 1000`;
    - **players**: they announce themselves by UDP multicast `225.0.0.222:54997`;
    - **devices over USB**: Android via `adb forward`, iOS via `iproxy`.
- Features: breakpoints (including in package code), stepping, evaluation, exceptions, "Attach to Unity Editor and Play" (needs U5).
- Spike first (1 week): attach to the Editor, hit a breakpoint, list locals. Go/no-go before investing further.

### U5: Editor connection

- `dev.prefabric.unity` opens a local socket (token-authenticated: see the security review) and offers:
  - Play / Pause / Step from the IDE toolbar, with play-mode state in the status bar;
  - Refresh assets (`AssetDatabase.Refresh`) on save, or on demand;
  - **Unity Console** tool window with streamed logs, clickable stack traces, and filters for log, warning and error;
  - compile status from the Editor;
  - "Show in Unity" (ping or select the asset or GameObject from U3 results).
- **Unity Test Runner** integration through `TestRunnerApi`: EditMode and PlayMode trees in the existing test UI, run and rerun from gutter icons.

### U6: Shaders

- ShaderLab: a Lezer grammar for structure and highlighting, with **embedded HLSL** in `CGPROGRAM`/`HLSLPROGRAM` blocks.
- HLSL language server: evaluate `shader-language-server` (Antaalt; check its licence) for diagnostics and completion. Include paths are resolved from `Packages/` and `Library/PackageCache`.

---

## 6. Order and rough size

Estimates are in engineer-weeks for someone who knows the codebase, and are rough.

| Order | Item                                     | Size     | Unblocks                  |
| ----- | ---------------------------------------- | -------- | ------------------------- |
| 1     | F5 fixtures and smoke tests              | 1        | everything (verification) |
| 2     | F1 LSP custom requests                   | 1–2      | R1 runnables, R3, C1      |
| 3     | U1 Unity package and editor registration | 2–3      | U3 analyzers, U5          |
| 4     | R1 Cargo model and tool window (F2)      | 3–4      | R2                        |
| 5     | C1 Roslyn LSP (after decision)           | 2–3      | U3 inspections            |
| 6     | U2 asset index                           | 3–4      | U3                        |
| 7     | U3 Unity code intelligence               | 3–4      | —                         |
| 8     | U4 spike → debugger                      | 1 + 4–6  | —                         |
| 9     | U5 Editor connection and test runner     | 3        | —                         |
| 10    | R2, R3, R4, C2, C3, U6                   | 2–3 each | can run in parallel       |

**Highest value first**:

- Unity users: U1 and U2–U3 (asset-aware find usages, working inspections) and U4 (debugger).
- Rust users: R1 (Cargo model and runnables) and R3 (macro expansion).

## 7. Decisions needed (label `needs-decision`)

1. **C# server**: Roslyn LSP (recommended) or csharp-ls. This affects analyzer support and Unity inspections.
2. **Unity debugger**: write our own DAP over Mono.Debugger.Soft (recommended, after a spike) or fork the archived MIT adapter.
3. **Unity package distribution**: OpenUPM, git URL, or bundled in the app and copied into `Packages/`.
4. **Core or plugin**:
   - Unity, Cargo and .NET features in core crates (recommended: they need LSP, debugger and index internals that the WIT API doesn't expose);
   - or bundled plugins once F1 adds LSP access to WIT.
5. **Security bar for the Editor socket (U5)**: local socket with a per-session token in `Library/` (not committed). It must never listen on non-loopback addresses.

## 8. Risks

- **Unity YAML scale**: large projects have thousands of prefabs. The index must be incremental and its memory bounded. Measure it against `perf/budgets.json` with a new budget.
- **Roslyn LSP**: its protocol extensions change between releases. Pin the version, and cover it with the smoke test.
- **Mono debugger**: Unity versions differ in protocol details. Test on two LTS versions (e.g. 2022.3 and Unity 6).
- **Licensing**: confirm redistribution terms for the Roslyn LSP builds, Microsoft.Unity.Analyzers and the HLSL server before bundling.

# Targeted Agent Skills Design

Date: 2026-06-25
Repo: G:\Side_Projects\U3D-Dev-Tools-AI
Package: com.sputnicyoji.u3d-dev-tools-ai
Status: approved for planning, not yet implemented

## Decision

Keep the existing three skill names and rewrite them as targeted operational skills.
Do not add a single router skill.
Do not split the UPM package.

The package remains the source of truth:

```text
Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/test-runner-mcp/SKILL.md
Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/unity-editor-debug-mcp/SKILL.md
Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/unity-lua-device-debug/SKILL.md
```

U3D Dev Tools AI Settings remains the sync surface:

```text
Edit > Project Settings > U3D Dev Tools AI
```

## Current repo facts

The package already contains three skill directories under `Agent~/skills`.
Each directory has `SKILL.md` and the Python client files needed by the agent.

The linker already supports multiple skill directories inside one package:

```text
AgentPackageSourceResolver.cs: scans Agent~/skills/*
AgentPackageSourceResolver.cs: uses skillName as sync ToolId when a package has multiple skills
AgentSyncOrchestrator.cs: copies to <project>/.u3d-ai-linker/skills
AgentSyncOrchestrator.cs: creates links into .claude/skills and .agents/skills
U3DAILinkerSettingsProvider.cs: exposes Sync Agent Assets and Repair Links
```

The missing part is not distribution.
The missing part is skill determinism and Settings UI clarity.

## Problem

The current skill files work as long protocol notes.
That makes an agent read too much before acting.
It also mixes invocation, workflow, protocol details, and troubleshooting in one file.

The result is weaker routing:

- test execution can be confused with general Unity batchmode testing.
- editor inspection can be confused with arbitrary reflection without an evidence loop.
- Lua device debugging can be attempted before checking `ILuaDeviceDebugHost` and command descriptors.
- users cannot see from the Unity Settings page which skills are synced, stale, or conflicting.

## Goals

1. Make each tool skill operational.
   Each `SKILL.md` gives a deterministic step sequence, not a protocol dump.

2. Keep model invocation precise.
   Each description is short and names real trigger branches.

3. Use progressive disclosure.
   Protocol tables, request bodies, troubleshooting, and examples live in `references/`.

4. Preserve one package install path.
   Users install one UPM package and sync skills from that package.

5. Add a visible Agent Skills panel.
   Users can see skill source, sync state, links, and copyable update instructions.

6. Keep sync explicit.
   No silent writes to `.claude/skills`, `.agents/skills`, `CLAUDE.md`, or `AGENTS.md`.

## Non-goals

- No package split.
- No new runtime service.
- No automatic background skill sync on package load.
- No direct writes into `Library/PackageCache`.
- No direct git pull into `.claude/skills` or `.agents/skills`.
- No arbitrary Lua eval.
- No change to existing port allocation semantics.

## Skill authoring rules

Apply these rules to all three `SKILL.md` files:

1. `description` is model-facing and short.
   It lists trigger branches only.

2. `SKILL.md` starts with the operational loop.
   The loop has ordered steps and checkable completion criteria.

3. Reference material moves down.
   Long protocol tables, endpoint schemas, examples, and troubleshooting belong in `references/`.

4. One meaning has one home.
   Port resolution rules should be shared by common wording or generated copies, not manually divergent prose.

5. Each tool has a refusal or fallback branch.
   The agent must know when the service is unavailable and what evidence to report.

6. Use project-aware resolution first.
   Prefer `--project <unity-project-root>`, then `--pid`, then `--port` only for forced debugging.

## Skill 1: test-runner-mcp

Purpose:
Run Unity tests while an Editor is online.

Leading word:
test loop.

Trigger branches:

- run Unity EditMode tests.
- run Unity PlayMode tests.
- trigger Unity recompilation.
- list or verify Unity test names.
- TDD verification when the Editor service is online.

Operational loop:

1. Resolve endpoint.
   Use `client.py --project <root> ping` when the Unity project root is known.
   Completion: ping returns service identity and state.

2. Gate on state.
   Continue only when state is Idle.
   Completion: non-Idle state is either waited out or reported with evidence.

3. Recompile when code changed.
   Call recompile and stop on compile errors.
   Completion: response says `hasErrors=false`, or compile failure is reported.

4. List tests when filters are uncertain.
   Use `list-tests` before constructing exact `testNames`.
   Completion: requested test filters match at least one discovered test, unless running all tests intentionally.

5. Run tests.
   Start async job, poll status, collect result.
   Completion: final status is completed or error, with passed/failed/skipped and failure details.

6. Report actionable failures.
   Include test name, assertion message, and stack frame.
   Completion: output lets the next agent edit the failing source without parsing XML manually.

References:

```text
references/run-e2e.py
references/protocol.md
references/troubleshooting.md
```

## Skill 2: unity-editor-debug-mcp

Purpose:
Inspect live Unity Editor state through HTTP+JSON reflection and diagnostics.

Leading word:
inspect.

Trigger branches:

- read Unity Console.
- inspect Selection, Hierarchy, assets, UI Toolkit, profiler, frame debugger, or Editor state.
- describe Unity types or members before invoking unknown APIs.
- collect runtime evidence from Play Mode without GUI clicking.

Operational loop:

1. Resolve endpoint.
   Use `client.py --project <root> ping` first.
   Completion: ping returns service identity and Editor state.

2. Choose evidence target.
   Console, selection, hierarchy, asset, type, method, profiler, or UI tree.
   Completion: the target is named before invoking APIs.

3. Describe before uncertain invocation.
   If type or overload is uncertain, call `describe` first.
   Completion: selected member signature is known.

4. Invoke or batch.
   Use `invoke-chain` or batch for multi-step reads.
   Completion: result is bounded, serialized, and not truncated beyond usefulness.

5. Verify with a second signal when mutation or visual state matters.
   Use Console, ping state, hierarchy readback, or screenshot-capable tools outside this package when available.
   Completion: report includes the evidence source.

6. Stop on unsafe eval.
   `/eval` remains disabled by default.
   Completion: agent uses describe/invoke instead of enabling eval.

References:

```text
references/api-cookbook.md
references/protocol.md
references/troubleshooting.md
references/manual-verification.md
references/run-e2e.py
```

## Skill 3: unity-lua-device-debug

Purpose:
Inspect project-registered Lua runtime diagnostics in Editor or Android Development Build.

Leading word:
device diagnostics.

Trigger branches:

- inspect Lua runtime state.
- list Lua debug commands.
- execute a registered Lua debug command.
- inspect Android Development Build through adb forward.
- diagnose missing or invalid `ILuaDeviceDebugHost` registration.

Operational loop:

1. Select target.
   Editor target uses project-aware local resolution.
   Android target uses adb forward first.
   Completion: target mode is named in the report.

2. Ping.
   Completion: ping proves service is reachable, or failure says whether Editor/package/device/forwarding is missing.

3. List commands.
   Completion: command descriptors are visible before execution.

4. Execute only registered commands.
   Completion: command id is present in descriptor list.

5. Guard mutation.
   Mutating commands require descriptor `mutating=true` and CLI `--allow-mutation`.
   Completion: non-mutating command ran, or mutation gate was explicitly satisfied.

6. Clean adb forward when owned by CLI.
   Completion: created forward is removed when the diagnostic session ends.

References:

```text
references/protocol.md
references/android-adb-forwarding.md
```

## Settings UI design

Add or refine a section named `Agent Skills` in `U3DAILinkerSettingsProvider`.

Fields:

```text
Source Package
  package name
  resolved path
  package revision or git hash

Skill rows
  skill name
  source path
  generated path
  Claude link state
  Codex link state
  ownership hash
  state: Synced | Stale | Missing | Conflict | Source Missing
```

Actions:

```text
Sync Agent Skills
Repair Skill Links
Open Generated Skills Folder
Copy Skill Update Instructions
Copy Diagnostic Report
```

State rules:

- `Synced`: generated skill exists, ownership tool id matches, content hash matches current source, links point to generated skill.
- `Stale`: generated skill exists, ownership is valid, source hash differs.
- `Missing`: source exists but generated target or links do not exist.
- `Conflict`: generated target or final link exists but is not linker-owned or is not a junction.
- `Source Missing`: package resolved path does not contain expected `Agent~/skills/<skill>/SKILL.md`.

The existing sync operation remains explicit.
A package update may make the panel show `Stale`, but does not write files until the user clicks sync.

## Data flow

```text
Git UPM package update
-> Unity resolves package into PackageCache or local file path
-> Settings UI reads PackageInfo.resolvedPath
-> AgentPackageSourceResolver scans Agent~/skills
-> Sync Agent Skills copies each skill to .u3d-ai-linker/skills/<skill>
-> AgentSyncService writes .u3d-ai-owner.json
-> WindowsJunctionManager links .claude/skills/<skill>
-> WindowsJunctionManager links .agents/skills/<skill>
-> ManagedFileSync updates CLAUDE.md and AGENTS.md managed blocks
```

## Error handling

1. Source missing.
   Show `Source Missing` and block sync for that skill.

2. Non-managed generated skill.
   Show `Conflict`; do not overwrite.

3. Non-junction `.claude/skills/<skill>` or `.agents/skills/<skill>` exists.
   Show `Conflict`; do not overwrite.

4. Junction points to stale generated path.
   `Repair Skill Links` may relink when generated ownership is valid.

5. PackageCache path changed.
   Recompute source from `PackageInfo.resolvedPath`; do not store absolute PackageCache paths in project settings.

6. Sync fails mid-operation.
   Preserve current rollback behavior: staging, ownership, backup, promote, junction repair, restore on failure.

## Testing plan

Fast checks:

```powershell
python -m py_compile Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/test-runner-mcp/client.py
python -m py_compile Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/unity-editor-debug-mcp/client.py
python -m py_compile Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/unity-lua-device-debug/client.py
python tools/test-agent-port-resolvers.py
python tools/test-single-package-layout.py
```

Add source-level tests:

```text
tools/test-agent-skill-shape.py
```

It should verify:

- exactly three public skill directories exist.
- each has valid frontmatter.
- each has a short model-facing description.
- each has an operational loop section.
- each names project-aware endpoint resolution.
- each long protocol detail is in `references/`, not duplicated in every `SKILL.md`.
- no `__pycache__` or generated bytecode is included in Agent~.

EditMode tests to update or add:

```text
AgentPackageSourceResolverTests
AgentSyncOrchestratorTests
U3DAILinkerSettingsProviderTests
PanelStateModelTests or a new AgentSkillStateModelTests
```

Unity validation:

```powershell
pwsh -File tools/run-editmode.ps1 -Project u3d-ai-linker -Unity <unity.exe>
```

If Unity Package Manager fails before script compilation with the known `path` undefined error, record the exact log and keep the fast checks as current verification.

## Rollout

1. Commit this design spec.
2. Write an implementation plan under `docs/superpowers/plans/` after spec review.
3. Rewrite the three skills.
4. Add skill shape tests.
5. Add or refine Agent Skills UI state model.
6. Update README with skill update flow.
7. Run fast checks and available EditMode tests.
8. Release only if requested.

## Acceptance criteria

- The package still installs as one Git URL UPM dependency.
- `Sync Agent Assets` still installs all three skill directories.
- The three `SKILL.md` files are shorter and operational.
- Long protocol details are still available through `references/`.
- Settings UI exposes skill sync state and update instructions.
- Existing ownership and conflict protections remain intact.
- All added tests pass.

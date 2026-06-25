# Targeted Agent Skills Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rewrite the three bundled agent skills as deterministic operational skills and expose their package-sync state in the Unity Settings panel.

**Architecture:** Keep `Agent~` as the package-owned source of truth. Add a pure `AgentSkillStateModel` that compares `Agent~/skills/*` against `.u3d-ai-linker/skills/*` plus `.claude` and `.agents` junctions, then render that model from `U3DAILinkerSettingsProvider`. Keep protocol detail in `references/` and keep sync explicit through the existing `AgentSyncOrchestrator`.

**Tech Stack:** Unity 2022.3 Editor C#, NUnit EditMode tests, Python source-shape tests, UPM Git package, PowerShell validation.

---

## File structure

### Create

- `tools/test-agent-skill-shape.py`
  - Source-level guard for `Agent~/skills` shape, frontmatter, operational-loop headings, short descriptions, project-aware resolver wording, and tracked bytecode exclusion.

- `Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/test-runner-mcp/references/protocol.md`
  - Long Test Runner HTTP endpoint and response detail moved out of `SKILL.md`.

- `Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/test-runner-mcp/references/troubleshooting.md`
  - Test Runner service failure and fallback cases.

- `Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/unity-lua-device-debug/references/protocol.md`
  - Lua Device Debug command, mutation, and endpoint contract.

- `Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/unity-lua-device-debug/references/android-adb-forwarding.md`
  - Android Development Build forwarding workflow.

- `Packages/com.sputnicyoji.u3d-dev-tools-ai/Editor/U3DAILinker/Settings/AgentSkillStateModel.cs`
  - Pure-ish state model for source/generated/link/ownership status.

- `Packages/com.sputnicyoji.u3d-dev-tools-ai/Tests/Editor/U3DAILinker/Settings/AgentSkillStateModelTests.cs`
  - EditMode tests for Synced, Stale, Missing, Conflict, and SourceMissing.

### Modify

- `Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/test-runner-mcp/SKILL.md`
  - Replace protocol-heavy body with operational loop and references.

- `Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/unity-editor-debug-mcp/SKILL.md`
  - Replace protocol-heavy body with inspect loop and references.

- `Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/unity-lua-device-debug/SKILL.md`
  - Replace short transport note with device diagnostics loop and references.

- `Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/fragments/*/{AGENTS.md,CLAUDE.md}`
  - Align fragments with shorter operational skill descriptions.

- `Packages/com.sputnicyoji.u3d-dev-tools-ai/Editor/U3DAILinker/Settings/U3DAILinkerSettingsProvider.cs`
  - Draw `Agent Skills` section, copy update instructions, and include skill rows in diagnostics.

- `Packages/com.sputnicyoji.u3d-dev-tools-ai/Tests/Editor/U3DAILinker/U3DAILinkerSettingsProviderTests.cs`
  - Assert update instructions and diagnostic Agent Skills output.

- `README.md`
  - Add skill update flow through package update plus Sync Agent Skills.

---

### Task 1: Add skill shape guard

**Files:**
- Create: `tools/test-agent-skill-shape.py`

- [ ] **Step 1: Write the failing source-shape test**

Create `tools/test-agent-skill-shape.py` with this content:

```python
from __future__ import annotations

import re
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SKILLS_ROOT = ROOT / "Packages" / "com.sputnicyoji.u3d-dev-tools-ai" / "Agent~" / "skills"
EXPECTED = {
    "test-runner-mcp",
    "unity-editor-debug-mcp",
    "unity-lua-device-debug",
}
MAX_DESCRIPTION_CHARS = 260


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def fail(message: str) -> None:
    raise AssertionError(message)


def parse_frontmatter(text: str) -> dict[str, str]:
    if not text.startswith("---\n"):
        fail("missing opening frontmatter fence")
    end = text.find("\n---\n", 4)
    if end < 0:
        fail("missing closing frontmatter fence")
    result: dict[str, str] = {}
    for line in text[4:end].splitlines():
        if not line.strip():
            continue
        if ":" not in line:
            fail(f"invalid frontmatter line: {line}")
        key, value = line.split(":", 1)
        result[key.strip()] = value.strip().strip('"')
    return result


def test_exact_skill_directories() -> None:
    actual = {p.name for p in SKILLS_ROOT.iterdir() if (p / "SKILL.md").is_file()}
    assert actual == EXPECTED, f"unexpected skills: {sorted(actual)}"


def test_each_skill_is_operational_and_project_aware() -> None:
    for skill in sorted(EXPECTED):
        path = SKILLS_ROOT / skill / "SKILL.md"
        text = read(path)
        frontmatter = parse_frontmatter(text)
        assert frontmatter.get("name") == skill
        description = frontmatter.get("description", "")
        assert 40 <= len(description) <= MAX_DESCRIPTION_CHARS, f"{skill} description length={len(description)}"
        assert "## Operational loop" in text, f"{skill} missing operational loop"
        assert "## References" in text, f"{skill} missing references"
        assert "Completion:" in text, f"{skill} missing completion criteria"
        assert "--project" in text, f"{skill} must prefer project-aware endpoint resolution"
        assert "## Protocol" not in text, f"{skill} protocol detail belongs in references"


def test_reference_files_exist_for_long_protocols() -> None:
    required = [
        SKILLS_ROOT / "test-runner-mcp" / "references" / "protocol.md",
        SKILLS_ROOT / "test-runner-mcp" / "references" / "troubleshooting.md",
        SKILLS_ROOT / "unity-editor-debug-mcp" / "references" / "protocol.md",
        SKILLS_ROOT / "unity-editor-debug-mcp" / "references" / "troubleshooting.md",
        SKILLS_ROOT / "unity-lua-device-debug" / "references" / "protocol.md",
        SKILLS_ROOT / "unity-lua-device-debug" / "references" / "android-adb-forwarding.md",
    ]
    missing = [str(p.relative_to(ROOT)) for p in required if not p.is_file()]
    assert not missing, "missing references: " + ", ".join(missing)


def test_no_tracked_python_bytecode_under_agent_assets() -> None:
    result = subprocess.run(
        ["git", "ls-files", "Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~"],
        cwd=ROOT,
        check=True,
        text=True,
        capture_output=True,
    )
    offenders = [line for line in result.stdout.splitlines() if re.search(r"(__pycache__|\.pyc$)", line)]
    assert offenders == [], "tracked bytecode under Agent~: " + ", ".join(offenders)


if __name__ == "__main__":
    for test in [
        test_exact_skill_directories,
        test_each_skill_is_operational_and_project_aware,
        test_reference_files_exist_for_long_protocols,
        test_no_tracked_python_bytecode_under_agent_assets,
    ]:
        test()
        print("OK", test.__name__)
```

- [ ] **Step 2: Run the shape test and verify it fails**

Run:

```powershell
python tools/test-agent-skill-shape.py
```

Expected: FAIL because current `SKILL.md` bodies do not all contain `## Operational loop`, and lua reference files do not exist.

- [ ] **Step 3: Commit the failing guard only if using strict TDD checkpointing**

Do not commit this red state on `main` if executing inline. Keep it staged or unstaged until Task 2 turns it green.

---

### Task 2: Rewrite the three bundled skills and references

**Files:**
- Modify: `Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/test-runner-mcp/SKILL.md`
- Modify: `Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/unity-editor-debug-mcp/SKILL.md`
- Modify: `Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/unity-lua-device-debug/SKILL.md`
- Create: `Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/test-runner-mcp/references/protocol.md`
- Create: `Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/test-runner-mcp/references/troubleshooting.md`
- Create: `Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/unity-lua-device-debug/references/protocol.md`
- Create: `Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/unity-lua-device-debug/references/android-adb-forwarding.md`
- Modify: `Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/fragments/test-runner-mcp/AGENTS.md`
- Modify: `Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/fragments/test-runner-mcp/CLAUDE.md`
- Modify: `Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/fragments/unity-editor-debug-mcp/AGENTS.md`
- Modify: `Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/fragments/unity-editor-debug-mcp/CLAUDE.md`
- Modify: `Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/fragments/unity-lua-device-debug/AGENTS.md`
- Modify: `Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/fragments/unity-lua-device-debug/CLAUDE.md`

- [ ] **Step 1: Replace `test-runner-mcp/SKILL.md`**

Use this body:

```markdown
---
name: test-runner-mcp
description: Run Unity Test Runner through the online Editor service. Use for EditMode/PlayMode tests, recompilation, listing tests, or TDD verification when Unity is already open.
---

# Test Runner MCP

Use this skill for the online Unity test loop.
Do not use it when the Editor is closed or the package is not installed.

## Operational loop

1. Resolve the endpoint.
   Run `python client.py --project <unity-project-root> ping` when the project root is known.
   Use `--pid <editor-pid>` to disambiguate multiple Editors for the same project.
   Use `--port <port>` only to force a known endpoint.
   Completion: ping returns `service`, `state`, `projectName`, and port metadata.

2. Gate on Idle.
   If state is `Compiling` or `Running`, poll or report the blocking state.
   Completion: state is `Idle`, or the response proves why testing cannot start.

3. Recompile after code changes.
   Run `python client.py --project <unity-project-root> recompile`.
   Stop when `hasErrors=true`.
   Completion: compile response has `hasErrors=false`, or compile errors are reported.

4. Verify test filters.
   Run `list-tests --mode EditMode` or `list-tests --mode PlayMode` before exact filtered runs.
   Skip this only for intentional run-all.
   Completion: each requested test name or filter matches at least one discovered test.

5. Run and poll.
   Run `run-tests --mode <EditMode|PlayMode>` with explicit filters or run-all.
   Poll `test-status` until the job is no longer running.
   Completion: final status is `completed` or `error`.

6. Report failures as edit targets.
   Include `name`, `message`, and the first useful stack frame from each failure.
   Completion: the next edit can start without parsing NUnit XML.

## Fallback

If ping cannot reach the service, report the exact command, timeout, and resolver path used.
Then fall back to Unity batchmode only when the task still requires tests.
Batchmode is outside this skill.

## References

- `references/protocol.md` for endpoints, request bodies, and response fields.
- `references/troubleshooting.md` for service failure cases.
- `references/run-e2e.py` for package-level smoke verification.
```

- [ ] **Step 2: Replace `unity-editor-debug-mcp/SKILL.md`**

Use this body:

```markdown
---
name: unity-editor-debug-mcp
description: Inspect a live Unity Editor through HTTP+JSON. Use for Console, Hierarchy, Selection, assets, UI Toolkit, profiler, type description, or reflection invoke evidence.
---

# Unity Editor Debug MCP

Use this skill to inspect the running Editor.
Prefer evidence reads over GUI assumptions.
Do not enable `/eval`; it is disabled by default.

## Operational loop

1. Resolve the endpoint.
   Run `python client.py --project <unity-project-root> ping` when the project root is known.
   Use `--pid <editor-pid>` for duplicate Editors.
   Use `--port <port>` only to force a known endpoint.
   Completion: ping returns the debug service identity and Editor state.

2. Name the evidence target.
   Choose Console, Selection, Hierarchy, asset database, UI Toolkit tree, profiler, frame debugger, type, member, or Play Mode state.
   Completion: the target is explicit before any invoke call.

3. Describe before uncertain reflection.
   Run `describe --type <Full.Type.Name>` when the type, member, overload, or static/instance shape is uncertain.
   Completion: the selected member signature is known.

4. Invoke or batch.
   Use `invoke`, `invoke-chain`, `console`, or `batch`.
   Prefer batch for related reads.
   Completion: the response is parsed and checked for `error` and truncation markers.

5. Verify state-changing claims with a second signal.
   Use Console, hierarchy readback, ping state, asset lookup, or another available Unity inspection tool.
   Completion: the report names both the action and the verification source.

6. Report bounded evidence.
   Quote only the relevant fields and file paths.
   Completion: the answer includes enough data to reproduce the inspected state.

## Fallback

If ping fails, report the exact resolver command and whether `.u3d-ai-linker/ports.json` existed.
Do not guess Editor state from stale logs.

## References

- `references/api-cookbook.md` for common inspection recipes.
- `references/protocol.md` for request and response contracts.
- `references/troubleshooting.md` for connection, type, overload, and truncation failures.
- `references/manual-verification.md` for service smoke checks.
- `references/run-e2e.py` for end-to-end verification.
```

- [ ] **Step 3: Replace `unity-lua-device-debug/SKILL.md`**

Use this body:

```markdown
---
name: unity-lua-device-debug
description: Inspect Unity Lua runtime diagnostics through registered commands. Use for Editor or Android Development Build Lua state, command listing, command execution, and adb forwarding.
---

# Unity Lua Device Debug

Use this skill for project-registered Lua diagnostics.
It exposes command transport only.
It does not provide arbitrary Lua eval.

## Operational loop

1. Select the target.
   Use Editor mode for an open Unity Editor.
   Use Android mode for a Development Build device.
   Completion: the report states `Editor` or `Android` target mode.

2. Resolve or forward.
   Editor: run `python client.py --project <unity-project-root> ping`.
   Android: run `python client.py adb-forward`, then `python client.py ping`.
   Use `--port <port>` only for a forced endpoint.
   Completion: ping proves the Lua debug service is reachable.

3. List registered commands.
   Run `python client.py commands` before execution.
   Completion: command descriptors are visible.

4. Execute only listed commands.
   Run `python client.py execute <command-id>` with `--arg key=value` pairs from the descriptor contract.
   Completion: the command id exists in the descriptor list and returns JSON.

5. Guard mutation.
   Mutating commands require descriptor `mutating=true` and CLI `--allow-mutation`.
   Completion: the mutation gate is satisfied, or the command is not executed.

6. Clean up owned Android forwards.
   Run `python client.py adb-remove` after Android diagnostics when this CLI created the forward.
   Completion: owned adb forward is removed or reported as already absent.

## Fallback

If `commands` is empty or ping reports no host, state that the target project must register `ILuaDeviceDebugHost`.
Do not invent Lua commands.

## References

- `references/protocol.md` for command descriptors and mutation rules.
- `references/android-adb-forwarding.md` for Development Build transport.
```

- [ ] **Step 4: Create Test Runner references**

Write `references/protocol.md` with endpoint summary for `/ping`, `/recompile`, `/list-tests`, `/run-tests`, and `/test-status`.
Include these facts exactly: project-aware resolution prefers `.u3d-ai-linker/ports.json`; legacy ports are `21890`, `21896`, `21897`; PlayMode uses temporary `DisableDomainReload`; filtered zero matches must be treated as error.

Write `references/troubleshooting.md` with cases for ping timeout, non-Idle state, compile errors, 409 running job, 404 job id, PlayMode dirty scene guard, and fallback to batchmode.

- [ ] **Step 5: Create Lua references**

Write `references/protocol.md` with commands `ping`, `commands`, `execute`, `adb-forward`, and `adb-remove`; include descriptor mutation gate and `ILuaDeviceDebugHost` requirement.

Write `references/android-adb-forwarding.md` with this flow: connect device, verify Development Build, run `adb-forward`, ping, commands, execute, remove owned forward.

- [ ] **Step 6: Update fragments**

For each `Agent~/fragments/<skill>/{AGENTS.md,CLAUDE.md}`, replace with a 4-6 line operational summary that names the skill, the preferred `--project` resolver, and the main action loop.

- [ ] **Step 7: Run the shape test green**

Run:

```powershell
python tools/test-agent-skill-shape.py
```

Expected: all four `OK` lines.

- [ ] **Step 8: Commit skill rewrite**

```powershell
git add tools/test-agent-skill-shape.py Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~
git commit -m "docs: sharpen bundled agent skills"
```

---

### Task 3: Add AgentSkillStateModel and tests

**Files:**
- Create: `Packages/com.sputnicyoji.u3d-dev-tools-ai/Editor/U3DAILinker/Settings/AgentSkillStateModel.cs`
- Create: `Packages/com.sputnicyoji.u3d-dev-tools-ai/Tests/Editor/U3DAILinker/Settings/AgentSkillStateModelTests.cs`

- [ ] **Step 1: Write tests for state transitions**

Create `AgentSkillStateModelTests.cs` with test cases:

```csharp
[Test] public void Build_SourceMissing_ReturnsSourceMissing()
[Test] public void Build_GeneratedMissing_ReturnsMissing()
[Test] public void Build_ValidOwnershipButHashDiff_ReturnsStale()
[Test] public void Build_ValidOwnershipAndLinks_ReturnsSynced()
[Test] public void Build_UserOwnedGeneratedDir_ReturnsConflict()
[Test] public void Build_UserDirectoryAtClaudeLink_ReturnsConflict()
```

Use temporary directories under `Path.GetTempPath()`, `FakeJunctionManager`, `OwnershipFile.Write`, and `ContentHash.OfDirectory`.

- [ ] **Step 2: Run test and verify compile fails**

Run:

```powershell
pwsh -File tools/run-editmode.ps1 -Project u3d-ai-linker -Unity <unity.exe>
```

Expected in a clean Unity environment: compile failure because `AgentSkillStateModel` does not exist.
If UPM fails before compilation with `The "path" argument must be of type string. Received undefined.`, record the log and continue with source-level checks after implementation.

- [ ] **Step 3: Implement `AgentSkillStateModel.cs`**

Define these public internal types inside `Yoji.U3DAILinker.Settings`:

```csharp
internal enum AgentSkillSyncState { Synced, Stale, Missing, Conflict, SourceMissing }
internal enum AgentSkillLinkState { Linked, Missing, Conflict }

internal sealed class AgentSkillStatusRow
{
    public string SkillName;
    public string SourcePath;
    public string GeneratedPath;
    public string ClaudeLinkPath;
    public string AgentsLinkPath;
    public string SourceHash;
    public string OwnershipHash;
    public AgentSkillSyncState State;
    public AgentSkillLinkState ClaudeLinkState;
    public AgentSkillLinkState AgentsLinkState;
    public string Message;
}
```

Implement:

```csharp
internal static AgentSkillStatusRow[] Build(
    string projectRoot,
    string packageRoot,
    IJunctionManager junctions)
```

Rules:

- Source skills are directories under `<packageRoot>/Agent~/skills` with `SKILL.md`.
- Generated skill path is `<projectRoot>/.u3d-ai-linker/skills/<skillName>`.
- Link paths are `<projectRoot>/.claude/skills/<skillName>` and `<projectRoot>/.agents/skills/<skillName>`.
- If source `SKILL.md` is missing, row state is `SourceMissing`.
- If generated dir exists without ownership for this skill, state is `Conflict`.
- If either final link exists as non-junction, state is `Conflict`.
- If generated dir or either link is missing, state is `Missing`.
- If ownership hash differs from source hash, state is `Stale`.
- If ownership hash matches and both junctions point to generated dir, state is `Synced`.

- [ ] **Step 4: Run model tests**

Run:

```powershell
pwsh -File tools/run-editmode.ps1 -Project u3d-ai-linker -Unity <unity.exe>
```

Expected in a clean Unity environment: `AgentSkillStateModelTests` pass.
If Unity UPM fails before compilation, run source checks and record the blocker.

- [ ] **Step 5: Commit model**

```powershell
git add Packages/com.sputnicyoji.u3d-dev-tools-ai/Editor/U3DAILinker/Settings/AgentSkillStateModel.cs Packages/com.sputnicyoji.u3d-dev-tools-ai/Tests/Editor/U3DAILinker/Settings/AgentSkillStateModelTests.cs
git commit -m "feat: add agent skill sync state model"
```

---

### Task 4: Wire Agent Skills section into Settings UI

**Files:**
- Modify: `Packages/com.sputnicyoji.u3d-dev-tools-ai/Editor/U3DAILinker/Settings/U3DAILinkerSettingsProvider.cs`
- Modify: `Packages/com.sputnicyoji.u3d-dev-tools-ai/Tests/Editor/U3DAILinker/U3DAILinkerSettingsProviderTests.cs`

- [ ] **Step 1: Add provider tests**

Add tests:

```csharp
[Test]
public void BuildSkillUpdateInstructions_IncludesSyncFlow()
{
    var text = U3DAILinkerSettingsProvider.BuildSkillUpdateInstructions(
        "com.sputnicyoji.u3d-dev-tools-ai",
        "G:/Project/Library/PackageCache/com.sputnicyoji.u3d-dev-tools-ai@hash");
    StringAssert.Contains("Update the UPM Git URL or package revision", text);
    StringAssert.Contains("Project Settings > U3D Dev Tools AI", text);
    StringAssert.Contains("Sync Agent Skills", text);
    StringAssert.Contains(".u3d-ai-linker/skills", text);
}
```

Extend `BuildDiagnosticReport_IncludesRowsAndSelfStatusInputs` to assert:

```csharp
StringAssert.Contains("Agent Skills:", report);
```

- [ ] **Step 2: Implement Settings UI section**

In `OnGui`, insert after `DrawPortsSection()`:

```csharp
EditorGUILayout.Space();
DrawAgentSkillsSection();
```

Add `DrawAgentSkillsSection()` that:

- calls `AgentSkillStateModel.Build(ProjectRoot, PackageRoot, new WindowsJunctionManager())`.
- renders Source Package name and `PackageRoot`.
- renders rows with skill name, state, Claude link state, Codex link state, ownership hash.
- provides buttons `Sync Agent Skills`, `Repair Skill Links`, `Open Generated Skills Folder`, `Copy Skill Update Instructions`.
- reuses `RequestSyncAgents`, `RequestRepairLinks`, and `RequestOpenFolder` for the first three actions.

Add:

```csharp
internal static string BuildSkillUpdateInstructions(string packageName, string packageRoot)
```

The returned text must say:

```text
1. Update the UPM Git URL or package revision in Packages/manifest.json.
2. Let Unity resolve the package.
3. Open Project Settings > U3D Dev Tools AI.
4. Press Sync Agent Skills.
5. Skills are copied to .u3d-ai-linker/skills and linked into .claude/skills and .agents/skills.
```

- [ ] **Step 3: Include Agent Skills in diagnostics**

In `BuildDiagnosticReport`, append `Agent Skills:` and one row per `AgentSkillStatusRow` when `projectRoot` and `packageRoot` are present.
Use `AgentSkillStateModel.Build(projectRoot, packageRoot, new WindowsJunctionManager())` inside a try/catch and write `Agent Skills: unavailable: <message>` on failure.

- [ ] **Step 4: Run Settings tests**

Run:

```powershell
pwsh -File tools/run-editmode.ps1 -Project u3d-ai-linker -Unity <unity.exe>
```

Expected in a clean Unity environment: Settings provider tests pass.
If UPM fails before compilation, record exact log.

- [ ] **Step 5: Commit UI wiring**

```powershell
git add Packages/com.sputnicyoji.u3d-dev-tools-ai/Editor/U3DAILinker/Settings/U3DAILinkerSettingsProvider.cs Packages/com.sputnicyoji.u3d-dev-tools-ai/Tests/Editor/U3DAILinker/U3DAILinkerSettingsProviderTests.cs
git commit -m "feat: show agent skill sync status"
```

---

### Task 5: Update README and validation scripts

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Add README skill update section**

Add section after `U3D Dev Tools AI settings`:

```markdown
### Updating agent skills

The package is the source of truth for bundled skills.
Do not edit `Library/PackageCache` and do not git-pull directly into `.claude/skills` or `.agents/skills`.

Update flow:

1. Update the UPM Git URL or package revision in `Packages/manifest.json`.
2. Let Unity resolve the package.
3. Open `Edit > Project Settings > U3D Dev Tools AI`.
4. Press `Sync Agent Skills`.
5. The linker copies skills to `.u3d-ai-linker/skills` and repairs junctions into `.claude/skills` and `.agents/skills`.

The Settings panel reports each skill as `Synced`, `Stale`, `Missing`, `Conflict`, or `Source Missing`.
```

- [ ] **Step 2: Run fast validation**

Run:

```powershell
python tools/test-agent-skill-shape.py
python tools/test-agent-port-resolvers.py
python tools/test-single-package-layout.py
python -m py_compile tools/test-agent-skill-shape.py
python -m py_compile Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/test-runner-mcp/client.py
python -m py_compile Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/unity-editor-debug-mcp/client.py
python -m py_compile Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/unity-lua-device-debug/client.py
```

Expected: all tests pass and `py_compile` exits 0.

- [ ] **Step 3: Run Unity validation if available**

Run:

```powershell
pwsh -File tools/run-editmode.ps1 -Project u3d-ai-linker -Unity <unity.exe>
```

Expected in a clean Unity environment: exit code 0.
Known blocker: UPM may fail before script compilation with `The "path" argument must be of type string. Received undefined.` Record this as Unity/UPM startup failure if it occurs.

- [ ] **Step 4: Commit docs and validation result**

```powershell
git add README.md
git commit -m "docs: document agent skill update flow"
```

---

## Final verification checklist

Run:

```powershell
git diff --check
python tools/test-agent-skill-shape.py
python tools/test-agent-port-resolvers.py
python tools/test-single-package-layout.py
python -m py_compile tools/test-agent-skill-shape.py
python -m py_compile Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/test-runner-mcp/client.py
python -m py_compile Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/unity-editor-debug-mcp/client.py
python -m py_compile Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/unity-lua-device-debug/client.py
```

Then run, when Unity is usable:

```powershell
pwsh -File tools/run-editmode.ps1 -Project u3d-ai-linker -Unity <unity.exe>
```

Do not release a new tag unless Yoji explicitly asks for a release.

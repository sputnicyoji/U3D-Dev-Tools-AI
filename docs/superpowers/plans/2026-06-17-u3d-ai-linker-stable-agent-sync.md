# U3D AI Linker Stable Agent Sync Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a taggable `0.1.0` stable release path where U3D AI Linker installs Git packages and synchronizes Agent Skills through real Windows Junction links.

**Architecture:** Keep the existing manifest/UPM pipeline, and add a narrow Agent asset orchestration layer above the already-tested `AgentSyncService`, `ManagedFileSync`, and `GitignoreBlockWriter`. Stable release is gated by Git install proof, real Junction proof, registry/tag consistency, and SLG_Prototype live-editor verification.

**Tech Stack:** Unity 2022.3+ Editor APIs, Unity Package Manager Git URLs, C# EditMode tests, Windows Junctions via existing `WindowsJunctionManager`, PowerShell release checks.

---

## Release Scope

Stable `0.1.0` should initially include:

- `com.yoji.editor-core`
- `com.yoji.editor-debug`
- `com.yoji.test-runner`
- `com.yoji.u3d-ai-linker`

Keep `com.yoji.lua-device-debug` out of stable `ready` until a generic non-SLG host integration path is documented and verified. It may remain `ready` in `Registry/dev.json` for local/dev validation.

Current prerequisite before starting:

```powershell
git -C E:\Yoji\U3D-Dev-Tools-AI status -sb
git -C E:\Yoji\U3D-Dev-Tools-AI push origin main
```

Expected after push: `main...origin/main` with no ahead count. The currently unpushed fix is `13ed2f5`.

---

## File Map

Create:

- `Packages/com.yoji.u3d-ai-linker/Editor/Settings/AgentPackageSourceResolver.cs`
  Resolve each enabled ready tool to its installed package `Agent~/skills/<skillName>` directory, fragments, and managed skill link paths.

- `Packages/com.yoji.u3d-ai-linker/Editor/Settings/IResolvedPackagePathProvider.cs`
  Injectable resolved-path lookup for tests and production.

- `Packages/com.yoji.u3d-ai-linker/Editor/Settings/UnityResolvedPackagePathProvider.cs`
  Production adapter over `UnityEditor.PackageManager.PackageInfo.FindForPackageName`.

- `Packages/com.yoji.u3d-ai-linker/Editor/Settings/AgentSyncOrchestrator.cs`
  Calls `AgentSyncService`, `ManagedFileSync`, and `GitignoreBlockWriter` in one operation.

- `Packages/com.yoji.u3d-ai-linker/Editor/Operations/WindowsJunctionSmoke.cs`
  Manual menu proof for real Windows Junction create/read/delete semantics.

- `tools/check-u3d-linker-stable.ps1`
  Release gate: version/revision/tag/snapshot checks.

- `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Settings/AgentPackageSourceResolverTests.cs`
- `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Settings/AgentSyncOrchestratorTests.cs`

Modify:

- `Packages/com.yoji.u3d-ai-linker/Editor/Registry/RegistryTypes.cs`
  Add `AgentAssets` to `RegistryEntryView`.

- `Packages/com.yoji.u3d-ai-linker/Editor/Settings/U3DAILinkerSettingsProvider.cs`
  Map `agentAssets`, wire `Sync Agent Assets` and `Repair Links`, keep disabled only when no registry or operation running.

- `Packages/com.yoji.u3d-ai-linker/Editor/Settings/U3DAILinkerActionService.cs`
  Return enough operation result metadata for post-install sync, or call orchestrator after queue completion if completion is observed in this process.

- `Packages/com.yoji.u3d-ai-linker/Editor/Operations/U3DAILinkerBootstrap.cs`
  After queue completion on domain reload, run Agent sync once.

- `Registry/stable.json`
- `Packages/com.yoji.u3d-ai-linker/Registry/stable.json`
  Move stable-scope packages to `ready` only after Git/Junction proof.

- `README.md`
- `progress.md`

Remove from committed package assets if present:

- `Packages/**/Agent~/**/__pycache__/**`
- `Packages/**/Agent~/**/*.pyc`

---

### Task 1: Agent Package Source Resolver

**Files:**
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Settings/IResolvedPackagePathProvider.cs`
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Settings/UnityResolvedPackagePathProvider.cs`
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Settings/AgentPackageSourceResolver.cs`
- Modify: `Packages/com.yoji.u3d-ai-linker/Editor/Registry/RegistryTypes.cs`
- Modify: `Packages/com.yoji.u3d-ai-linker/Editor/Settings/U3DAILinkerSettingsProvider.cs`
- Test: `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Settings/AgentPackageSourceResolverTests.cs`

- [ ] **Step 1: Add failing resolver tests**

Create `AgentPackageSourceResolverTests.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Yoji.U3DAILinker.Registry;
using Yoji.U3DAILinker.Settings;

namespace Yoji.U3DAILinker.Tests.Settings
{
    public sealed class AgentPackageSourceResolverTests
    {
        private string m_Root;

        [SetUp]
        public void SetUp()
        {
            m_Root = Path.Combine(Path.GetTempPath(), "u3dlinker_resolver_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_Root);
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(m_Root)) Directory.Delete(m_Root, true); } catch { }
        }

        [Test]
        public void Resolve_ReadyToolWithAgentAssets_FindsSkillAndFragments()
        {
            var packageRoot = Path.Combine(m_Root, "pkg");
            var skillDir = Path.Combine(packageRoot, "Agent~", "skills", "test-runner-mcp");
            Directory.CreateDirectory(skillDir);
            File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), "skill");
            Directory.CreateDirectory(Path.Combine(packageRoot, "Agent~", "fragments"));
            File.WriteAllText(Path.Combine(packageRoot, "Agent~", "fragments", "CLAUDE.md"), "claude body");
            File.WriteAllText(Path.Combine(packageRoot, "Agent~", "fragments", "AGENTS.md"), "agents body");

            var provider = new FakeResolvedPathProvider("com.yoji.test-runner", packageRoot);
            var result = AgentPackageSourceResolver.Resolve(
                Registry("test-runner", "com.yoji.test-runner", agentAssets: "Agent~"),
                new[] { "test-runner" },
                provider);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("test-runner", result[0].ToolId);
            Assert.AreEqual("test-runner-mcp", result[0].SkillName);
            Assert.AreEqual(skillDir, result[0].SourceDir);
            Assert.AreEqual("claude body", result[0].ClaudeFragment);
            Assert.AreEqual("agents body", result[0].AgentsFragment);
            CollectionAssert.AreEqual(
                new[] { ".claude/skills/test-runner-mcp", ".agents/skills/test-runner-mcp" },
                result[0].ManagedSkillRelativePaths);
        }

        [Test]
        public void Resolve_MissingSkillMarker_FailsWithToolId()
        {
            var packageRoot = Path.Combine(m_Root, "pkg");
            Directory.CreateDirectory(Path.Combine(packageRoot, "Agent~", "skills", "test-runner-mcp"));
            var provider = new FakeResolvedPathProvider("com.yoji.test-runner", packageRoot);

            var ex = Assert.Throws<System.InvalidOperationException>(() =>
                AgentPackageSourceResolver.Resolve(
                    Registry("test-runner", "com.yoji.test-runner", agentAssets: "Agent~"),
                    new[] { "test-runner" },
                    provider));

            StringAssert.Contains("test-runner", ex.Message);
            StringAssert.Contains("SKILL.md", ex.Message);
        }

        [Test]
        public void Resolve_SkipsDisabledTool()
        {
            var provider = new FakeResolvedPathProvider("com.yoji.test-runner", "C:/unused");
            var result = AgentPackageSourceResolver.Resolve(
                Registry("test-runner", "com.yoji.test-runner", agentAssets: "Agent~"),
                new string[0],
                provider);

            Assert.AreEqual(0, result.Count);
        }

        private static LinkerRegistry Registry(string id, string packageName, string agentAssets)
        {
            return new LinkerRegistry
            {
                SchemaVersion = 1,
                Channel = LinkerChannel.Dev,
                Entries =
                {
                    new RegistryEntryView
                    {
                        Id = id,
                        PackageName = packageName,
                        PackagePath = "Packages/" + packageName,
                        Status = ToolStatus.Ready,
                        Kind = ToolKind.Tool,
                        Order = 20,
                        AgentAssets = agentAssets,
                    },
                },
            };
        }

        private sealed class FakeResolvedPathProvider : IResolvedPackagePathProvider
        {
            private readonly string m_PackageName;
            private readonly string m_Path;

            public FakeResolvedPathProvider(string packageName, string path)
            {
                m_PackageName = packageName;
                m_Path = path;
            }

            public string GetResolvedPath(string packageName)
                => packageName == m_PackageName ? m_Path : null;
        }
    }
}
```

- [ ] **Step 2: Run tests and verify RED**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\run-editmode.ps1 -Project u3d-ai-linker -TimeoutSec 600
```

Expected: compile failure because `AgentPackageSourceResolver`, `IResolvedPackagePathProvider`, and `RegistryEntryView.AgentAssets` do not exist.

- [ ] **Step 3: Implement resolver interfaces**

Create `IResolvedPackagePathProvider.cs`:

```csharp
namespace Yoji.U3DAILinker.Settings
{
    internal interface IResolvedPackagePathProvider
    {
        string GetResolvedPath(string packageName);
    }
}
```

Create `UnityResolvedPackagePathProvider.cs`:

```csharp
using UnityEditor.PackageManager;

namespace Yoji.U3DAILinker.Settings
{
    internal sealed class UnityResolvedPackagePathProvider : IResolvedPackagePathProvider
    {
        public string GetResolvedPath(string packageName)
        {
            var info = PackageInfo.FindForPackageName(packageName);
            return info != null ? info.resolvedPath : null;
        }
    }
}
```

Modify `RegistryTypes.cs` by adding this field to `RegistryEntryView`:

```csharp
public string AgentAssets;
```

Modify `U3DAILinkerSettingsProvider.ToRegistryView` to map:

```csharp
AgentAssets = entry.AgentAssets,
```

- [ ] **Step 4: Implement `AgentPackageSourceResolver`**

Create `AgentPackageSourceResolver.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Yoji.U3DAILinker.Agents;
using Yoji.U3DAILinker.Registry;

namespace Yoji.U3DAILinker.Settings
{
    internal sealed class AgentSyncTarget : IFragmentSource
    {
        public string ToolId { get; set; }
        public string PackageName { get; set; }
        public string SkillName { get; set; }
        public int Order { get; set; }
        public string SourceDir { get; set; }
        public string SourceRevision { get; set; }
        public string ClaudeFragment { get; set; }
        public string AgentsFragment { get; set; }
        public IReadOnlyList<string> ManagedSkillRelativePaths { get; set; }
    }

    internal static class AgentPackageSourceResolver
    {
        public static IReadOnlyList<AgentSyncTarget> Resolve(
            LinkerRegistry registry,
            IReadOnlyCollection<string> enabledToolIds,
            IResolvedPackagePathProvider pathProvider)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (pathProvider == null) throw new ArgumentNullException(nameof(pathProvider));

            var enabled = new HashSet<string>(enabledToolIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            var result = new List<AgentSyncTarget>();

            foreach (var entry in registry.Entries.OrderBy(e => e.Order).ThenBy(e => e.Id, StringComparer.Ordinal))
            {
                if (!enabled.Contains(entry.Id)) continue;
                if (entry.Status != ToolStatus.Ready) continue;
                if (string.IsNullOrEmpty(entry.AgentAssets)) continue;

                var resolvedPath = pathProvider.GetResolvedPath(entry.PackageName);
                if (string.IsNullOrEmpty(resolvedPath))
                    throw new InvalidOperationException("Package not resolved for tool '" + entry.Id + "': " + entry.PackageName);

                var agentRoot = Path.Combine(resolvedPath, entry.AgentAssets);
                var skillsRoot = Path.Combine(agentRoot, "skills");
                if (!Directory.Exists(skillsRoot))
                    throw new InvalidOperationException("Agent skills directory missing for tool '" + entry.Id + "': " + skillsRoot);

                var skillDirs = Directory.GetDirectories(skillsRoot)
                    .Where(d => File.Exists(Path.Combine(d, "SKILL.md")))
                    .OrderBy(d => d, StringComparer.Ordinal)
                    .ToArray();
                if (skillDirs.Length != 1)
                    throw new InvalidOperationException("Expected exactly one SKILL.md under tool '" + entry.Id + "', found " + skillDirs.Length);

                var skillName = Path.GetFileName(skillDirs[0]);
                result.Add(new AgentSyncTarget
                {
                    ToolId = entry.Id,
                    PackageName = entry.PackageName,
                    SkillName = skillName,
                    Order = entry.Order,
                    SourceDir = skillDirs[0],
                    SourceRevision = entry.Revision,
                    ClaudeFragment = ReadOptional(Path.Combine(agentRoot, "fragments", "CLAUDE.md")),
                    AgentsFragment = ReadOptional(Path.Combine(agentRoot, "fragments", "AGENTS.md")),
                    ManagedSkillRelativePaths = new[]
                    {
                        ".claude/skills/" + skillName,
                        ".agents/skills/" + skillName,
                    },
                });
            }

            return result;
        }

        private static string ReadOptional(string path)
            => File.Exists(path) ? File.ReadAllText(path).Replace("\r\n", "\n").TrimEnd('\n') : null;
    }
}
```

- [ ] **Step 5: Run tests and verify GREEN**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\run-editmode.ps1 -Project u3d-ai-linker -TimeoutSec 600
```

Expected: previous tests plus new resolver tests pass.

- [ ] **Step 6: Commit**

```powershell
git add Packages/com.yoji.u3d-ai-linker/Editor/Registry/RegistryTypes.cs `
  Packages/com.yoji.u3d-ai-linker/Editor/Settings/U3DAILinkerSettingsProvider.cs `
  Packages/com.yoji.u3d-ai-linker/Editor/Settings/IResolvedPackagePathProvider.cs `
  Packages/com.yoji.u3d-ai-linker/Editor/Settings/UnityResolvedPackagePathProvider.cs `
  Packages/com.yoji.u3d-ai-linker/Editor/Settings/AgentPackageSourceResolver.cs `
  Packages/com.yoji.u3d-ai-linker/Tests/Editor/Settings/AgentPackageSourceResolverTests.cs
git commit -m "feat(linker): resolve package Agent skill sources"
```

---

### Task 2: Agent Sync Orchestrator

**Files:**
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Settings/AgentSyncOrchestrator.cs`
- Test: `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Settings/AgentSyncOrchestratorTests.cs`

- [ ] **Step 1: Add failing orchestrator tests**

Create `AgentSyncOrchestratorTests.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Yoji.U3DAILinker.Operations;
using Yoji.U3DAILinker.Settings;
using Yoji.U3DAILinker.Tests;

namespace Yoji.U3DAILinker.Tests.Settings
{
    public sealed class AgentSyncOrchestratorTests
    {
        private string m_ProjectRoot;
        private string m_SourceDir;
        private FakeJunctionManager m_Junctions;

        [SetUp]
        public void SetUp()
        {
            m_ProjectRoot = Path.Combine(Path.GetTempPath(), "u3dlinker_orchestrator_" + System.Guid.NewGuid().ToString("N"));
            m_SourceDir = Path.Combine(m_ProjectRoot, "source-skill");
            Directory.CreateDirectory(m_SourceDir);
            File.WriteAllText(Path.Combine(m_SourceDir, "SKILL.md"), "skill body");
            m_Junctions = new FakeJunctionManager();
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(m_ProjectRoot)) Directory.Delete(m_ProjectRoot, true); } catch { }
        }

        [Test]
        public void Sync_CopiesSkillCreatesJunctionsAndWritesManagedFiles()
        {
            var target = Target("test-runner", "test-runner-mcp", "claude fragment", "agents fragment");
            var result = new AgentSyncOrchestrator(m_Junctions).Sync(
                m_ProjectRoot,
                new[] { target },
                operationId: "op1");

            Assert.IsTrue(result.Success, result.Error);
            Assert.IsTrue(File.Exists(Path.Combine(m_ProjectRoot, ".u3d-ai-linker", "skills", "test-runner", "SKILL.md")));
            Assert.IsTrue(m_Junctions.IsJunction(Path.Combine(m_ProjectRoot, ".claude", "skills", "test-runner-mcp")));
            Assert.IsTrue(m_Junctions.IsJunction(Path.Combine(m_ProjectRoot, ".agents", "skills", "test-runner-mcp")));
            StringAssert.Contains("claude fragment", File.ReadAllText(Path.Combine(m_ProjectRoot, "CLAUDE.md")));
            StringAssert.Contains("agents fragment", File.ReadAllText(Path.Combine(m_ProjectRoot, "AGENTS.md")));
            StringAssert.Contains("/.u3d-ai-linker/", File.ReadAllText(Path.Combine(m_ProjectRoot, ".gitignore")));
            StringAssert.Contains("/.claude/skills/test-runner-mcp", File.ReadAllText(Path.Combine(m_ProjectRoot, ".gitignore")));
        }

        [Test]
        public void Sync_WhenManagedFileConflict_FailsWithoutWritingGitignore()
        {
            File.WriteAllText(Path.Combine(m_ProjectRoot, "CLAUDE.md"), "<!-- u3d-ai-linker:start -->\n");

            var result = new AgentSyncOrchestrator(m_Junctions).Sync(
                m_ProjectRoot,
                new[] { Target("test-runner", "test-runner-mcp", "claude fragment", "agents fragment") },
                operationId: "op1");

            Assert.IsFalse(result.Success);
            StringAssert.Contains("CLAUDE.md", result.Error);
            Assert.IsFalse(File.Exists(Path.Combine(m_ProjectRoot, ".gitignore")));
        }

        [Test]
        public void Sync_WhenJunctionPathIsUserDirectory_FailsBeforeWritingManagedFiles()
        {
            Directory.CreateDirectory(Path.Combine(m_ProjectRoot, ".claude", "skills", "test-runner-mcp"));

            var result = new AgentSyncOrchestrator(m_Junctions).Sync(
                m_ProjectRoot,
                new[] { Target("test-runner", "test-runner-mcp", "claude fragment", "agents fragment") },
                operationId: "op1");

            Assert.IsFalse(result.Success);
            StringAssert.Contains("non-junction skill link", result.Error);
            Assert.IsFalse(File.Exists(Path.Combine(m_ProjectRoot, "CLAUDE.md")));
            Assert.IsFalse(File.Exists(Path.Combine(m_ProjectRoot, "AGENTS.md")));
            Assert.IsFalse(File.Exists(Path.Combine(m_ProjectRoot, ".gitignore")));
        }

        private AgentSyncTarget Target(string toolId, string skillName, string claude, string agents)
        {
            return new AgentSyncTarget
            {
                ToolId = toolId,
                SkillName = skillName,
                SourceDir = m_SourceDir,
                SourceRevision = "rev",
                ClaudeFragment = claude,
                AgentsFragment = agents,
                Order = 20,
                ManagedSkillRelativePaths = new[]
                {
                    ".claude/skills/" + skillName,
                    ".agents/skills/" + skillName,
                },
            };
        }
    }
}
```

- [ ] **Step 2: Run tests and verify RED**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\run-editmode.ps1 -Project u3d-ai-linker -TimeoutSec 600
```

Expected: compile failure because `AgentSyncOrchestrator` does not exist.

- [ ] **Step 3: Implement orchestrator**

Create `AgentSyncOrchestrator.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Yoji.U3DAILinker.Agents;
using Yoji.U3DAILinker.Operations;

namespace Yoji.U3DAILinker.Settings
{
    internal sealed class AgentSyncOrchestratorResult
    {
        public bool Success;
        public string Error;
        public static AgentSyncOrchestratorResult Ok() => new AgentSyncOrchestratorResult { Success = true };
        public static AgentSyncOrchestratorResult Fail(string error) => new AgentSyncOrchestratorResult { Success = false, Error = error };
    }

    internal sealed class AgentSyncOrchestrator
    {
        private readonly IJunctionManager m_Junctions;

        public AgentSyncOrchestrator(IJunctionManager junctions)
        {
            m_Junctions = junctions ?? throw new ArgumentNullException(nameof(junctions));
        }

        public AgentSyncOrchestratorResult Sync(
            string projectRoot,
            IReadOnlyList<AgentSyncTarget> targets,
            string operationId)
        {
            if (string.IsNullOrEmpty(projectRoot)) return AgentSyncOrchestratorResult.Fail("project root missing");
            if (targets == null) return AgentSyncOrchestratorResult.Fail("targets missing");

            var claudePath = Path.Combine(projectRoot, "CLAUDE.md");
            var agentsPath = Path.Combine(projectRoot, "AGENTS.md");
            var gitignorePath = Path.Combine(projectRoot, ".gitignore");

            var preflight = PreflightTargets(projectRoot, targets);
            if (!preflight.Success) return preflight;
            if (!PreflightManagedBlock(claudePath, out var claudeError)) return AgentSyncOrchestratorResult.Fail("CLAUDE.md: " + claudeError);
            if (!PreflightManagedBlock(agentsPath, out var agentsError)) return AgentSyncOrchestratorResult.Fail("AGENTS.md: " + agentsError);
            if (!PreflightGitignore(gitignorePath, out var gitignoreError)) return AgentSyncOrchestratorResult.Fail(".gitignore: " + gitignoreError);

            if (!ManagedFileSync.Sync(claudePath, agentsPath, targets, out var managedError))
                return AgentSyncOrchestratorResult.Fail(managedError);

            var sync = new AgentSyncService(m_Junctions);
            foreach (var target in targets)
            {
                var request = new AgentSyncRequest
                {
                    ToolId = target.ToolId,
                    SourceDir = target.SourceDir,
                    SourceRevision = target.SourceRevision,
                    OperationId = operationId,
                    SkillsRoot = Path.Combine(projectRoot, ".u3d-ai-linker"),
                    RequiredSkillMarkers = new[] { "SKILL.md" },
                    JunctionLinks = target.ManagedSkillRelativePaths.Select(p => Path.Combine(projectRoot, p.Replace('/', Path.DirectorySeparatorChar))).ToArray(),
                };
                var result = sync.Sync(request);
                if (!result.Success)
                    return AgentSyncOrchestratorResult.Fail(target.ToolId + ": " + result.FailureStage + ": " + result.Message);
            }

            var gitignoreResult = GitignoreBlockWriter.Sync(
                gitignorePath,
                targets.SelectMany(t => t.ManagedSkillRelativePaths).ToArray());
            if (gitignoreResult.IsConflict)
                return AgentSyncOrchestratorResult.Fail(".gitignore: " + gitignoreResult.Message);

            return AgentSyncOrchestratorResult.Ok();
        }

        private AgentSyncOrchestratorResult PreflightTargets(string projectRoot, IReadOnlyList<AgentSyncTarget> targets)
        {
            var skillsRoot = Path.Combine(projectRoot, ".u3d-ai-linker");
            foreach (var target in targets)
            {
                var toolDir = Path.Combine(skillsRoot, "skills", target.ToolId);
                if (Directory.Exists(toolDir) && !OwnershipGuard.MayOverwrite(toolDir, target.ToolId))
                    return AgentSyncOrchestratorResult.Fail(target.ToolId + ": refusing to overwrite non-managed target: " + toolDir);

                foreach (var relativePath in target.ManagedSkillRelativePaths)
                {
                    var link = Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
                    if (!m_Junctions.IsJunction(link) && (Directory.Exists(link) || File.Exists(link)))
                        return AgentSyncOrchestratorResult.Fail(target.ToolId + ": refusing to overwrite non-junction skill link: " + link);
                }
            }
            return AgentSyncOrchestratorResult.Ok();
        }

        private static bool PreflightManagedBlock(string path, out string error)
        {
            error = null;
            var existing = File.Exists(path) ? File.ReadAllText(path).Replace("\r\n", "\n") : string.Empty;
            var startCount = CountOccurrences(existing, ManagedBlockWriter.StartMarker);
            var endCount = CountOccurrences(existing, ManagedBlockWriter.EndMarker);
            if (startCount > 1 || endCount > 1) { error = "Duplicate u3d-ai-linker markers found."; return false; }
            if (startCount != endCount) { error = "Unbalanced u3d-ai-linker markers found."; return false; }
            if (startCount == 1 && existing.IndexOf(ManagedBlockWriter.EndMarker, StringComparison.Ordinal) < existing.IndexOf(ManagedBlockWriter.StartMarker, StringComparison.Ordinal))
            { error = "u3d-ai-linker end marker precedes start marker."; return false; }
            return true;
        }

        private static bool PreflightGitignore(string path, out string error)
        {
            error = null;
            var existing = File.Exists(path) ? File.ReadAllText(path).Replace("\r\n", "\n") : string.Empty;
            var startCount = CountOccurrences(existing, GitignoreBlockWriter.StartMarker);
            var endCount = CountOccurrences(existing, GitignoreBlockWriter.EndMarker);
            if (startCount > 1 || endCount > 1) { error = "Duplicate u3d-ai-linker gitignore markers."; return false; }
            if (startCount != endCount) { error = "Unbalanced u3d-ai-linker gitignore markers."; return false; }
            if (startCount == 1 && existing.IndexOf(GitignoreBlockWriter.EndMarker, StringComparison.Ordinal) < existing.IndexOf(GitignoreBlockWriter.StartMarker, StringComparison.Ordinal))
            { error = "u3d-ai-linker gitignore end marker precedes start."; return false; }
            return true;
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            int count = 0, i = 0;
            while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
            {
                count++;
                i += needle.Length;
            }
            return count;
        }
    }
}
```

- [ ] **Step 4: Run tests and verify GREEN**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\run-editmode.ps1 -Project u3d-ai-linker -TimeoutSec 600
```

Expected: all tests pass.

- [ ] **Step 5: Commit**

```powershell
git add Packages/com.yoji.u3d-ai-linker/Editor/Settings/AgentSyncOrchestrator.cs `
  Packages/com.yoji.u3d-ai-linker/Tests/Editor/Settings/AgentSyncOrchestratorTests.cs
git commit -m "feat(linker): orchestrate agent skill sync"
```

---

### Task 3: Wire Sync Agent Assets and Repair Links

**Files:**
- Modify: `Packages/com.yoji.u3d-ai-linker/Editor/Settings/U3DAILinkerSettingsProvider.cs`
- Test: `Packages/com.yoji.u3d-ai-linker/Tests/Editor/U3DAILinkerSettingsProviderTests.cs`

- [ ] **Step 1: Add failing button-state test**

Add to `U3DAILinkerSettingsProviderTests.cs`:

```csharp
[Test]
public void AgentButtons_AreEnabledWhenRegistryLoadedAndIdle()
{
    Assert.IsFalse(U3DAILinkerSettingsProvider.AreAgentButtonsEnabled(OperationState.Running, registryLoaded: true));
    Assert.IsFalse(U3DAILinkerSettingsProvider.AreAgentButtonsEnabled(OperationState.Idle, registryLoaded: false));
    Assert.IsTrue(U3DAILinkerSettingsProvider.AreAgentButtonsEnabled(OperationState.Idle, registryLoaded: true));
}
```

- [ ] **Step 2: Run tests and verify RED**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\run-editmode.ps1 -Project u3d-ai-linker -TimeoutSec 600
```

Expected: compile failure because `AreAgentButtonsEnabled` does not exist.

- [ ] **Step 3: Implement button state**

Add to `U3DAILinkerSettingsProvider.cs`:

```csharp
internal static bool AreAgentButtonsEnabled(OperationState state, bool registryLoaded)
{
    return ActionsWired && registryLoaded && state != OperationState.Running;
}
```

Replace the always-disabled block in `DrawActionSection` with:

```csharp
using (new EditorGUI.DisabledScope(!AreAgentButtonsEnabled(_operationState, _registry != null)))
{
    if (GUILayout.Button("Sync Agent Assets")) RequestSyncAgents();
    if (GUILayout.Button("Repair Links")) RequestRepairLinks();
}
```

- [ ] **Step 4: Wire request methods**

Replace `RequestSyncAgents` and `RequestRepairLinks` in `U3DAILinkerSettingsProvider.cs`:

```csharp
private static void RequestSyncAgents()
{
    if (!EnsureRegistryLoaded())
        return;

    try
    {
        var targets = AgentPackageSourceResolver.Resolve(
            _registry,
            _project.EnabledToolIds,
            new UnityResolvedPackagePathProvider());
        var result = new AgentSyncOrchestrator(new WindowsJunctionManager()).Sync(
            ProjectRoot,
            targets,
            NewOperationId());
        if (!result.Success)
        {
            _operationState = OperationState.Failed;
            Debug.LogError("[U3DAILinker] Sync Agent Assets failed: " + result.Error);
            return;
        }
        _operationState = OperationState.Idle;
        Debug.Log("[U3DAILinker] Sync Agent Assets succeeded: " + targets.Count + " tools.");
    }
    catch (System.Exception e)
    {
        _operationState = OperationState.Failed;
        Debug.LogError("[U3DAILinker] Sync Agent Assets failed: " + e.Message);
    }
}

private static void RequestRepairLinks()
{
    RequestSyncAgents();
}
```

Add this helper to `U3DAILinkerSettingsProvider.cs`:

```csharp
private static string NewOperationId()
{
    return System.DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + System.Guid.NewGuid().ToString("N");
}
```

- [ ] **Step 5: Run tests and verify GREEN**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\run-editmode.ps1 -Project u3d-ai-linker -TimeoutSec 600
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```powershell
git add Packages/com.yoji.u3d-ai-linker/Editor/Settings/U3DAILinkerSettingsProvider.cs `
  Packages/com.yoji.u3d-ai-linker/Tests/Editor/U3DAILinkerSettingsProviderTests.cs
git commit -m "feat(linker): wire agent sync buttons"
```

---

### Task 4: Auto Sync After UPM Queue Completion

**Files:**
- Modify: `Packages/com.yoji.u3d-ai-linker/Editor/Operations/U3DAILinkerBootstrap.cs`
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Settings/U3DAILinkerProjectPaths.cs`
- Test: `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Operations/U3DAILinkerBootstrapDecisionTests.cs`

- [ ] **Step 1: Add failing decision test**

Create `U3DAILinkerBootstrapDecisionTests.cs`:

```csharp
using NUnit.Framework;
using Yoji.U3DAILinker.Operations;

namespace Yoji.U3DAILinker.Tests.Operations
{
    public sealed class U3DAILinkerBootstrapDecisionTests
    {
        [Test]
        public void ShouldRunAgentSync_AfterQueueAlreadySatisfied()
        {
            Assert.IsTrue(U3DAILinkerBootstrap.ShouldRunAgentSync(QueueStepResult.AlreadySatisfied));
            Assert.IsFalse(U3DAILinkerBootstrap.ShouldRunAgentSync(QueueStepResult.Requested));
            Assert.IsFalse(U3DAILinkerBootstrap.ShouldRunAgentSync(QueueStepResult.Faulted));
        }
    }
}
```

- [ ] **Step 2: Run tests and verify RED**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\run-editmode.ps1 -Project u3d-ai-linker -TimeoutSec 600
```

Expected: compile failure because `ShouldRunAgentSync` does not exist.

- [ ] **Step 3: Add project path helper**

Create `U3DAILinkerProjectPaths.cs`:

```csharp
using System.IO;
using UnityEngine;

namespace Yoji.U3DAILinker.Settings
{
    internal static class U3DAILinkerProjectPaths
    {
        public static string ProjectRoot
            => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        public static string LibraryRoot
            => Path.Combine(ProjectRoot, "Library");
    }
}
```

- [ ] **Step 4: Implement bootstrap decision and sync call**

Modify `U3DAILinkerBootstrap.cs`:

```csharp
using Yoji.U3DAILinker.Registry;
using Yoji.U3DAILinker.Settings;
```

Add:

```csharp
internal static bool ShouldRunAgentSync(QueueStepResult result)
{
    return result == QueueStepResult.AlreadySatisfied;
}
```

After `var result = runner.Advance(log);`, add:

```csharp
if (ShouldRunAgentSync(result))
    TrySyncAgentsAfterInstall();
```

Add:

```csharp
private static void TrySyncAgentsAfterInstall()
{
    try
    {
        var projectSettings = U3DAILinkerSettingsStore.LoadOrCreateProjectSettings();
        if (!U3DAILinkerSettingsProvider.TryLoadBundledRegistryForCurrentProject(projectSettings.Channel, out var registry, out var error))
        {
            UnityEngine.Debug.LogError("[U3DAILinker] Post-install agent sync skipped: " + error);
            return;
        }

        var targets = AgentPackageSourceResolver.Resolve(
            registry,
            projectSettings.EnabledToolIds,
            new UnityResolvedPackagePathProvider());
        var result = new AgentSyncOrchestrator(new WindowsJunctionManager()).Sync(
            U3DAILinkerProjectPaths.ProjectRoot,
            targets,
            "post-install-" + System.Guid.NewGuid().ToString("N"));
        if (!result.Success)
            UnityEngine.Debug.LogError("[U3DAILinker] Post-install agent sync failed: " + result.Error);
    }
    catch (System.Exception e)
    {
        UnityEngine.Debug.LogError("[U3DAILinker] Post-install agent sync failed: " + e.Message);
    }
}
```

Expose a wrapper in `U3DAILinkerSettingsProvider.cs`:

```csharp
internal static bool TryLoadBundledRegistryForCurrentProject(
    LinkerChannel channel,
    out LinkerRegistry registry,
    out string error)
{
    return TryLoadRegistryForChannel(channel, out registry, out error);
}
```

- [ ] **Step 5: Run tests and verify GREEN**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\run-editmode.ps1 -Project u3d-ai-linker -TimeoutSec 600
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```powershell
git add Packages/com.yoji.u3d-ai-linker/Editor/Operations/U3DAILinkerBootstrap.cs `
  Packages/com.yoji.u3d-ai-linker/Editor/Settings/U3DAILinkerProjectPaths.cs `
  Packages/com.yoji.u3d-ai-linker/Editor/Settings/U3DAILinkerSettingsProvider.cs `
  Packages/com.yoji.u3d-ai-linker/Tests/Editor/Operations/U3DAILinkerBootstrapDecisionTests.cs
git commit -m "feat(linker): sync agent assets after package install"
```

---

### Task 5: Windows Junction Manual Smoke Menu

**Files:**
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Operations/WindowsJunctionSmoke.cs`
- Test: existing EditMode compile coverage

- [ ] **Step 1: Add menu implementation**

Create `WindowsJunctionSmoke.cs`:

```csharp
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Yoji.U3DAILinker.Operations
{
    internal static class WindowsJunctionSmoke
    {
        [MenuItem("Tools/U3D AI Linker/Run Windows Junction Smoke")]
        public static void Run()
        {
            var root = Path.Combine(Application.dataPath, "..", "Library", "U3DAILinker", "junction-smoke");
            root = Path.GetFullPath(root);
            var target = Path.Combine(root, "target");
            var link = Path.Combine(root, "link");
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "marker.txt"), "ok");

            var junctions = new WindowsJunctionManager();
            if (junctions.IsJunction(link))
                junctions.Delete(link);
            else if (Directory.Exists(link) || File.Exists(link))
                throw new IOException("smoke link path is occupied by non-junction: " + link);

            junctions.Create(link, target);
            if (!junctions.IsJunction(link))
                throw new IOException("junction was not created: " + link);
            if (junctions.GetTarget(link) != target)
                throw new IOException("junction target mismatch: " + junctions.GetTarget(link));
            if (!File.Exists(Path.Combine(link, "marker.txt")))
                throw new IOException("junction does not expose target marker.");

            junctions.Delete(link);
            if (!File.Exists(Path.Combine(target, "marker.txt")))
                throw new IOException("junction delete damaged target.");

            Debug.Log("[U3DAILinker] Windows Junction smoke passed: " + root);
        }
    }
}
```

- [ ] **Step 2: Run compile/test verification**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\run-editmode.ps1 -Project u3d-ai-linker -TimeoutSec 600
```

Expected: all tests pass.

- [ ] **Step 3: Manual smoke in SLG_Prototype**

In the live Editor:

1. Open `Tools > U3D AI Linker > Run Windows Junction Smoke`.
2. Expected Console line: `[U3DAILinker] Windows Junction smoke passed`.
3. Verify target marker still exists:

```powershell
Test-Path E:\Yoji\SLG_Prototype\Core_Client\Library\U3DAILinker\junction-smoke\target\marker.txt
```

Expected: `True`.

- [ ] **Step 4: Commit**

```powershell
git add Packages/com.yoji.u3d-ai-linker/Editor/Operations/WindowsJunctionSmoke.cs
git commit -m "test(linker): add windows junction smoke menu"
```

---

### Task 6: Clean Agent Package Assets

**Files:**
- Modify/delete generated files under `Packages/*/Agent~`
- Modify: root `.gitignore` if needed

- [ ] **Step 1: Find generated Python cache files**

Run:

```powershell
Get-ChildItem Packages -Recurse -Force |
  Where-Object { $_.FullName -like '*Agent~*' -and ($_.Name -eq '__pycache__' -or $_.Extension -eq '.pyc') } |
  Select-Object FullName
```

Expected before cleanup: any `__pycache__` or `.pyc` files are listed.

- [ ] **Step 2: Delete only generated cache artifacts**

Run:

```powershell
Get-ChildItem Packages -Recurse -Directory -Force -Filter '__pycache__' |
  Where-Object { $_.FullName -like '*Agent~*' } |
  ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force }

Get-ChildItem Packages -Recurse -File -Force -Filter '*.pyc' |
  Where-Object { $_.FullName -like '*Agent~*' } |
  ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }
```

- [ ] **Step 3: Add ignore rules if absent**

Ensure root `.gitignore` contains:

```gitignore
__pycache__/
*.pyc
```

- [ ] **Step 4: Verify cleanup**

Run:

```powershell
Get-ChildItem Packages -Recurse -Force |
  Where-Object { $_.FullName -like '*Agent~*' -and ($_.Name -eq '__pycache__' -or $_.Extension -eq '.pyc') } |
  Select-Object FullName
```

Expected: no output.

- [ ] **Step 5: Run tests**

```powershell
powershell -ExecutionPolicy Bypass -File tools\run-editmode.ps1 -Project u3d-ai-linker -TimeoutSec 600
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```powershell
git add .gitignore
git add -u -- Packages/com.yoji.editor-debug/Agent~ `
  Packages/com.yoji.test-runner/Agent~ `
  Packages/com.yoji.lua-device-debug/Agent~
git commit -m "chore(agent): remove generated python caches"
```

---

### Task 7: Stable Release Check Script

**Files:**
- Create: `tools/check-u3d-linker-stable.ps1`

- [ ] **Step 1: Create script**

Create `tools/check-u3d-linker-stable.ps1`:

```powershell
param(
  [string]$RepoRoot = (Resolve-Path ".").Path,
  [switch]$RequireRemoteTags
)

$ErrorActionPreference = "Stop"

function Read-Json($path) {
  Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
}

$stablePath = Join-Path $RepoRoot "Registry/stable.json"
$snapshotPath = Join-Path $RepoRoot "Packages/com.yoji.u3d-ai-linker/Registry/stable.json"
$stable = Read-Json $stablePath
$snapshot = Read-Json $snapshotPath

$stableText = (Get-Content -LiteralPath $stablePath -Raw).Replace("`r`n", "`n").TrimEnd("`n")
$snapshotText = (Get-Content -LiteralPath $snapshotPath -Raw).Replace("`r`n", "`n").TrimEnd("`n")
if ($stableText -ne $snapshotText) {
  throw "stable registry snapshot differs from root Registry/stable.json"
}

$ready = @($stable.entries | Where-Object { $_.status -eq "ready" })
$requiredReadyIds = @("editor-core", "editor-debug", "test-runner")
$requiredNotReadyIds = @("lua-device-debug")
$readyIds = @($ready | ForEach-Object { $_.id })

foreach ($id in $requiredReadyIds) {
  if ($readyIds -notcontains $id) {
    throw "stable registry missing required ready entry: $id"
  }
}

foreach ($id in $readyIds) {
  if ($requiredReadyIds -notcontains $id) {
    throw "stable registry has unexpected ready entry: $id"
  }
}

foreach ($id in $requiredNotReadyIds) {
  $entry = $stable.entries | Where-Object { $_.id -eq $id } | Select-Object -First 1
  if ($null -eq $entry) {
    throw "stable registry missing expected non-ready entry: $id"
  }
  if ($entry.status -eq "ready") {
    throw "stable registry entry must remain non-ready until generic host proof exists: $id"
  }
}

function Assert-LocalTag($tag) {
  $tagExists = git -C $RepoRoot tag --list $tag
  if ([string]::IsNullOrWhiteSpace($tagExists)) {
    throw "missing local tag $tag"
  }
}

function Assert-RemoteTag($tag) {
  if (!$RequireRemoteTags) { return }
  $remoteTag = git -C $RepoRoot ls-remote --tags origin $tag
  if ([string]::IsNullOrWhiteSpace($remoteTag)) {
    throw "missing remote origin tag $tag"
  }
}

foreach ($entry in $ready) {
  $pkgPath = Join-Path $RepoRoot ($entry.packagePath + "/package.json")
  if (!(Test-Path -LiteralPath $pkgPath)) {
    throw "missing package.json for $($entry.id): $pkgPath"
  }

  $pkg = Read-Json $pkgPath
  $expectedRevision = "$($entry.id)-v$($pkg.version)"
  if ($entry.revision -ne $expectedRevision) {
    throw "revision mismatch for $($entry.id): registry=$($entry.revision) expected=$expectedRevision"
  }

  Assert-LocalTag $entry.revision
  Assert-RemoteTag $entry.revision
}

$linkerPkg = Read-Json (Join-Path $RepoRoot "Packages/com.yoji.u3d-ai-linker/package.json")
$linkerTag = "u3d-ai-linker-v$($linkerPkg.version)"
Assert-LocalTag $linkerTag
Assert-RemoteTag $linkerTag

Write-Host "stable release checks passed"
```

- [ ] **Step 2: Run script before stable update**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-u3d-linker-stable.ps1
```

Expected now: failure because stable is missing required ready entries and tags do not exist.

- [ ] **Step 3: Commit script**

```powershell
git add tools/check-u3d-linker-stable.ps1
git commit -m "chore(release): add stable linker release gate"
```

---

### Task 8: Git Install and Agent Sync Live Verification

**Files:**
- No code files required unless verification exposes a bug.
- Evidence files: `progress.md`, `README.md`

- [ ] **Step 1: Push current main**

```powershell
git -C E:\Yoji\U3D-Dev-Tools-AI push origin main
git -C E:\Yoji\U3D-Dev-Tools-AI status -sb
```

Expected: no ahead count.

- [ ] **Step 2: Convert SLG_Prototype to Git SHA URLs for verification**

Use full SHA:

```powershell
$sha = git -C E:\Yoji\U3D-Dev-Tools-AI rev-parse HEAD
$repo = "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git"
```

Edit `E:\Yoji\SLG_Prototype\Core_Client\Packages\manifest.json` so the four stable-scope `com.yoji.*` lines become Git SHA URLs. Keep `com.yoji.lua-device-debug` as a local `file:` dependency in SLG_Prototype until it enters stable scope; the project currently compiles against its host contract.

```json
"com.yoji.editor-core": "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.editor-core#<sha>",
"com.yoji.editor-debug": "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.editor-debug#<sha>",
"com.yoji.lua-device-debug": "file:E:/Yoji/U3D-Dev-Tools-AI/Packages/com.yoji.lua-device-debug",
"com.yoji.test-runner": "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.test-runner#<sha>",
"com.yoji.u3d-ai-linker": "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.u3d-ai-linker#<sha>",
```

Replace `<sha>` with the actual full commit hash.

- [ ] **Step 3: Wait for Unity package resolve**

In SLG_Prototype Editor, focus the Editor until Package Manager finishes resolving. Then verify:

```powershell
Select-String -Path E:\Yoji\SLG_Prototype\Core_Client\Packages\packages-lock.json -Pattern "github.com/sputnicyoji/U3D-Dev-Tools-AI"
```

Expected: the four stable-scope packages are resolved from Git; `com.yoji.lua-device-debug` remains `source=local`.

- [ ] **Step 4: Verify service endpoints**

Run from the installed Agent skill client locations under Unity Package Cache or use the local clients if still available:

```powershell
python E:\Yoji\U3D-Dev-Tools-AI\Packages\com.yoji.editor-debug\Agent~\skills\unity-editor-debug-mcp\client.py ping
python E:\Yoji\U3D-Dev-Tools-AI\Packages\com.yoji.test-runner\Agent~\skills\test-runner-mcp\client.py ping
```

Expected:

- editor-debug ping reports `ok=true`
- test-runner ping reports service online

- [ ] **Step 5: Run linker asset probe in Git mode**

In Unity:

```text
Tools > U3D AI Linker > Run Agent Asset Probe
```

Verify:

```powershell
Get-Content E:\Yoji\SLG_Prototype\Core_Client\Library\U3DAILinker\probe-result.json
```

Expected:

- `recommendedMode` is `directory`
- editor-debug SKILL exists under Git package `resolvedPath`
- test-runner SKILL exists under Git package `resolvedPath`
- linker `BundledSkills~` directory exists

- [ ] **Step 6: Run Sync Agent Assets**

In Unity:

```text
Edit > Project Settings > U3D AI Linker > Refresh Registry
Edit > Project Settings > U3D AI Linker > Sync Agent Assets
```

Verify:

```powershell
Test-Path E:\Yoji\SLG_Prototype\Core_Client\.u3d-ai-linker\skills\editor-debug\SKILL.md
Test-Path E:\Yoji\SLG_Prototype\Core_Client\.u3d-ai-linker\skills\test-runner\SKILL.md
Test-Path E:\Yoji\SLG_Prototype\Core_Client\.claude\skills\unity-editor-debug-mcp\SKILL.md
Test-Path E:\Yoji\SLG_Prototype\Core_Client\.agents\skills\test-runner-mcp\SKILL.md
Select-String -Path E:\Yoji\SLG_Prototype\Core_Client\CLAUDE.md -Pattern "unity-editor-debug-mcp"
Select-String -Path E:\Yoji\SLG_Prototype\Core_Client\AGENTS.md -Pattern "test-runner-mcp"
Select-String -Path E:\Yoji\SLG_Prototype\Core_Client\.gitignore -Pattern ".u3d-ai-linker|.claude/skills|.agents/skills"
```

Expected: all commands find paths or matching text.

- [ ] **Step 7: Run Repair Links after deleting one junction**

Delete one junction only:

```powershell
Remove-Item -LiteralPath E:\Yoji\SLG_Prototype\Core_Client\.claude\skills\unity-editor-debug-mcp -Force
```

In Unity:

```text
Edit > Project Settings > U3D AI Linker > Repair Links
```

Verify:

```powershell
Test-Path E:\Yoji\SLG_Prototype\Core_Client\.claude\skills\unity-editor-debug-mcp\SKILL.md
```

Expected: `True`.

- [ ] **Step 8: Commit only verification docs**

Do not commit SLG_Prototype Git URL experiment unless the user asks. Revert or leave explicit. In `U3D-Dev-Tools-AI`, update `progress.md` and `README.md` with proof dates and commands, then:

```powershell
git add progress.md README.md
git commit -m "docs(progress): record git install agent sync proof"
```

---

### Task 9: Stable Registry and Tag Release

**Files:**
- Modify: `Registry/stable.json`
- Modify: `Packages/com.yoji.u3d-ai-linker/Registry/stable.json`
- Modify: `README.md`
- Modify: `progress.md`

- [ ] **Step 1: Update stable registry scope**

Set stable ready entries:

```json
{
  "id": "editor-core",
  "status": "ready",
  "kind": "infra",
  "order": 10,
  "packageName": "com.yoji.editor-core",
  "packagePath": "Packages/com.yoji.editor-core",
  "revision": "editor-core-v0.1.0",
  "defaultEnabled": false,
  "userToggle": false,
  "agentAssets": null,
  "minUnity": "2022.3",
  "dependsOn": []
}
```

For `editor-debug`:

```json
"status": "ready",
"revision": "editor-debug-v0.1.0"
```

For `test-runner`:

```json
"status": "ready",
"revision": "test-runner-v0.1.0"
```

Keep `lua-device-debug` as:

```json
"status": "planned",
"revision": "lua-device-debug-v0.1.0"
```

Copy root `Registry/stable.json` to `Packages/com.yoji.u3d-ai-linker/Registry/stable.json`.

- [ ] **Step 2: Run tests**

```powershell
powershell -ExecutionPolicy Bypass -File tools\run-editmode.ps1 -Project u3d-ai-linker -TimeoutSec 600
```

Expected: all tests pass. If `StableRegistry_AllToolsArePlannedOrSkillOnly` fails, update it to assert only non-release-scope packages are not ready:

```csharp
[Test]
public void StableRegistry_LuaDeviceDebugNotReadyUntilGenericHostProof()
{
    var root = FindRepoRoot();
    Assume.That(root, Is.Not.Null);
    var doc = RegistryParser.Parse(ReadRegistry(root, "Registry/stable.json"));
    var lua = System.Array.Find(doc.Entries, e => e.Id == "lua-device-debug");
    Assert.IsNotNull(lua);
    Assert.AreNotEqual("ready", lua.Status);
}
```

- [ ] **Step 3: Commit registry/docs**

```powershell
git add Registry/stable.json Packages/com.yoji.u3d-ai-linker/Registry/stable.json README.md progress.md Packages/com.yoji.u3d-ai-linker/Tests/Editor/RegistryFixtureTests.cs
git commit -m "release(linker): mark stable agent sync packages ready"
```

- [ ] **Step 4: Tag stable packages**

```powershell
git tag editor-core-v0.1.0
git tag editor-debug-v0.1.0
git tag test-runner-v0.1.0
git tag u3d-ai-linker-v0.1.0
```

- [ ] **Step 5: Run release gate**

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-u3d-linker-stable.ps1
```

Expected:

```text
stable release checks passed
```

- [ ] **Step 6: Push commit and tags**

```powershell
git push origin main
git push origin editor-core-v0.1.0 editor-debug-v0.1.0 test-runner-v0.1.0 u3d-ai-linker-v0.1.0
```

- [ ] **Step 7: Verify remote tag gate**

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-u3d-linker-stable.ps1 -RequireRemoteTags
```

Expected:

```text
stable release checks passed
```

- [ ] **Step 8: Verify tag install URLs**

In a clean Unity project or SLG_Prototype after backup, use:

```json
"com.yoji.editor-core": "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.editor-core#editor-core-v0.1.0",
"com.yoji.editor-debug": "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.editor-debug#editor-debug-v0.1.0",
"com.yoji.test-runner": "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.test-runner#test-runner-v0.1.0",
"com.yoji.u3d-ai-linker": "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.u3d-ai-linker#u3d-ai-linker-v0.1.0"
```

Expected: Package Manager resolves all four, Editor compiles, `Sync Agent Assets` still works.

---

## Verification Gates

Before calling stable complete, all must pass:

```powershell
powershell -ExecutionPolicy Bypass -File tools\run-editmode.ps1 -Project u3d-ai-linker -TimeoutSec 600
powershell -ExecutionPolicy Bypass -File tools\check-u3d-linker-stable.ps1
powershell -ExecutionPolicy Bypass -File tools\check-u3d-linker-stable.ps1 -RequireRemoteTags
git status -sb
git tag --list "*-v*"
```

Manual gates:

- Git package install resolves from Git Package Cache, not local `file:`.
- `Tools > U3D AI Linker > Run Agent Asset Probe` passes in Git install mode.
- `Sync Agent Assets` creates `.u3d-ai-linker/skills/*`.
- `.claude/skills/*` and `.agents/skills/*` are real Junction links and expose `SKILL.md`.
- `Repair Links` recreates a deleted Junction without deleting target skill content.
- `CLAUDE.md`, `AGENTS.md`, and `.gitignore` contain managed blocks and preserve user content outside blocks.
- Stable tag URLs resolve after pushing tags.

## Self-Review

Spec coverage:

- Agent source location: Task 1.
- Junction-based skill sync: Task 2 and Task 5.
- UI wiring for Sync/Repair: Task 3.
- Post-install sync: Task 4.
- Git install proof: Task 8.
- Stable registry/tag release: Task 9.

Placeholder scan:

- This plan intentionally avoids `TBD` and open-ended "add tests" instructions. Each task has exact files, test snippets, commands, and expected results.

Type consistency:

- `AgentSyncTarget` implements `IFragmentSource`.
- `AgentPackageSourceResolver.Resolve` feeds `AgentSyncOrchestrator.Sync`.
- `IResolvedPackagePathProvider` is used by tests and `UnityResolvedPackagePathProvider`.

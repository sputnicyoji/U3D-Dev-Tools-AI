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

        public static AgentSyncOrchestratorResult Ok()
            => new AgentSyncOrchestratorResult { Success = true };

        public static AgentSyncOrchestratorResult Fail(string error)
            => new AgentSyncOrchestratorResult { Success = false, Error = error };
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
                    JunctionLinks = target.ManagedSkillRelativePaths
                        .Select(p => Path.Combine(projectRoot, p.Replace('/', Path.DirectorySeparatorChar)))
                        .ToArray(),
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

        private AgentSyncOrchestratorResult PreflightTargets(
            string projectRoot,
            IReadOnlyList<AgentSyncTarget> targets)
        {
            var skillsRoot = Path.Combine(projectRoot, ".u3d-ai-linker");
            foreach (var target in targets)
            {
                if (!Directory.Exists(target.SourceDir))
                    return AgentSyncOrchestratorResult.Fail(target.ToolId + ": source dir missing: " + target.SourceDir);
                if (!File.Exists(Path.Combine(target.SourceDir, "SKILL.md")))
                    return AgentSyncOrchestratorResult.Fail(target.ToolId + ": required marker missing: SKILL.md");

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

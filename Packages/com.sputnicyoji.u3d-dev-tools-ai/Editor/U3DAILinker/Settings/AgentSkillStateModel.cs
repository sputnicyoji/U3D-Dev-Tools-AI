using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Yoji.U3DAILinker.Operations;

namespace Yoji.U3DAILinker.Settings
{
    internal enum AgentSkillSyncState
    {
        Synced,
        Stale,
        Missing,
        Conflict,
        SourceMissing,
    }

    internal enum AgentSkillLinkState
    {
        Linked,
        Missing,
        Conflict,
    }

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

    internal static class AgentSkillStateModel
    {
        private static readonly string[] KnownSkills =
        {
            "test-runner-mcp",
            "unity-editor-debug-mcp",
            "unity-lua-device-debug",
        };

        public static AgentSkillStatusRow[] Build(
            string projectRoot,
            string packageRoot,
            IJunctionManager junctions)
        {
            if (string.IsNullOrEmpty(projectRoot)) throw new ArgumentException("project root missing", nameof(projectRoot));
            if (string.IsNullOrEmpty(packageRoot)) throw new ArgumentException("package root missing", nameof(packageRoot));
            if (junctions == null) throw new ArgumentNullException(nameof(junctions));

            var skillsRoot = Path.Combine(packageRoot, "Agent~", "skills");
            var skillNames = DiscoverSkillNames(skillsRoot);

            return skillNames
                .Select(skillName => BuildRow(projectRoot, skillsRoot, skillName, junctions))
                .OrderBy(row => row.SkillName, StringComparer.Ordinal)
                .ToArray();
        }

        private static string[] DiscoverSkillNames(string skillsRoot)
        {
            var result = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var knownSkill in KnownSkills)
                result.Add(knownSkill);

            if (!Directory.Exists(skillsRoot))
                return result.ToArray();

            foreach (var dir in Directory.GetDirectories(skillsRoot))
            {
                if (!File.Exists(Path.Combine(dir, "SKILL.md")))
                    continue;

                var name = Path.GetFileName(dir);
                if (!string.IsNullOrEmpty(name))
                    result.Add(name);
            }

            return result.ToArray();
        }

        private static AgentSkillStatusRow BuildRow(
            string projectRoot,
            string skillsRoot,
            string skillName,
            IJunctionManager junctions)
        {
            var sourcePath = Path.Combine(skillsRoot, skillName);
            var generatedPath = Path.Combine(projectRoot, ".u3d-ai-linker", "skills", skillName);
            var claudeLinkPath = Path.Combine(projectRoot, ".claude", "skills", skillName);
            var agentsLinkPath = Path.Combine(projectRoot, ".agents", "skills", skillName);

            var row = new AgentSkillStatusRow
            {
                SkillName = skillName,
                SourcePath = sourcePath,
                GeneratedPath = generatedPath,
                ClaudeLinkPath = claudeLinkPath,
                AgentsLinkPath = agentsLinkPath,
                ClaudeLinkState = GetLinkState(claudeLinkPath, generatedPath, junctions),
                AgentsLinkState = GetLinkState(agentsLinkPath, generatedPath, junctions),
            };

            if (!File.Exists(Path.Combine(sourcePath, "SKILL.md")))
            {
                row.State = AgentSkillSyncState.SourceMissing;
                row.Message = "source skill missing";
                return row;
            }

            row.SourceHash = ContentHash.OfDirectory(sourcePath);

            var claudeConflict = row.ClaudeLinkState == AgentSkillLinkState.Conflict;
            var agentsConflict = row.AgentsLinkState == AgentSkillLinkState.Conflict;
            string generatedHash = null;

            if (File.Exists(generatedPath))
            {
                row.State = AgentSkillSyncState.Conflict;
                row.Message = "generated skill path is a file";
                return row;
            }

            if (Directory.Exists(generatedPath))
            {
                var ownership = OwnershipFile.Read(generatedPath);
                if (ownership == null)
                {
                    row.State = AgentSkillSyncState.Conflict;
                    row.Message = "generated skill ownership missing or invalid";
                    return row;
                }

                row.OwnershipHash = ownership.ContentHash;

                if (!string.Equals(ownership.ToolId, skillName, StringComparison.Ordinal))
                {
                    row.State = AgentSkillSyncState.Conflict;
                    row.Message = "generated skill ownership toolId mismatch";
                    return row;
                }

                if (string.IsNullOrEmpty(ownership.ContentHash))
                {
                    row.State = AgentSkillSyncState.Conflict;
                    row.Message = "generated skill ownership hash missing";
                    return row;
                }

                generatedHash = ContentHash.OfDirectory(generatedPath);
            }

            if (claudeConflict || agentsConflict)
            {
                row.State = AgentSkillSyncState.Conflict;
                row.Message = "skill link conflict";
                return row;
            }

            if (!Directory.Exists(generatedPath) ||
                row.ClaudeLinkState == AgentSkillLinkState.Missing ||
                row.AgentsLinkState == AgentSkillLinkState.Missing)
            {
                row.State = AgentSkillSyncState.Missing;
                row.Message = "generated skill or link missing";
                return row;
            }

            if (!string.Equals(row.OwnershipHash, row.SourceHash, StringComparison.Ordinal))
            {
                row.State = AgentSkillSyncState.Stale;
                row.Message = "source hash differs from ownership hash";
                return row;
            }

            if (!string.Equals(generatedHash, row.OwnershipHash, StringComparison.Ordinal))
            {
                row.State = AgentSkillSyncState.Stale;
                row.Message = "generated hash differs from ownership hash";
                return row;
            }

            row.State = AgentSkillSyncState.Synced;
            row.Message = "synced";
            return row;
        }

        private static AgentSkillLinkState GetLinkState(
            string linkPath,
            string generatedPath,
            IJunctionManager junctions)
        {
            if (junctions.IsJunction(linkPath))
                return SamePath(junctions.GetTarget(linkPath), generatedPath)
                    ? AgentSkillLinkState.Linked
                    : AgentSkillLinkState.Conflict;

            if (Directory.Exists(linkPath) || File.Exists(linkPath))
                return AgentSkillLinkState.Conflict;

            return AgentSkillLinkState.Missing;
        }

        private static bool SamePath(string left, string right)
        {
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
                return false;

            var comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return string.Equals(Normalize(left), Normalize(right), comparison);
        }

        private static string Normalize(string path)
            => Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
    }
}

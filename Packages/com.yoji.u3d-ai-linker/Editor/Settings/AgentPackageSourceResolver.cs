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

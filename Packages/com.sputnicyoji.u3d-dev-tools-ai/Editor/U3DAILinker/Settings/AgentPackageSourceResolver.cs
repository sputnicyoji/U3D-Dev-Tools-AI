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
                if (skillDirs.Length == 0)
                    throw new InvalidOperationException("No SKILL.md found under tool '" + entry.Id + "': " + skillsRoot);

                foreach (var skillDir in skillDirs)
                {
                    var skillName = Path.GetFileName(skillDir);
                    var syncToolId = skillDirs.Length == 1 ? entry.Id : skillName;
                    result.Add(new AgentSyncTarget
                    {
                        ToolId = syncToolId,
                        PackageName = entry.PackageName,
                        SkillName = skillName,
                        Order = entry.Order,
                        SourceDir = skillDir,
                        SourceRevision = entry.Revision,
                        ClaudeFragment = ReadFragment(agentRoot, skillName, "CLAUDE.md"),
                        AgentsFragment = ReadFragment(agentRoot, skillName, "AGENTS.md"),
                        ManagedSkillRelativePaths = new[]
                        {
                            ".claude/skills/" + skillName,
                            ".agents/skills/" + skillName,
                        },
                    });
                }
            }

            return result;
        }

        private static string ReadFragment(string agentRoot, string skillName, string fileName)
        {
            var perSkill = Path.Combine(agentRoot, "fragments", skillName, fileName);
            if (File.Exists(perSkill)) return ReadFile(perSkill);

            var legacy = Path.Combine(agentRoot, "fragments", fileName);
            return File.Exists(legacy) ? ReadFile(legacy) : null;
        }

        private static string ReadFile(string path)
            => File.ReadAllText(path).Replace("\r\n", "\n").TrimEnd('\n');
    }
}

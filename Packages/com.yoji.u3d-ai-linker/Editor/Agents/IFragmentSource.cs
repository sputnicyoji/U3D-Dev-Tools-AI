using System.Collections.Generic;

namespace Yoji.U3DAILinker.Agents
{
    /// <summary>Which managed Markdown file a block targets.</summary>
    public enum ManagedBlockKind
    {
        Claude,
        Agents
    }

    /// <summary>
    /// One enabled tool's contribution to managed Agent files and .gitignore.
    /// Implemented by the Registry subsystem; consumed read-only here.
    /// </summary>
    public interface IFragmentSource
    {
        /// <summary>Stable tool id, e.g. "test-runner". Used as the secondary sort key.</summary>
        string ToolId { get; }

        /// <summary>Unique Skill name, e.g. "test-runner-mcp". Duplicates fail preflight.</summary>
        string SkillName { get; }

        /// <summary>Registry order. Primary ascending sort key. Must be a non-negative integer.</summary>
        int Order { get; }

        /// <summary>Content of fragments/CLAUDE.md, or null if absent.</summary>
        string ClaudeFragment { get; }

        /// <summary>Content of fragments/AGENTS.md, or null if absent.</summary>
        string AgentsFragment { get; }

        /// <summary>
        /// Managed skill paths this tool owns, relative to project root, forward slashes,
        /// e.g. ".claude/skills/test-runner-mcp". Each becomes one ignored line.
        /// </summary>
        IReadOnlyList<string> ManagedSkillRelativePaths { get; }
    }
}

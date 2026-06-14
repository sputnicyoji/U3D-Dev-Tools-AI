using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Yoji.U3DAILinker.Agents
{
    /// <summary>
    /// Deterministically merges enabled tools' CLAUDE.md / AGENTS.md fragments into
    /// a single managed-block body. Preflight rejects negative order and duplicate
    /// Skill names; on failure no body is produced (callers must not write partially).
    /// </summary>
    public static class FragmentMerger
    {
        public static FragmentMergeResult Merge(
            IReadOnlyList<IFragmentSource> sources,
            ManagedBlockKind kind)
        {
            if (sources == null) throw new ArgumentNullException(nameof(sources));

            // Preflight 1: order must be a non-negative integer.
            foreach (var s in sources)
            {
                if (s.Order < 0)
                {
                    return FragmentMergeResult.Fail(string.Format(
                        CultureInfo.InvariantCulture,
                        "Tool '{0}' has negative order {1}; order must be a non-negative integer.",
                        s.ToolId, s.Order));
                }
            }

            // Preflight 2: Skill names must be unique across all enabled tools.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in sources)
            {
                if (!seen.Add(s.SkillName))
                {
                    return FragmentMergeResult.Fail(string.Format(
                        CultureInfo.InvariantCulture,
                        "Duplicate Skill name '{0}'; sync preflight aborted.",
                        s.SkillName));
                }
            }

            // Deterministic order: by Order ascending, then ToolId ordinal.
            var ordered = sources
                .OrderBy(s => s.Order)
                .ThenBy(s => s.ToolId, StringComparer.Ordinal)
                .ToList();

            var sb = new StringBuilder();
            bool first = true;
            foreach (var s in ordered)
            {
                string fragment = kind == ManagedBlockKind.Claude
                    ? s.ClaudeFragment
                    : s.AgentsFragment;
                if (string.IsNullOrEmpty(fragment))
                    continue;

                if (!first) sb.Append('\n');
                first = false;

                sb.Append("<!-- source: ")
                  .Append(s.SkillName)
                  .Append(" (tool: ")
                  .Append(s.ToolId)
                  .Append(") -->")
                  .Append('\n');
                sb.Append(fragment.Replace("\r\n", "\n").TrimEnd('\n'));
                sb.Append('\n');
            }

            return FragmentMergeResult.Ok(sb.ToString().TrimEnd('\n'));
        }
    }
}

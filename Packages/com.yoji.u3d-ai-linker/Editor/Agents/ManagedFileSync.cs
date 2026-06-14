using System.Collections.Generic;
using System.IO;

namespace Yoji.U3DAILinker.Agents
{
    /// <summary>
    /// Orchestrates fragment merge + managed-block write for CLAUDE.md and AGENTS.md.
    /// Preflight (negative order, duplicate Skill names) runs for both files before any
    /// write, so a failure leaves both files untouched. A corrupt marker in either file
    /// aborts with a conflict and leaves that file byte-for-byte unchanged.
    /// </summary>
    public static class ManagedFileSync
    {
        public static bool Sync(
            string claudePath,
            string agentsPath,
            IReadOnlyList<IFragmentSource> sources,
            out string error)
        {
            var claudeMerge = FragmentMerger.Merge(sources, ManagedBlockKind.Claude);
            if (!claudeMerge.Succeeded)
            {
                error = claudeMerge.Error;
                return false;
            }

            var agentsMerge = FragmentMerger.Merge(sources, ManagedBlockKind.Agents);
            if (!agentsMerge.Succeeded)
            {
                error = agentsMerge.Error;
                return false;
            }

            var claudeWrite = ManagedBlockWriter.Write(claudePath, claudeMerge.Body);
            if (claudeWrite.IsConflict)
            {
                error = FileName(claudePath) + ": " + claudeWrite.Message;
                return false;
            }

            var agentsWrite = ManagedBlockWriter.Write(agentsPath, agentsMerge.Body);
            if (agentsWrite.IsConflict)
            {
                error = FileName(agentsPath) + ": " + agentsWrite.Message;
                return false;
            }

            error = null;
            return true;
        }

        private static string FileName(string path) => Path.GetFileName(path);
    }
}

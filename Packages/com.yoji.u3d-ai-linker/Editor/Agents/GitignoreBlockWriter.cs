using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Yoji.U3DAILinker.Agents
{
    /// <summary>
    /// Maintains a dedicated managed block in the project root .gitignore.
    /// Ignores only specific managed skill paths plus the linker working dir;
    /// never the whole .claude/ or .agents/. Corrupt markers abort without writing.
    /// Removal deletes only ownership-matching ignore lines.
    /// </summary>
    public static class GitignoreBlockWriter
    {
        public const string StartMarker = "# >>> u3d-ai-linker >>>";
        public const string EndMarker = "# <<< u3d-ai-linker <<<";
        public const string InfraLine = "/.u3d-ai-linker/";

        /// <summary>Creates/updates the block to ignore exactly the given managed skill paths.</summary>
        public static BlockWriteResult Sync(string filePath, IReadOnlyList<string> managedSkillPaths)
        {
            if (managedSkillPaths == null) throw new ArgumentNullException(nameof(managedSkillPaths));

            string existing = File.Exists(filePath)
                ? File.ReadAllText(filePath).Replace("\r\n", "\n")
                : string.Empty;

            if (!TryLocate(existing, out var locate))
                return BlockWriteResult.Conflict(locate.Error);

            var ignoreLines = BuildIgnoreLines(managedSkillPaths);
            return Rewrite(filePath, existing, locate, ignoreLines);
        }

        /// <summary>Removes only the ignore lines owned by the uninstalled tool(s).</summary>
        public static BlockWriteResult Remove(string filePath, IReadOnlyList<string> removedSkillPaths)
        {
            if (removedSkillPaths == null) throw new ArgumentNullException(nameof(removedSkillPaths));

            if (!File.Exists(filePath))
                return BlockWriteResult.Unchanged();

            string existing = File.ReadAllText(filePath).Replace("\r\n", "\n");

            if (!TryLocate(existing, out var locate))
                return BlockWriteResult.Conflict(locate.Error);

            if (!locate.HasBlock)
                return BlockWriteResult.Unchanged();

            var removeSet = new HashSet<string>(
                removedSkillPaths.Select(NormalizeLine), StringComparer.Ordinal);

            var kept = locate.BlockLines
                .Where(line => line != InfraLine && !removeSet.Contains(line))
                .ToList();

            // Rebuild remaining ignore lines: infra line kept only if any managed skill remains.
            var ignoreLines = kept.Count > 0
                ? new List<string> { InfraLine }.Concat(kept).ToList()
                : new List<string>();

            return Rewrite(filePath, existing, locate, ignoreLines);
        }

        private static List<string> BuildIgnoreLines(IReadOnlyList<string> managedSkillPaths)
        {
            var skillLines = managedSkillPaths
                .Select(NormalizeLine)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();

            if (skillLines.Count == 0)
                return new List<string>();

            var lines = new List<string> { InfraLine };
            lines.AddRange(skillLines);
            return lines;
        }

        private static string NormalizeLine(string path)
        {
            string p = path.Replace("\\", "/").Trim();
            if (!p.StartsWith("/")) p = "/" + p;
            return p;
        }

        private struct Locate
        {
            public bool HasBlock;
            public int StartLineIndex;   // index in Lines of start marker
            public int EndLineIndex;     // index in Lines of end marker
            public List<string> Lines;   // full file split by '\n'
            public List<string> BlockLines; // ignore lines strictly between markers
            public string Error;
        }

        private static bool TryLocate(string content, out Locate locate)
        {
            locate = new Locate
            {
                Lines = SplitLines(content),
                BlockLines = new List<string>(),
                StartLineIndex = -1,
                EndLineIndex = -1
            };

            int startCount = 0, endCount = 0;
            for (int i = 0; i < locate.Lines.Count; i++)
            {
                string t = locate.Lines[i].Trim();
                if (t == StartMarker) { startCount++; locate.StartLineIndex = i; }
                else if (t == EndMarker) { endCount++; locate.EndLineIndex = i; }
            }

            if (startCount > 1 || endCount > 1)
            { locate.Error = "Duplicate u3d-ai-linker gitignore markers."; return false; }
            if (startCount != endCount)
            { locate.Error = "Unbalanced u3d-ai-linker gitignore markers."; return false; }

            if (startCount == 0)
            {
                locate.HasBlock = false;
                return true;
            }

            if (locate.EndLineIndex < locate.StartLineIndex)
            { locate.Error = "u3d-ai-linker gitignore end marker precedes start."; return false; }

            locate.HasBlock = true;
            for (int i = locate.StartLineIndex + 1; i < locate.EndLineIndex; i++)
            {
                string line = locate.Lines[i].Trim();
                if (line.Length > 0) locate.BlockLines.Add(line);
            }
            return true;
        }

        private static BlockWriteResult Rewrite(
            string filePath, string existing, Locate locate, List<string> ignoreLines)
        {
            var outLines = new List<string>();

            if (locate.HasBlock)
            {
                for (int i = 0; i < locate.StartLineIndex; i++)
                    outLines.Add(locate.Lines[i]);
                AppendBlock(outLines, ignoreLines);
                for (int i = locate.EndLineIndex + 1; i < locate.Lines.Count; i++)
                    outLines.Add(locate.Lines[i]);
            }
            else
            {
                foreach (var l in locate.Lines) outLines.Add(l);
                if (ignoreLines.Count > 0)
                {
                    EnsureTrailingBlank(outLines);
                    AppendBlock(outLines, ignoreLines);
                }
            }

            string newText = JoinLines(outLines);
            if (newText == existing)
                return BlockWriteResult.Unchanged();

            AtomicTextFile.Write(filePath, newText);
            return BlockWriteResult.Written();
        }

        private static void AppendBlock(List<string> outLines, List<string> ignoreLines)
        {
            // No ignore lines => no block at all (e.g. last skill removed).
            if (ignoreLines.Count == 0) return;
            outLines.Add(StartMarker);
            outLines.AddRange(ignoreLines);
            outLines.Add(EndMarker);
        }

        private static void EnsureTrailingBlank(List<string> outLines)
        {
            // Drop trailing empty lines introduced by split, then re-add one separator
            // only when there is preceding user content.
            while (outLines.Count > 0 && outLines[outLines.Count - 1].Length == 0)
                outLines.RemoveAt(outLines.Count - 1);
            if (outLines.Count > 0)
                outLines.Add(string.Empty);
        }

        private static List<string> SplitLines(string content)
        {
            // Preserve a trailing newline as a final empty element so round-trips match.
            if (content.Length == 0) return new List<string>();
            return content.Split('\n').ToList();
        }

        private static string JoinLines(List<string> lines)
        {
            if (lines.Count == 0) return string.Empty;
            return string.Join("\n", lines);
        }
    }
}

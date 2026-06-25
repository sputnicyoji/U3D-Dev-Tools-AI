using System;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Yoji.U3DAILinker.Operations
{
    /// 通过 `git ls-remote <repo> <branch>` 解析远端分支 SHA。
    /// 真实进程调用,列为手动验证;EditMode 测试用 FakeGitRefResolver。
    internal sealed class GitLsRemoteResolver : IGitRefResolver
    {
        private static readonly Regex Sha40 = new Regex("^[0-9a-f]{40}$", RegexOptions.Compiled);

        private readonly string _repoUrl;

        public GitLsRemoteResolver(string repoUrl)
        {
            _repoUrl = repoUrl;
        }

        public string ResolveBranchSha(string branch)
        {
            if (branch != "main")
                throw new InvalidOperationException("Dev channel only allows branch 'main', got: " + branch);

            return ResolveRefSha("refs/heads/" + branch);
        }

        public string ResolveTagSha(string tag)
        {
            if (string.IsNullOrEmpty(tag))
                throw new InvalidOperationException("tag missing");
            var peeled = ResolveRefSha("refs/tags/" + tag + "^{}", allowMissing: true);
            if (!string.IsNullOrEmpty(peeled))
                return peeled;
            return ResolveRefSha("refs/tags/" + tag);
        }

        private string ResolveRefSha(string gitRef, bool allowMissing = false)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "ls-remote " + _repoUrl + " " + gitRef,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using (var process = Process.Start(psi))
            {
                if (process == null)
                    throw new InvalidOperationException("Failed to start git process.");

                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                    throw new InvalidOperationException("git ls-remote failed: " + stderr);

                var sha = ExtractSha(stdout);
                if (string.IsNullOrEmpty(sha) && allowMissing)
                    return null;
                if (!Sha40.IsMatch(sha))
                    throw new InvalidOperationException("git ls-remote returned non-SHA output: " + stdout);
                return sha;
            }
        }

        private static string ExtractSha(string lsRemoteOutput)
        {
            if (string.IsNullOrEmpty(lsRemoteOutput))
                return string.Empty;
            var firstLine = lsRemoteOutput.Split('\n')[0];
            var tabIndex = firstLine.IndexOf('\t');
            return tabIndex > 0 ? firstLine.Substring(0, tabIndex).Trim() : firstLine.Trim();
        }
    }
}

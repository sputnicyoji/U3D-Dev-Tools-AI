using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using NUnit.Framework;
using Yoji.U3DAILinker.Operations;

namespace Yoji.U3DAILinker.Tests.Operations
{
    /// Real git process checks against a local repo and a silent loopback remote; ignored without git.
    public class GitLsRemoteResolverTests
    {
        private const string Identity = "-c user.name=test -c user.email=test@example.invalid ";

        private string m_Root;

        [SetUp] public void SetUp()
        {
            m_Root = Path.Combine(Path.GetTempPath(), "u3dlink_git_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_Root);
            try { Git("init -q"); }
            catch (Win32Exception) { Assert.Ignore("git is not on PATH."); }
        }

        [TearDown] public void TearDown()
        {
            try { if (m_Root != null && Directory.Exists(m_Root)) Directory.Delete(m_Root, true); } catch { }
        }

        [Test] public void ResolveBranchAndTags_LocalRepo_ReturnHeadSha()
        {
            Git(Identity + "-c commit.gpgsign=false commit -q --allow-empty --no-verify -m init");
            Git("branch -M main");
            Git("tag light");
            Git(Identity + "-c tag.gpgsign=false tag -a annotated -m annotated");
            var head = Git("rev-parse HEAD").Trim();
            var resolver = new GitLsRemoteResolver(m_Root);

            Assert.AreEqual(head, resolver.ResolveBranchSha("main"));
            Assert.AreEqual(head, resolver.ResolveTagSha("light"));
            Assert.AreEqual(head, resolver.ResolveTagSha("annotated"));
        }

        [Test] public void ResolveBranchSha_MissingRepo_ReportsGitError()
        {
            var resolver = new GitLsRemoteResolver(Path.Combine(m_Root, "missing"));

            var ex = Assert.Throws<InvalidOperationException>(() => resolver.ResolveBranchSha("main"));
            StringAssert.StartsWith("git ls-remote failed:", ex.Message);
        }

        [Test] public void ResolveBranchSha_SilentRemote_TimesOutAndThrows()
        {
            // Accepts the connection in the kernel backlog but never answers, like a network black hole.
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                var resolver = new GitLsRemoteResolver("git://127.0.0.1:" + port + "/repo.git", 1000);
                var watch = Stopwatch.StartNew();

                var ex = Assert.Throws<InvalidOperationException>(() => resolver.ResolveBranchSha("main"));
                StringAssert.Contains("timed out", ex.Message);
                Assert.Less(watch.ElapsedMilliseconds, 10000);
            }
            finally
            {
                listener.Stop();
            }
        }

        private string Git(string args)
        {
            var psi = new ProcessStartInfo("git", args)
            {
                WorkingDirectory = m_Root,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using (var process = Process.Start(psi))
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException("git " + args + " exited with " + process.ExitCode);
                return output;
            }
        }
    }
}

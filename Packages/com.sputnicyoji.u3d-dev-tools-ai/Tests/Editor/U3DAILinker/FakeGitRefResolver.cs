using System;
using Yoji.U3DAILinker.Operations;

namespace Yoji.U3DAILinker.Tests
{
    /// 测试用 git 解析器:返回预置 SHA,记录被请求的分支。
    internal sealed class FakeGitRefResolver : IGitRefResolver
    {
        private readonly string _sha;
        public string LastBranch { get; private set; }

        public FakeGitRefResolver(string sha)
        {
            _sha = sha;
        }

        public string ResolveBranchSha(string branch)
        {
            LastBranch = branch;
            if (_sha == null)
                throw new InvalidOperationException("fake resolver configured to fail");
            return _sha;
        }
    }
}

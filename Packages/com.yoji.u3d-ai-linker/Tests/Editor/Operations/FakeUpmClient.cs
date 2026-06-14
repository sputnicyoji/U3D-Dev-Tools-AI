using System.Collections.Generic;
using Yoji.U3DAILinker.Operations;

namespace Yoji.U3DAILinker.Tests.Operations
{
    /// 记录 Add 调用序列的假 UPM 客户端。可配置某个 identifier 返回错误句柄。
    internal sealed class FakeUpmClient : IUpmClient
    {
        public readonly List<string> AddCalls = new List<string>();
        public string FailIdentifierContains; // 命中则返回 IsError 句柄

        public UpmAddHandle Add(string identifier)
        {
            AddCalls.Add(identifier);
            if (FailIdentifierContains != null && identifier.Contains(FailIdentifierContains))
                return new UpmAddHandle { IsComplete = true, IsError = true, ErrorMessage = "fake UPM failure" };
            return new UpmAddHandle { IsComplete = true, IsError = false };
        }
    }
}

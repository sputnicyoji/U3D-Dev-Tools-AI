using System.Collections.Generic;
using Yoji.U3DAILinker.Operations;

namespace Yoji.U3DAILinker.Tests.Operations
{
    /// 可编程的已安装包探针。测试用 Set 模拟"某包已解析到某 URL"，模拟域重载后的实际状态。
    internal sealed class FakeInstalledPackageProbe : IInstalledPackageProbe
    {
        private readonly Dictionary<string, string> m_Installed = new Dictionary<string, string>();

        public void Set(string packageName, string url) => m_Installed[packageName] = url;

        public string GetInstalledUrl(string packageName)
            => m_Installed.TryGetValue(packageName, out var url) ? url : null;
    }
}

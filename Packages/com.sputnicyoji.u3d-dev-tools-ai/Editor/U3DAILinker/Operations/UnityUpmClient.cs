using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

namespace Yoji.U3DAILinker.Operations
{
    /// 真实 UPM 客户端：把 UnityEditor.PackageManager.Client.Add 适配为 IUpmClient。
    /// 不进自动化测试（依赖真实 Editor / 网络）；由 Task 26 手动验证覆盖。
    internal sealed class UnityUpmClient : IUpmClient
    {
        public UpmAddHandle Add(string identifier)
        {
            var req = Client.Add(identifier);
            return new RequestHandle(req).View;
        }

        // 把 AddRequest 状态投影到 UpmAddHandle。Add 触发的域重载会丢弃本句柄；
        // 这没关系——恢复时改用 IInstalledPackageProbe 以实际 manifest 为准核对，不依赖句柄。
        private sealed class RequestHandle
        {
            private readonly AddRequest m_Req;
            public readonly UpmAddHandle View = new UpmAddHandle();

            public RequestHandle(AddRequest req)
            {
                m_Req = req;
                Refresh();
            }

            public void Refresh()
            {
                if (m_Req.Status == StatusCode.InProgress) return;
                View.IsComplete = true;
                if (m_Req.Status == StatusCode.Failure)
                {
                    View.IsError = true;
                    View.ErrorMessage = m_Req.Error != null ? m_Req.Error.message : "unknown UPM error";
                }
            }
        }
    }
}

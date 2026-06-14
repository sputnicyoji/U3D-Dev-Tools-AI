namespace Yoji.U3DAILinker.Operations
{
    /// 一次 UPM Add 请求的可轮询句柄。抽象掉 UnityEditor.PackageManager.Requests.AddRequest，
    /// 使队列逻辑可在 EditMode 用 fake 驱动。注意：真实 AddRequest 无法跨域重载保留，
    /// 因此 runner 永不持久化句柄，只在当前域内用它判断"本次是否已发出请求"。
    internal sealed class UpmAddHandle
    {
        public bool IsComplete;
        public bool IsError;
        public string ErrorMessage;
    }

    /// UPM 安装客户端抽象。Add 串行调用，调用方在前一项达成后才发下一项。
    internal interface IUpmClient
    {
        UpmAddHandle Add(string identifier);
    }
}

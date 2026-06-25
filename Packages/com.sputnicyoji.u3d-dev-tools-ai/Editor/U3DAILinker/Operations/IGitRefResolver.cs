namespace Yoji.U3DAILinker.Operations
{
    /// 解析远端某分支当前 commit SHA。把真实 git ls-remote 进程调用抽出去,
    /// 使 RefreshDev 计划生成可在 EditMode 用 fake 单测。
    internal interface IGitRefResolver
    {
        /// 返回 40 位小写十六进制 commit SHA。无法解析时抛异常。
        string ResolveBranchSha(string branch);
    }
}

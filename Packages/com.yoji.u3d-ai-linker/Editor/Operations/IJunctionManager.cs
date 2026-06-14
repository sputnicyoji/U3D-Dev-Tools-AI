namespace Yoji.U3DAILinker.Operations
{
    /// Windows directory junction 的抽象，便于把真实 reparse-point 副作用与同步逻辑解耦。
    /// 生产实现 = WindowsJunctionManager（P/Invoke）；测试用 FakeJunctionManager（内存）。
    internal interface IJunctionManager
    {
        /// linkPath 是否是一个 junction（reparse point）。不存在或是普通目录返回 false。
        bool IsJunction(string linkPath);

        /// 返回 junction 指向的目标目录；linkPath 不是 junction 时返回 null。
        string GetTarget(string linkPath);

        /// 在 linkPath 创建指向 targetDir 的 junction。linkPath 的父目录必须已存在。
        void Create(string linkPath, string targetDir);

        /// 删除 junction 本身（不删除目标内容）。linkPath 不是 junction 时不应误删普通目录。
        void Delete(string linkPath);
    }
}

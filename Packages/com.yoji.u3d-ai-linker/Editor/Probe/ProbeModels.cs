using System;
using System.Collections.Generic;

namespace Yoji.U3DAILinker.Probe
{
    /// <summary>
    /// 一个被探测的资产位置及其存在性事实。kind 区分 File 还是 Directory。
    /// </summary>
    public sealed class ProbeTarget
    {
        /// <summary>稳定标识,如 "editor-debug.SKILL.md" / "linker.BundledSkills~".</summary>
        public string Id { get; set; }

        /// <summary>被探测的绝对路径(由真实 resolvedPath 拼出,或测试直接给定)。</summary>
        public string Path { get; set; }

        /// <summary>"File" 或 "Directory"。</summary>
        public string Kind { get; set; }

        /// <summary>该路径是否真实存在。由 IO 层填入或测试直接给定。</summary>
        public bool Exists { get; set; }

        public ProbeTarget() { }

        public ProbeTarget(string id, string path, string kind, bool exists)
        {
            Id = id;
            Path = path;
            Kind = kind;
            Exists = exists;
        }
    }

    /// <summary>
    /// 探针总结果。allTargetsReadable 为总判定;recommendedMode 是给 LINK-1/4/7 的决策门。
    /// </summary>
    public sealed class ProbeResult
    {
        /// <summary>结果 schema 版本,便于面板/后续解析做兼容。</summary>
        public int SchemaVersion { get; set; } = 1;

        /// <summary>探针运行的 UTC 时间(ISO 8601)。</summary>
        public string ProbedAtUtc { get; set; }

        /// <summary>所有目标是否都可读。</summary>
        public bool AllTargetsReadable { get; set; }

        /// <summary>"directory" 或 "zip-fallback"。</summary>
        public string RecommendedMode { get; set; }

        /// <summary>逐项明细,顺序与输入一致。</summary>
        public List<ProbeTarget> Targets { get; set; } = new List<ProbeTarget>();
    }

    /// <summary>recommendedMode 取值常量,避免散落字符串字面量。</summary>
    public static class ProbeMode
    {
        public const string Directory = "directory";
        public const string ZipFallback = "zip-fallback";
    }
}

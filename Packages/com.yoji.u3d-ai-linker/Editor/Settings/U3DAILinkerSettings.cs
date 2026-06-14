using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Yoji.U3DAILinker.Settings
{
    /// 工具的项目期望版本条目(通道无关的 tool->revision 映射)。
    [System.Serializable]
    internal sealed class DesiredVersion
    {
        public string ToolId;
        public string Revision;
    }

    /// ProjectSettings 层状态:通道、启用工具、项目期望版本。
    /// 必须可提交、不含本机绝对路径(设计 416 行)。
    /// 本机仓库路径属于 U3DAILinkerUserSettings,不在此类。
    internal sealed class U3DAILinkerSettings : ScriptableObject
    {
        public LinkerChannel Channel = LinkerChannel.Stable;
        public List<string> EnabledToolIds = new List<string>();
        public List<DesiredVersion> DesiredVersions = new List<DesiredVersion>();

        // Windows 盘符绝对路径 (E:\ 或 E:/) 或 file: 前缀或 Unix 绝对路径 (/...)。
        private static readonly Regex AbsolutePathPattern =
            new Regex(@"^([a-zA-Z]:[\\/]|file:|/)", RegexOptions.Compiled);

        /// 任一 revision 看起来像本机绝对路径/ file: 路径,即视为污染。
        public bool ContainsAbsolutePath()
        {
            foreach (var dv in DesiredVersions)
            {
                if (dv != null && LooksAbsolute(dv.Revision))
                    return true;
            }
            return false;
        }

        /// 校验 ProjectSettings 不变量:不得出现绝对路径。失败时 error 给出违例 toolId。
        public bool Validate(out string error)
        {
            foreach (var dv in DesiredVersions)
            {
                if (dv != null && LooksAbsolute(dv.Revision))
                {
                    error = "DesiredVersion for tool '" + dv.ToolId +
                            "' contains an absolute/file path, which must live in UserSettings, not ProjectSettings: " +
                            dv.Revision;
                    return false;
                }
            }
            error = null;
            return true;
        }

        private static bool LooksAbsolute(string value)
        {
            return !string.IsNullOrEmpty(value) && AbsolutePathPattern.IsMatch(value);
        }
    }
}

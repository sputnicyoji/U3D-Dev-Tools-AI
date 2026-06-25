using UnityEngine;

namespace Yoji.U3DAILinker.Settings
{
    /// UserSettings 层:本机仓库路径与面板偏好。不要求提交(设计 417 行)。
    /// Local 通道的绝对路径只能存这里,绝不进 ProjectSettings。
    internal sealed class U3DAILinkerUserSettings : ScriptableObject
    {
        /// Local 通道使用的本机仓库根,如 C:\Example\U3D-Dev-Tools-AI。
        public string LocalRepoRoot = string.Empty;

        /// 面板是否展开 infra 包细节。
        public bool ShowInfraDetails = false;
    }
}

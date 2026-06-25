using System.IO;
using UnityEditor;
using UnityEngine;

namespace Yoji.U3DAILinker.Settings
{
    /// 负责 ProjectSettings / UserSettings 两层 ScriptableObject 的加载与保存。
    /// ProjectSettings 落 ProjectSettings/ 目录(可提交、不含绝对路径);
    /// UserSettings 落 UserSettings/ 目录(本机偏好)。
    /// 保存 ProjectSettings 前强制校验绝对路径不变量。
    /// 序列化用 EditorJsonUtility(跨 Unity 版本稳定;文件内容为 JSON,文件在 Assets 外不被 Unity 导入)。
    internal static class U3DAILinkerSettingsStore
    {
        public const string ProjectSettingsAssetPath = "ProjectSettings/U3DAILinkerSettings.asset";
        public const string UserSettingsAssetPath = "UserSettings/U3DAILinkerUserSettings.asset";

        public static U3DAILinkerSettings LoadOrCreateProjectSettings()
        {
            var loaded = LoadFromFile<U3DAILinkerSettings>(ProjectSettingsAssetPath);
            return loaded != null ? loaded : ScriptableObject.CreateInstance<U3DAILinkerSettings>();
        }

        public static U3DAILinkerUserSettings LoadOrCreateUserSettings()
        {
            var loaded = LoadFromFile<U3DAILinkerUserSettings>(UserSettingsAssetPath);
            return loaded != null ? loaded : ScriptableObject.CreateInstance<U3DAILinkerUserSettings>();
        }

        /// 保存 ProjectSettings;先校验不变量,失败则不写盘。
        public static bool TrySaveProjectSettings(U3DAILinkerSettings settings, out string error)
        {
            if (!settings.Validate(out error))
                return false;
            SaveToFile(ProjectSettingsAssetPath, settings);
            error = null;
            return true;
        }

        public static void SaveUserSettings(U3DAILinkerUserSettings settings)
        {
            SaveToFile(UserSettingsAssetPath, settings);
        }

        private static T LoadFromFile<T>(string path) where T : ScriptableObject
        {
            if (!File.Exists(path))
                return null;
            try
            {
                var instance = ScriptableObject.CreateInstance<T>();
                EditorJsonUtility.FromJsonOverwrite(File.ReadAllText(path), instance);
                return instance;
            }
            catch
            {
                return null;
            }
        }

        private static void SaveToFile(string path, ScriptableObject settings)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, EditorJsonUtility.ToJson(settings, true));
        }
    }
}

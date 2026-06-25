using UnityEditor;
using UnityEngine;

namespace Yoji.LuaDeviceDebug.Editor
{
    internal static class LuaDeviceDebugRuntimeConfigEditor
    {
        private const string k_AssetPath = "Assets/Resources/YojiLuaDeviceDebugRuntimeConfig.asset";

        [MenuItem("Tools/Yoji/Lua Device Debug/Create Runtime Port Config")]
        private static void CreateRuntimePortConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<LuaDeviceDebugRuntimeConfig>(k_AssetPath);
            if (config == null)
            {
                var existing = AssetDatabase.LoadMainAssetAtPath(k_AssetPath);
                if (existing != null)
                {
                    Debug.LogError("[LuaDeviceDebug] Runtime port config path is occupied by another asset: " + k_AssetPath);
                    Selection.activeObject = existing;
                    EditorGUIUtility.PingObject(existing);
                    return;
                }

                if (!EnsureFolder())
                {
                    Debug.LogError("[LuaDeviceDebug] Failed to create Assets/Resources for runtime port config");
                    return;
                }

                config = ScriptableObject.CreateInstance<LuaDeviceDebugRuntimeConfig>();
                AssetDatabase.CreateAsset(config, k_AssetPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                config = AssetDatabase.LoadAssetAtPath<LuaDeviceDebugRuntimeConfig>(k_AssetPath);
            }

            if (config == null)
                return;

            Selection.activeObject = config;
            EditorGUIUtility.PingObject(config);
            EditorUtility.FocusProjectWindow();
        }

        private static bool EnsureFolder()
        {
            if (AssetDatabase.IsValidFolder("Assets/Resources"))
                return true;

            if (!AssetDatabase.IsValidFolder("Assets"))
                return false;

            var guid = AssetDatabase.CreateFolder("Assets", "Resources");
            if (string.IsNullOrEmpty(guid))
                return AssetDatabase.IsValidFolder("Assets/Resources");

            return AssetDatabase.GUIDToAssetPath(guid) == "Assets/Resources"
                && AssetDatabase.IsValidFolder("Assets/Resources");
        }
    }
}

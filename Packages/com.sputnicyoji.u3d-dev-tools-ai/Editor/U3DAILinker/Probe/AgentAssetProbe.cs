using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Yoji.U3DAILinker.Probe
{
    /// <summary>
    /// 一次性探针入口:验证 Agent~/ 与 BundledSkills~/ 在 Git Package Cache 的
    /// resolvedPath 下是否可读,结果写 Library/U3DAILinker/probe-result.json。
    /// 这是 LINK 系列的硬前置;探针失败 -> 切 zip fallback(见 probe-result.json 的 recommendedMode)。
    /// </summary>
    public static class AgentAssetProbe
    {
        private const string PackageName = "com.sputnicyoji.u3d-dev-tools-ai";

        private static ListRequest _listRequest;

        [MenuItem("Tools/U3D Dev Tools AI/Run Agent Asset Probe")]
        public static void Run()
        {
            if (_listRequest != null)
            {
                Debug.LogWarning("[U3DAILinker] Probe already running.");
                return;
            }

            Debug.Log("[U3DAILinker] Agent asset probe started. Listing packages...");
            // offlineMode=false,includeIndirectDependencies=true,确保拿到完整 resolvedPath。
            _listRequest = Client.List(offlineMode: false, includeIndirectDependencies: true);
            EditorApplication.update += PollList;
        }

        private static void PollList()
        {
            if (_listRequest == null || !_listRequest.IsCompleted)
            {
                return;
            }

            EditorApplication.update -= PollList;
            ListRequest request = _listRequest;
            _listRequest = null;

            if (request.Status != StatusCode.Success)
            {
                string err = request.Error != null ? request.Error.message : "unknown error";
                Debug.LogError("[U3DAILinker] Client.List failed: " + err);
                return;
            }

            string packagePath = ResolvePath(request, PackageName);

            var targets = new List<ProbeTarget>
            {
                MakeFileTarget(
                    "editor-debug.SKILL.md",
                    packagePath,
                    "Agent~/skills/unity-editor-debug-mcp/SKILL.md"),
                MakeFileTarget(
                    "test-runner.SKILL.md",
                    packagePath,
                    "Agent~/skills/test-runner-mcp/SKILL.md"),
                MakeFileTarget(
                    "lua-device-debug.SKILL.md",
                    packagePath,
                    "Agent~/skills/unity-lua-device-debug/SKILL.md"),
                MakeDirectoryTarget(
                    "agent.fragments",
                    packagePath,
                    "Agent~/fragments"),
            };

            ProbeResult result = ProbeEvaluator.Evaluate(targets);
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string written = ProbeResultWriter.Write(projectRoot, result);

            if (result.AllTargetsReadable)
            {
                Debug.Log(
                    "[U3DAILinker] Probe PASSED. All Agent~/BundledSkills~ targets readable. " +
                    "recommendedMode=directory. Wrote " + written);
            }
            else
            {
                Debug.LogWarning(
                    "[U3DAILinker] Probe FAILED. At least one target missing under resolvedPath. " +
                    "recommendedMode=zip-fallback. Inspect " + written +
                    " and switch LINK-1/4/7 to zip-bytes mode.");
            }
        }

        private static string ResolvePath(ListRequest request, string packageName)
        {
            foreach (var info in request.Result)
            {
                if (info.name == packageName)
                {
                    return info.resolvedPath;
                }
            }
            return null;
        }

        private static ProbeTarget MakeFileTarget(string id, string resolvedPath, string relative)
        {
            if (string.IsNullOrEmpty(resolvedPath))
            {
                return new ProbeTarget(id, "<package-not-installed:" + id + ">", "File", false);
            }
            string full = Path.Combine(resolvedPath, relative);
            return new ProbeTarget(id, full, "File", File.Exists(full));
        }

        private static ProbeTarget MakeDirectoryTarget(string id, string resolvedPath, string relative)
        {
            if (string.IsNullOrEmpty(resolvedPath))
            {
                return new ProbeTarget(id, "<package-not-installed:" + id + ">", "Directory", false);
            }
            string full = Path.Combine(resolvedPath, relative);
            return new ProbeTarget(id, full, "Directory", Directory.Exists(full));
        }
    }
}

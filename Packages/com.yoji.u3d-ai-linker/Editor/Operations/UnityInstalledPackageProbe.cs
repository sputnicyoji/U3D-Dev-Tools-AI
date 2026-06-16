using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Yoji.U3DAILinker.Operations
{
    /// 真实探针：从 PackageInfo 读取目标工程对某包的依赖来源字符串。
    /// Unity 的 PackageInfo 不直接暴露 manifest 顶层声明值，故用 packageId 的 source 段
    /// (packageId 形如 "name@<source>")：Git 包得 Git URL、Local 包得 file: 路径、
    /// Registry 包得版本号。这对 IsManagedUrl 判定（是否本仓库 Git URL / 本机 file:）已足够，
    /// 且在 2022.3 与 Unity 6 上一致可用。不进自动化测试；由 Task 26 / SLG 手动验证覆盖。
    internal sealed class UnityInstalledPackageProbe : IInstalledPackageProbe
    {
        public string GetInstalledUrl(string packageName)
        {
            var info = PackageInfo.FindForPackageName(packageName);
            if (info == null) return null;
            var manifestUrl = TryReadManifestDependency(packageName);
            if (!string.IsNullOrEmpty(manifestUrl))
                return manifestUrl;
            var id = info.packageId;
            if (string.IsNullOrEmpty(id)) return id;
            // 包名不含 '@'，故第一个 '@' 即 name 与 source 的分隔符。
            int at = id.IndexOf('@');
            return at >= 0 ? id.Substring(at + 1) : id;
        }

        internal static string TryReadManifestDependency(string packageName)
        {
            if (string.IsNullOrEmpty(packageName))
                return null;

            var manifest = Path.Combine(
                Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                "Packages",
                "manifest.json");
            if (!File.Exists(manifest))
                return null;

            try
            {
                var json = JObject.Parse(File.ReadAllText(manifest));
                var deps = json["dependencies"] as JObject;
                return deps != null ? deps.Value<string>(packageName) : null;
            }
            catch
            {
                return null;
            }
        }
    }
}

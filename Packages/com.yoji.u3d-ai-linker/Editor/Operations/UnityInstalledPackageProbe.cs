using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor.PackageManager;
using UnityEngine;
using Yoji.U3DAILinker.Registry;

namespace Yoji.U3DAILinker.Operations
{
    /// 真实探针：PackageInfo 确认包已解析，manifest 声明值用于 UI 当前 URL，
    /// PackageInfo.git.hash/revision 用于发现 tag URL 锁在旧 hash 的情况。
    internal sealed class UnityInstalledPackageProbe : IInstalledPackageProbe
    {
        public string GetInstalledUrl(string packageName)
        {
            var installed = GetInstalledPackageInfo(packageName, expectedHash: null);
            return installed != null ? installed.ResolvedUrl : null;
        }

        public InstalledPackageInfo GetInstalledPackageInfo(string packageName, string expectedHash)
        {
            var info = PackageInfo.FindForPackageName(packageName);
            if (info == null) return null;
            var manifestUrl = TryReadManifestDependency(packageName);
            var id = info.packageId;
            var packageIdUrl = ExtractPackageIdUrl(id);
            var resolvedUrl = !string.IsNullOrEmpty(manifestUrl) ? manifestUrl : packageIdUrl;
            var git = ReadMember(info, "git") as object;
            var gitHash = ReadStringMember(git, "hash");
            var gitRevision = ReadStringMember(git, "revision");
            return new InstalledPackageInfo(
                packageName,
                resolvedUrl,
                ToolUrlBuilder.IsManagedUrl(resolvedUrl),
                manifestUrl,
                packageIdUrl,
                gitHash,
                gitRevision,
                expectedHash);
        }

        private static string ExtractPackageIdUrl(string packageId)
        {
            if (string.IsNullOrEmpty(packageId)) return packageId;
            // 包名不含 '@'，故第一个 '@' 即 name 与 source 的分隔符。
            int at = packageId.IndexOf('@');
            return at >= 0 ? packageId.Substring(at + 1) : packageId;
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

        private static object ReadMember(object instance, string name)
        {
            if (instance == null)
                return null;
            var type = instance.GetType();
            var prop = type.GetProperty(name);
            if (prop != null)
                return prop.GetValue(instance, null);
            var field = type.GetField(name);
            return field != null ? field.GetValue(instance) : null;
        }

        private static string ReadStringMember(object instance, string name)
        {
            return ReadMember(instance, name) as string;
        }
    }
}

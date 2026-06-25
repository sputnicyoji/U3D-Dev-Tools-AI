using UnityEditor.PackageManager;

namespace Yoji.U3DAILinker.Settings
{
    internal sealed class UnityResolvedPackagePathProvider : IResolvedPackagePathProvider
    {
        public string GetResolvedPath(string packageName)
        {
            var info = PackageInfo.FindForPackageName(packageName);
            return info != null ? info.resolvedPath : null;
        }
    }
}

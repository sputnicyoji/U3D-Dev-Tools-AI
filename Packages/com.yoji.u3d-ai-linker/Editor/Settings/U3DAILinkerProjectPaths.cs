using System.IO;
using UnityEngine;

namespace Yoji.U3DAILinker.Settings
{
    internal static class U3DAILinkerProjectPaths
    {
        public static string ProjectRoot
            => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        public static string LibraryRoot
            => Path.Combine(ProjectRoot, "Library");
    }
}

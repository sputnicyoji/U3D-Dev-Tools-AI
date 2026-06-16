using System.IO;
using UnityEditor;
using UnityEngine;

namespace Yoji.U3DAILinker.Operations
{
    internal static class WindowsJunctionSmoke
    {
        [MenuItem("Tools/U3D AI Linker/Run Windows Junction Smoke")]
        public static void Run()
        {
            var root = Path.Combine(Application.dataPath, "..", "Library", "U3DAILinker", "junction-smoke");
            root = Path.GetFullPath(root);
            var target = Path.Combine(root, "target");
            var link = Path.Combine(root, "link");
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "marker.txt"), "ok");

            var junctions = new WindowsJunctionManager();
            if (junctions.IsJunction(link))
                junctions.Delete(link);
            else if (Directory.Exists(link) || File.Exists(link))
                throw new IOException("smoke link path is occupied by non-junction: " + link);

            junctions.Create(link, target);
            if (!junctions.IsJunction(link))
                throw new IOException("junction was not created: " + link);
            if (junctions.GetTarget(link) != target)
                throw new IOException("junction target mismatch: " + junctions.GetTarget(link));
            if (!File.Exists(Path.Combine(link, "marker.txt")))
                throw new IOException("junction does not expose target marker.");

            junctions.Delete(link);
            if (!File.Exists(Path.Combine(target, "marker.txt")))
                throw new IOException("junction delete damaged target.");

            Debug.Log("[U3DAILinker] Windows Junction smoke passed: " + root);
        }
    }
}

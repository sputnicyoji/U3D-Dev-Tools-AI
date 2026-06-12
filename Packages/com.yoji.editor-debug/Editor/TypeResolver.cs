using System;
using System.Collections.Generic;
using System.Reflection;

namespace Yoji.EditorDebug
{
    /// 类型名解析。顺序：Type.GetType -> 显式程序集后缀 -> 常用 Unity 程序集 -> 全程序集扫描。
    /// 找不到抛 TypeAccessException（协议约定的错误类型）。
    internal static class TypeResolver
    {
        private static readonly string[] s_PreferredAssemblies =
        {
            "UnityEditor", "UnityEngine",
            "UnityEditor.CoreModule", "UnityEngine.CoreModule",
            "UnityEngine.UIElementsModule", "UnityEditor.UIElementsModule",
            "UnityEngine.PhysicsModule", "UnityEngine.AnimationModule",
            "UnityEngine.ProfilerModule",
            "mscorlib", "netstandard",
        };

        public static Type Resolve(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                throw new TypeAccessException("type name is empty");
            typeName = typeName.Trim();

            var t = Type.GetType(typeName);
            if (t != null) return t;

            var all = AppDomain.CurrentDomain.GetAssemblies();
            var byName = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
            foreach (var asm in all) byName[asm.GetName().Name] = asm;

            var comma = typeName.IndexOf(',');
            if (comma > 0)
            {
                var pureName = typeName.Substring(0, comma).Trim();
                var asmName = typeName.Substring(comma + 1).Trim();
                if (byName.TryGetValue(asmName, out var named))
                {
                    t = named.GetType(pureName);
                    if (t != null) return t;
                }
                typeName = pureName;
            }

            foreach (var name in s_PreferredAssemblies)
            {
                if (byName.TryGetValue(name, out var asm))
                {
                    t = asm.GetType(typeName);
                    if (t != null) return t;
                }
            }

            foreach (var asm in all)
            {
                t = asm.GetType(typeName);
                if (t != null) return t;
            }
            throw new TypeAccessException("type not found: " + typeName);
        }

        public static bool TryResolve(string typeName, out Type type)
        {
            try { type = Resolve(typeName); return true; }
            catch (TypeAccessException) { type = null; return false; }
        }
    }
}

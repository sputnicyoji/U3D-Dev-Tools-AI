using System;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace Yoji.EditorDebug
{
    /// /describe：列出类型全部成员（含 internal/private）的人类可读签名。
    internal static class DescribeHandler
    {
        private const BindingFlags k_All = BindingFlags.Public | BindingFlags.NonPublic |
                                           BindingFlags.Instance | BindingFlags.Static;

        public static JObject Describe(string typeName)
        {
            var type = TypeResolver.Resolve(typeName);
            return new JObject
            {
                ["FullName"] = type.FullName,
                ["Assembly"] = type.Assembly.GetName().Name,
                ["Methods"] = new JArray(type.GetMethods(k_All)
                    .Where(m => !m.IsSpecialName).Select(Signature).Cast<object>().ToArray()),
                ["Properties"] = new JArray(type.GetProperties(k_All).Select(p =>
                    Access(p.GetGetMethod(true) ?? p.GetSetMethod(true)) + " " +
                    Short(p.PropertyType) + " " + p.Name + " { " +
                    (p.CanRead ? "get; " : "") + (p.CanWrite ? "set; " : "") + "}")
                    .Cast<object>().ToArray()),
                ["Fields"] = new JArray(type.GetFields(k_All).Select(f =>
                    FieldAccess(f) + (f.IsStatic ? " static " : " ") + Short(f.FieldType) + " " + f.Name)
                    .Cast<object>().ToArray()),
            };
        }

        public static string Signature(MethodInfo m) =>
            Access(m) + (m.IsStatic ? " static " : " ") + Short(m.ReturnType) + " " + m.Name + "(" +
            string.Join(", ", m.GetParameters().Select(p => Short(p.ParameterType) + " " + p.Name)) + ")";

        private static string Access(MethodBase m) =>
            m == null ? "?" : m.IsPublic ? "public" : m.IsFamily ? "protected" : m.IsAssembly ? "internal" : "private";

        private static string FieldAccess(FieldInfo f) =>
            f.IsPublic ? "public" : f.IsFamily ? "protected" : f.IsAssembly ? "internal" : "private";

        private static string Short(Type t) => t.IsGenericType
            ? t.Name.Split('`')[0] + "<" + string.Join(",", t.GetGenericArguments().Select(Short)) + ">"
            : t.Name;
    }
}

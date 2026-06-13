using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Yoji.EditorDebug
{
    /// void 方法返回值哨兵：信封层写 "void":true。
    internal sealed class VoidResult
    {
        public static readonly VoidResult Instance = new VoidResult();
        private VoidResult() { }
    }

    /// CLR 对象 -> JSON 摘要。
    /// 全局熔断 maxDepth=12 / maxNodes=2000；VisualElement 树单独熔断 maxDepth=16 / maxNodes=500。
    /// UnityEngine.Object 降级 ref 摘要；占位符见 protocol.md。
    internal sealed class ResultSerializer
    {
        private const int k_MaxDepth = 12;
        private const int k_MaxNodes = 2000;
        private const int k_VeMaxDepth = 16;
        private const int k_VeMaxNodes = 500;
        private const int k_TaskWaitMs = 3000;

        private int m_Nodes;

        public static JToken ToJson(object value) => new ResultSerializer().Serialize(value, 0);

        private JToken Serialize(object value, int depth)
        {
            m_Nodes++;
            if (m_Nodes > k_MaxNodes) return Truncated("maxNodes");
            if (depth > k_MaxDepth) return Truncated("maxDepth");

            if (value == null || value is VoidResult) return JValue.CreateNull();

            var type = value.GetType();
            if (value is string s) return new JValue(s);
            if (value is float f)
                return float.IsNaN(f) || float.IsInfinity(f) ? (JToken)f.ToString() : new JValue(f);
            if (value is double d)
                return double.IsNaN(d) || double.IsInfinity(d) ? (JToken)d.ToString() : new JValue(d);
            if (value is IntPtr || value is UIntPtr || value is System.Runtime.InteropServices.SafeHandle)
                return new JObject { ["__skipped"] = "IntPtr" };
            if (type.IsPrimitive || value is decimal) return JToken.FromObject(value);
            if (type.IsEnum) return new JValue(value.ToString());
            if (value is Delegate del)
                return new JObject
                {
                    ["__delegate"] = true,
                    ["target"] = (del.Method.DeclaringType?.Name ?? "?") + "." + del.Method.Name,
                };
            if (value is Task task) return SerializeTask(task, depth);
            if (value is VisualElement ve) return new VisualElementWalker().Walk(ve, 0);
            if (value is UnityEngine.Object uo) return SerializeUnityObject(uo);
            if (value is IEnumerator) return new JObject { ["__enumerator"] = type.FullName };
            if (value is IDictionary dict)
            {
                var o = new JObject();
                foreach (DictionaryEntry e in dict)
                {
                    if (m_Nodes > k_MaxNodes) break;
                    o[e.Key?.ToString() ?? "null"] = Serialize(e.Value, depth + 1);
                }
                return o;
            }
            if (value is IEnumerable en)
            {
                var arr = new JArray();
                foreach (var item in en)
                {
                    arr.Add(Serialize(item, depth + 1));
                    if (m_Nodes > k_MaxNodes) break;
                }
                return arr;
            }
            return SerializeComplex(value, type, depth);
        }

        private static JToken SerializeUnityObject(UnityEngine.Object uo)
        {
            if (uo == null) return JValue.CreateNull(); // Unity fake null（已销毁对象）
            var o = new JObject
            {
                ["__ref"] = true,
                ["instanceID"] = SerializeObjectId(uo),
                ["type"] = uo.GetType().FullName,
                ["name"] = uo.name,
            };
            var go = uo as GameObject ?? (uo as Component)?.gameObject;
            if (go != null) o["path"] = HierarchyPath(go.transform);
            var assetPath = UnityEditor.AssetDatabase.GetAssetPath(uo);
            if (!string.IsNullOrEmpty(assetPath))
                o["guid"] = UnityEditor.AssetDatabase.AssetPathToGUID(assetPath);
            return o;
        }

        private static JToken SerializeObjectId(UnityEngine.Object uo)
        {
#if UNITY_6000_4_OR_NEWER
            return new JValue(EntityId.ToULong(uo.GetEntityId())
                .ToString(CultureInfo.InvariantCulture));
#else
            return new JValue(uo.GetInstanceID());
#endif
        }

        private static string HierarchyPath(Transform t)
        {
            var parts = new List<string>();
            for (; t != null; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        private JToken SerializeTask(Task task, int depth)
        {
            try
            {
                if (!task.Wait(k_TaskWaitMs)) return new JObject { ["__pending"] = true };
            }
            catch (AggregateException ae)
            {
                var inner = ae.InnerException ?? ae;
                return new JObject { ["__error"] = inner.GetType().Name, ["message"] = inner.Message };
            }
            var resultProp = task.GetType().GetProperty("Result");
            return resultProp != null && resultProp.PropertyType.Name != "VoidTaskResult"
                ? Serialize(resultProp.GetValue(task), depth + 1)
                : JValue.CreateNull();
        }

        private JToken SerializeComplex(object value, Type type, int depth)
        {
            var o = new JObject();
            foreach (var fi in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                o[fi.Name] = Guard(() => Serialize(fi.GetValue(value), depth + 1));
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanRead || p.GetIndexParameters().Length > 0) continue;
                o[p.Name] = Guard(() => Serialize(p.GetValue(value), depth + 1));
            }
            return o;
        }

        private static JToken Guard(Func<JToken> read)
        {
            try { return read(); }
            catch (Exception e)
            {
                var inner = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
                return new JObject { ["__error"] = inner.GetType().Name, ["message"] = inner.Message };
            }
        }

        private static JObject Truncated(string reason)
            => new JObject { ["__truncated"] = true, ["reason"] = reason };

        /// VisualElement 树摘要（独立熔断 + 循环检测）。
        private sealed class VisualElementWalker
        {
            private int m_Nodes;
            private readonly HashSet<VisualElement> m_Visited = new HashSet<VisualElement>();

            public JToken Walk(VisualElement ve, int depth)
            {
                if (ve == null) return JValue.CreateNull();
                if (!m_Visited.Add(ve)) return new JObject { ["__cycle"] = ve.name ?? "" };
                m_Nodes++;
                if (m_Nodes > k_VeMaxNodes) return Truncated("maxNodes");
                if (depth > k_VeMaxDepth) return Truncated("maxDepth");

                var o = new JObject
                {
                    ["name"] = ve.name ?? "",
                    ["type"] = ve.GetType().Name,
                    ["classList"] = new JArray(ve.GetClasses().Cast<object>().ToArray()),
                    ["pickingMode"] = ve.pickingMode.ToString(),
                    ["visible"] = ve.visible,
                    ["enabledSelf"] = ve.enabledSelf,
                };
                var r = ve.layout;
                o["layout"] = new JObject
                {
                    ["x"] = Num(r.x), ["y"] = Num(r.y), ["w"] = Num(r.width), ["h"] = Num(r.height),
                };
                if (ve is TextElement te) o["text"] = te.text;
                if (!string.IsNullOrEmpty(ve.viewDataKey)) o["viewDataKey"] = ve.viewDataKey;
                if (ve is IBindable b && !string.IsNullOrEmpty(b.bindingPath)) o["bindingPath"] = b.bindingPath;
                o["childCount"] = ve.childCount;
                var children = new JArray();
                foreach (var child in ve.Children()) children.Add(Walk(child, depth + 1));
                o["children"] = children;
                return o;
            }

            private static JToken Num(float v) => float.IsNaN(v) ? (JToken)"NaN" : new JValue(v);
        }
    }
}

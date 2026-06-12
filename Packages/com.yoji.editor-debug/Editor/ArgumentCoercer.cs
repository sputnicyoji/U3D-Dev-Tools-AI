using System;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace Yoji.EditorDebug
{
    /// 把 JSON 实参转换成目标 CLR 类型。
    /// 特殊规则：string -> System.Type（走 TypeResolver）；string -> enum（容忍带命名空间前缀的写法）；
    /// {instanceID:N} -> UnityEngine.Object 反查。
    internal static class ArgumentCoercer
    {
        public static object Coerce(JToken arg, Type targetType)
        {
            if (arg == null || arg.Type == JTokenType.Null)
                return null;

            if (typeof(Type).IsAssignableFrom(targetType) && arg.Type == JTokenType.String)
                return TypeResolver.Resolve(arg.Value<string>());

            if (targetType.IsEnum)
            {
                if (arg.Type == JTokenType.String)
                {
                    var s = arg.Value<string>();
                    var lastDot = s.LastIndexOf('.');
                    if (lastDot > 0) s = s.Substring(lastDot + 1);
                    return Enum.Parse(targetType, s, ignoreCase: true);
                }
                return Enum.ToObject(targetType, arg.Value<long>());
            }

            if (typeof(UnityEngine.Object).IsAssignableFrom(targetType) && arg.Type == JTokenType.Object)
            {
                var id = arg["instanceID"];
                if (id != null) return EditorUtility.InstanceIDToObject(id.Value<int>());
            }

            if (targetType == typeof(object))
            {
                switch (arg.Type)
                {
                    case JTokenType.Integer: return arg.Value<long>();
                    case JTokenType.Float: return arg.Value<double>();
                    case JTokenType.String: return arg.Value<string>();
                    case JTokenType.Boolean: return arg.Value<bool>();
                }
            }
            return arg.ToObject(targetType);
        }
    }
}

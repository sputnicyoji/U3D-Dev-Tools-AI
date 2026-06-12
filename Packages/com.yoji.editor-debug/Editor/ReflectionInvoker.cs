using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Yoji.EditorDebug
{
    /// /invoke：单步或链式反射调用。kind = get | set | call | index。
    /// 重载决议：argTypes 严格匹配 > 唯一候选 > 按实参个数过滤（含可选参数）> AmbiguousMatchException。
    internal static class ReflectionInvoker
    {
        private const BindingFlags k_All = BindingFlags.Public | BindingFlags.NonPublic |
                                           BindingFlags.Instance | BindingFlags.Static;

        public static object Execute(JObject req)
        {
            var type = TypeResolver.Resolve(req.Value<string>("type"));
            object target = ResolveTarget(req["target"]);

            var steps = req["steps"] as JArray;
            if (steps != null && steps.Count > 0)
            {
                object current = target;
                Type currentType = target?.GetType() ?? type;
                foreach (var stepTok in steps)
                {
                    current = ExecuteStep(current, currentType, (JObject)stepTok);
                    if (current is VoidResult && !ReferenceEquals(stepTok, steps.Last()))
                        throw new InvalidOperationException("void result cannot be chained");
                    currentType = current?.GetType() ?? typeof(object);
                }
                return current;
            }
            return ExecuteStep(target, target?.GetType() ?? type, req);
        }

        private static object ResolveTarget(JToken targetTok)
        {
            if (targetTok == null || targetTok.Type != JTokenType.Object) return null;
            var id = targetTok["instanceID"];
            if (id == null) return null;
            var obj = EditorUtility.InstanceIDToObject(id.Value<int>());
            if (obj == null)
                throw new MissingReferenceException("no alive object with instanceID " + id.Value<int>());
            return obj;
        }

        internal static object ExecuteStep(object target, Type type, JObject step)
        {
            var member = step.Value<string>("member");
            var kind = step.Value<string>("kind") ?? "get";
            var args = step["args"] as JArray ?? new JArray();
            var argTypes = step["argTypes"] as JArray;

            switch (kind)
            {
                case "get": return DoGet(target, type, member);
                case "set": DoSet(target, type, member, args); return VoidResult.Instance;
                case "call": return DoCall(target, type, member, args, argTypes);
                case "index": return DoIndex(target, args);
                default: throw new ArgumentException("unknown kind: " + kind);
            }
        }

        private static object DoGet(object target, Type type, string member)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var prop = t.GetProperty(member, k_All | BindingFlags.DeclaredOnly);
                if (prop != null)
                {
                    var getter = prop.GetGetMethod(true);
                    if (getter == null) throw new MissingMethodException(type.FullName, member + " (no getter)");
                    return getter.Invoke(getter.IsStatic ? null : target, null);
                }
                var field = t.GetField(member, k_All | BindingFlags.DeclaredOnly);
                if (field != null) return field.GetValue(field.IsStatic ? null : target);
            }
            throw new MissingMethodException(type.FullName, member);
        }

        private static void DoSet(object target, Type type, string member, JArray args)
        {
            if (args.Count != 1) throw new ArgumentException("set expects exactly 1 arg");
            for (var t = type; t != null; t = t.BaseType)
            {
                var prop = t.GetProperty(member, k_All | BindingFlags.DeclaredOnly);
                if (prop != null)
                {
                    var setter = prop.GetSetMethod(true);
                    if (setter == null) throw new MissingMethodException(type.FullName, member + " (no setter)");
                    setter.Invoke(setter.IsStatic ? null : target,
                        new[] { ArgumentCoercer.Coerce(args[0], prop.PropertyType) });
                    return;
                }
                var field = t.GetField(member, k_All | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    field.SetValue(field.IsStatic ? null : target,
                        ArgumentCoercer.Coerce(args[0], field.FieldType));
                    return;
                }
            }
            throw new MissingMethodException(type.FullName, member);
        }

        private static object DoCall(object target, Type type, string member, JArray args, JArray argTypes)
        {
            var candidates = new List<MethodInfo>();
            for (var t = type; t != null; t = t.BaseType)
                candidates.AddRange(t.GetMethods(k_All | BindingFlags.DeclaredOnly)
                    .Where(m => m.Name == member && !m.IsGenericMethodDefinition));
            if (candidates.Count == 0) throw new MissingMethodException(type.FullName, member);

            MethodInfo chosen;
            if (argTypes != null && argTypes.Count > 0)
            {
                var wanted = argTypes.Select(x => x.Value<string>()).ToArray();
                chosen = candidates.FirstOrDefault(m =>
                {
                    var ps = m.GetParameters();
                    return ps.Length == wanted.Length &&
                           ps.Select(p => p.ParameterType.FullName).SequenceEqual(wanted);
                });
                if (chosen == null)
                    throw new MissingMethodException(type.FullName,
                        member + "(" + string.Join(",", wanted) + ") -- available: " +
                        string.Join(" | ", candidates.Select(DescribeHandler.Signature)));
            }
            else if (candidates.Count == 1)
            {
                chosen = candidates[0];
            }
            else
            {
                var byCount = candidates.Where(m =>
                {
                    var ps = m.GetParameters();
                    int required = ps.Count(p => !p.HasDefaultValue);
                    return args.Count >= required && args.Count <= ps.Length;
                }).ToList();
                if (byCount.Count == 1) chosen = byCount[0];
                else if (byCount.Count == 0) throw new MissingMethodException(type.FullName, member);
                else throw new AmbiguousMatchException(
                    member + " has " + byCount.Count + " overloads: " +
                    string.Join(" | ", byCount.Select(DescribeHandler.Signature)) +
                    " -- disambiguate with argTypes");
            }

            var pars = chosen.GetParameters();
            var clrArgs = new object[pars.Length];
            for (int i = 0; i < pars.Length; i++)
                clrArgs[i] = i < args.Count
                    ? ArgumentCoercer.Coerce(args[i], pars[i].ParameterType)
                    : pars[i].DefaultValue;

            object result = chosen.Invoke(chosen.IsStatic ? null : target, clrArgs);
            return chosen.ReturnType == typeof(void) ? VoidResult.Instance : result;
        }

        private static object DoIndex(object target, JArray args)
        {
            if (target == null) throw new NullReferenceException("index on null target");
            if (args.Count != 1) throw new ArgumentException("index expects exactly 1 arg");
            if (target is IList list) return list[(int)ArgumentCoercer.Coerce(args[0], typeof(int))];
            if (target is IDictionary dict) return dict[args[0].Value<string>()];
            var indexer = target.GetType().GetProperty("Item", k_All);
            if (indexer != null)
                return indexer.GetValue(target,
                    new[] { ArgumentCoercer.Coerce(args[0], indexer.GetIndexParameters()[0].ParameterType) });
            throw new MissingMethodException(target.GetType().FullName, "Item (indexer)");
        }
    }
}

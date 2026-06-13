using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Yoji.TestRunner
{
    internal sealed class FilterSpec
    {
        public string TestMode;
        public string[] TestNames = Array.Empty<string>();
        public string[] AssemblyNames = Array.Empty<string>();
        public string[] CategoryNames = Array.Empty<string>();
        public string[] GroupNames = Array.Empty<string>();
        public bool IsRunAll;
    }

    /// 解析 /run-tests 请求体为 FilterSpec。非法 -> ArgumentException（调用方返 400）。
    /// run-all 扩展：testNames/assemblyNames/categoryNames/groupNames 全空 = 跑该 testMode 全套件。
    internal static class TestFilterBuilder
    {
        public static FilterSpec Parse(JObject req)
        {
            var modeToken = req["testMode"];
            var mode = modeToken != null && modeToken.Type == JTokenType.String ? (string)modeToken : null;
            if (mode != "EditMode" && mode != "PlayMode")
                throw new ArgumentException("testMode must be EditMode or PlayMode");

            var spec = new FilterSpec
            {
                TestMode = mode,
                TestNames = ToArray(req["testNames"]),
                AssemblyNames = ToArray(req["assemblyNames"]),
                CategoryNames = ToArray(req["categoryNames"]),
                GroupNames = ToArray(req["groupNames"]),
            };
            spec.IsRunAll = spec.TestNames.Length == 0 && spec.AssemblyNames.Length == 0
                && spec.CategoryNames.Length == 0 && spec.GroupNames.Length == 0;
            return spec;
        }

        private static string[] ToArray(JToken t)
        {
            if (t == null || t.Type != JTokenType.Array) return Array.Empty<string>();
            var list = new List<string>();
            foreach (var x in (JArray)t)
            {
                var s = (string)x;
                if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
            }
            return list.ToArray();
        }
    }
}

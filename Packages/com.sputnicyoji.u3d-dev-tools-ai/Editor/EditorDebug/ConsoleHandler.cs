using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace Yoji.EditorDebug
{
    /// /console：读 Unity Console 真实日志条目。LogEntries/LogEntry 是 editor-internal、仅主线程，
    /// 故全程反射（不硬类型引用，随 Unity 版本敏感）。StartGettingEntries/EndGettingEntries 必须
    /// try/finally 配对，否则 Console UI 会楔死。单条不可读时跳过，整体不可用时返 {__unavailable}。
    internal static class ConsoleHandler
    {
        // LogMessageFlags 位子集（UnityEngine 内部常量，版本敏感，best-effort 分类）。
        private const int k_ModeError =
            (1 << 0) | (1 << 1) | (1 << 4) | (1 << 6) | (1 << 8) | (1 << 11) | (1 << 13) | (1 << 17) | (1 << 21);
        // Error Assert Fatal AssetImportError ScriptingError ScriptCompileError StickyError ScriptingException ScriptingAssertion
        private const int k_ModeWarning = (1 << 7) | (1 << 9) | (1 << 12);
        // AssetImportWarning ScriptingWarning ScriptCompileWarning

        private static bool s_Resolved;
        private static Type s_LogEntryType;
        private static MethodInfo s_StartGetting, s_EndGetting, s_GetEntry;
        private static FieldInfo s_FMessage, s_FFile, s_FLine, s_FMode, s_FInstanceId, s_FCallstack;

        /// 主线程调用。返回 {count,total,entries:[...]} 或 {__unavailable:reason}。
        public static object Read(JObject req)
        {
            int count = ReadInt(req, "count", 50);
            if (count < 1) count = 1;
            if (count > 1000) count = 1000;
            string filter = (req?["filter"]?.Value<string>() ?? "all").ToLowerInvariant();
            bool includeStack = req?["includeStack"]?.Value<bool>() ?? false;

            if (!EnsureReflection(out string err))
                return new JObject { ["__unavailable"] = err };

            int total;
            try { total = (int)s_StartGetting.Invoke(null, null); }
            catch (Exception e) { return new JObject { ["__unavailable"] = "StartGettingEntries failed: " + e.Message }; }

            var collected = new List<JObject>();
            try
            {
                var entry = Activator.CreateInstance(s_LogEntryType, true); // 兼顾非 public 构造
                var getArgs = new object[2];
                for (int i = 0; i < total; i++)
                {
                    getArgs[0] = i;
                    getArgs[1] = entry;
                    try { s_GetEntry.Invoke(null, getArgs); }
                    catch { continue; }

                    int mode = ReadField<int>(entry, s_FMode);
                    string type = Classify(mode);
                    if (!PassesFilter(type, filter)) continue;

                    var item = new JObject
                    {
                        ["message"] = ReadField<string>(entry, s_FMessage),
                        ["type"] = type,
                        ["file"] = ReadField<string>(entry, s_FFile),
                        ["line"] = ReadField<int>(entry, s_FLine),
                        ["instanceID"] = ReadField<int>(entry, s_FInstanceId),
                    };
                    if (includeStack && s_FCallstack != null)
                    {
                        var st = ReadField<string>(entry, s_FCallstack);
                        if (!string.IsNullOrEmpty(st)) item["stackTrace"] = st;
                    }
                    collected.Add(item);
                }
            }
            finally
            {
                try { s_EndGetting.Invoke(null, null); } catch { /* 必须尝试配对 */ }
            }

            // 取最近 count 条（LogEntries 按时间序，末尾最新）
            var arr = new JArray();
            for (int i = Math.Max(0, collected.Count - count); i < collected.Count; i++)
                arr.Add(collected[i]);
            return new JObject { ["count"] = arr.Count, ["total"] = total, ["entries"] = arr };
        }

        private static bool EnsureReflection(out string err)
        {
            err = null;
            if (s_Resolved) return s_StartGetting != null;
            s_Resolved = true;
            try
            {
                if (!TypeResolver.TryResolve("UnityEditor.LogEntries", out var logEntries) ||
                    !TypeResolver.TryResolve("UnityEditor.LogEntry", out s_LogEntryType))
                {
                    err = "UnityEditor.LogEntries/LogEntry type not found";
                    return false;
                }
                const BindingFlags ms = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                const BindingFlags inf = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                s_StartGetting = logEntries.GetMethod("StartGettingEntries", ms);
                s_EndGetting = logEntries.GetMethod("EndGettingEntries", ms);
                s_GetEntry = logEntries.GetMethod("GetEntryInternal", ms);
                s_FMessage = s_LogEntryType.GetField("message", inf);
                s_FFile = s_LogEntryType.GetField("file", inf);
                s_FLine = s_LogEntryType.GetField("line", inf);
                s_FMode = s_LogEntryType.GetField("mode", inf);
                s_FInstanceId = s_LogEntryType.GetField("instanceID", inf);
                s_FCallstack = s_LogEntryType.GetField("callstackText", inf); // 可能不存在，best-effort
                if (s_StartGetting == null || s_EndGetting == null || s_GetEntry == null ||
                    s_FMessage == null || s_FMode == null)
                {
                    err = "required LogEntries member missing on this Unity version";
                    s_StartGetting = null;
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                err = e.GetType().Name + ": " + e.Message;
                s_StartGetting = null;
                return false;
            }
        }

        private static string Classify(int mode)
            => (mode & k_ModeError) != 0 ? "Error" : (mode & k_ModeWarning) != 0 ? "Warning" : "Log";

        private static bool PassesFilter(string type, string filter) => filter switch
        {
            "error" => type == "Error",
            "warning" => type == "Error" || type == "Warning",
            _ => true, // all
        };

        private static T ReadField<T>(object obj, FieldInfo f)
        {
            if (f == null) return default;
            try { return f.GetValue(obj) is T t ? t : default; }
            catch { return default; }
        }

        private static int ReadInt(JObject req, string key, int def)
        {
            var t = req?[key];
            return t != null && t.Type == JTokenType.Integer ? t.Value<int>() : def;
        }
    }
}

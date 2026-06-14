using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Yoji.U3DAILinker.Probe
{
    /// <summary>
    /// 把 ProbeResult 序列化并写入 <projectRoot>/Library/U3DAILinker/probe-result.json。
    /// 路径解析与序列化均为纯逻辑,projectRoot 由调用方注入,便于单测。
    /// </summary>
    public static class ProbeResultWriter
    {
        public const string LibraryDirName = "Library";
        public const string LinkerDirName = "U3DAILinker";
        public const string ResultFileName = "probe-result.json";

        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Include
        };

        /// <summary>计算结果文件绝对路径,不触碰磁盘。</summary>
        public static string ResultPathFor(string projectRoot)
        {
            if (string.IsNullOrEmpty(projectRoot))
            {
                throw new ArgumentException("projectRoot must not be null or empty", nameof(projectRoot));
            }

            return Path.Combine(projectRoot, LibraryDirName, LinkerDirName, ResultFileName);
        }

        /// <summary>把 result 序列化为 JSON。</summary>
        public static string Serialize(ProbeResult result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            return JsonConvert.SerializeObject(result, Settings);
        }

        /// <summary>
        /// 写入结果文件,自动创建缺失目录,覆盖已存在文件。返回写入的绝对路径。
        /// </summary>
        public static string Write(string projectRoot, ProbeResult result)
        {
            string path = ResultPathFor(projectRoot);
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, Serialize(result));
            return path;
        }
    }
}

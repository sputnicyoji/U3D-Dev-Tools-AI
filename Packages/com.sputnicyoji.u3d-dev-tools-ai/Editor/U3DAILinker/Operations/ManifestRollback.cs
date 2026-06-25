using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Yoji.U3DAILinker.Operations
{
    /// 用 operation.json 记录的 backup 整体恢复 manifest。
    /// 恢复前重读当前 manifest，若被事务写入的托管依赖已被手动改动则停，不覆盖用户改动。
    /// 纯文件逻辑，可在 EditMode 用临时目录单测。
    public static class ManifestRollback
    {
        private const string TmpSuffix = ".u3d-ai-linker.tmp";

        public static ManifestTransactionResult Rollback(
            string manifestPath, string stateDir, OperationRecord record)
        {
            var result = new ManifestTransactionResult();

            if (record == null || string.IsNullOrEmpty(record.BackupPath) || !File.Exists(record.BackupPath))
            {
                result.FailureReason = "backup missing: " + (record == null ? "<null record>" : record.BackupPath);
                return result;
            }

            // 重读当前 manifest，检测是否被手动改动。
            JObject current;
            try
            {
                current = JObject.Parse(File.ReadAllText(manifestPath));
            }
            catch (Exception e)
            {
                result.FailureReason = "read/parse current manifest failed: " + e.Message;
                return result;
            }

            var deps = current["dependencies"] as JObject ?? new JObject();
            if (record.DependencyChanges != null)
            {
                foreach (var c in record.DependencyChanges)
                {
                    if (c.NewValue == null) continue;   // Remove 类变更不阻断回滚
                    var nowValue = deps[c.PackageName] != null ? (string)deps[c.PackageName] : null;
                    if (nowValue != c.NewValue)
                    {
                        result.FailureReason =
                            "manifest changed since operation; refuse rollback. package=" + c.PackageName +
                            " expected=" + c.NewValue + " actual=" + (nowValue ?? "<absent>");
                        return result;
                    }
                }
            }

            // 读 backup，经 tmp + 解析校验 + 原子替换写回。
            string backupText;
            try
            {
                backupText = File.ReadAllText(record.BackupPath);
                JObject.Parse(backupText);   // 复验 backup 合法
            }
            catch (Exception e)
            {
                result.FailureReason = "backup not valid json: " + e.Message;
                return result;
            }

            var tmpPath = manifestPath + TmpSuffix;
            try
            {
                File.WriteAllText(tmpPath, backupText);
                JObject.Parse(File.ReadAllText(tmpPath));
                File.Replace(tmpPath, manifestPath, null);
            }
            catch (Exception e)
            {
                SafeDelete(tmpPath);
                result.FailureReason = "restore write failed: " + e.Message;
                return result;
            }

            // 更新 operation.json 状态为 rolledback（失败不致命）。
            record.Status = "rolledback";
            try
            {
                Directory.CreateDirectory(stateDir);
                File.WriteAllText(Path.Combine(stateDir, "operation.json"),
                    JsonConvert.SerializeObject(record, Formatting.Indented));
            }
            catch { /* 记账失败不影响已恢复的 manifest */ }

            result.Committed = true;
            result.Record = record;
            return result;
        }

        private static void SafeDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* 清理失败不致命 */ }
        }
    }
}

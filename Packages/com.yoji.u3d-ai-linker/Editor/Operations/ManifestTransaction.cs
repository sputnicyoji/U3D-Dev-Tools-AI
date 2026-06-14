using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Yoji.U3DAILinker.Operations
{
    /// 把 Packages/manifest.json 当配置文件做的原子事务：保序保留未知字段、backup、只改托管
    /// com.yoji.* 依赖、tmp + 解析校验 + File.Replace 原子替换、写 operation.json 记账。
    /// 纯文件逻辑，路径从构造注入，可在 EditMode 用临时目录单测。
    public sealed class ManifestTransaction
    {
        private const string TmpSuffix = ".u3d-ai-linker.tmp";

        private readonly string m_ManifestPath;
        private readonly string m_StateDir;   // 通常 <project>/Library/U3DAILinker

        public ManifestTransaction(string manifestPath, string stateDir)
        {
            m_ManifestPath = manifestPath;
            m_StateDir = stateDir;
        }

        public ManifestTransactionResult Apply(ManifestPlan plan, bool acceptConflicts)
        {
            var result = new ManifestTransactionResult();

            JObject root;
            try
            {
                root = JObject.Parse(File.ReadAllText(m_ManifestPath));
            }
            catch (Exception e)
            {
                result.FailureReason = "read/parse manifest failed: " + e.Message;
                return result;
            }

            var deps = root["dependencies"] as JObject;
            if (deps == null)
            {
                deps = new JObject();
                root["dependencies"] = deps;
            }

            // 1) 冲突预检 + 计算实际变更（此阶段不写任何文件）。
            var changes = new List<DependencyChange>();
            foreach (var edit in plan.Edits)
            {
                var existing = deps[edit.PackageName] != null ? (string)deps[edit.PackageName] : null;
                var ownership = ManifestUrlClassifier.Classify(edit.PackageName, existing);

                if (ownership == DependencyOwnership.Unmanaged && !acceptConflicts)
                {
                    result.Conflicts.Add(new ManifestConflict
                    {
                        PackageName = edit.PackageName,
                        ExistingValue = existing,
                    });
                    continue;
                }

                if (edit.ChangeType == ManifestChangeType.Remove)
                {
                    if (ownership == DependencyOwnership.Absent)
                        continue;   // 不存在 -> no-op
                    changes.Add(new DependencyChange
                    {
                        PackageName = edit.PackageName,
                        ChangeType = "Remove",
                        OldValue = existing,
                        NewValue = null,
                    });
                }
                else
                {
                    if (existing == edit.NewValue)
                        continue;   // 值未变 -> no-op
                    changes.Add(new DependencyChange
                    {
                        PackageName = edit.PackageName,
                        ChangeType = ownership == DependencyOwnership.Absent ? "Add" : "Update",
                        OldValue = existing,
                        NewValue = edit.NewValue,
                    });
                }
            }

            // 任一冲突 -> 整体拒绝，不写盘。
            if (result.Conflicts.Count > 0)
                return result;

            // 无实际变更 -> 直接 committed，不写 backup/tmp/op log。
            if (changes.Count == 0)
            {
                result.Committed = true;
                result.Record = new OperationRecord
                {
                    OperationId = plan.OperationId,
                    Channel = plan.Channel,
                    Revision = plan.Revision,
                    Status = "committed",
                };
                return result;
            }

            // 2) backup。
            string backupPath;
            try
            {
                var backupDir = Path.Combine(m_StateDir, "backups");
                Directory.CreateDirectory(backupDir);
                backupPath = Path.Combine(backupDir, "manifest-" + plan.OperationId + ".json");
                File.Copy(m_ManifestPath, backupPath, overwrite: true);
            }
            catch (Exception e)
            {
                result.FailureReason = "backup failed: " + e.Message;
                return result;
            }

            // 3) 应用变更到内存 JObject。
            foreach (var c in changes)
            {
                if (c.ChangeType == "Remove") deps.Remove(c.PackageName);
                else deps[c.PackageName] = c.NewValue;
            }

            // 4) 写 tmp。
            var tmpPath = m_ManifestPath + TmpSuffix;
            try
            {
                File.WriteAllText(tmpPath, root.ToString(Formatting.Indented));
            }
            catch (Exception e)
            {
                SafeDelete(tmpPath);
                result.FailureReason = "write tmp failed: " + e.Message;
                return result;
            }

            // 5) 解析 tmp 复验合法 JSON。
            try
            {
                JObject.Parse(File.ReadAllText(tmpPath));
            }
            catch (Exception e)
            {
                SafeDelete(tmpPath);
                result.FailureReason = "tmp not valid json: " + e.Message;
                return result;
            }

            // 6) 原子替换。File.Replace 要求目标已存在；manifest 一定存在（前面已读过）。
            try
            {
                File.Replace(tmpPath, m_ManifestPath, null);
            }
            catch (Exception e)
            {
                SafeDelete(tmpPath);
                result.FailureReason = "atomic replace failed: " + e.Message;
                return result;
            }

            // 7) 写 operation.json 记账。
            var record = new OperationRecord
            {
                OperationId = plan.OperationId,
                Channel = plan.Channel,
                Revision = plan.Revision,
                BackupPath = backupPath,
                Status = "committed",
                DependencyChanges = changes,
            };
            try
            {
                Directory.CreateDirectory(m_StateDir);
                File.WriteAllText(Path.Combine(m_StateDir, "operation.json"),
                    JsonConvert.SerializeObject(record, Formatting.Indented));
            }
            catch
            {
                // 记账失败不回滚已提交的 manifest；保留 backup 供手动恢复。
            }

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

using System;
using System.Collections.Generic;
using System.IO;

namespace Yoji.U3DAILinker.Operations
{
    /// 一次 Agent 资产同步请求。来源定位（PackageInfo.resolvedPath/Agent~ 或 BundledSkills~ / zip 解压）
    /// 由上层完成；此处只要求 SourceDir 是已存在、含全部 RequiredSkillMarkers 的目录。
    internal sealed class AgentSyncRequest
    {
        public string ToolId;                            // Registry 工具 ID
        public string SourceDir;                         // 源 skill 目录（含 SKILL.md/scripts/...）
        public string SourceRevision;                    // 写入 ownership 的来源 revision
        public string OperationId;                       // 本次操作 ID，用于 staging/backup 子目录唯一命名
        public string SkillsRoot;                        // <project>/.u3d-ai-linker
        public IReadOnlyList<string> RequiredSkillMarkers; // 必须存在的相对文件，至少 ["SKILL.md"]
        public IReadOnlyList<string> JunctionLinks;      // 指向最终 skills/<tool> 的链接路径集合
    }

    /// 同步结果。Success=false 时 FailureStage 标明失败阶段：
    /// staging | validate | ownership | replace | junction。
    internal sealed class AgentSyncResult
    {
        public bool Success;
        public string ToolDir;        // 最终 skills/<tool> 路径
        public string ContentHash;    // 成功时的内容哈希
        public string FailureStage;
        public string Message;
    }

    /// 事务式目录替换（spec 306-315）：
    /// 1 复制到 .staging/<tool>-<op>
    /// 2 校验 SKILL.md 等 marker
    /// 3 写 .u3d-ai-owner.json（toolId/revision/hash）
    /// 4 若旧目标存在且 ownership 合法，move 到 .backup/<tool>-<op>
    /// 5 staging move 为 skills/<tool>
    /// 6 建/修 Junction，成功后删 backup
    /// 所有 move 同卷（同在 SkillsRoot 下）。步骤 4 之后失败恢复 backup；步骤 4 之前失败只删 staging。
    /// 目标缺合法 ownership = 用户目录，不覆盖不删除。junction 副作用经 IJunctionManager 注入（测试用 fake）。
    internal sealed class AgentSyncService
    {
        private readonly IJunctionManager m_Junctions;

        public AgentSyncService(IJunctionManager junctions)
        {
            m_Junctions = junctions ?? throw new ArgumentNullException(nameof(junctions));
        }

        public AgentSyncResult Sync(AgentSyncRequest request)
        {
            var toolDir = Path.Combine(request.SkillsRoot, "skills", request.ToolId);
            var stagingDir = Path.Combine(request.SkillsRoot, ".staging", request.ToolId + "-" + request.OperationId);
            var backupDir = Path.Combine(request.SkillsRoot, ".backup", request.ToolId + "-" + request.OperationId);

            // ---- 步骤 1：复制到 staging（步骤 4 之前，失败只清 staging）----
            try
            {
                if (!Directory.Exists(request.SourceDir))
                    return Fail("staging", "source dir missing: " + request.SourceDir, stagingDir, backupDir, false);
                SafeDelete(stagingDir);
                CopyTree(request.SourceDir, stagingDir);
            }
            catch (Exception e)
            {
                return Fail("staging", "staging copy failed: " + e.Message, stagingDir, backupDir, false);
            }

            // ---- 步骤 2：校验 marker（步骤 4 之前）----
            foreach (var marker in request.RequiredSkillMarkers)
            {
                var markerPath = Path.Combine(stagingDir, marker.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(markerPath))
                    return Fail("validate", "required marker missing: " + marker, stagingDir, backupDir, false);
            }

            // ---- ownership 前置校验：目标若是用户目录或别的工具，拒绝（步骤 4 之前）----
            if (Directory.Exists(toolDir) && !OwnershipGuard.MayOverwrite(toolDir, request.ToolId))
                return Fail("ownership", "refusing to overwrite non-managed target: " + toolDir, stagingDir, backupDir, false);

            // ---- 步骤 3：写 ownership（hash 基于 staging 内容；ownership 文件自身被 hash 排除）----
            string hash;
            try
            {
                hash = ContentHash.OfDirectory(stagingDir);
                OwnershipFile.Write(stagingDir, new OwnershipRecord
                {
                    ToolId = request.ToolId,
                    SourceRevision = request.SourceRevision,
                    ContentHash = hash,
                });
            }
            catch (Exception e)
            {
                return Fail("validate", "ownership stamp failed: " + e.Message, stagingDir, backupDir, false);
            }

            // ---- 步骤 4：backup 旧目标（此后失败需恢复 backup）----
            bool backedUp = false;
            try
            {
                if (Directory.Exists(toolDir))
                {
                    SafeDelete(backupDir);
                    Directory.CreateDirectory(Path.GetDirectoryName(backupDir));
                    Directory.Move(toolDir, backupDir);
                    backedUp = true;
                }
            }
            catch (Exception e)
            {
                return Fail("replace", "backup move failed: " + e.Message, stagingDir, backupDir, false);
            }

            // ---- 步骤 5：staging move 为 skills/<tool> ----
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(toolDir));
                Directory.Move(stagingDir, toolDir);
            }
            catch (Exception e)
            {
                RestoreBackup(backedUp, backupDir, toolDir);
                return Fail("replace", "promote staging failed: " + e.Message, stagingDir, backupDir, backedUp);
            }

            // ---- 步骤 6：建/修 Junction，成功后删 backup ----
            try
            {
                foreach (var link in request.JunctionLinks)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(link));
                    if (m_Junctions.IsJunction(link))
                    {
                        if (m_Junctions.GetTarget(link) != toolDir)
                        {
                            m_Junctions.Delete(link);
                            m_Junctions.Create(link, toolDir);
                        }
                    }
                    else
                    {
                        if (Directory.Exists(link)) SafeDelete(link); // 空壳/旧目录，避免 Create 撞名
                        m_Junctions.Create(link, toolDir);
                    }
                }
            }
            catch (Exception e)
            {
                // junction 失败：把新目标退回 staging 名，恢复旧 backup
                try { SafeDelete(stagingDir); Directory.Move(toolDir, stagingDir); } catch { }
                RestoreBackup(backedUp, backupDir, toolDir);
                SafeDelete(stagingDir);
                return new AgentSyncResult { Success = false, FailureStage = "junction", Message = e.Message, ToolDir = toolDir };
            }

            SafeDelete(backupDir);
            return new AgentSyncResult { Success = true, ToolDir = toolDir, ContentHash = hash };
        }

        private static void RestoreBackup(bool backedUp, string backupDir, string toolDir)
        {
            if (!backedUp) return;
            try
            {
                if (Directory.Exists(toolDir)) SafeDelete(toolDir);
                Directory.Move(backupDir, toolDir);
            }
            catch { /* 恢复尽力而为；FailureStage 已告知调用方需人工介入 */ }
        }

        private AgentSyncResult Fail(string stage, string message, string stagingDir, string backupDir, bool backedUp)
        {
            // Fail 仅在步骤 4（promote）之前调用：此时只有 staging 需清理，现有目标未动；
            // backup 的恢复由 RestoreBackup 负责，不在此处。
            SafeDelete(stagingDir);
            return new AgentSyncResult { Success = false, FailureStage = stage, Message = message };
        }

        private static void CopyTree(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(dir.Replace(src, dst));
            foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                File.Copy(file, file.Replace(src, dst), true);
        }

        private static void SafeDelete(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Yoji.U3DAILinker.Operations;
using Yoji.U3DAILinker.Registry;

namespace Yoji.U3DAILinker.Settings
{
    internal sealed class U3DAILinkerActionResult
    {
        public bool Success;
        public string Error;
        public QueueStepResult? QueueResult;
        public ManifestTransactionResult ManifestResult;

        public static U3DAILinkerActionResult Ok(
            ManifestTransactionResult manifestResult,
            QueueStepResult? queueResult = null)
        {
            return new U3DAILinkerActionResult
            {
                Success = true,
                ManifestResult = manifestResult,
                QueueResult = queueResult,
            };
        }

        public static U3DAILinkerActionResult Fail(string error, ManifestTransactionResult manifestResult = null)
        {
            return new U3DAILinkerActionResult
            {
                Success = false,
                Error = error,
                ManifestResult = manifestResult,
            };
        }
    }

    /// Orchestrates settings-panel actions using the existing manifest transaction and UPM queue primitives.
    internal sealed class U3DAILinkerActionService
    {
        private readonly string m_ProjectRoot;
        private readonly IUpmClient m_Upm;
        private readonly IInstalledPackageProbe m_Probe;
        private readonly string m_DevSha;

        public U3DAILinkerActionService(
            string projectRoot,
            IUpmClient upm,
            IInstalledPackageProbe probe,
            string devSha = null)
        {
            m_ProjectRoot = projectRoot;
            m_Upm = upm;
            m_Probe = probe;
            m_DevSha = devSha;
        }

        public U3DAILinkerActionResult InstallOrUpdateSelected(
            LinkerRegistry registry,
            U3DAILinkerSettings settings,
            U3DAILinkerUserSettings userSettings,
            bool acceptConflicts)
        {
            if (settings == null)
                return U3DAILinkerActionResult.Fail("settings missing");

            return InstallOrUpdate(
                "install-selected",
                registry,
                settings,
                userSettings,
                settings.EnabledToolIds,
                acceptConflicts);
        }

        public U3DAILinkerActionResult InstallOrUpdateAll(
            LinkerRegistry registry,
            U3DAILinkerSettings settings,
            U3DAILinkerUserSettings userSettings,
            bool acceptConflicts)
        {
            if (registry == null)
                return U3DAILinkerActionResult.Fail("registry missing");

            var enabled = registry.Entries
                .Where(e => e.Kind != ToolKind.Infra && e.UserToggle)
                .Select(e => e.Id)
                .ToArray();

            return InstallOrUpdate(
                "install-all",
                registry,
                settings,
                userSettings,
                enabled,
                acceptConflicts);
        }

        public U3DAILinkerActionResult RestoreToChannel(
            LinkerRegistry targetRegistry,
            LinkerChannel targetChannel,
            string devSha,
            IReadOnlyDictionary<string, InstalledPackageInfo> installed,
            bool acceptConflicts)
        {
            if (targetRegistry == null)
                return U3DAILinkerActionResult.Fail("registry missing");

            var changes = RestorePlanner.BuildRestore(installed, targetRegistry, devSha);
            var plan = new ManifestPlan(NewOperationId(), ChannelName(targetChannel), devSha ?? targetRegistry.Branch)
            {
                Edits = changes
                    .Select(c => new ManifestEdit(c.PackageName, ManifestChangeType.Update, c.NewValue))
                    .ToList(),
            };

            var tx = NewTransaction();
            var txResult = tx.Apply(plan, acceptConflicts);
            if (!txResult.Committed)
                return U3DAILinkerActionResult.Fail(DescribeFailure(txResult), txResult);

            SaveLastManifestRecord(txResult.Record);
            return U3DAILinkerActionResult.Ok(txResult);
        }

        public U3DAILinkerActionResult RollbackManifest()
        {
            var stateDir = StateDir;
            var logPath = Path.Combine(stateDir, "operation.json");
            var recordPath = File.Exists(logPath) ? logPath : LastManifestRecordPath;
            if (!File.Exists(recordPath))
                return U3DAILinkerActionResult.Fail("operation log missing");

            var text = File.ReadAllText(recordPath);
            var record = TryReadOperationRecord(text);
            if (record == null)
                return U3DAILinkerActionResult.Fail("operation log does not contain a manifest backup");

            var result = ManifestRollback.Rollback(ManifestPath, stateDir, record);
            if (!result.Committed)
                return U3DAILinkerActionResult.Fail(DescribeFailure(result), result);
            return U3DAILinkerActionResult.Ok(result);
        }

        private U3DAILinkerActionResult InstallOrUpdate(
            string action,
            LinkerRegistry registry,
            U3DAILinkerSettings settings,
            U3DAILinkerUserSettings userSettings,
            IReadOnlyCollection<string> enabledToolIds,
            bool acceptConflicts)
        {
            if (registry == null)
                return U3DAILinkerActionResult.Fail("registry missing");
            if (settings == null)
                return U3DAILinkerActionResult.Fail("settings missing");
            if (userSettings == null)
                return U3DAILinkerActionResult.Fail("user settings missing");

            IReadOnlyList<ResolvedTool> resolved;
            IReadOnlyList<QueueItem> queue;
            try
            {
                resolved = ResolveTools(registry, settings.Channel, userSettings.LocalRepoRoot);
                queue = new InstallQueueBuilder().Build(resolved, enabledToolIds ?? Array.Empty<string>());
            }
            catch (Exception e)
            {
                return U3DAILinkerActionResult.Fail(e.Message);
            }
            if (queue.Count == 0)
                return U3DAILinkerActionResult.Fail(
                    "no installable ready tools for channel " + ChannelName(settings.Channel));

            var operationId = NewOperationId();
            var plan = new ManifestPlan(operationId, ChannelName(settings.Channel), RevisionLabel(settings.Channel, registry))
            {
                Edits = queue
                    .Select(item => new ManifestEdit(item.PackageName, ManifestChangeType.Update, item.PackageUrl))
                    .ToList(),
            };

            var txResult = NewTransaction().Apply(plan, acceptConflicts);
            if (!txResult.Committed)
                return U3DAILinkerActionResult.Fail(DescribeFailure(txResult), txResult);

            if (txResult.Record.DependencyChanges == null || txResult.Record.DependencyChanges.Count == 0)
                return U3DAILinkerActionResult.Ok(txResult);

            SaveLastManifestRecord(txResult.Record);
            var log = new OperationLog
            {
                OperationId = operationId,
                Action = action,
                ToolIds = enabledToolIds.ToArray(),
                CurrentIndex = 0,
                Phase = OperationPhase.Pending,
                Channel = ChannelName(settings.Channel),
                ResolvedRevision = plan.Revision,
                ManifestBackupPath = txResult.Record.BackupPath,
                DependencyChanges = txResult.Record.DependencyChanges,
                Completed = new List<string>(),
                RetryCount = 0,
            };

            var queueResult = NewQueueRunner().Advance(log);
            return queueResult == QueueStepResult.Faulted
                ? U3DAILinkerActionResult.Fail("UPM Add failed", txResult)
                : U3DAILinkerActionResult.Ok(txResult, queueResult);
        }

        private IReadOnlyList<ResolvedTool> ResolveTools(
            LinkerRegistry registry,
            LinkerChannel channel,
            string localRepoRoot)
        {
            return registry.Entries
                .OrderBy(e => e.Order)
                .Select(e => new ResolvedTool
                {
                    ToolId = e.Id,
                    PackageName = e.PackageName,
                    Kind = KindName(e.Kind),
                    Status = StatusName(e.Status),
                    DependsOn = e.DependsOn != null ? e.DependsOn.ToArray() : Array.Empty<string>(),
                    IsLinker = e.Kind == ToolKind.Linker || e.PackageName == U3DAILinkerPackage.PackageName,
                    PackageUrl = BuildPackageUrl(e, channel, localRepoRoot),
                })
                .ToArray();
        }

        private string BuildPackageUrl(RegistryEntryView entry, LinkerChannel channel, string localRepoRoot)
        {
            switch (channel)
            {
                case LinkerChannel.Local:
                    return ToolUrlBuilder.BuildLocalFile(localRepoRoot, entry.PackagePath);
                case LinkerChannel.Dev:
                    return ToolUrlBuilder.BuildDevSha(entry.PackagePath, RequireDevSha());
                default:
                    return ToolUrlBuilder.BuildStable(entry.PackagePath, entry.Revision);
            }
        }

        private ManifestTransaction NewTransaction()
        {
            return new ManifestTransaction(ManifestPath, StateDir);
        }

        private UpmQueueRunner NewQueueRunner()
        {
            return new UpmQueueRunner(new OperationLogStore(LibraryRoot), m_Upm, m_Probe);
        }

        private string ManifestPath => Path.Combine(m_ProjectRoot, "Packages", "manifest.json");
        private string LibraryRoot => Path.Combine(m_ProjectRoot, "Library");
        private string StateDir => Path.Combine(LibraryRoot, "U3DAILinker");
        private string LastManifestRecordPath => Path.Combine(StateDir, "last-manifest-operation.json");

        private void SaveLastManifestRecord(OperationRecord record)
        {
            if (record == null || string.IsNullOrEmpty(record.BackupPath))
                return;
            Directory.CreateDirectory(StateDir);
            File.WriteAllText(LastManifestRecordPath, JsonConvert.SerializeObject(record, Formatting.Indented));
        }

        private string RequireDevSha()
        {
            if (string.IsNullOrEmpty(m_DevSha) || IsAllZeroSha(m_DevSha))
                throw new InvalidOperationException("Dev channel requires a resolved commit SHA. Refresh Registry first.");
            return m_DevSha;
        }

        private static bool IsAllZeroSha(string value)
        {
            if (value == null || value.Length != 40)
                return false;
            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] != '0')
                    return false;
            }
            return true;
        }

        private static OperationRecord TryReadOperationRecord(string json)
        {
            try
            {
                var record = JsonConvert.DeserializeObject<OperationRecord>(json);
                if (record != null && !string.IsNullOrEmpty(record.BackupPath))
                    return record;
            }
            catch
            {
                // Try OperationLog below.
            }

            try
            {
                var log = JsonConvert.DeserializeObject<OperationLog>(json);
                if (log == null || string.IsNullOrEmpty(log.ManifestBackupPath))
                    return null;
                return new OperationRecord
                {
                    OperationId = log.OperationId,
                    Channel = log.Channel,
                    Revision = log.ResolvedRevision,
                    BackupPath = log.ManifestBackupPath,
                    Status = log.Phase,
                    DependencyChanges = log.DependencyChanges,
                };
            }
            catch
            {
                return null;
            }
        }

        private static string DescribeFailure(ManifestTransactionResult result)
        {
            if (result == null)
                return "operation failed";
            if (!string.IsNullOrEmpty(result.FailureReason))
                return result.FailureReason;
            if (result.Conflicts != null && result.Conflicts.Count > 0)
                return "manifest conflict: " + string.Join(", ", result.Conflicts.Select(c => c.PackageName));
            return "operation failed";
        }

        private static string RevisionLabel(LinkerChannel channel, LinkerRegistry registry)
        {
            if (channel == LinkerChannel.Local)
                return "local";
            return registry.Branch ?? channel.ToString();
        }

        private static string NewOperationId()
        {
            return DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N");
        }

        private static string ChannelName(LinkerChannel channel)
        {
            return channel.ToString().ToLowerInvariant();
        }

        private static string KindName(ToolKind kind)
        {
            return kind == ToolKind.Infra ? "infra" : "tool";
        }

        private static string StatusName(ToolStatus status)
        {
            switch (status)
            {
                case ToolStatus.Ready:
                    return "ready";
                case ToolStatus.SkillOnly:
                    return "skill-only";
                default:
                    return "planned";
            }
        }
    }
}

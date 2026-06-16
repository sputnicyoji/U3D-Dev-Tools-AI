using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Yoji.U3DAILinker.Operations;
using Yoji.U3DAILinker.Registry;

namespace Yoji.U3DAILinker.Settings
{
    /// Project Settings 入口 Project/U3D AI Linker。
    /// 渲染顶部状态区 / 工具列表 / 底部操作区。所有状态计算委托 PanelStateModel 等纯逻辑类;
    /// 真实 UPM/Junction 动作由其它子系统接入(此处仅触发其入口)。
    internal static class U3DAILinkerSettingsProvider
    {
        internal const string ProviderPath = U3DAILinkerPackage.SettingsPath;
        internal const bool ActionsWired = true;

        private static U3DAILinkerSettings _project;
        private static U3DAILinkerUserSettings _user;
        private static Vector2 _scroll;

        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            return new SettingsProvider(ProviderPath, SettingsScope.Project)
            {
                label = U3DAILinkerPackage.DisplayName,
                guiHandler = OnGui,
                keywords = new[] { "linker", "agent", "skill", "upm", "registry" },
                activateHandler = (searchContext, root) =>
                {
                    _project = U3DAILinkerSettingsStore.LoadOrCreateProjectSettings();
                    _user = U3DAILinkerSettingsStore.LoadOrCreateUserSettings();
                },
            };
        }

        private static void OnGui(string searchContext)
        {
            if (_project == null)
                _project = U3DAILinkerSettingsStore.LoadOrCreateProjectSettings();
            if (_user == null)
                _user = U3DAILinkerSettingsStore.LoadOrCreateUserSettings();

            DrawStatusSection();
            EditorGUILayout.Space();
            DrawToolList();
            EditorGUILayout.Space();
            DrawLocalChannelWarning();
            EditorGUILayout.Space();
            DrawActionSection();
        }

        private static void DrawStatusSection()
        {
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                var newChannel = (LinkerChannel)EditorGUILayout.EnumPopup("Channel", _project.Channel);
                if (newChannel != _project.Channel)
                {
                    _project.Channel = newChannel;
                    _registry = null;
                    _currentRevision = null;
                    _devSha = null;
                    _installed.Clear();
                    _agentStates.Clear();
                    if (U3DAILinkerSettingsStore.TrySaveProjectSettings(_project, out var error))
                    {
                        // saved
                    }
                    else
                    {
                        Debug.LogError("[U3DAILinker] " + error);
                    }
                }

                EditorGUILayout.LabelField("Registry Source", _registrySource.ToString());
                EditorGUILayout.LabelField("Schema Version",
                    _registry != null ? _registry.SchemaVersion.ToString() : "-");
                EditorGUILayout.LabelField("Current Revision", _currentRevision ?? "-");
                EditorGUILayout.LabelField("Operation", _operationState.ToString());

                EditorGUILayout.LabelField("Git Available", _gitAvailable ? "yes" : "no");
                EditorGUILayout.LabelField("Network Available", _networkAvailable ? "yes" : "no");
                EditorGUILayout.LabelField("Package Manager", _upmIdle ? "idle" : "busy");
            }

            if (_project.Channel == LinkerChannel.Local)
            {
                var newRoot = EditorGUILayout.TextField("Local Repo Root", _user.LocalRepoRoot);
                if (newRoot != _user.LocalRepoRoot)
                {
                    _user.LocalRepoRoot = newRoot;
                    U3DAILinkerSettingsStore.SaveUserSettings(_user);
                }
            }
        }

        private static void DrawToolList()
        {
            EditorGUILayout.LabelField("Tools", EditorStyles.boldLabel);
            if (_registry == null)
            {
                EditorGUILayout.HelpBox("Registry not loaded. Press Refresh Registry.", MessageType.Info);
                return;
            }

            var rows = PanelStateModel.Build(
                _registry, _installed, _project.EnabledToolIds, _agentStates,
                _project.Channel, _user.LocalRepoRoot);

            using (var scope = new EditorGUILayout.ScrollViewScope(_scroll))
            {
                _scroll = scope.scrollPosition;
                foreach (var row in rows)
                    DrawToolRow(row);
            }
        }

        private static void DrawToolRow(ToolRow row)
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (row.EnabledVisible)
                    {
                        var enabled = EditorGUILayout.Toggle(row.Enabled, GUILayout.Width(20));
                        if (enabled != row.Enabled)
                            ToggleTool(row.Id, enabled);
                    }
                    else
                    {
                        GUILayout.Space(24);
                    }

                    EditorGUILayout.LabelField(row.DisplayName, GUILayout.Width(160));
                    EditorGUILayout.LabelField(row.Kind.ToString(), GUILayout.Width(60));
                    EditorGUILayout.LabelField(row.Status.ToString(), GUILayout.Width(70));
                    EditorGUILayout.LabelField(row.Installed.ToString(), GUILayout.Width(80));
                    EditorGUILayout.LabelField(row.Agent.ToString(), GUILayout.Width(90));

                    if (row.Kind == ToolKind.Infra && row.RequiredBy != null && row.RequiredBy.Length > 0)
                        EditorGUILayout.LabelField("req by: " + string.Join(",", row.RequiredBy));
                }

                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.LabelField("Current", string.IsNullOrEmpty(row.Current) ? "-" : row.Current);
                    EditorGUILayout.LabelField("Target", string.IsNullOrEmpty(row.Desired) ? "-" : row.Desired);
                    EditorGUILayout.LabelField("Current Hash", string.IsNullOrEmpty(row.CurrentHash) ? "-" : row.CurrentHash);
                    EditorGUILayout.LabelField("Target Hash", string.IsNullOrEmpty(row.ExpectedHash) ? "-" : row.ExpectedHash);
                }
            }
        }

        private static void DrawLocalChannelWarning()
        {
            if (_project.Channel != LinkerChannel.Local)
                return;

            EditorGUILayout.HelpBox(
                "Local channel writes file: dependencies into Packages/manifest.json. " +
                "These must not be committed. Absolute paths live only in UserSettings.",
                MessageType.Warning);

            if (RestorePlanner.HasLocalFileDependencies(_installed))
            {
                EditorGUILayout.HelpBox(
                    "manifest.json currently contains managed file: dependencies.",
                    MessageType.Warning);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Restore Stable"))
                    RequestRestore(LinkerChannel.Stable);
                using (new EditorGUI.DisabledScope(_devSha == null))
                {
                    if (GUILayout.Button("Restore Dev"))
                        RequestRestore(LinkerChannel.Dev);
                }
            }
        }

        private static void DrawActionSection()
        {
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Install/Update, Restore, Rollback, Agent asset sync, junction repair, folder reveal, and diagnostics are wired.",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(!AreActionButtonsEnabled(_operationState)))
            using (new EditorGUILayout.VerticalScope())
            {
                if (GUILayout.Button("Refresh Registry")) RequestRefreshRegistry();
                if (GUILayout.Button("Install/Update Selected")) RequestInstallSelected();
                if (GUILayout.Button("Install/Update All")) RequestInstallAll();
                using (new EditorGUI.DisabledScope(!AreAgentButtonsEnabled(_operationState, _registry != null)))
                {
                    if (GUILayout.Button("Sync Agent Assets")) RequestSyncAgents();
                    if (GUILayout.Button("Repair Links")) RequestRepairLinks();
                }
                if (GUILayout.Button("Rollback Manifest")) RequestRollback();
                if (GUILayout.Button("Open Generated Folder")) RequestOpenFolder();
                if (GUILayout.Button("Copy Diagnostic Report")) RequestCopyDiagnostics();
            }
        }

        internal static bool AreActionButtonsEnabled(OperationState state)
        {
            return ActionsWired && state != OperationState.Running;
        }

        internal static bool AreAgentButtonsEnabled(OperationState state, bool registryLoaded)
        {
            return ActionsWired && registryLoaded && state != OperationState.Running;
        }

        // --- 外部子系统注入点(由组装阶段接线;此切片提供默认空实现以保证编译与手动验证)。---

        private static LinkerRegistry _registry;
        private static RegistrySource _registrySource = RegistrySource.BundledSnapshot;
        private static OperationState _operationState = OperationState.Idle;
        private static string _currentRevision;
        private static string _devSha;
        private static bool _gitAvailable = true;
        private static bool _networkAvailable = true;
        private static bool _upmIdle = true;

        private static readonly Dictionary<string, InstalledPackageInfo> _installed =
            new Dictionary<string, InstalledPackageInfo>();
        private static readonly Dictionary<string, AgentState> _agentStates =
            new Dictionary<string, AgentState>();
        private static readonly Dictionary<string, string> _expectedHashesByPackageName =
            new Dictionary<string, string>();

        private static void ToggleTool(string toolId, bool enabled)
        {
            if (enabled)
            {
                if (!_project.EnabledToolIds.Contains(toolId))
                    _project.EnabledToolIds.Add(toolId);
            }
            else
            {
                _project.EnabledToolIds.Remove(toolId);
            }

            if (!U3DAILinkerSettingsStore.TrySaveProjectSettings(_project, out var error))
                Debug.LogError("[U3DAILinker] " + error);
        }

        private static void RequestRestore(LinkerChannel target)
        {
            if (!TryLoadRegistryForChannel(target, out var targetRegistry, out var error))
            {
                Debug.LogError("[U3DAILinker] " + error);
                return;
            }

            var result = NewActionService().RestoreToChannel(
                targetRegistry,
                target,
                _devSha,
                _installed,
                acceptConflicts: false);
            ReportAction("Restore " + target, result);
            if (result != null && result.Success)
                ApplyRestoredChannel(target, targetRegistry);
        }

        private static void RequestRefreshRegistry()
        {
            if (TryLoadBundledRegistry(RegistryChannelForCurrentProject(), out var registry, out var error))
            {
                _registry = registry;
                _registrySource = RegistrySource.BundledSnapshot;
                if (_project != null && _project.Channel == LinkerChannel.Dev)
                {
                    if (!TryResolveDevSha(registry, out _devSha, out error))
                    {
                        Debug.LogError("[U3DAILinker] " + error);
                        return;
                    }
                    _currentRevision = _devSha;
                }
                else
                {
                    _devSha = null;
                    _currentRevision = registry.Branch ?? registry.Channel.ToString();
                }
                RefreshExpectedHashes(_registry, _project != null ? _project.Channel : LinkerChannel.Stable, _devSha);
                RefreshInstalledSnapshot(_registry, new UnityInstalledPackageProbe());
                Debug.Log("[U3DAILinker] Registry refreshed: " + _registry.Entries.Count + " entries.");
            }
            else
            {
                Debug.LogError("[U3DAILinker] " + error);
            }
        }

        private static void RequestInstallSelected()
        {
            if (!EnsureRegistryLoaded())
                return;
            var result = NewActionService().InstallOrUpdateSelected(
                _registry,
                _project,
                _user,
                acceptConflicts: false);
            ReportAction("Install/Update Selected", result);
        }

        private static void RequestInstallAll()
        {
            if (!EnsureRegistryLoaded())
                return;
            var result = NewActionService().InstallOrUpdateAll(
                _registry,
                _project,
                _user,
                acceptConflicts: false);
            ReportAction("Install/Update All", result);
        }

        private static void RequestSyncAgents()
        {
            if (!EnsureRegistryLoaded())
                return;

            try
            {
                _operationState = OperationState.Running;
                var targets = AgentPackageSourceResolver.Resolve(
                    _registry,
                    _project.EnabledToolIds,
                    new UnityResolvedPackagePathProvider());
                var result = new AgentSyncOrchestrator(new WindowsJunctionManager()).Sync(
                    ProjectRoot,
                    targets,
                    NewOperationId());
                if (!result.Success)
                {
                    _operationState = OperationState.Failed;
                    Debug.LogError("[U3DAILinker] Sync Agent Assets failed: " + result.Error);
                    return;
                }

                _operationState = OperationState.Idle;
                Debug.Log("[U3DAILinker] Sync Agent Assets succeeded: " + targets.Count + " tools.");
            }
            catch (System.Exception e)
            {
                _operationState = OperationState.Failed;
                Debug.LogError("[U3DAILinker] Sync Agent Assets failed: " + e.Message);
            }
        }

        private static void RequestRepairLinks()
        {
            RequestSyncAgents();
        }

        private static string NewOperationId()
        {
            return System.DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + System.Guid.NewGuid().ToString("N");
        }
        private static void RequestRollback()
        {
            var result = NewActionService().RollbackManifest();
            ReportAction("Rollback Manifest", result);
        }

        private static void RequestOpenFolder()
        {
            var path = Path.Combine(ProjectRoot, ".u3d-ai-linker");
            if (!Directory.Exists(path))
            {
                Debug.LogWarning("[U3DAILinker] Generated folder does not exist yet: " + path);
                return;
            }
            EditorUtility.RevealInFinder(path);
        }

        private static void RequestCopyDiagnostics()
        {
            var report = BuildDiagnosticReport(
                _project,
                _user,
                _registry,
                _installed,
                _agentStates,
                _registrySource,
                _operationState,
                _currentRevision,
                _devSha,
                ProjectRoot,
                PackageRoot);
            EditorGUIUtility.systemCopyBuffer = report;
            Debug.Log("[U3DAILinker] Diagnostic report copied to clipboard.");
        }

        private static bool EnsureRegistryLoaded()
        {
            if (_registry != null)
                return true;
            RequestRefreshRegistry();
            return _registry != null;
        }

        private static U3DAILinkerActionService NewActionService()
        {
            return new U3DAILinkerActionService(
                ProjectRoot,
                new UnityUpmClient(),
                new UnityInstalledPackageProbe(),
                _devSha);
        }

        private static void ApplyRestoredChannel(LinkerChannel target, LinkerRegistry targetRegistry)
        {
            _project.Channel = target;
            _registry = targetRegistry;
            _registry.Channel = target;
            if (target != LinkerChannel.Dev)
                _devSha = null;
            _currentRevision = target == LinkerChannel.Dev
                ? _devSha
                : targetRegistry.Branch ?? targetRegistry.Channel.ToString();

            if (!U3DAILinkerSettingsStore.TrySaveProjectSettings(_project, out var error))
                Debug.LogError("[U3DAILinker] " + error);

            RefreshExpectedHashes(_registry, target, _devSha);
            RefreshInstalledSnapshot(_registry, new UnityInstalledPackageProbe());
        }

        internal static Dictionary<string, InstalledPackageInfo> BuildInstalledSnapshot(
            LinkerRegistry registry,
            IInstalledPackageProbe probe,
            IReadOnlyDictionary<string, string> expectedHashes = null)
        {
            var result = new Dictionary<string, InstalledPackageInfo>();
            if (registry == null || probe == null)
                return result;

            foreach (var entry in registry.Entries)
            {
                var expectedHash = expectedHashes != null && expectedHashes.TryGetValue(entry.PackageName, out var hash)
                    ? hash
                    : null;
                var info = BuildInstalledPackageInfo(probe, entry.PackageName, expectedHash);
                if (info != null && !string.IsNullOrEmpty(info.ResolvedUrl))
                    result[entry.PackageName] = info;
            }

            return result;
        }

        private static InstalledPackageInfo BuildInstalledPackageInfo(
            IInstalledPackageProbe probe,
            string packageName,
            string expectedHash)
        {
            if (probe is UnityInstalledPackageProbe unityProbe)
                return unityProbe.GetInstalledPackageInfo(packageName, expectedHash);

            var url = probe.GetInstalledUrl(packageName);
            return string.IsNullOrEmpty(url)
                ? null
                : new InstalledPackageInfo(packageName, url, ToolUrlBuilder.IsManagedUrl(url))
                {
                    ExpectedHash = expectedHash,
                };
        }

        private static void RefreshInstalledSnapshot(LinkerRegistry registry, IInstalledPackageProbe probe)
        {
            _installed.Clear();
            foreach (var kv in BuildInstalledSnapshot(registry, probe, _expectedHashesByPackageName))
                _installed[kv.Key] = kv.Value;
        }

        private static void RefreshExpectedHashes(
            LinkerRegistry registry,
            LinkerChannel channel,
            string devSha)
        {
            _expectedHashesByPackageName.Clear();
            if (registry == null)
                return;

            if (channel == LinkerChannel.Dev)
            {
                if (string.IsNullOrEmpty(devSha))
                    return;
                foreach (var entry in registry.Entries.Where(e => e.Status == ToolStatus.Ready))
                    _expectedHashesByPackageName[entry.PackageName] = devSha;
                return;
            }

            if (channel != LinkerChannel.Stable)
                return;

            var resolver = new GitLsRemoteResolver(ToolUrlBuilder.RepoUrl);
            foreach (var entry in registry.Entries.Where(e => e.Status == ToolStatus.Ready))
            {
                try
                {
                    _expectedHashesByPackageName[entry.PackageName] = resolver.ResolveTagSha(entry.Revision);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[U3DAILinker] resolve stable tag hash failed for " +
                                     entry.Id + " (" + entry.Revision + "): " + e.Message);
                }
            }
        }

        private static void ReportAction(string action, U3DAILinkerActionResult result)
        {
            if (result == null)
            {
                Debug.LogError("[U3DAILinker] " + action + " failed: no result");
                return;
            }

            if (!result.Success)
            {
                _operationState = OperationState.Failed;
                Debug.LogError("[U3DAILinker] " + action + " failed: " + result.Error);
                return;
            }

            _operationState = result.QueueResult == QueueStepResult.Requested
                ? OperationState.Running
                : OperationState.Idle;

            if (_registry != null)
                RefreshInstalledSnapshot(_registry, new UnityInstalledPackageProbe());

            var queue = result.QueueResult.HasValue ? " queue=" + result.QueueResult.Value : string.Empty;
            Debug.Log("[U3DAILinker] " + action + " succeeded." + queue);
        }

        internal static string BuildDiagnosticReport(
            U3DAILinkerSettings project,
            U3DAILinkerUserSettings user,
            LinkerRegistry registry,
            IReadOnlyDictionary<string, InstalledPackageInfo> installed,
            IReadOnlyDictionary<string, AgentState> agentStates,
            RegistrySource registrySource,
            OperationState operationState,
            string currentRevision,
            string devSha,
            string projectRoot,
            string packageRoot)
        {
            var sb = new StringBuilder();
            sb.AppendLine("U3D AI Linker Diagnostic Report");
            sb.AppendLine("GeneratedUtc: " + System.DateTime.UtcNow.ToString("o"));
            sb.AppendLine("ProjectRoot: " + (projectRoot ?? "-"));
            sb.AppendLine("PackageRoot: " + (packageRoot ?? "-"));
            sb.AppendLine("ProviderPath: " + ProviderPath);
            sb.AppendLine("Channel: " + (project != null ? project.Channel.ToString() : "-"));
            sb.AppendLine("EnabledToolIds: " + (project != null ? string.Join(",", project.EnabledToolIds) : "-"));
            sb.AppendLine("LocalRepoRootSet: " + (user != null && !string.IsNullOrEmpty(user.LocalRepoRoot) ? "yes" : "no"));
            sb.AppendLine("RegistrySource: " + registrySource);
            sb.AppendLine("RegistryEntries: " + (registry != null ? registry.Entries.Count.ToString() : "-"));
            sb.AppendLine("CurrentRevision: " + (currentRevision ?? "-"));
            sb.AppendLine("DevSha: " + (devSha ?? "-"));
            sb.AppendLine("Operation: " + operationState);
            sb.AppendLine("SelfPackage: " + BuildSelfPackageSummary());
            sb.AppendLine();

            if (registry == null)
            {
                sb.AppendLine("Rows: registry not loaded");
                return sb.ToString();
            }

            var rows = PanelStateModel.Build(
                registry,
                installed,
                project != null ? project.EnabledToolIds : null,
                agentStates,
                project != null ? project.Channel : LinkerChannel.Stable,
                user != null ? user.LocalRepoRoot : null);

            sb.AppendLine("Rows:");
            foreach (var row in rows)
            {
                sb.AppendLine("- " + row.Id +
                              " kind=" + row.Kind +
                              " status=" + row.Status +
                              " enabled=" + row.Enabled +
                              " install=" + row.Installed +
                              " agent=" + row.Agent);
                sb.AppendLine("  current=" + (row.Current ?? "-"));
                sb.AppendLine("  target=" + (row.Desired ?? "-"));
                sb.AppendLine("  currentHash=" + (row.CurrentHash ?? "-"));
                sb.AppendLine("  targetHash=" + (row.ExpectedHash ?? "-"));
            }
            return sb.ToString();
        }

        private static string BuildSelfPackageSummary()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                typeof(U3DAILinkerPackage).Assembly);
            if (info == null)
                return "-";

            var git = ReadMember(info, "git");
            var hash = ReadMember(git, "hash") ?? "-";
            var revision = ReadMember(git, "revision") ?? "-";
            return "name=" + info.name +
                   " source=" + info.source +
                   " packageId=" + info.packageId +
                   " resolvedPath=" + info.resolvedPath +
                   " gitHash=" + hash +
                   " gitRevision=" + revision;
        }

        private static string ReadMember(object instance, string name)
        {
            if (instance == null)
                return null;
            var type = instance.GetType();
            var prop = type.GetProperty(name);
            if (prop != null)
                return prop.GetValue(instance, null) as string;
            var field = type.GetField(name);
            return field != null ? field.GetValue(instance) as string : null;
        }

        private static bool TryLoadBundledRegistry(
            RegistryChannel channel,
            out LinkerRegistry registry,
            out string error)
        {
            registry = null;
            error = null;

            var fileName = channel == RegistryChannel.Dev ? "dev.json" : "stable.json";
            var path = Path.Combine(PackageRoot, "Registry", fileName);
            if (!File.Exists(path))
            {
                error = "bundled registry missing: " + path;
                return false;
            }

            try
            {
                var doc = RegistryParser.Parse(File.ReadAllText(path));
                RegistryValidator.Validate(doc, channel);
                registry = ToRegistryView(doc, _project != null ? _project.Channel : LinkerChannel.Stable);
                return true;
            }
            catch (System.Exception e)
            {
                error = "load bundled registry failed: " + e.Message;
                return false;
            }
        }

        private static bool TryLoadRegistryForChannel(
            LinkerChannel channel,
            out LinkerRegistry registry,
            out string error)
        {
            registry = null;
            error = null;
            var registryChannel = channel == LinkerChannel.Stable
                ? RegistryChannel.Stable
                : RegistryChannel.Dev;

            if (!TryLoadBundledRegistry(registryChannel, out registry, out error))
                return false;

            registry.Channel = channel;
            if (channel == LinkerChannel.Dev && !TryResolveDevSha(registry, out _devSha, out error))
                return false;

            return true;
        }

        internal static bool TryLoadBundledRegistryForCurrentProject(
            LinkerChannel channel,
            out LinkerRegistry registry,
            out string error)
        {
            var registryChannel = RegistryChannelForProject(channel);
            if (!TryLoadBundledRegistry(registryChannel, out registry, out error))
                return false;
            registry.Channel = channel;
            return true;
        }

        private static bool TryResolveDevSha(LinkerRegistry registry, out string sha, out string error)
        {
            sha = null;
            error = null;
            try
            {
                sha = new GitLsRemoteResolver(ToolUrlBuilder.RepoUrl).ResolveBranchSha(registry.Branch);
                return true;
            }
            catch (System.Exception e)
            {
                error = "resolve dev SHA failed: " + e.Message;
                return false;
            }
        }

        private static LinkerRegistry ToRegistryView(RegistryDocument doc, LinkerChannel channel)
        {
            var result = new LinkerRegistry
            {
                SchemaVersion = doc.SchemaVersion,
                Channel = channel,
                Branch = doc.Branch,
            };

            foreach (var entry in doc.Entries.OrderBy(e => e.Order))
            {
                ToolStatusExtensions.TryParse(entry.Status, out var status);
                ToolKindExtensions.TryParse(entry.Kind, out var kind);
                result.Entries.Add(new RegistryEntryView
                {
                    Id = entry.Id,
                    Status = status,
                    Kind = kind,
                    Order = entry.Order,
                    PackageName = entry.PackageName,
                    PackagePath = entry.PackagePath,
                    Revision = entry.Revision,
                    DefaultEnabled = entry.DefaultEnabled,
                    UserToggle = entry.UserToggle,
                    AgentAssets = entry.AgentAssets,
                    MinUnity = entry.MinUnity,
                    DisplayName = entry.Id,
                    DependsOn = entry.DependsOn != null
                        ? entry.DependsOn.ToList()
                        : new List<string>(),
                });
            }

            return result;
        }

        internal static RegistryChannel RegistryChannelForProject(LinkerChannel channel)
        {
            return channel == LinkerChannel.Dev || channel == LinkerChannel.Local
                ? RegistryChannel.Dev
                : RegistryChannel.Stable;
        }

        private static RegistryChannel RegistryChannelForCurrentProject()
        {
            return RegistryChannelForProject(_project != null ? _project.Channel : LinkerChannel.Stable);
        }

        private static string ProjectRoot
            => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        private static string PackageRoot
        {
            get
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                    typeof(U3DAILinkerPackage).Assembly);
                if (info != null && !string.IsNullOrEmpty(info.resolvedPath))
                    return info.resolvedPath;
                return Path.Combine(ProjectRoot, "Packages", U3DAILinkerPackage.PackageName);
            }
        }
    }
}

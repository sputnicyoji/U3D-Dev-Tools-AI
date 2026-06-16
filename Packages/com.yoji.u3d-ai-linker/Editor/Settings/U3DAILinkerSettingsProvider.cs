using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            using (new EditorGUILayout.HorizontalScope("box"))
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
                EditorGUILayout.LabelField(row.Installed.ToString(), GUILayout.Width(80));
                EditorGUILayout.LabelField(row.Agent.ToString(), GUILayout.Width(90));

                if (row.Kind == ToolKind.Infra && row.RequiredBy != null && row.RequiredBy.Length > 0)
                    EditorGUILayout.LabelField("req by: " + string.Join(",", row.RequiredBy));
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
                "Install/Update, Restore, and Rollback are wired. Agent asset sync and junction repair remain read-only in this build.",
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

        private static void RequestOpenFolder() { Debug.Log("[U3DAILinker] Open Generated Folder requested."); }
        private static void RequestCopyDiagnostics() { Debug.Log("[U3DAILinker] Copy Diagnostic Report requested."); }

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

            var queue = result.QueueResult.HasValue ? " queue=" + result.QueueResult.Value : string.Empty;
            Debug.Log("[U3DAILinker] " + action + " succeeded." + queue);
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

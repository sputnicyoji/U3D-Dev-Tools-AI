using System.Collections.Generic;
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
            using (new EditorGUI.DisabledScope(_operationState == OperationState.Running))
            using (new EditorGUILayout.VerticalScope())
            {
                if (GUILayout.Button("Refresh Registry")) RequestRefreshRegistry();
                if (GUILayout.Button("Install/Update Selected")) RequestInstallSelected();
                if (GUILayout.Button("Install/Update All")) RequestInstallAll();
                if (GUILayout.Button("Sync Agent Assets")) RequestSyncAgents();
                if (GUILayout.Button("Repair Links")) RequestRepairLinks();
                if (GUILayout.Button("Rollback Manifest")) RequestRollback();
                if (GUILayout.Button("Open Generated Folder")) RequestOpenFolder();
                if (GUILayout.Button("Copy Diagnostic Report")) RequestCopyDiagnostics();
            }
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
            if (_registry == null)
                return;
            var targetReg = _registry;
            var changes = RestorePlanner.BuildRestore(_installed, targetReg, _devSha);
            Debug.Log("[U3DAILinker] Restore plan to " + target + ": " + changes.Length + " changes.");
            // 实际 manifest 事务由 Manifest 子系统执行(组装阶段接线)。
        }

        private static void RequestRefreshRegistry() { Debug.Log("[U3DAILinker] Refresh Registry requested."); }
        private static void RequestInstallSelected() { Debug.Log("[U3DAILinker] Install/Update Selected requested."); }
        private static void RequestInstallAll() { Debug.Log("[U3DAILinker] Install/Update All requested."); }
        private static void RequestSyncAgents() { Debug.Log("[U3DAILinker] Sync Agent Assets requested."); }
        private static void RequestRepairLinks() { Debug.Log("[U3DAILinker] Repair Links requested."); }
        private static void RequestRollback() { Debug.Log("[U3DAILinker] Rollback Manifest requested."); }
        private static void RequestOpenFolder() { Debug.Log("[U3DAILinker] Open Generated Folder requested."); }
        private static void RequestCopyDiagnostics() { Debug.Log("[U3DAILinker] Copy Diagnostic Report requested."); }
    }
}

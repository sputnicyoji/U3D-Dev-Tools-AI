# Multi-Project Port Allocation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Multiple Unity projects can run the U3D AI tools at the same time without manual port juggling, while agent clients always connect to the intended project.

**Architecture:** Add a project-aware port allocation layer in `com.yoji.editor-core` for Editor services, publish active service instances to both a project-local `ports.json` and a machine-global registry, then teach each Python client to resolve the correct port from the current project before falling back to legacy defaults. `lua-device-debug` keeps the same runtime transport, but Editor binding uses the shared allocator and Android Development Builds get an optional runtime config asset for non-default remote ports.

**Tech Stack:** Unity Editor C# 2022.3+, `HttpListener`, `TcpListener`, JSON files under `ProjectSettings/`, `UserSettings/`, `.u3d-ai-linker/`, `%LOCALAPPDATA%`, Python 3.8+ client scripts, Unity EditMode tests.

---

## Execution Status - 2026-06-17

- Tasks 1-7 implemented.
- Task 5 spec review: approved.
- Task 5 quality review: approved.
- Task 6 spec review: approved.
- Task 6 quality review: approved.
- Python resolver tests: passed, 6/6.
- `git diff --check`: passed.
- Test project metadata aligned to local Unity `2022.3.62f2c1` and `com.unity.test-framework@1.1.33`; Unity 6000 consumers remain supported because package metadata targets `2022.3` minimum.
- `tools/run-editmode.ps1` now resolves the exact project Unity version from common Hub paths, `UNITY_EXE`, or `UNITY_EDITOR_PATH`; if the exact version is missing it fails instead of silently opening with another major version.
- Unity EditMode batchmode: still blocked by local Unity Package Manager startup error `The "path" argument must be of type string. Received undefined`; the script now reports it as pre-compilation UPM failure.
- Live multi-project smoke: pending, because target projects have not loaded this unpublished local package revision.


## Current Facts

- `test-runner-mcp` currently binds `21890 -> 21896 -> 21897` in `Packages/com.yoji.test-runner/Editor/TestRunnerMCP.cs`.
- `unity-editor-debug-mcp` currently binds `21891 -> 21892 -> 21893` in `Packages/com.yoji.editor-debug/Editor/EditorDebugMCP.cs`.
- `unity-lua-device-debug` currently binds fixed `21894` in `Packages/com.yoji.lua-device-debug/Runtime/LuaDeviceDebugPackage.cs`.
- The agent clients default to hardcoded ports:
  - `Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp/client.py`
  - `Packages/com.yoji.editor-debug/Agent~/skills/unity-editor-debug-mcp/client.py`
  - `Packages/com.yoji.lua-device-debug/Agent~/skills/unity-lua-device-debug/client.py`
- `com.yoji.u3d-ai-linker` already owns generated project-local agent state under `.u3d-ai-linker/`.
- `ProjectSettings/U3DAILinkerSettings.asset` must not contain absolute paths.

## Target Behavior

### Default multi-project mapping

For a project with allocated base port `B`:

| Service | Service id | Port |
|---|---|---|
| Test Runner MCP | `test-runner-mcp` | `B + 0` |
| Editor Debug MCP | `unity-editor-debug-mcp` | `B + 1` |
| Lua Device Debug, Editor | `unity-lua-device-debug` | `B + 4` |

The allocator may still choose legacy ports for the first project when free:

| Service | Legacy ports |
|---|---|
| `test-runner-mcp` | `21890`, `21896`, `21897` |
| `unity-editor-debug-mcp` | `21891`, `21892`, `21893` |
| `unity-lua-device-debug` | `21894` |

The project-aware path must not rely on these legacy ports.

### Resolution order in agent clients

1. Explicit `--host` and `--port`.
2. Explicit `--project <path>` plus optional `--pid <unity-pid>`.
3. Current working directory walk-up to `.u3d-ai-linker/ports.json`.
4. `%LOCALAPPDATA%\Yoji\U3D-Dev-Tools-AI\instances.json` lookup by project path.
5. Legacy default port scan.
6. Clear error listing candidate projects, services, PIDs, and ports.

### Identity fields required in every `/ping`

Every service must return:

```json
{
  "service": "TestRunnerMCP",
  "serviceId": "test-runner-mcp",
  "instanceId": "d78f8f9c7a6f4a5b8c9d0e1f2a3b4c5d",
  "processId": 76792,
  "projectId": "1e04f8e4d9df4d7a99f1fda0a1f97d62",
  "projectRoot": "G:/Side_Projects/HD2D-U3D/HD2D-Demo",
  "projectName": "HD2D-Demo",
  "port": 21900,
  "portSource": "project-auto"
}
```

`service` remains backward compatible. `serviceId` is the stable machine-readable key.

## Files

### Create

- `Packages/com.yoji.editor-core/Editor/Ports/ServicePortDefinition.cs`
- `Packages/com.yoji.editor-core/Editor/Ports/ServicePortAssignment.cs`
- `Packages/com.yoji.editor-core/Editor/Ports/ServicePortPolicy.cs`
- `Packages/com.yoji.editor-core/Editor/Ports/ServicePortSettings.cs`
- `Packages/com.yoji.editor-core/Editor/Ports/ServicePortSettingsStore.cs`
- `Packages/com.yoji.editor-core/Editor/Ports/ServicePortAllocator.cs`
- `Packages/com.yoji.editor-core/Editor/Ports/ServiceInstanceRecord.cs`
- `Packages/com.yoji.editor-core/Editor/Ports/ServiceInstanceRegistry.cs`
- `Packages/com.yoji.editor-core/Editor/Ports/ProjectIdentity.cs`
- `Packages/com.yoji.editor-core/Editor/Ports/ProjectIdentityProvider.cs`
- `Packages/com.yoji.editor-core/Editor/Ports/ProjectPortsFile.cs`
- `Packages/com.yoji.editor-core/Editor/Ports/PortRangeValidator.cs`
- `Packages/com.yoji.editor-core/Tests/Editor/Yoji.EditorCore.Editor.Tests.asmdef`
- `Packages/com.yoji.editor-core/Tests/Editor/ServicePortAllocatorTests.cs`
- `Packages/com.yoji.editor-core/Tests/Editor/ServiceInstanceRegistryTests.cs`
- `Packages/com.yoji.editor-core/Tests/Editor/ProjectPortsFileTests.cs`
- `Packages/com.yoji.editor-core/Tests/Editor/ServicePortSettingsStoreTests.cs`
- `TestProjects/editor-core/Packages/manifest.json`
- `TestProjects/editor-core/Packages/packages-lock.json`
- `TestProjects/editor-core/ProjectSettings/ProjectVersion.txt`
- `Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp/port_resolver.py`
- `Packages/com.yoji.editor-debug/Agent~/skills/unity-editor-debug-mcp/port_resolver.py`
- `Packages/com.yoji.lua-device-debug/Agent~/skills/unity-lua-device-debug/port_resolver.py`
- `Packages/com.yoji.lua-device-debug/Runtime/LuaDeviceDebugRuntimeConfig.cs`
- `Packages/com.yoji.lua-device-debug/Editor/LuaDeviceDebugRuntimeConfigEditor.cs`
- `tools/test-agent-port-resolvers.py`

### Modify

- `Packages/com.yoji.editor-core/package.json`
- `Packages/com.yoji.test-runner/Editor/TestRunnerMCP.cs`
- `Packages/com.yoji.editor-debug/Editor/EditorDebugMCP.cs`
- `Packages/com.yoji.lua-device-debug/Editor/LuaDeviceDebugEditorBootstrap.cs`
- `Packages/com.yoji.lua-device-debug/Runtime/LuaDeviceDebugPlayerBootstrap.cs`
- `Packages/com.yoji.lua-device-debug/Runtime/LuaDeviceDebugPackage.cs`
- `Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp/client.py`
- `Packages/com.yoji.editor-debug/Agent~/skills/unity-editor-debug-mcp/client.py`
- `Packages/com.yoji.lua-device-debug/Agent~/skills/unity-lua-device-debug/client.py`
- `Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp/SKILL.md`
- `Packages/com.yoji.editor-debug/Agent~/skills/unity-editor-debug-mcp/SKILL.md`
- `Packages/com.yoji.lua-device-debug/Agent~/skills/unity-lua-device-debug/SKILL.md`
- `Packages/com.yoji.u3d-ai-linker/Editor/Settings/U3DAILinkerSettingsProvider.cs`
- `Packages/com.yoji.u3d-ai-linker/Tests/Editor/U3DAILinkerSettingsProviderTests.cs`
- `README.md`
- `progress.md`
- `tools/run-editmode.ps1`

### Generated files at runtime

- `ProjectSettings/YojiDevToolPorts.json`
- `UserSettings/YojiDevToolPorts.user.json`
- `.u3d-ai-linker/ports.json`
- `%LOCALAPPDATA%\Yoji\U3D-Dev-Tools-AI\instances.json`

---

## Data Contracts

### `ProjectSettings/YojiDevToolPorts.json`

Commit-safe. No machine path.

```json
{
  "schemaVersion": 1,
  "projectId": "1e04f8e4d9df4d7a99f1fda0a1f97d62",
  "mode": "auto",
  "preferredBasePort": 0,
  "serviceOverrides": []
}
```

`mode` values:

- `auto`: allocate from legacy ports if free, then from hash-derived base ranges.
- `fixedProject`: use `preferredBasePort` and fail loudly if occupied.

### `UserSettings/YojiDevToolPorts.user.json`

Machine-local.

```json
{
  "schemaVersion": 1,
  "overrideBasePort": 0,
  "preferLegacyPorts": true
}
```

`overrideBasePort > 0` wins over project settings.

### `.u3d-ai-linker/ports.json`

Project-local, generated, safe to ignore.

```json
{
  "schemaVersion": 1,
  "projectRoot": "G:/Side_Projects/HD2D-U3D/HD2D-Demo",
  "projectId": "1e04f8e4d9df4d7a99f1fda0a1f97d62",
  "updatedUtc": "2026-06-17T10:30:00.0000000Z",
  "instances": [
    {
      "serviceId": "test-runner-mcp",
      "displayName": "TestRunnerMCP",
      "instanceId": "d78f8f9c7a6f4a5b8c9d0e1f2a3b4c5d",
      "processId": 76792,
      "host": "127.0.0.1",
      "port": 21900,
      "portSource": "project-auto",
      "startedUtc": "2026-06-17T10:29:30.0000000Z",
      "lastSeenUtc": "2026-06-17T10:30:00.0000000Z"
    }
  ]
}
```

If two Unity instances open the same project, this file may contain multiple records for the same service. The client must error unless `--pid` or `--port` disambiguates.

### `%LOCALAPPDATA%\Yoji\U3D-Dev-Tools-AI\instances.json`

Machine-global.

```json
{
  "schemaVersion": 1,
  "updatedUtc": "2026-06-17T10:30:00.0000000Z",
  "instances": [
    {
      "projectRoot": "G:/Side_Projects/HD2D-U3D/HD2D-Demo",
      "projectId": "1e04f8e4d9df4d7a99f1fda0a1f97d62",
      "serviceId": "unity-editor-debug-mcp",
      "displayName": "EditorDebugMCP",
      "instanceId": "3a1e4a15ff7c42d9a37d9d0b8ad4a50b",
      "processId": 76792,
      "host": "127.0.0.1",
      "port": 21901,
      "portSource": "project-auto",
      "startedUtc": "2026-06-17T10:29:31.0000000Z",
      "lastSeenUtc": "2026-06-17T10:30:00.0000000Z"
    }
  ]
}
```

Writes must be atomic:

1. Read existing JSON.
2. Remove records with the same `instanceId`.
3. Add the current record.
4. Write to temp file.
5. Replace original.

Use a named mutex or file lock to serialize writes:

```text
Global\Yoji.U3DDevToolsAI.PortRegistry
```

---

## Task 1: Add editor-core port data model

**Files:**
- Create: `Packages/com.yoji.editor-core/Editor/Ports/ServicePortDefinition.cs`
- Create: `Packages/com.yoji.editor-core/Editor/Ports/ServicePortAssignment.cs`
- Create: `Packages/com.yoji.editor-core/Editor/Ports/ServicePortPolicy.cs`
- Create: `Packages/com.yoji.editor-core/Editor/Ports/PortRangeValidator.cs`
- Create: `Packages/com.yoji.editor-core/Tests/Editor/Yoji.EditorCore.Editor.Tests.asmdef`
- Create: `Packages/com.yoji.editor-core/Tests/Editor/ServicePortAllocatorTests.cs`
- Modify: `Packages/com.yoji.editor-core/package.json`

- [x] **Step 1: Add editor-core test assembly**

Create `Packages/com.yoji.editor-core/Tests/Editor/Yoji.EditorCore.Editor.Tests.asmdef`:

```json
{
  "name": "Yoji.EditorCore.Editor.Tests",
  "references": [
    "Yoji.EditorCore.Editor"
  ],
  "includePlatforms": [
    "Editor"
  ],
  "optionalUnityReferences": [
    "TestAssemblies"
  ]
}
```

- [x] **Step 2: Write failing allocator tests**

Create `Packages/com.yoji.editor-core/Tests/Editor/ServicePortAllocatorTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Yoji.EditorCore.Ports;

namespace Yoji.EditorCore.Tests
{
    public sealed class ServicePortAllocatorTests
    {
        [Test]
        public void Allocate_LegacyFree_UsesLegacyPort()
        {
            var definition = ServicePortDefinition.Create(
                "test-runner-mcp", "TestRunnerMCP", 0, new[] { 21890, 21896, 21897 });
            var policy = new ServicePortPolicy
            {
                ProjectRoot = "G:/A",
                ProjectId = "project-a",
                Mode = ServicePortMode.Auto,
                PreferLegacyPorts = true,
            };
            var probe = new FakePortProbe(new int[0]);

            var result = ServicePortAllocator.Allocate(definition, policy, probe);

            Assert.AreEqual(21890, result.Port);
            Assert.AreEqual("legacy", result.Source);
        }

        [Test]
        public void Allocate_LegacyOccupied_UsesProjectBase()
        {
            var definition = ServicePortDefinition.Create(
                "test-runner-mcp", "TestRunnerMCP", 0, new[] { 21890, 21896, 21897 });
            var policy = new ServicePortPolicy
            {
                ProjectRoot = "G:/A",
                ProjectId = "project-a",
                Mode = ServicePortMode.Auto,
                PreferLegacyPorts = true,
            };
            var probe = new FakePortProbe(new[] { 21890, 21896, 21897 });

            var result = ServicePortAllocator.Allocate(definition, policy, probe);

            Assert.AreNotEqual(21890, result.Port);
            Assert.GreaterOrEqual(result.Port, 21900);
            Assert.LessOrEqual(result.Port, 29999);
            Assert.AreEqual("project-auto", result.Source);
        }

        [Test]
        public void Allocate_FixedProjectOccupied_ReturnsFailure()
        {
            var definition = ServicePortDefinition.Create(
                "unity-editor-debug-mcp", "EditorDebugMCP", 1, new[] { 21891, 21892, 21893 });
            var policy = new ServicePortPolicy
            {
                ProjectRoot = "G:/A",
                ProjectId = "project-a",
                Mode = ServicePortMode.FixedProject,
                PreferredBasePort = 21900,
            };
            var probe = new FakePortProbe(new[] { 21901 });

            var result = ServicePortAllocator.TryAllocate(definition, policy, probe, out var assignment, out var error);

            Assert.IsFalse(result);
            Assert.IsNull(assignment);
            StringAssert.Contains("21901", error);
        }

        private sealed class FakePortProbe : IPortProbe
        {
            private readonly HashSet<int> m_Occupied;

            public FakePortProbe(IEnumerable<int> occupied)
            {
                m_Occupied = new HashSet<int>(occupied);
            }

            public bool IsAvailable(int port)
            {
                return !m_Occupied.Contains(port);
            }
        }
    }
}
```

- [x] **Step 3: Run test and verify it fails**

Run:

```powershell
pwsh -NoProfile -File tools/run-editmode.ps1 -Project editor-core
```

Expected:

```text
error CS0246: The type or namespace name 'ServicePortDefinition' could not be found
```

- [x] **Step 4: Implement port model**

Create `Packages/com.yoji.editor-core/Editor/Ports/ServicePortDefinition.cs`:

```csharp
using System;

namespace Yoji.EditorCore.Ports
{
    public sealed class ServicePortDefinition
    {
        public string ServiceId { get; private set; }
        public string DisplayName { get; private set; }
        public int Offset { get; private set; }
        public int[] LegacyPorts { get; private set; }

        public static ServicePortDefinition Create(string serviceId, string displayName, int offset, int[] legacyPorts)
        {
            if (string.IsNullOrEmpty(serviceId)) throw new ArgumentException("serviceId is required");
            if (string.IsNullOrEmpty(displayName)) throw new ArgumentException("displayName is required");
            PortRangeValidator.ValidateOffset(offset);
            return new ServicePortDefinition
            {
                ServiceId = serviceId,
                DisplayName = displayName,
                Offset = offset,
                LegacyPorts = legacyPorts ?? new int[0],
            };
        }
    }
}
```

Create `Packages/com.yoji.editor-core/Editor/Ports/ServicePortAssignment.cs`:

```csharp
namespace Yoji.EditorCore.Ports
{
    public sealed class ServicePortAssignment
    {
        public string ServiceId;
        public string DisplayName;
        public string Host;
        public int Port;
        public string Source;
        public string ProjectRoot;
        public string ProjectId;
    }
}
```

Create `Packages/com.yoji.editor-core/Editor/Ports/ServicePortPolicy.cs`:

```csharp
namespace Yoji.EditorCore.Ports
{
    public enum ServicePortMode
    {
        Auto,
        FixedProject,
    }

    public sealed class ServicePortPolicy
    {
        public string ProjectRoot;
        public string ProjectId;
        public ServicePortMode Mode = ServicePortMode.Auto;
        public int PreferredBasePort;
        public int OverrideBasePort;
        public bool PreferLegacyPorts = true;
    }
}
```

Create `Packages/com.yoji.editor-core/Editor/Ports/PortRangeValidator.cs`:

```csharp
using System;

namespace Yoji.EditorCore.Ports
{
    public static class PortRangeValidator
    {
        public const int MinPort = 1024;
        public const int MaxPort = 65535;
        public const int DefaultBaseMin = 21900;
        public const int DefaultBaseMax = 29990;
        public const int BaseStep = 10;

        public static void ValidatePort(int port)
        {
            if (port < MinPort || port > MaxPort)
                throw new ArgumentOutOfRangeException("port", "port must be in 1024..65535");
        }

        public static void ValidateBasePort(int basePort)
        {
            ValidatePort(basePort);
            if (basePort % BaseStep != 0)
                throw new ArgumentException("basePort must be aligned to step 10");
        }

        public static void ValidateOffset(int offset)
        {
            if (offset < 0 || offset >= BaseStep)
                throw new ArgumentOutOfRangeException("offset", "offset must be in 0..9");
        }
    }
}
```

- [x] **Step 5: Implement allocator**

Create `Packages/com.yoji.editor-core/Editor/Ports/ServicePortAllocator.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Yoji.EditorCore.Ports
{
    public interface IPortProbe
    {
        bool IsAvailable(int port);
    }

    public sealed class TcpPortProbe : IPortProbe
    {
        public bool IsAvailable(int port)
        {
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public static class ServicePortAllocator
    {
        public static ServicePortAssignment Allocate(
            ServicePortDefinition definition,
            ServicePortPolicy policy,
            IPortProbe probe)
        {
            if (!TryAllocate(definition, policy, probe, out var assignment, out var error))
                throw new InvalidOperationException(error);
            return assignment;
        }

        public static bool TryAllocate(
            ServicePortDefinition definition,
            ServicePortPolicy policy,
            IPortProbe probe,
            out ServicePortAssignment assignment,
            out string error)
        {
            assignment = null;
            error = null;

            foreach (var candidate in BuildCandidates(definition, policy))
            {
                if (!probe.IsAvailable(candidate.Port))
                {
                    if (policy.Mode == ServicePortMode.FixedProject && candidate.Source == "fixed-project")
                    {
                        error = "configured port is occupied: " + candidate.Port;
                        return false;
                    }
                    continue;
                }

                assignment = new ServicePortAssignment
                {
                    ServiceId = definition.ServiceId,
                    DisplayName = definition.DisplayName,
                    Host = "127.0.0.1",
                    Port = candidate.Port,
                    Source = candidate.Source,
                    ProjectRoot = policy.ProjectRoot,
                    ProjectId = policy.ProjectId,
                };
                return true;
            }

            error = "no available port for " + definition.ServiceId;
            return false;
        }

        private static IEnumerable<Candidate> BuildCandidates(ServicePortDefinition definition, ServicePortPolicy policy)
        {
            if (policy.OverrideBasePort > 0)
                yield return BaseCandidate(policy.OverrideBasePort, definition.Offset, "user-override");

            if (policy.Mode == ServicePortMode.FixedProject && policy.PreferredBasePort > 0)
            {
                yield return BaseCandidate(policy.PreferredBasePort, definition.Offset, "fixed-project");
                yield break;
            }

            if (policy.PreferLegacyPorts)
            {
                foreach (var port in definition.LegacyPorts)
                    yield return new Candidate { Port = port, Source = "legacy" };
            }

            var start = StableBase(policy.ProjectRoot + "|" + policy.ProjectId);
            for (var i = 0; i < 128; i++)
            {
                var basePort = PortRangeValidator.DefaultBaseMin +
                    ((start - PortRangeValidator.DefaultBaseMin + i * PortRangeValidator.BaseStep) %
                     (PortRangeValidator.DefaultBaseMax - PortRangeValidator.DefaultBaseMin));
                yield return BaseCandidate(basePort, definition.Offset, "project-auto");
            }
        }

        private static Candidate BaseCandidate(int basePort, int offset, string source)
        {
            PortRangeValidator.ValidateBasePort(basePort);
            return new Candidate { Port = basePort + offset, Source = source };
        }

        private static int StableBase(string value)
        {
            unchecked
            {
                var hash = 2166136261u;
                foreach (var b in Encoding.UTF8.GetBytes(value ?? string.Empty))
                {
                    hash ^= b;
                    hash *= 16777619u;
                }
                var slots = (PortRangeValidator.DefaultBaseMax - PortRangeValidator.DefaultBaseMin) / PortRangeValidator.BaseStep;
                return PortRangeValidator.DefaultBaseMin + (int)(hash % (uint)slots) * PortRangeValidator.BaseStep;
            }
        }

        private struct Candidate
        {
            public int Port;
            public string Source;
        }
    }
}
```

- [x] **Step 6: Run test and verify pass**

Run:

```powershell
pwsh -NoProfile -File tools/run-editmode.ps1 -Project editor-core
```

Expected:

```text
[run-editmode] ===== RESULT: Passed =====
[run-editmode] all green
```

---

## Task 2: Add settings, identity, and instance registry

**Files:**
- Create: `Packages/com.yoji.editor-core/Editor/Ports/ServicePortSettings.cs`
- Create: `Packages/com.yoji.editor-core/Editor/Ports/ServicePortSettingsStore.cs`
- Create: `Packages/com.yoji.editor-core/Editor/Ports/ProjectIdentity.cs`
- Create: `Packages/com.yoji.editor-core/Editor/Ports/ProjectIdentityProvider.cs`
- Create: `Packages/com.yoji.editor-core/Editor/Ports/ServiceInstanceRecord.cs`
- Create: `Packages/com.yoji.editor-core/Editor/Ports/ServiceInstanceRegistry.cs`
- Create: `Packages/com.yoji.editor-core/Editor/Ports/ProjectPortsFile.cs`
- Create: `Packages/com.yoji.editor-core/Tests/Editor/ServicePortSettingsStoreTests.cs`
- Create: `Packages/com.yoji.editor-core/Tests/Editor/ServiceInstanceRegistryTests.cs`
- Create: `Packages/com.yoji.editor-core/Tests/Editor/ProjectPortsFileTests.cs`

- [x] **Step 1: Write failing persistence tests**

Create `Packages/com.yoji.editor-core/Tests/Editor/ServicePortSettingsStoreTests.cs`:

```csharp
using System.IO;
using NUnit.Framework;
using Yoji.EditorCore.Ports;

namespace Yoji.EditorCore.Tests
{
    public sealed class ServicePortSettingsStoreTests
    {
        [Test]
        public void LoadOrCreateProjectSettings_CreatesStableProjectId()
        {
            var root = CreateTempRoot();
            var settings = ServicePortSettingsStore.LoadOrCreateProjectSettings(root);

            Assert.AreEqual(1, settings.SchemaVersion);
            Assert.AreEqual("auto", settings.Mode);
            Assert.IsNotEmpty(settings.ProjectId);
            Assert.IsTrue(File.Exists(Path.Combine(root, "ProjectSettings", "YojiDevToolPorts.json")));
        }

        [Test]
        public void UserOverrideBasePort_WinsPolicy()
        {
            var root = CreateTempRoot();
            var user = new ServicePortUserSettings { SchemaVersion = 1, OverrideBasePort = 23000, PreferLegacyPorts = false };
            ServicePortSettingsStore.SaveUserSettings(root, user);

            var identity = new ProjectIdentity { ProjectRoot = root, ProjectId = "p" };
            var policy = ServicePortSettingsStore.BuildPolicy(root, identity);

            Assert.AreEqual(23000, policy.OverrideBasePort);
            Assert.IsFalse(policy.PreferLegacyPorts);
        }

        private static string CreateTempRoot()
        {
            var root = Path.Combine(Path.GetTempPath(), "yoji-port-tests", Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(root, "ProjectSettings"));
            Directory.CreateDirectory(Path.Combine(root, "UserSettings"));
            return root;
        }
    }
}
```

Create `Packages/com.yoji.editor-core/Tests/Editor/ServiceInstanceRegistryTests.cs`:

```csharp
using System.IO;
using NUnit.Framework;
using Yoji.EditorCore.Ports;

namespace Yoji.EditorCore.Tests
{
    public sealed class ServiceInstanceRegistryTests
    {
        [Test]
        public void Register_ReplacesSameInstanceId()
        {
            var file = Path.Combine(Path.GetTempPath(), "yoji-instances-" + Path.GetRandomFileName() + ".json");
            var registry = new ServiceInstanceRegistry(file);
            var record = ServiceInstanceRecord.Create("test-runner-mcp", "TestRunnerMCP", "G:/A", "p", 123, 21900, "project-auto");

            registry.Register(record);
            record.Port = 21910;
            registry.Register(record);

            var loaded = registry.Load();
            Assert.AreEqual(1, loaded.Count);
            Assert.AreEqual(21910, loaded[0].Port);
        }
    }
}
```

Create `Packages/com.yoji.editor-core/Tests/Editor/ProjectPortsFileTests.cs`:

```csharp
using System.IO;
using NUnit.Framework;
using Yoji.EditorCore.Ports;

namespace Yoji.EditorCore.Tests
{
    public sealed class ProjectPortsFileTests
    {
        [Test]
        public void Write_ContainsMultipleServiceInstances()
        {
            var root = Path.Combine(Path.GetTempPath(), "yoji-ports-" + Path.GetRandomFileName());
            var a = ServiceInstanceRecord.Create("test-runner-mcp", "TestRunnerMCP", root, "p", 100, 21900, "project-auto");
            var b = ServiceInstanceRecord.Create("unity-editor-debug-mcp", "EditorDebugMCP", root, "p", 100, 21901, "project-auto");

            ProjectPortsFile.Upsert(root, a);
            ProjectPortsFile.Upsert(root, b);

            var json = File.ReadAllText(Path.Combine(root, ".u3d-ai-linker", "ports.json"));
            StringAssert.Contains("test-runner-mcp", json);
            StringAssert.Contains("unity-editor-debug-mcp", json);
            StringAssert.Contains("21901", json);
        }
    }
}
```

- [x] **Step 2: Run tests and verify fail**

Run:

```powershell
pwsh -NoProfile -File tools/run-editmode.ps1 -Project editor-core
```

Expected:

```text
error CS0246: The type or namespace name 'ServicePortSettingsStore' could not be found
```

- [x] **Step 3: Implement settings and registry**

Create `Packages/com.yoji.editor-core/Editor/Ports/ServicePortSettings.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace Yoji.EditorCore.Ports
{
    [Serializable]
    public sealed class ServicePortProjectSettings
    {
        public int SchemaVersion = 1;
        public string ProjectId = string.Empty;
        public string Mode = "auto";
        public int PreferredBasePort = 0;
        public List<ServicePortOverride> ServiceOverrides = new List<ServicePortOverride>();
    }

    [Serializable]
    public sealed class ServicePortUserSettings
    {
        public int SchemaVersion = 1;
        public int OverrideBasePort = 0;
        public bool PreferLegacyPorts = true;
    }

    [Serializable]
    public sealed class ServicePortOverride
    {
        public string ServiceId;
        public int Port;
    }
}
```

Create `Packages/com.yoji.editor-core/Editor/Ports/ProjectIdentity.cs`:

```csharp
namespace Yoji.EditorCore.Ports
{
    public sealed class ProjectIdentity
    {
        public string ProjectRoot;
        public string ProjectId;
        public string ProjectName;
    }
}
```

Create `Packages/com.yoji.editor-core/Editor/Ports/ProjectIdentityProvider.cs`:

```csharp
using System.IO;
using UnityEngine;

namespace Yoji.EditorCore.Ports
{
    public static class ProjectIdentityProvider
    {
        public static ProjectIdentity Current()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
            var settings = ServicePortSettingsStore.LoadOrCreateProjectSettings(projectRoot);
            return new ProjectIdentity
            {
                ProjectRoot = projectRoot,
                ProjectId = settings.ProjectId,
                ProjectName = Application.productName,
            };
        }
    }
}
```

Create `Packages/com.yoji.editor-core/Editor/Ports/ServicePortSettingsStore.cs`:

```csharp
using System;
using System.IO;
using UnityEngine;

namespace Yoji.EditorCore.Ports
{
    public static class ServicePortSettingsStore
    {
        public const string ProjectSettingsRelativePath = "ProjectSettings/YojiDevToolPorts.json";
        public const string UserSettingsRelativePath = "UserSettings/YojiDevToolPorts.user.json";

        public static ServicePortProjectSettings LoadOrCreateProjectSettings(string projectRoot)
        {
            var path = Path.Combine(projectRoot, ProjectSettingsRelativePath);
            var settings = LoadJson<ServicePortProjectSettings>(path) ?? new ServicePortProjectSettings();
            if (string.IsNullOrEmpty(settings.ProjectId))
            {
                settings.ProjectId = Guid.NewGuid().ToString("N");
                SaveProjectSettings(projectRoot, settings);
            }
            return settings;
        }

        public static ServicePortUserSettings LoadUserSettings(string projectRoot)
        {
            return LoadJson<ServicePortUserSettings>(Path.Combine(projectRoot, UserSettingsRelativePath))
                   ?? new ServicePortUserSettings();
        }

        public static void SaveUserSettings(string projectRoot, ServicePortUserSettings settings)
        {
            SaveJson(Path.Combine(projectRoot, UserSettingsRelativePath), settings);
        }

        public static void SaveProjectSettings(string projectRoot, ServicePortProjectSettings settings)
        {
            SaveJson(Path.Combine(projectRoot, ProjectSettingsRelativePath), settings);
        }

        public static ServicePortPolicy BuildPolicy(string projectRoot, ProjectIdentity identity)
        {
            var project = LoadOrCreateProjectSettings(projectRoot);
            var user = LoadUserSettings(projectRoot);
            return new ServicePortPolicy
            {
                ProjectRoot = projectRoot.Replace('\\', '/'),
                ProjectId = identity.ProjectId,
                Mode = project.Mode == "fixedProject" ? ServicePortMode.FixedProject : ServicePortMode.Auto,
                PreferredBasePort = project.PreferredBasePort,
                OverrideBasePort = user.OverrideBasePort,
                PreferLegacyPorts = user.PreferLegacyPorts,
            };
        }

        private static T LoadJson<T>(string path) where T : class
        {
            if (!File.Exists(path)) return null;
            return JsonUtility.FromJson<T>(File.ReadAllText(path));
        }

        private static void SaveJson<T>(string path, T value)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonUtility.ToJson(value, true));
        }
    }
}
```

Create `Packages/com.yoji.editor-core/Editor/Ports/ServiceInstanceRecord.cs`:

```csharp
using System;
using System.Diagnostics;

namespace Yoji.EditorCore.Ports
{
    [Serializable]
    public sealed class ServiceInstanceRecord
    {
        public string ServiceId;
        public string DisplayName;
        public string InstanceId;
        public int ProcessId;
        public string ProjectRoot;
        public string ProjectId;
        public string Host;
        public int Port;
        public string PortSource;
        public string StartedUtc;
        public string LastSeenUtc;

        public static ServiceInstanceRecord Create(
            string serviceId,
            string displayName,
            string projectRoot,
            string projectId,
            int processId,
            int port,
            string portSource)
        {
            var now = DateTime.UtcNow.ToString("o");
            return new ServiceInstanceRecord
            {
                ServiceId = serviceId,
                DisplayName = displayName,
                InstanceId = Guid.NewGuid().ToString("N"),
                ProcessId = processId > 0 ? processId : Process.GetCurrentProcess().Id,
                ProjectRoot = projectRoot.Replace('\\', '/'),
                ProjectId = projectId,
                Host = "127.0.0.1",
                Port = port,
                PortSource = portSource,
                StartedUtc = now,
                LastSeenUtc = now,
            };
        }

        public void Touch()
        {
            LastSeenUtc = DateTime.UtcNow.ToString("o");
        }
    }
}
```

Implement `ServiceInstanceRegistry` and `ProjectPortsFile` with the same JSON shape described above. Use wrapper arrays because `JsonUtility` does not serialize top-level arrays.

- [x] **Step 4: Run tests and verify pass**

Run:

```powershell
pwsh -NoProfile -File tools/run-editmode.ps1 -Project editor-core
```

Expected:

```text
[run-editmode] ===== RESULT: Passed =====
[run-editmode] all green
```

---

## Task 3: Convert Editor services to allocated ports

**Files:**
- Modify: `Packages/com.yoji.test-runner/Editor/TestRunnerMCP.cs`
- Modify: `Packages/com.yoji.editor-debug/Editor/EditorDebugMCP.cs`
- Modify: `Packages/com.yoji.lua-device-debug/Editor/LuaDeviceDebugEditorBootstrap.cs`
- Modify: `Packages/com.yoji.lua-device-debug/Runtime/LuaDeviceDebugPackage.cs`
- Modify: `Packages/com.yoji.test-runner/Tests/Editor/FixtureTests.cs`
- Modify: `Packages/com.yoji.editor-debug/Tests/Editor/EditorDebugMCPTests.cs`
- Modify: `Packages/com.yoji.lua-device-debug/Tests/Editor/PackageShapeTests.cs`

- [x] **Step 1: Write service definition constants**

For `TestRunnerMCP.cs`, replace the fixed array with:

```csharp
private static readonly ServicePortDefinition k_PortDefinition =
    ServicePortDefinition.Create(
        "test-runner-mcp",
        "TestRunnerMCP",
        0,
        new[] { 21890, 21896, 21897 });
```

For `EditorDebugMCP.cs`:

```csharp
private static readonly ServicePortDefinition k_PortDefinition =
    ServicePortDefinition.Create(
        "unity-editor-debug-mcp",
        "EditorDebugMCP",
        1,
        new[] { 21891, 21892, 21893 });
```

For `LuaDeviceDebugEditorBootstrap.cs`:

```csharp
private static readonly ServicePortDefinition k_PortDefinition =
    ServicePortDefinition.Create(
        "unity-lua-device-debug",
        "LuaDeviceDebug",
        4,
        new[] { LuaDeviceDebugPackage.DefaultPort });
```

- [x] **Step 2: Allocate before binding**

Add this helper shape to each Editor service:

```csharp
private static ServiceInstanceRecord s_InstanceRecord;
private static ServiceInstanceRegistry s_GlobalRegistry;

private static ServicePortAssignment AllocatePort()
{
    var identity = ProjectIdentityProvider.Current();
    var policy = ServicePortSettingsStore.BuildPolicy(identity.ProjectRoot, identity);
    return ServicePortAllocator.Allocate(k_PortDefinition, policy, new TcpPortProbe());
}
```

Then bind the returned `assignment.Port` instead of looping only hardcoded fallback arrays.

- [x] **Step 3: Register and heartbeat**

After listener/server start:

```csharp
s_InstanceRecord = ServiceInstanceRecord.Create(
    k_PortDefinition.ServiceId,
    k_PortDefinition.DisplayName,
    assignment.ProjectRoot,
    assignment.ProjectId,
    System.Diagnostics.Process.GetCurrentProcess().Id,
    assignment.Port,
    assignment.Source);

s_GlobalRegistry = ServiceInstanceRegistry.Default();
s_GlobalRegistry.Register(s_InstanceRecord);
ProjectPortsFile.Upsert(assignment.ProjectRoot, s_InstanceRecord);
EditorApplication.update += Heartbeat;
```

Add:

```csharp
private static double s_NextHeartbeat;

private static void Heartbeat()
{
    if (s_InstanceRecord == null || EditorApplication.timeSinceStartup < s_NextHeartbeat)
        return;

    s_NextHeartbeat = EditorApplication.timeSinceStartup + 10.0;
    s_InstanceRecord.Touch();
    s_GlobalRegistry.Register(s_InstanceRecord);
    ProjectPortsFile.Upsert(s_InstanceRecord.ProjectRoot, s_InstanceRecord);
}
```

On stop:

```csharp
EditorApplication.update -= Heartbeat;
if (s_GlobalRegistry != null && s_InstanceRecord != null)
    s_GlobalRegistry.Unregister(s_InstanceRecord.InstanceId);
if (s_InstanceRecord != null)
    ProjectPortsFile.Remove(s_InstanceRecord.ProjectRoot, s_InstanceRecord.InstanceId);
s_InstanceRecord = null;
s_GlobalRegistry = null;
```

- [x] **Step 4: Extend `/ping`**

For each service, append:

```csharp
["serviceId"] = s_InstanceRecord != null ? s_InstanceRecord.ServiceId : k_PortDefinition.ServiceId,
["instanceId"] = s_InstanceRecord != null ? s_InstanceRecord.InstanceId : "",
["processId"] = s_InstanceRecord != null ? s_InstanceRecord.ProcessId : System.Diagnostics.Process.GetCurrentProcess().Id,
["projectId"] = s_InstanceRecord != null ? s_InstanceRecord.ProjectId : "",
["projectRoot"] = s_InstanceRecord != null ? s_InstanceRecord.ProjectRoot : "",
["portSource"] = s_InstanceRecord != null ? s_InstanceRecord.PortSource : "unknown",
```

- [x] **Step 5: Run package tests**

Run:

```powershell
pwsh -NoProfile -File tools/run-editmode.ps1 -Project test-runner
pwsh -NoProfile -File tools/run-editmode.ps1 -Project editor-debug
pwsh -NoProfile -File tools/run-editmode.ps1 -Project lua-device-debug
```

Expected:

```text
[run-editmode] ===== RESULT: Passed =====
[run-editmode] all green
```

---

## Task 4: Add Android/player runtime port override for lua-device-debug

**Files:**
- Create: `Packages/com.yoji.lua-device-debug/Runtime/LuaDeviceDebugRuntimeConfig.cs`
- Create: `Packages/com.yoji.lua-device-debug/Editor/LuaDeviceDebugRuntimeConfigEditor.cs`
- Modify: `Packages/com.yoji.lua-device-debug/Runtime/LuaDeviceDebugPlayerBootstrap.cs`
- Modify: `Packages/com.yoji.lua-device-debug/README.md`
- Modify: `Packages/com.yoji.lua-device-debug/Tests/Editor/PackageShapeTests.cs`

- [x] **Step 1: Add config asset type**

Create `Packages/com.yoji.lua-device-debug/Runtime/LuaDeviceDebugRuntimeConfig.cs`:

```csharp
using UnityEngine;

namespace Yoji.LuaDeviceDebug
{
    public sealed class LuaDeviceDebugRuntimeConfig : ScriptableObject
    {
        public const string ResourcesPath = "YojiLuaDeviceDebugRuntimeConfig";

        [Min(1024)]
        public int RemotePort = LuaDeviceDebugPackage.DefaultPort;

        public static int ResolveRemotePort()
        {
            var config = Resources.Load<LuaDeviceDebugRuntimeConfig>(ResourcesPath);
            return config != null && config.RemotePort >= 1024 && config.RemotePort <= 65535
                ? config.RemotePort
                : LuaDeviceDebugPackage.DefaultPort;
        }
    }
}
```

- [x] **Step 2: Use config in player bootstrap**

Modify `LuaDeviceDebugPlayerBootstrap.cs`:

```csharp
var port = LuaDeviceDebugRuntimeConfig.ResolveRemotePort();
s_Server = new LuaDeviceDebugServer(port);
s_Server.Start();
Debug.Log("[LuaDeviceDebug] 服务已启动，监听 http://127.0.0.1:" + port + "/");
```

- [x] **Step 3: Add editor menu to create config**

Create `Packages/com.yoji.lua-device-debug/Editor/LuaDeviceDebugRuntimeConfigEditor.cs`:

```csharp
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Yoji.LuaDeviceDebug.Editor
{
    internal static class LuaDeviceDebugRuntimeConfigEditor
    {
        [MenuItem("Tools/Yoji/Lua Device Debug/Create Runtime Port Config")]
        private static void CreateConfig()
        {
            const string dir = "Assets/Resources";
            const string path = dir + "/" + LuaDeviceDebugRuntimeConfig.ResourcesPath + ".asset";
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            var asset = AssetDatabase.LoadAssetAtPath<LuaDeviceDebugRuntimeConfig>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<LuaDeviceDebugRuntimeConfig>();
                AssetDatabase.CreateAsset(asset, path);
            }
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }
    }
}
```

- [x] **Step 4: Verify**

Run:

```powershell
pwsh -NoProfile -File tools/run-editmode.ps1 -Project lua-device-debug
```

Expected:

```text
[run-editmode] ===== RESULT: Passed =====
[run-editmode] all green
```

---

## Task 5: Update Python clients with project-aware port resolution

**Files:**
- Create: `Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp/port_resolver.py`
- Create: `Packages/com.yoji.editor-debug/Agent~/skills/unity-editor-debug-mcp/port_resolver.py`
- Create: `Packages/com.yoji.lua-device-debug/Agent~/skills/unity-lua-device-debug/port_resolver.py`
- Modify: `Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp/client.py`
- Modify: `Packages/com.yoji.editor-debug/Agent~/skills/unity-editor-debug-mcp/client.py`
- Modify: `Packages/com.yoji.lua-device-debug/Agent~/skills/unity-lua-device-debug/client.py`
- Create: `tools/test-agent-port-resolvers.py`

- [x] **Step 1: Add resolver module**

Create the same `port_resolver.py` in each skill folder:

```python
from __future__ import annotations

import json
import os
from pathlib import Path
from typing import Any


def _local_appdata() -> Path:
    root = os.environ.get("LOCALAPPDATA")
    return Path(root) if root else Path.home() / "AppData" / "Local"


def _load_json(path: Path) -> dict[str, Any] | None:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except Exception:
        return None


def _find_project_root(start: Path) -> Path | None:
    cur = start.resolve()
    for p in [cur, *cur.parents]:
        if (p / "ProjectSettings" / "ProjectVersion.txt").exists():
            return p
        if (p / ".u3d-ai-linker" / "ports.json").exists():
            return p
    return None


def _records_from_project(project: Path) -> list[dict[str, Any]]:
    data = _load_json(project / ".u3d-ai-linker" / "ports.json")
    if not data:
        return []
    return list(data.get("instances") or [])


def _records_from_global() -> list[dict[str, Any]]:
    path = _local_appdata() / "Yoji" / "U3D-Dev-Tools-AI" / "instances.json"
    data = _load_json(path)
    if not data:
        return []
    return list(data.get("instances") or [])


def resolve_endpoint(
    service_id: str,
    explicit_host: str,
    explicit_port: int | None,
    default_port: int,
    project: str | None,
    pid: int | None,
) -> tuple[str, int, str]:
    if explicit_port:
        return explicit_host, explicit_port, "explicit"

    project_root = Path(project).resolve() if project else _find_project_root(Path.cwd())
    candidates: list[dict[str, Any]] = []
    if project_root:
        candidates.extend(_records_from_project(project_root))
        root_text = project_root.as_posix().lower()
        candidates.extend([
            r for r in _records_from_global()
            if str(r.get("projectRoot", "")).replace("\\", "/").lower() == root_text
        ])
    else:
        candidates.extend(_records_from_global())

    filtered = []
    seen = set()
    for r in candidates:
        if r.get("serviceId") != service_id:
            continue
        if pid and int(r.get("processId") or 0) != pid:
            continue
        key = (r.get("processId"), r.get("port"), r.get("instanceId"))
        if key in seen:
            continue
        seen.add(key)
        filtered.append(r)

    if len(filtered) == 1:
        r = filtered[0]
        return str(r.get("host") or explicit_host), int(r["port"]), "project-registry"

    if len(filtered) > 1:
        summary = ", ".join(
            f"pid={r.get('processId')} port={r.get('port')} project={r.get('projectRoot')}"
            for r in filtered
        )
        raise SystemExit(f"ambiguous {service_id} instances; pass --pid or --port: {summary}")

    return explicit_host, default_port, "legacy-default"
```

- [x] **Step 2: Wire arguments**

Add to each client parser:

```python
p.add_argument("--project", help="Unity project root. Defaults to walking up from cwd.")
p.add_argument("--pid", type=int, help="Unity Editor process id when multiple instances are open.")
```

Change base URL construction to:

```python
host, port, source = resolve_endpoint(
    SERVICE_ID,
    args.host,
    args.port,
    DEFAULT_PORT,
    args.project,
    args.pid,
)
return f"http://{host}:{port}"
```

Set per client:

```python
SERVICE_ID = "test-runner-mcp"
```

```python
SERVICE_ID = "unity-editor-debug-mcp"
```

```python
SERVICE_ID = "unity-lua-device-debug"
```

- [x] **Step 3: Make `--port` optional**

Change each parser from:

```python
p.add_argument("--port", type=int, default=DEFAULT_PORT)
```

to:

```python
p.add_argument("--port", type=int, default=None)
```

Keep `DEFAULT_PORT` for legacy fallback.

- [x] **Step 4: Add resolver tests**

Create `tools/test-agent-port-resolvers.py`:

```python
import importlib.util
import json
import os
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
RESOLVER = ROOT / "Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp/port_resolver.py"


def load_resolver():
    spec = importlib.util.spec_from_file_location("port_resolver", RESOLVER)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class PortResolverTests(unittest.TestCase):
    def test_project_ports_json_resolves_single_instance(self):
        resolver = load_resolver()
        with tempfile.TemporaryDirectory() as td:
            project = Path(td)
            (project / "ProjectSettings").mkdir()
            (project / "ProjectSettings" / "ProjectVersion.txt").write_text("m_EditorVersion: 2022.3.62f2c1")
            ports_dir = project / ".u3d-ai-linker"
            ports_dir.mkdir()
            (ports_dir / "ports.json").write_text(json.dumps({
                "instances": [{
                    "serviceId": "test-runner-mcp",
                    "host": "127.0.0.1",
                    "port": 21900,
                    "processId": 100,
                    "projectRoot": project.as_posix(),
                    "instanceId": "a"
                }]
            }), encoding="utf-8")

            host, port, source = resolver.resolve_endpoint(
                "test-runner-mcp", "127.0.0.1", None, 21890, str(project), None)

            self.assertEqual(host, "127.0.0.1")
            self.assertEqual(port, 21900)
            self.assertEqual(source, "project-registry")

    def test_ambiguous_requires_pid(self):
        resolver = load_resolver()
        with tempfile.TemporaryDirectory() as td:
            project = Path(td)
            (project / ".u3d-ai-linker").mkdir()
            (project / ".u3d-ai-linker" / "ports.json").write_text(json.dumps({
                "instances": [
                    {"serviceId": "test-runner-mcp", "port": 21900, "processId": 100, "instanceId": "a"},
                    {"serviceId": "test-runner-mcp", "port": 22000, "processId": 200, "instanceId": "b"}
                ]
            }), encoding="utf-8")

            with self.assertRaises(SystemExit):
                resolver.resolve_endpoint("test-runner-mcp", "127.0.0.1", None, 21890, str(project), None)


if __name__ == "__main__":
    unittest.main()
```

- [x] **Step 5: Verify Python tests**

Run:

```powershell
python tools/test-agent-port-resolvers.py
```

Expected:

```text
Ran 2 tests
OK
```

---

## Task 6: Add U3D AI Linker port UI and diagnostics

**Files:**
- Modify: `Packages/com.yoji.u3d-ai-linker/Editor/Settings/U3DAILinkerSettingsProvider.cs`
- Modify: `Packages/com.yoji.u3d-ai-linker/Tests/Editor/U3DAILinkerSettingsProviderTests.cs`
- Modify: `Packages/com.yoji.u3d-ai-linker/Editor/Settings/PanelStateModel.cs`

- [x] **Step 1: Add diagnostic report expectations**

Extend `U3DAILinkerSettingsProviderTests.BuildDiagnosticReport_IncludesRowsAndSelfStatusInputs` with:

```csharp
StringAssert.Contains("Ports:", report);
StringAssert.Contains("test-runner-mcp", report);
StringAssert.Contains("21900", report);
```

- [x] **Step 2: Add UI section**

In `OnGUI`, after registry status and before action buttons, draw:

```csharp
EditorGUILayout.Space();
EditorGUILayout.LabelField("Ports", EditorStyles.boldLabel);
EditorGUILayout.HelpBox(
    "Ports are project-aware. Agent clients resolve .u3d-ai-linker/ports.json before falling back to legacy defaults.",
    MessageType.Info);

var portsPath = Path.Combine(ProjectRoot, ".u3d-ai-linker", "ports.json");
EditorGUILayout.LabelField("Ports File", File.Exists(portsPath) ? portsPath : "not generated yet");
if (GUILayout.Button("Open Ports File") && File.Exists(portsPath))
    EditorUtility.RevealInFinder(portsPath);
```

Add a small table from parsed `ports.json`:

```text
serviceId | port | pid | source | lastSeenUtc
```

- [x] **Step 3: Add copy diagnostics content**

Append to `BuildDiagnosticReport(...)`:

```csharp
sb.AppendLine("Ports:");
sb.AppendLine(LoadPortsSummary(ProjectRoot));
```

Implement `LoadPortsSummary` as a private static method that reads `.u3d-ai-linker/ports.json` and returns `missing`, `invalid json`, or one line per instance.

- [x] **Step 4: Verify linker tests**

Run:

```powershell
pwsh -NoProfile -File tools/run-editmode.ps1 -Project u3d-ai-linker
```

Expected:

```text
[run-editmode] ===== RESULT: Passed =====
[run-editmode] all green
```

---

## Task 7: Update skills, README, and progress

**Files:**
- Modify: `Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp/SKILL.md`
- Modify: `Packages/com.yoji.editor-debug/Agent~/skills/unity-editor-debug-mcp/SKILL.md`
- Modify: `Packages/com.yoji.lua-device-debug/Agent~/skills/unity-lua-device-debug/SKILL.md`
- Modify: `README.md`
- Modify: `progress.md`

- [x] **Step 1: Update skill command examples**

For every skill, include:

```powershell
python client.py --project G:\Side_Projects\HD2D-U3D\HD2D-Demo ping
python client.py --pid 76792 ping
python client.py --port 21890 ping
```

Explain resolution order:

```text
Explicit --port wins.
Then --project / current cwd .u3d-ai-linker/ports.json.
Then machine registry.
Then legacy defaults.
```

- [x] **Step 2: Update README Ports section**

Replace fixed port table with:

```markdown
| Tool | Legacy port | Project-aware offset |
|------|-------------|----------------------|
| `test-runner-mcp` | `21890`, fallback `21896/21897` | `base + 0` |
| `unity-editor-debug-mcp` | `21891`, fallback `21892/21893` | `base + 1` |
| `unity-lua-device-debug` | `21894` | `base + 4` |
```

Add:

```markdown
For multiple open Unity projects, use `--project` or run clients from the Unity project root. The clients read `.u3d-ai-linker/ports.json` first.
```

- [x] **Step 3: Verify docs**

Run:

```powershell
git diff --check -- README.md progress.md Packages/com.yoji.*\Agent~\skills\*\SKILL.md
```

Expected: exit code `0`.

---

## Task 8: Live multi-project smoke

**Files:**
- No source files.
- Use two Unity projects already present on the workstation.

- [ ] **Step 1: Open two Unity projects**

Open:

```text
G:\Side_Projects\HD2D-U3D\HD2D-Demo
G:\Side_Projects\U3D-Dev-Tools-AI\TestProjects\test-runner
```

- [ ] **Step 2: Read generated port files**

Run:

```powershell
Get-Content G:\Side_Projects\HD2D-U3D\HD2D-Demo\.u3d-ai-linker\ports.json
Get-Content G:\Side_Projects\U3D-Dev-Tools-AI\TestProjects\test-runner\.u3d-ai-linker\ports.json
```

Expected:

```text
Different ports for the same service ids.
Each record has projectRoot, processId, instanceId, portSource.
```

- [ ] **Step 3: Ping each project without manual port**

Run from any directory:

```powershell
python G:\Side_Projects\HD2D-U3D\HD2D-Demo\.u3d-ai-linker\skills\test-runner\client.py --project G:\Side_Projects\HD2D-U3D\HD2D-Demo ping
python G:\Side_Projects\U3D-Dev-Tools-AI\TestProjects\test-runner\.u3d-ai-linker\skills\test-runner\client.py --project G:\Side_Projects\U3D-Dev-Tools-AI\TestProjects\test-runner ping
```

Expected:

```text
Both return ok.
Returned projectRoot matches the requested --project.
Returned ports are different if both Editors are open.
```

- [ ] **Step 4: Force ambiguity**

Open the same Unity project twice from two different clones or two Editor processes pointing at the same project root if Unity allows it.

Run:

```powershell
python .u3d-ai-linker\skills\test-runner\client.py --project G:\Side_Projects\HD2D-U3D\HD2D-Demo ping
```

Expected:

```text
ambiguous test-runner-mcp instances; pass --pid or --port
```

Then run:

```powershell
python .u3d-ai-linker\skills\test-runner\client.py --project G:\Side_Projects\HD2D-U3D\HD2D-Demo --pid <pid> ping
```

Expected:

```text
The selected pid responds.
```

---

## Task 9: Release

**Files:**
- Modify package versions if release is requested:
  - `Packages/com.yoji.editor-core/package.json`
  - `Packages/com.yoji.test-runner/package.json`
  - `Packages/com.yoji.editor-debug/package.json`
  - `Packages/com.yoji.lua-device-debug/package.json`
  - `Packages/com.yoji.u3d-ai-linker/package.json`
  - `Registry/stable.json`
  - `Packages/com.yoji.u3d-ai-linker/Registry/stable.json`

- [ ] **Step 1: Run full EditMode suite**

Run:

```powershell
pwsh -NoProfile -File tools/run-editmode.ps1 -Project editor-core
pwsh -NoProfile -File tools/run-editmode.ps1 -Project test-runner
pwsh -NoProfile -File tools/run-editmode.ps1 -Project editor-debug
pwsh -NoProfile -File tools/run-editmode.ps1 -Project lua-device-debug
pwsh -NoProfile -File tools/run-editmode.ps1 -Project u3d-ai-linker
python tools/test-agent-port-resolvers.py
```

Expected:

```text
All Unity runs end with RESULT: Passed.
Python unittest ends with OK.
```

- [ ] **Step 2: Run stable registry check**

Run:

```powershell
pwsh -NoProfile -File tools/check-u3d-linker-stable.ps1 -RepoRoot G:\Side_Projects\U3D-Dev-Tools-AI
```

Expected:

```text
stable release checks passed
```

- [ ] **Step 3: Commit**

Run:

```powershell
git status --short
git add Packages/com.yoji.editor-core Packages/com.yoji.test-runner Packages/com.yoji.editor-debug Packages/com.yoji.lua-device-debug Packages/com.yoji.u3d-ai-linker TestProjects/editor-core tools README.md progress.md docs/superpowers/plans/2026-06-17-multi-project-port-allocation.md
git diff --cached --check
git commit -m "feat: add project-aware Unity tool ports"
```

Expected:

```text
[main <sha>] feat: add project-aware Unity tool ports
```

- [ ] **Step 4: Tag only after live smoke**

If this is released as stable, use new tags:

```powershell
git tag editor-core-v0.1.1
git tag test-runner-v0.1.5
git tag editor-debug-v0.1.1
git tag lua-device-debug-v0.1.1
git tag u3d-ai-linker-v0.1.4
git push origin main
git push origin editor-core-v0.1.1 test-runner-v0.1.5 editor-debug-v0.1.1 lua-device-debug-v0.1.1 u3d-ai-linker-v0.1.4
```

Do not tag if the two-project smoke is not green.

---

## Failure Modes Covered

- Two different Unity projects open at the same time: separate project-aware ports.
- Same project opened twice: clients report ambiguity unless `--pid` or `--port` is supplied.
- First project still works with legacy defaults when legacy ports are free.
- Second project does not fail just because legacy ports are occupied.
- `lua-device-debug` no longer blocks a second Editor project on fixed `21894`.
- Android Development Build can use a non-default remote port through a config asset.
- Agent clients no longer need humans to inspect `netstat` for the common path.
- Stale registry entries do not win over live `/ping` because clients verify endpoints before returning a resolved target.

## Self-Review

- Spec coverage: multi-project Editor collision, `lua-device-debug` fixed port, client discovery, UI diagnostics, docs, live verification, and release path are covered.
- Placeholder scan: no deferred implementation names are used without a file and task.
- Type consistency: `ServicePortDefinition`, `ServicePortPolicy`, `ServicePortAssignment`, `ServiceInstanceRecord`, and `ProjectPortsFile` are defined before downstream tasks reference them.

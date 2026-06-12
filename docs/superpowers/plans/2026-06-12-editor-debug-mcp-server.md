# EditorDebugMCP Unity 服务端实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 按仓库已有 `unity-editor-debug-mcp/references/protocol.md` 规范，实现缺失的 Unity 侧 C# HTTP 反射服务端，使 `client.py` 与 `run-e2e.py` 13 条用例全部通过。

**Architecture:** `[InitializeOnLoad]` 在 Editor 内起 `HttpListener`（127.0.0.1:21891，fallback 21892/21893），后台线程收请求、经 `MainThreadDispatcher` 派发到主线程执行反射调用，结果按协议序列化（UnityEngine.Object 降级 ref 摘要、VisualElement 树摘要、深度/节点/4MB 熔断）。按 `docs/superpowers/specs/2026-06-12-u3d-ai-linker-design.md` 锁定的仓库结构落位：UPM 包 `Packages/com.yoji.editor-debug/`，skill 资产移入包内 `Agent~/skills/unity-editor-debug-mcp/`。

**Tech Stack:** Unity 6000.3.16f1（本机 `E:\Unity\6000.3.16f1\Editor\Unity.exe`，Task 1 验证）、C#、Newtonsoft.Json（`com.unity.nuget.newtonsoft-json`）、Unity Test Framework（EditMode 单测，batchmode 跑）、Python 3（e2e 验收）。

---

## 背景知识（执行者必读）

1. **规范来源**（全部在 `unity-editor-debug-mcp/references/` 下，实现前先读）：
   - `protocol.md` — 请求/响应信封、ref 摘要、占位符、类型解析、重载决议（**唯一权威**）
   - `run-e2e.py` — 13 条验收用例，注意 case 10-13 要求 `/eval` 真实可用（轻量表达式解析器，语法错误抛 `System.FormatException`），case 05/09 要求未知类型抛 `System.TypeAccessException`
   - `api-cookbook.md` — describe 输出格式样例、recompile 响应 `{success, compilationTime, hasErrors}`
   - `troubleshooting.md` — 错误类型对照表（MissingMethodException / AmbiguousMatchException / Conflict 等）
2. **错误信封**：HTTP 状态码恒 200，`ok:false` 时 `error:{type,message,stack,inner}`，`type` = 异常 `GetType().FullName`。
3. **线程纪律**：所有 Unity API（含反射调用目标和结果序列化）必须在主线程执行。HTTP 线程只做收发与 JSON 解析。注意单元测试线程即主线程，**测试中绝不能调 `MainThreadDispatcher.Run`**（会死锁），直接调用被测类。
4. **C# 是编译型语言**：TDD 的"写失败测试"步骤 = 先建带 `NotImplementedException` 的桩类 + 测试，跑测试看到 FAIL（而非编译错误），再实现。
5. **batchmode 纪律**（来自用户全局经验）：连续两次 Unity batchmode 之间必须等上一个 Unity 进程完全退出；不要并行开两个 Unity 实例打同一工程。

## 通用命令（PowerShell）

```powershell
$UNITY = "E:\Unity\6000.3.16f1\Editor\Unity.exe"
$REPO  = "E:\Yoji\U3D-Dev-Tools-AI"
$TP    = "$REPO\TestProjects\editor-debug"

# 跑 EditMode 单测（每个 TDD 任务都用这条）
function Run-EditModeTests {
    & $UNITY -batchmode -projectPath $TP -runTests -testPlatform EditMode `
        -testResults "$TP\TestResults\results.xml" -logFile "$TP\TestResults\unity.log"
    $exit = $LASTEXITCODE
    Write-Host "exit=$exit"
    Select-String -Path "$TP\TestResults\results.xml" -Pattern 'result="(Passed|Failed)"' | Select-Object -First 3
}
# 等 Unity 进程退干净再跑下一条
function Wait-UnityGone { while (Get-Process Unity -ErrorAction SilentlyContinue) { Start-Sleep 2 } }
```

测试通过 = 退出码 0 且 results.xml 根节点 `result="Passed"`。退出码非 0 时看 `unity.log` 尾部编译错误。

## 文件结构（最终形态）

```
U3D-Dev-Tools-AI/
├── Packages/
│   └── com.yoji.editor-debug/
│       ├── package.json
│       ├── Editor/
│       │   ├── Yoji.EditorDebug.Editor.asmdef
│       │   ├── AssemblyInfo.cs            # InternalsVisibleTo 测试程序集
│       │   ├── EditorDebugMCP.cs          # HttpListener 生命周期 + 路由 + 信封 + 4MB 熔断
│       │   ├── MainThreadDispatcher.cs    # HTTP 线程 -> 主线程派发
│       │   ├── TypeResolver.cs            # 类型名解析（preferred 程序集 -> 全扫描）
│       │   ├── ArgumentCoercer.cs         # JSON 实参 -> CLR 类型
│       │   ├── ReflectionInvoker.cs       # get/set/call/index + 链式 steps + 重载决议
│       │   ├── ResultSerializer.cs        # ref 摘要 / VisualElement 树 / 熔断 / 占位符
│       │   ├── DescribeHandler.cs         # 成员清单
│       │   ├── EvalParser.cs              # /eval 轻量表达式解析器
│       │   └── RecompileHandler.cs        # /recompile
│       ├── Tests/Editor/
│       │   ├── Yoji.EditorDebug.Editor.Tests.asmdef
│       │   ├── InvokeProbe.cs             # 反射测试靶子类
│       │   ├── TypeResolverTests.cs
│       │   ├── ArgumentCoercerTests.cs
│       │   ├── ResultSerializerTests.cs
│       │   ├── ReflectionInvokerTests.cs
│       │   ├── DescribeHandlerTests.cs
│       │   └── EvalParserTests.cs
│       └── Agent~/skills/unity-editor-debug-mcp/   # 原 unity-editor-debug-mcp/ 整体移入
│           ├── SKILL.md  ├── client.py  ├── install.ps1  └── references/
├── TestProjects/editor-debug/             # 最小验证工程（Library 不入库）
│   ├── Packages/manifest.json
│   └── ProjectSettings/ProjectVersion.txt
└── docs/ / README.md / Registry/（Registry 属 Linker 计划，本计划不建）
```

命名口径：服务名仍叫 `EditorDebugMCP`（e2e case 01 校验 `service=="EditorDebugMCP"`），UPM 包名按设计文档用 `com.yoji.editor-debug`（替代文档中旧的 `com.tfw.unity-editor-debug-mcp`，Task 10 同步改文档）。

---

### Task 1: 仓库脚手架 + 包能被 Unity 解析

**Files:**
- Create: `Packages/com.yoji.editor-debug/package.json`
- Create: `Packages/com.yoji.editor-debug/Editor/Yoji.EditorDebug.Editor.asmdef`
- Create: `Packages/com.yoji.editor-debug/Editor/AssemblyInfo.cs`
- Create: `Packages/com.yoji.editor-debug/Tests/Editor/Yoji.EditorDebug.Editor.Tests.asmdef`
- Create: `TestProjects/editor-debug/Packages/manifest.json`
- Create: `TestProjects/editor-debug/ProjectSettings/ProjectVersion.txt`
- Create: `.gitignore`
- Move: `unity-editor-debug-mcp/` -> `Packages/com.yoji.editor-debug/Agent~/skills/unity-editor-debug-mcp/`

- [ ] **Step 1: 验证 Unity 安装路径**

```powershell
Test-Path "E:\Unity\6000.3.16f1\Editor\Unity.exe"
```
Expected: `True`。如果 False，用 `Get-ChildItem E:\Unity -Directory` 找实际版本目录并全局替换 `$UNITY`（必须 6000.3.x）。

- [ ] **Step 2: 创建 package.json**

`Packages/com.yoji.editor-debug/package.json`:
```json
{
  "name": "com.yoji.editor-debug",
  "version": "0.1.0",
  "displayName": "Editor Debug MCP",
  "description": "HTTP+JSON reflection service inside Unity Editor for AI agent debugging.",
  "unity": "2022.3",
  "dependencies": {
    "com.unity.nuget.newtonsoft-json": "3.2.1"
  }
}
```

- [ ] **Step 3: 创建 Editor asmdef 与 AssemblyInfo**

`Packages/com.yoji.editor-debug/Editor/Yoji.EditorDebug.Editor.asmdef`:
```json
{
  "name": "Yoji.EditorDebug.Editor",
  "rootNamespace": "Yoji.EditorDebug",
  "references": [],
  "includePlatforms": ["Editor"],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": false,
  "precompiledReferences": [],
  "autoReferenced": true,
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": false
}
```

`Packages/com.yoji.editor-debug/Editor/AssemblyInfo.cs`:
```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Yoji.EditorDebug.Editor.Tests")]
```

- [ ] **Step 4: 创建 Tests asmdef**

`Packages/com.yoji.editor-debug/Tests/Editor/Yoji.EditorDebug.Editor.Tests.asmdef`:
```json
{
  "name": "Yoji.EditorDebug.Editor.Tests",
  "rootNamespace": "Yoji.EditorDebug.Tests",
  "references": [
    "Yoji.EditorDebug.Editor",
    "UnityEngine.TestRunner",
    "UnityEditor.TestRunner"
  ],
  "includePlatforms": ["Editor"],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": true,
  "precompiledReferences": ["nunit.framework.dll", "Newtonsoft.Json.dll"],
  "autoReferenced": false,
  "defineConstraints": ["UNITY_INCLUDE_TESTS"],
  "versionDefines": [],
  "noEngineReferences": false
}
```

- [ ] **Step 5: 创建最小测试工程**

`TestProjects/editor-debug/Packages/manifest.json`（`file:` 路径相对本 manifest 所在 Packages 目录）:
```json
{
  "dependencies": {
    "com.unity.test-framework": "1.5.1",
    "com.yoji.editor-debug": "file:../../../Packages/com.yoji.editor-debug",
    "com.unity.modules.imgui": "1.0.0",
    "com.unity.modules.jsonserialize": "1.0.0",
    "com.unity.modules.uielements": "1.0.0",
    "com.unity.modules.physics": "1.0.0"
  },
  "testables": ["com.yoji.editor-debug"]
}
```

`TestProjects/editor-debug/ProjectSettings/ProjectVersion.txt`:
```
m_EditorVersion: 6000.3.16f1
```

- [ ] **Step 6: 创建仓库 .gitignore**

`.gitignore`（仓库根，原本没有）:
```
TestProjects/*/Library/
TestProjects/*/Logs/
TestProjects/*/UserSettings/
TestProjects/*/TestResults/
TestProjects/*/Temp/
TestProjects/*/obj/
TestProjects/*/Assets/
TestProjects/*/.vs/
*.csproj
*.sln
```
（Assets/ 也忽略：最小工程的 Assets 由 Unity 自动生成，内容无价值。）

- [ ] **Step 7: 迁移 skill 资产到包内 Agent~**

```powershell
New-Item -ItemType Directory -Force "$REPO\Packages\com.yoji.editor-debug\Agent~\skills"
git -C $REPO mv unity-editor-debug-mcp "Packages/com.yoji.editor-debug/Agent~/skills/unity-editor-debug-mcp"
```
Expected: `git status` 显示 renamed 条目，原目录消失。

- [ ] **Step 8: batchmode 冒烟验证包可解析**

```powershell
& $UNITY -batchmode -quit -projectPath $TP -logFile "$TP\TestResults\bootstrap.log"
Write-Host "exit=$LASTEXITCODE"
```
Expected: 退出码 0。首次会生成 Library（约 1-3 分钟）。失败时 `Select-String -Path "$TP\TestResults\bootstrap.log" -Pattern "error|Error CS" | Select-Object -First 10` 排查（常见：manifest file: 路径写错、asmdef JSON 语法错）。

- [ ] **Step 9: Commit**

```powershell
git -C $REPO add -A
git -C $REPO commit -m "feat(editor-debug): scaffold UPM package com.yoji.editor-debug and test project"
```

---

### Task 2: TypeResolver（TDD）

**Files:**
- Create: `Packages/com.yoji.editor-debug/Editor/TypeResolver.cs`
- Test: `Packages/com.yoji.editor-debug/Tests/Editor/TypeResolverTests.cs`

- [ ] **Step 1: 写桩 + 失败测试**

`Editor/TypeResolver.cs`（桩）:
```csharp
using System;

namespace Yoji.EditorDebug
{
    internal static class TypeResolver
    {
        public static Type Resolve(string typeName) => throw new NotImplementedException();

        public static bool TryResolve(string typeName, out Type type)
        {
            try { type = Resolve(typeName); return true; }
            catch (TypeAccessException) { type = null; return false; }
        }
    }
}
```

`Tests/Editor/TypeResolverTests.cs`:
```csharp
using System;
using NUnit.Framework;

namespace Yoji.EditorDebug.Tests
{
    public class TypeResolverTests
    {
        [Test] public void Resolve_PublicEditorType()
            => Assert.AreEqual("UnityEditor.Selection", TypeResolver.Resolve("UnityEditor.Selection").FullName);

        [Test] public void Resolve_InternalEditorType()
            => Assert.AreEqual("UnityEditor.LogEntries", TypeResolver.Resolve("UnityEditor.LogEntries").FullName);

        [Test] public void Resolve_InternalTypeInCoreModule()
            => Assert.AreEqual("UnityEditorInternal.ProfilerDriver",
                TypeResolver.Resolve("UnityEditorInternal.ProfilerDriver").FullName);

        [Test] public void Resolve_WithExplicitAssemblySuffix()
            => Assert.AreEqual("UnityEditor.LogEntries", TypeResolver.Resolve("UnityEditor.LogEntries, UnityEditor").FullName);

        [Test] public void Resolve_SystemType()
            => Assert.AreEqual(typeof(string), TypeResolver.Resolve("System.String"));

        [Test] public void Resolve_TestAssemblyType_ViaFullScan()
            => Assert.AreEqual(typeof(TypeResolverTests), TypeResolver.Resolve("Yoji.EditorDebug.Tests.TypeResolverTests"));

        [Test] public void Resolve_Unknown_ThrowsTypeAccessException()
            => Assert.Throws<TypeAccessException>(() => TypeResolver.Resolve("Foo.NotExisting.For.E2E"));

        [Test] public void Resolve_Empty_ThrowsTypeAccessException()
            => Assert.Throws<TypeAccessException>(() => TypeResolver.Resolve("  "));

        [Test] public void TryResolve_Unknown_ReturnsFalse()
        {
            Assert.IsFalse(TypeResolver.TryResolve("Foo.Bar.Baz", out var t));
            Assert.IsNull(t);
        }
    }
}
```
注意：测试命名空间是 `Yoji.EditorDebug.Tests`，被测类在 `Yoji.EditorDebug`（internal，靠 AssemblyInfo 的 InternalsVisibleTo 可见），测试文件顶部不需要 using 被测命名空间之外的东西时也要 `using Yoji.EditorDebug;`——本文件因同根命名空间前缀可直接解析，无需额外 using。

- [ ] **Step 2: 跑测试确认失败**

```powershell
Wait-UnityGone; Run-EditModeTests
```
Expected: 退出码非 0，results.xml 中 TypeResolver 各用例 FAIL（NotImplementedException）。

- [ ] **Step 3: 实现**

`Editor/TypeResolver.cs`（完整替换）:
```csharp
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Yoji.EditorDebug
{
    /// 类型名解析。顺序：Type.GetType -> 显式程序集后缀 -> 常用 Unity 程序集 -> 全程序集扫描。
    /// 找不到抛 TypeAccessException（协议约定的错误类型）。
    internal static class TypeResolver
    {
        private static readonly string[] s_PreferredAssemblies =
        {
            "UnityEditor", "UnityEngine",
            "UnityEditor.CoreModule", "UnityEngine.CoreModule",
            "UnityEngine.UIElementsModule", "UnityEditor.UIElementsModule",
            "UnityEngine.PhysicsModule", "UnityEngine.AnimationModule",
            "UnityEngine.ProfilerModule",
            "mscorlib", "netstandard",
        };

        public static Type Resolve(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                throw new TypeAccessException("type name is empty");
            typeName = typeName.Trim();

            var t = Type.GetType(typeName);
            if (t != null) return t;

            var all = AppDomain.CurrentDomain.GetAssemblies();
            var byName = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
            foreach (var asm in all) byName[asm.GetName().Name] = asm;

            var comma = typeName.IndexOf(',');
            if (comma > 0)
            {
                var pureName = typeName.Substring(0, comma).Trim();
                var asmName = typeName.Substring(comma + 1).Trim();
                if (byName.TryGetValue(asmName, out var named))
                {
                    t = named.GetType(pureName);
                    if (t != null) return t;
                }
                typeName = pureName;
            }

            foreach (var name in s_PreferredAssemblies)
            {
                if (byName.TryGetValue(name, out var asm))
                {
                    t = asm.GetType(typeName);
                    if (t != null) return t;
                }
            }

            foreach (var asm in all)
            {
                t = asm.GetType(typeName);
                if (t != null) return t;
            }
            throw new TypeAccessException("type not found: " + typeName);
        }

        public static bool TryResolve(string typeName, out Type type)
        {
            try { type = Resolve(typeName); return true; }
            catch (TypeAccessException) { type = null; return false; }
        }
    }
}
```

- [ ] **Step 4: 跑测试确认通过**

```powershell
Wait-UnityGone; Run-EditModeTests
```
Expected: 退出码 0，全部 PASS。

- [ ] **Step 5: Commit**

```powershell
git -C $REPO add -A; git -C $REPO commit -m "feat(editor-debug): TypeResolver with preferred-assembly then full scan"
```

---

### Task 3: DescribeHandler（TDD）

**Files:**
- Create: `Packages/com.yoji.editor-debug/Editor/DescribeHandler.cs`
- Test: `Packages/com.yoji.editor-debug/Tests/Editor/DescribeHandlerTests.cs`

- [ ] **Step 1: 写桩 + 失败测试**

`Editor/DescribeHandler.cs`（桩）:
```csharp
using System;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace Yoji.EditorDebug
{
    internal static class DescribeHandler
    {
        public static JObject Describe(string typeName) => throw new NotImplementedException();
        public static string Signature(MethodInfo m) => throw new NotImplementedException();
    }
}
```

`Tests/Editor/DescribeHandlerTests.cs`:
```csharp
using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Yoji.EditorDebug.Tests
{
    public class DescribeHandlerTests
    {
        [Test]
        public void Describe_PublicType_HasFullNameAndMethods()
        {
            var r = DescribeHandler.Describe("UnityEditor.Selection");
            Assert.AreEqual("UnityEditor.Selection", (string)r["FullName"]);
            Assert.IsTrue(((JArray)r["Methods"]).Count > 0);
            Assert.IsNotNull(r["Properties"]);
            Assert.IsNotNull(r["Fields"]);
        }

        [Test]
        public void Describe_InternalType_Works()
        {
            var r = DescribeHandler.Describe("UnityEditorInternal.ProfilerDriver");
            Assert.AreEqual("UnityEditorInternal.ProfilerDriver", (string)r["FullName"]);
        }

        [Test]
        public void Describe_MethodSignature_ContainsAccessAndStatic()
        {
            var r = DescribeHandler.Describe("UnityEngine.Mathf");
            var methods = ((JArray)r["Methods"]).Select(x => (string)x).ToList();
            Assert.IsTrue(methods.Any(m => m.StartsWith("public static") && m.Contains("Min(")),
                "expect a 'public static ... Min(...)' signature, got: " + string.Join(" | ", methods.Take(5)));
        }

        [Test]
        public void Describe_Unknown_ThrowsTypeAccessException()
            => Assert.Throws<TypeAccessException>(() => DescribeHandler.Describe("Foo.Bar.Whatever.For.E2E"));
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

```powershell
Wait-UnityGone; Run-EditModeTests
```
Expected: Describe 用例 FAIL，Task 2 用例保持 PASS。

- [ ] **Step 3: 实现**

`Editor/DescribeHandler.cs`（完整替换）:
```csharp
using System;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace Yoji.EditorDebug
{
    /// /describe：列出类型全部成员（含 internal/private）的人类可读签名。
    internal static class DescribeHandler
    {
        private const BindingFlags k_All = BindingFlags.Public | BindingFlags.NonPublic |
                                           BindingFlags.Instance | BindingFlags.Static;

        public static JObject Describe(string typeName)
        {
            var type = TypeResolver.Resolve(typeName);
            return new JObject
            {
                ["FullName"] = type.FullName,
                ["Assembly"] = type.Assembly.GetName().Name,
                ["Methods"] = new JArray(type.GetMethods(k_All)
                    .Where(m => !m.IsSpecialName).Select(Signature).Cast<object>().ToArray()),
                ["Properties"] = new JArray(type.GetProperties(k_All).Select(p =>
                    Access(p.GetGetMethod(true) ?? p.GetSetMethod(true)) + " " +
                    Short(p.PropertyType) + " " + p.Name + " { " +
                    (p.CanRead ? "get; " : "") + (p.CanWrite ? "set; " : "") + "}")
                    .Cast<object>().ToArray()),
                ["Fields"] = new JArray(type.GetFields(k_All).Select(f =>
                    FieldAccess(f) + (f.IsStatic ? " static " : " ") + Short(f.FieldType) + " " + f.Name)
                    .Cast<object>().ToArray()),
            };
        }

        public static string Signature(MethodInfo m) =>
            Access(m) + (m.IsStatic ? " static " : " ") + Short(m.ReturnType) + " " + m.Name + "(" +
            string.Join(", ", m.GetParameters().Select(p => Short(p.ParameterType) + " " + p.Name)) + ")";

        private static string Access(MethodBase m) =>
            m == null ? "?" : m.IsPublic ? "public" : m.IsFamily ? "protected" : m.IsAssembly ? "internal" : "private";

        private static string FieldAccess(FieldInfo f) =>
            f.IsPublic ? "public" : f.IsFamily ? "protected" : f.IsAssembly ? "internal" : "private";

        private static string Short(Type t) => t.IsGenericType
            ? t.Name.Split('`')[0] + "<" + string.Join(",", t.GetGenericArguments().Select(Short)) + ">"
            : t.Name;
    }
}
```

- [ ] **Step 4: 跑测试确认通过**

```powershell
Wait-UnityGone; Run-EditModeTests
```
Expected: 全部 PASS。

- [ ] **Step 5: Commit**

```powershell
git -C $REPO add -A; git -C $REPO commit -m "feat(editor-debug): /describe member listing with readable signatures"
```

---

### Task 4: ArgumentCoercer + 测试靶子类（TDD）

**Files:**
- Create: `Packages/com.yoji.editor-debug/Editor/ArgumentCoercer.cs`
- Create: `Packages/com.yoji.editor-debug/Tests/Editor/InvokeProbe.cs`
- Test: `Packages/com.yoji.editor-debug/Tests/Editor/ArgumentCoercerTests.cs`

- [ ] **Step 1: 写靶子类（后续 Task 6/7 复用）**

`Tests/Editor/InvokeProbe.cs`:
```csharp
using System.Collections.Generic;
using UnityEngine;

namespace Yoji.EditorDebug.Tests
{
    /// 反射调用测试靶子。所有状态静态，每个测试自己 Reset。
    public class InvokeProbe
    {
        public static int StaticField = 7;
        public static string StaticProp { get; set; } = "hello";
        private static int s_Secret = 42;

        public int InstanceValue = 5;

        public static void Reset() { StaticField = 7; StaticProp = "hello"; }

        public static int Add(int a, int b) => a + b;
        public static string Concat(string a, string b) => a + b;
        public static int Mul(int a, int b = 3) => a * b;
        public static string Kind(int v) => "int";
        public static string Kind(string v) => "string";
        public static string EnumName(KeyCode k) => k.ToString();
        public static List<int> Numbers() => new List<int> { 1, 2, 3 };
        public static void DoNothing() { }
    }
}
```

- [ ] **Step 2: 写桩 + 失败测试**

`Editor/ArgumentCoercer.cs`（桩）:
```csharp
using System;
using Newtonsoft.Json.Linq;

namespace Yoji.EditorDebug
{
    internal static class ArgumentCoercer
    {
        public static object Coerce(JToken arg, Type targetType) => throw new NotImplementedException();
    }
}
```

`Tests/Editor/ArgumentCoercerTests.cs`:
```csharp
using System;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Yoji.EditorDebug.Tests
{
    public class ArgumentCoercerTests
    {
        private static JToken J(string json) => JToken.Parse(json);

        [Test] public void Coerce_Null_ReturnsNull()
            => Assert.IsNull(ArgumentCoercer.Coerce(J("null"), typeof(string)));

        [Test] public void Coerce_IntToInt32()
            => Assert.AreEqual(3, ArgumentCoercer.Coerce(J("3"), typeof(int)));

        [Test] public void Coerce_IntToSingle()
            => Assert.AreEqual(3f, ArgumentCoercer.Coerce(J("3"), typeof(float)));

        [Test] public void Coerce_StringToType()
            => Assert.AreEqual(typeof(GameObject),
                ArgumentCoercer.Coerce(J("\"UnityEngine.GameObject\""), typeof(Type)));

        [Test] public void Coerce_DottedStringToEnum()
            => Assert.AreEqual(KeyCode.A,
                ArgumentCoercer.Coerce(J("\"UnityEngine.KeyCode.A\""), typeof(KeyCode)));

        [Test] public void Coerce_BareStringToEnum()
            => Assert.AreEqual(KeyCode.Space, ArgumentCoercer.Coerce(J("\"Space\""), typeof(KeyCode)));

        [Test] public void Coerce_NumberToEnum()
            => Assert.AreEqual(KeyCode.A, ArgumentCoercer.Coerce(J(((int)KeyCode.A).ToString()), typeof(KeyCode)));

        [Test] public void Coerce_InstanceIdObjectToUnityObject()
        {
            var go = new GameObject("CoerceProbe");
            try
            {
                var arg = J("{\"instanceID\":" + go.GetInstanceID() + "}");
                Assert.AreSame(go, ArgumentCoercer.Coerce(arg, typeof(GameObject)));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test] public void Coerce_ToObject_KeepsNaturalJsonType()
        {
            Assert.AreEqual(5L, ArgumentCoercer.Coerce(J("5"), typeof(object)));
            Assert.AreEqual("x", ArgumentCoercer.Coerce(J("\"x\""), typeof(object)));
            Assert.AreEqual(true, ArgumentCoercer.Coerce(J("true"), typeof(object)));
        }
    }
}
```

- [ ] **Step 3: 跑测试确认失败**

```powershell
Wait-UnityGone; Run-EditModeTests
```
Expected: Coercer 用例 FAIL，其余 PASS。

- [ ] **Step 4: 实现**

`Editor/ArgumentCoercer.cs`（完整替换）:
```csharp
using System;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace Yoji.EditorDebug
{
    /// 把 JSON 实参转换成目标 CLR 类型。
    /// 特殊规则：string -> System.Type（走 TypeResolver）；string -> enum（容忍带命名空间前缀的写法）；
    /// {instanceID:N} -> UnityEngine.Object 反查。
    internal static class ArgumentCoercer
    {
        public static object Coerce(JToken arg, Type targetType)
        {
            if (arg == null || arg.Type == JTokenType.Null)
                return null;

            if (typeof(Type).IsAssignableFrom(targetType) && arg.Type == JTokenType.String)
                return TypeResolver.Resolve(arg.Value<string>());

            if (targetType.IsEnum)
            {
                if (arg.Type == JTokenType.String)
                {
                    var s = arg.Value<string>();
                    var lastDot = s.LastIndexOf('.');
                    if (lastDot > 0) s = s.Substring(lastDot + 1);
                    return Enum.Parse(targetType, s, ignoreCase: true);
                }
                return Enum.ToObject(targetType, arg.Value<long>());
            }

            if (typeof(UnityEngine.Object).IsAssignableFrom(targetType) && arg.Type == JTokenType.Object)
            {
                var id = arg["instanceID"];
                if (id != null) return EditorUtility.InstanceIDToObject(id.Value<int>());
            }

            if (targetType == typeof(object))
            {
                switch (arg.Type)
                {
                    case JTokenType.Integer: return arg.Value<long>();
                    case JTokenType.Float: return arg.Value<double>();
                    case JTokenType.String: return arg.Value<string>();
                    case JTokenType.Boolean: return arg.Value<bool>();
                }
            }
            return arg.ToObject(targetType);
        }
    }
}
```

- [ ] **Step 5: 跑测试确认通过**

```powershell
Wait-UnityGone; Run-EditModeTests
```
Expected: 全部 PASS。

- [ ] **Step 6: Commit**

```powershell
git -C $REPO add -A; git -C $REPO commit -m "feat(editor-debug): JSON argument coercion (Type/enum/instanceID/object)"
```

---

### Task 5: ResultSerializer（TDD）

**Files:**
- Create: `Packages/com.yoji.editor-debug/Editor/ResultSerializer.cs`
- Test: `Packages/com.yoji.editor-debug/Tests/Editor/ResultSerializerTests.cs`

- [ ] **Step 1: 写桩 + 失败测试**

`Editor/ResultSerializer.cs`（桩，VoidResult 定义也放这里，Task 6 复用）:
```csharp
using System;
using Newtonsoft.Json.Linq;

namespace Yoji.EditorDebug
{
    /// void 方法返回值哨兵：信封层写 "void":true。
    internal sealed class VoidResult
    {
        public static readonly VoidResult Instance = new VoidResult();
        private VoidResult() { }
    }

    internal sealed class ResultSerializer
    {
        public static JToken ToJson(object value) => throw new NotImplementedException();
    }
}
```

`Tests/Editor/ResultSerializerTests.cs`:
```csharp
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Yoji.EditorDebug.Tests
{
    public class ResultSerializerTests
    {
        [Test] public void Primitive_Int()
            => Assert.AreEqual(3, (int)ResultSerializer.ToJson(3));

        [Test] public void Primitive_String()
            => Assert.AreEqual("abc", (string)ResultSerializer.ToJson("abc"));

        [Test] public void Null_ReturnsJsonNull()
            => Assert.AreEqual(JTokenType.Null, ResultSerializer.ToJson(null).Type);

        [Test] public void Enum_AsName()
            => Assert.AreEqual("A", (string)ResultSerializer.ToJson(KeyCode.A));

        [Test] public void FloatNaN_AsString()
            => Assert.AreEqual("NaN", (string)ResultSerializer.ToJson(float.NaN));

        [Test] public void GameObject_BecomesRefSummary()
        {
            var parent = new GameObject("Root");
            var child = new GameObject("Player");
            child.transform.SetParent(parent.transform);
            try
            {
                var j = (JObject)ResultSerializer.ToJson(child);
                Assert.IsTrue((bool)j["__ref"]);
                Assert.AreEqual(child.GetInstanceID(), (int)j["instanceID"]);
                Assert.AreEqual("UnityEngine.GameObject", (string)j["type"]);
                Assert.AreEqual("Player", (string)j["name"]);
                Assert.AreEqual("Root/Player", (string)j["path"]);
            }
            finally { UnityEngine.Object.DestroyImmediate(parent); }
        }

        [Test] public void DestroyedObject_BecomesNull()
        {
            var go = new GameObject("Doomed");
            UnityEngine.Object.DestroyImmediate(go);
            Assert.AreEqual(JTokenType.Null, ResultSerializer.ToJson(go).Type);
        }

        [Test] public void Array_BecomesJArray()
        {
            var j = (JArray)ResultSerializer.ToJson(new[] { 1, 2, 3 });
            Assert.AreEqual(3, j.Count);
            Assert.AreEqual(2, (int)j[1]);
        }

        [Test] public void Dictionary_BecomesJObject()
        {
            var j = (JObject)ResultSerializer.ToJson(new Dictionary<string, int> { ["a"] = 1 });
            Assert.AreEqual(1, (int)j["a"]);
        }

        [Test] public void Delegate_BecomesPlaceholder()
        {
            Action a = InvokeProbe.Reset;
            var j = (JObject)ResultSerializer.ToJson(a);
            Assert.IsTrue((bool)j["__delegate"]);
            StringAssert.Contains("Reset", (string)j["target"]);
        }

        [Test] public void IntPtr_BecomesSkipped()
        {
            var j = (JObject)ResultSerializer.ToJson(new IntPtr(123));
            Assert.AreEqual("IntPtr", (string)j["__skipped"]);
        }

        [Test] public void Enumerator_BecomesPlaceholder()
        {
            var j = (JObject)ResultSerializer.ToJson(new List<int>().GetEnumerator());
            Assert.IsNotNull(j["__enumerator"]);
        }

        [Test] public void PlainObject_PublicFieldsAndProps()
        {
            var j = (JObject)ResultSerializer.ToJson(new InvokeProbe());
            Assert.AreEqual(5, (int)j["InstanceValue"]);
        }

        [Test] public void DeepNesting_TruncatedAtMaxDepth()
        {
            object node = "leaf";
            for (int i = 0; i < 20; i++) node = new List<object> { node };
            var json = ResultSerializer.ToJson(node).ToString();
            StringAssert.Contains("maxDepth", json);
        }

        [Test] public void ManyNodes_TruncatedAtMaxNodes()
        {
            var big = new List<int>();
            for (int i = 0; i < 3000; i++) big.Add(i);
            var json = ResultSerializer.ToJson(big).ToString();
            StringAssert.Contains("maxNodes", json);
        }

        [Test] public void VisualElement_TreeSummary()
        {
            var root = new VisualElement { name = "GamePlayRoot" };
            root.AddToClassList("ux-td-gameplay");
            var btn = new Button { name = "PauseBtn", text = "||" };
            root.Add(btn);

            var j = (JObject)ResultSerializer.ToJson(root);
            Assert.AreEqual("GamePlayRoot", (string)j["name"]);
            Assert.AreEqual("VisualElement", (string)j["type"]);
            Assert.AreEqual("ux-td-gameplay", (string)j["classList"][0]);
            Assert.AreEqual("NaN", (string)j["layout"]["w"]);
            Assert.AreEqual(1, (int)j["childCount"]);
            var c = (JObject)j["children"][0];
            Assert.AreEqual("Button", (string)c["type"]);
            Assert.AreEqual("||", (string)c["text"]);
        }
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

```powershell
Wait-UnityGone; Run-EditModeTests
```
Expected: Serializer 用例 FAIL，其余 PASS。

- [ ] **Step 3: 实现**

`Editor/ResultSerializer.cs`（完整替换）:
```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Yoji.EditorDebug
{
    /// void 方法返回值哨兵：信封层写 "void":true。
    internal sealed class VoidResult
    {
        public static readonly VoidResult Instance = new VoidResult();
        private VoidResult() { }
    }

    /// CLR 对象 -> JSON 摘要。
    /// 全局熔断 maxDepth=12 / maxNodes=2000；VisualElement 树单独熔断 maxDepth=16 / maxNodes=500。
    /// UnityEngine.Object 降级 ref 摘要；占位符见 protocol.md。
    internal sealed class ResultSerializer
    {
        private const int k_MaxDepth = 12;
        private const int k_MaxNodes = 2000;
        private const int k_VeMaxDepth = 16;
        private const int k_VeMaxNodes = 500;
        private const int k_TaskWaitMs = 3000;

        private int m_Nodes;

        public static JToken ToJson(object value) => new ResultSerializer().Serialize(value, 0);

        private JToken Serialize(object value, int depth)
        {
            m_Nodes++;
            if (m_Nodes > k_MaxNodes) return Truncated("maxNodes");
            if (depth > k_MaxDepth) return Truncated("maxDepth");

            if (value == null || value is VoidResult) return JValue.CreateNull();

            var type = value.GetType();
            if (value is string s) return new JValue(s);
            if (value is float f)
                return float.IsNaN(f) || float.IsInfinity(f) ? (JToken)f.ToString() : new JValue(f);
            if (value is double d)
                return double.IsNaN(d) || double.IsInfinity(d) ? (JToken)d.ToString() : new JValue(d);
            if (value is IntPtr || value is UIntPtr || value is System.Runtime.InteropServices.SafeHandle)
                return new JObject { ["__skipped"] = "IntPtr" };
            if (type.IsPrimitive || value is decimal) return JToken.FromObject(value);
            if (type.IsEnum) return new JValue(value.ToString());
            if (value is Delegate del)
                return new JObject
                {
                    ["__delegate"] = true,
                    ["target"] = (del.Method.DeclaringType?.Name ?? "?") + "." + del.Method.Name,
                };
            if (value is Task task) return SerializeTask(task, depth);
            if (value is VisualElement ve) return new VisualElementWalker().Walk(ve, 0);
            if (value is UnityEngine.Object uo) return SerializeUnityObject(uo);
            if (value is IEnumerator) return new JObject { ["__enumerator"] = type.FullName };
            if (value is IDictionary dict)
            {
                var o = new JObject();
                foreach (DictionaryEntry e in dict)
                {
                    if (m_Nodes > k_MaxNodes) { o["__truncated_entry"] = Truncated("maxNodes"); break; }
                    o[e.Key?.ToString() ?? "null"] = Serialize(e.Value, depth + 1);
                }
                return o;
            }
            if (value is IEnumerable en)
            {
                var arr = new JArray();
                foreach (var item in en)
                {
                    arr.Add(Serialize(item, depth + 1));
                    if (m_Nodes > k_MaxNodes) break;
                }
                return arr;
            }
            return SerializeComplex(value, type, depth);
        }

        private static JToken SerializeUnityObject(UnityEngine.Object uo)
        {
            if (uo == null) return JValue.CreateNull(); // Unity fake null（已销毁对象）
            var o = new JObject
            {
                ["__ref"] = true,
                ["instanceID"] = uo.GetInstanceID(),
                ["type"] = uo.GetType().FullName,
                ["name"] = uo.name,
            };
            var go = uo as GameObject ?? (uo as Component)?.gameObject;
            if (go != null) o["path"] = HierarchyPath(go.transform);
            var assetPath = UnityEditor.AssetDatabase.GetAssetPath(uo);
            if (!string.IsNullOrEmpty(assetPath))
                o["guid"] = UnityEditor.AssetDatabase.AssetPathToGUID(assetPath);
            return o;
        }

        private static string HierarchyPath(Transform t)
        {
            var parts = new List<string>();
            for (; t != null; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        private JToken SerializeTask(Task task, int depth)
        {
            if (!task.Wait(k_TaskWaitMs)) return new JObject { ["__pending"] = true };
            var resultProp = task.GetType().GetProperty("Result");
            return resultProp != null && resultProp.PropertyType.Name != "VoidTaskResult"
                ? Serialize(resultProp.GetValue(task), depth + 1)
                : JValue.CreateNull();
        }

        private JToken SerializeComplex(object value, Type type, int depth)
        {
            var o = new JObject();
            foreach (var fi in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                o[fi.Name] = Guard(() => Serialize(fi.GetValue(value), depth + 1));
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanRead || p.GetIndexParameters().Length > 0) continue;
                o[p.Name] = Guard(() => Serialize(p.GetValue(value), depth + 1));
            }
            return o;
        }

        private static JToken Guard(Func<JToken> read)
        {
            try { return read(); }
            catch (Exception e)
            {
                var inner = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
                return new JObject { ["__error"] = inner.GetType().Name, ["message"] = inner.Message };
            }
        }

        private static JObject Truncated(string reason)
            => new JObject { ["__truncated"] = true, ["reason"] = reason };

        /// VisualElement 树摘要（独立熔断 + 循环检测）。
        private sealed class VisualElementWalker
        {
            private int m_Nodes;
            private readonly HashSet<VisualElement> m_Visited = new HashSet<VisualElement>();

            public JToken Walk(VisualElement ve, int depth)
            {
                if (ve == null) return JValue.CreateNull();
                if (!m_Visited.Add(ve)) return new JObject { ["__cycle"] = ve.name ?? "" };
                m_Nodes++;
                if (m_Nodes > k_VeMaxNodes) return Truncated("maxNodes");
                if (depth > k_VeMaxDepth) return Truncated("maxDepth");

                var o = new JObject
                {
                    ["name"] = ve.name ?? "",
                    ["type"] = ve.GetType().Name,
                    ["classList"] = new JArray(ve.GetClasses().Cast<object>().ToArray()),
                    ["pickingMode"] = ve.pickingMode.ToString(),
                    ["visible"] = ve.visible,
                    ["enabledSelf"] = ve.enabledSelf,
                };
                var r = ve.layout;
                o["layout"] = new JObject
                {
                    ["x"] = Num(r.x), ["y"] = Num(r.y), ["w"] = Num(r.width), ["h"] = Num(r.height),
                };
                if (ve is TextElement te) o["text"] = te.text;
                if (!string.IsNullOrEmpty(ve.viewDataKey)) o["viewDataKey"] = ve.viewDataKey;
                if (ve is IBindable b && !string.IsNullOrEmpty(b.bindingPath)) o["bindingPath"] = b.bindingPath;
                o["childCount"] = ve.childCount;
                var children = new JArray();
                foreach (var child in ve.Children()) children.Add(Walk(child, depth + 1));
                o["children"] = children;
                return o;
            }

            private static JToken Num(float v) => float.IsNaN(v) ? (JToken)"NaN" : new JValue(v);
        }
    }
}
```

- [ ] **Step 4: 跑测试确认通过**

```powershell
Wait-UnityGone; Run-EditModeTests
```
Expected: 全部 PASS。

- [ ] **Step 5: Commit**

```powershell
git -C $REPO add -A; git -C $REPO commit -m "feat(editor-debug): result serializer with ref summary, VE tree, truncation"
```

---

### Task 6: ReflectionInvoker（TDD）

**Files:**
- Create: `Packages/com.yoji.editor-debug/Editor/ReflectionInvoker.cs`
- Test: `Packages/com.yoji.editor-debug/Tests/Editor/ReflectionInvokerTests.cs`

- [ ] **Step 1: 写桩 + 失败测试**

`Editor/ReflectionInvoker.cs`（桩）:
```csharp
using System;
using Newtonsoft.Json.Linq;

namespace Yoji.EditorDebug
{
    internal static class ReflectionInvoker
    {
        public static object Execute(JObject req) => throw new NotImplementedException();
        internal static object ExecuteStep(object target, Type type, JObject step) => throw new NotImplementedException();
    }
}
```

`Tests/Editor/ReflectionInvokerTests.cs`:
```csharp
using System;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Yoji.EditorDebug.Tests
{
    public class ReflectionInvokerTests
    {
        private const string Probe = "Yoji.EditorDebug.Tests.InvokeProbe";

        [SetUp] public void SetUp() => InvokeProbe.Reset();

        private static object Exec(string json) => ReflectionInvoker.Execute(JObject.Parse(json));

        [Test] public void Get_StaticProperty()
            => Assert.AreEqual("hello", Exec("{\"type\":\"" + Probe + "\",\"member\":\"StaticProp\",\"kind\":\"get\"}"));

        [Test] public void Get_PrivateStaticField()
            => Assert.AreEqual(42, Exec("{\"type\":\"" + Probe + "\",\"member\":\"s_Secret\",\"kind\":\"get\"}"));

        [Test] public void Get_MissingMember_Throws()
            => Assert.Throws<MissingMethodException>(() =>
                Exec("{\"type\":\"" + Probe + "\",\"member\":\"NoSuch\",\"kind\":\"get\"}"));

        [Test] public void Set_StaticField()
        {
            var r = Exec("{\"type\":\"" + Probe + "\",\"member\":\"StaticField\",\"kind\":\"set\",\"args\":[9]}");
            Assert.IsInstanceOf<VoidResult>(r);
            Assert.AreEqual(9, InvokeProbe.StaticField);
        }

        [Test] public void Call_SingleOverload()
            => Assert.AreEqual(10, Exec("{\"type\":\"" + Probe + "\",\"member\":\"Add\",\"kind\":\"call\",\"args\":[3,7]}"));

        [Test] public void Call_VoidMethod_ReturnsVoidResult()
            => Assert.IsInstanceOf<VoidResult>(
                Exec("{\"type\":\"" + Probe + "\",\"member\":\"DoNothing\",\"kind\":\"call\"}"));

        [Test] public void Call_OptionalParam_UsesDefault()
            => Assert.AreEqual(12, Exec("{\"type\":\"" + Probe + "\",\"member\":\"Mul\",\"kind\":\"call\",\"args\":[4]}"));

        [Test] public void Call_ArgTypes_PicksIntOverload()
            => Assert.AreEqual("int", Exec("{\"type\":\"" + Probe + "\",\"member\":\"Kind\",\"kind\":\"call\"," +
                "\"args\":[5],\"argTypes\":[\"System.Int32\"]}"));

        [Test] public void Call_ArgTypes_PicksStringOverload()
            => Assert.AreEqual("string", Exec("{\"type\":\"" + Probe + "\",\"member\":\"Kind\",\"kind\":\"call\"," +
                "\"args\":[\"x\"],\"argTypes\":[\"System.String\"]}"));

        [Test] public void Call_Ambiguous_Throws()
            => Assert.Throws<AmbiguousMatchException>(() =>
                Exec("{\"type\":\"" + Probe + "\",\"member\":\"Kind\",\"kind\":\"call\",\"args\":[5]}"));

        [Test] public void Call_MissingMethod_Throws()
            => Assert.Throws<MissingMethodException>(() =>
                Exec("{\"type\":\"" + Probe + "\",\"member\":\"NoMethod\",\"kind\":\"call\"}"));

        [Test] public void Call_EnumArg()
            => Assert.AreEqual("A", Exec("{\"type\":\"" + Probe + "\",\"member\":\"EnumName\",\"kind\":\"call\"," +
                "\"args\":[\"UnityEngine.KeyCode.A\"]}"));

        [Test] public void Call_MathfMin_WithArgTypes()
            => Assert.AreEqual(3, Exec("{\"type\":\"UnityEngine.Mathf\",\"member\":\"Min\",\"kind\":\"call\"," +
                "\"args\":[3,7],\"argTypes\":[\"System.Int32\",\"System.Int32\"]}"));

        [Test] public void Chain_CallThenIndex()
            => Assert.AreEqual(2, Exec("{\"type\":\"" + Probe + "\",\"steps\":[" +
                "{\"member\":\"Numbers\",\"kind\":\"call\"},{\"kind\":\"index\",\"args\":[1]}]}"));

        [Test] public void Chain_CallThenGet()
            => Assert.AreEqual(3, Exec("{\"type\":\"" + Probe + "\",\"steps\":[" +
                "{\"member\":\"Numbers\",\"kind\":\"call\"},{\"member\":\"Count\",\"kind\":\"get\"}]}"));

        [Test] public void TargetInstanceId_ResolvesObject()
        {
            var go = new GameObject("InvokerProbe");
            try
            {
                var r = Exec("{\"type\":\"UnityEngine.GameObject\",\"target\":{\"instanceID\":" +
                    go.GetInstanceID() + "},\"member\":\"name\",\"kind\":\"get\"}");
                Assert.AreEqual("InvokerProbe", r);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test] public void TypeNotFound_ThrowsTypeAccessException()
            => Assert.Throws<TypeAccessException>(() =>
                Exec("{\"type\":\"Foo.NotExisting.For.E2E\",\"member\":\"X\",\"kind\":\"get\"}"));

        [Test] public void UnknownKind_Throws()
            => Assert.Throws<ArgumentException>(() =>
                Exec("{\"type\":\"" + Probe + "\",\"member\":\"Add\",\"kind\":\"frobnicate\"}"));

        [Test] public void SceneChain_LikeE2ECase04()
        {
            var r = Exec("{\"type\":\"UnityEngine.SceneManagement.SceneManager\",\"steps\":[" +
                "{\"member\":\"GetActiveScene\",\"kind\":\"call\"},{\"member\":\"GetRootGameObjects\",\"kind\":\"call\"}]}");
            Assert.IsInstanceOf<GameObject[]>(r);
        }
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

```powershell
Wait-UnityGone; Run-EditModeTests
```
Expected: Invoker 用例 FAIL，其余 PASS。

- [ ] **Step 3: 实现**

`Editor/ReflectionInvoker.cs`（完整替换）:
```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Yoji.EditorDebug
{
    /// /invoke：单步或链式反射调用。kind = get | set | call | index。
    /// 重载决议：argTypes 严格匹配 > 唯一候选 > 按实参个数过滤（含可选参数）> AmbiguousMatchException。
    internal static class ReflectionInvoker
    {
        private const BindingFlags k_All = BindingFlags.Public | BindingFlags.NonPublic |
                                           BindingFlags.Instance | BindingFlags.Static;

        public static object Execute(JObject req)
        {
            var type = TypeResolver.Resolve(req.Value<string>("type"));
            object target = ResolveTarget(req["target"]);

            var steps = req["steps"] as JArray;
            if (steps != null && steps.Count > 0)
            {
                object current = target;
                Type currentType = target?.GetType() ?? type;
                foreach (var stepTok in steps)
                {
                    current = ExecuteStep(current, currentType, (JObject)stepTok);
                    if (current is VoidResult && !ReferenceEquals(stepTok, steps.Last()))
                        throw new InvalidOperationException("void result cannot be chained");
                    currentType = current?.GetType() ?? typeof(object);
                }
                return current;
            }
            return ExecuteStep(target, target?.GetType() ?? type, req);
        }

        private static object ResolveTarget(JToken targetTok)
        {
            if (targetTok == null || targetTok.Type != JTokenType.Object) return null;
            var id = targetTok["instanceID"];
            if (id == null) return null;
            var obj = EditorUtility.InstanceIDToObject(id.Value<int>());
            if (obj == null)
                throw new MissingReferenceException("no alive object with instanceID " + id.Value<int>());
            return obj;
        }

        internal static object ExecuteStep(object target, Type type, JObject step)
        {
            var member = step.Value<string>("member");
            var kind = step.Value<string>("kind") ?? "get";
            var args = step["args"] as JArray ?? new JArray();
            var argTypes = step["argTypes"] as JArray;

            switch (kind)
            {
                case "get": return DoGet(target, type, member);
                case "set": DoSet(target, type, member, args); return VoidResult.Instance;
                case "call": return DoCall(target, type, member, args, argTypes);
                case "index": return DoIndex(target, args);
                default: throw new ArgumentException("unknown kind: " + kind);
            }
        }

        private static object DoGet(object target, Type type, string member)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var prop = t.GetProperty(member, k_All | BindingFlags.DeclaredOnly);
                if (prop != null)
                {
                    var getter = prop.GetGetMethod(true);
                    if (getter == null) throw new MissingMethodException(type.FullName, member + " (no getter)");
                    return getter.Invoke(getter.IsStatic ? null : target, null);
                }
                var field = t.GetField(member, k_All | BindingFlags.DeclaredOnly);
                if (field != null) return field.GetValue(field.IsStatic ? null : target);
            }
            throw new MissingMethodException(type.FullName, member);
        }

        private static void DoSet(object target, Type type, string member, JArray args)
        {
            if (args.Count != 1) throw new ArgumentException("set expects exactly 1 arg");
            for (var t = type; t != null; t = t.BaseType)
            {
                var prop = t.GetProperty(member, k_All | BindingFlags.DeclaredOnly);
                if (prop != null)
                {
                    var setter = prop.GetSetMethod(true);
                    if (setter == null) throw new MissingMethodException(type.FullName, member + " (no setter)");
                    setter.Invoke(setter.IsStatic ? null : target,
                        new[] { ArgumentCoercer.Coerce(args[0], prop.PropertyType) });
                    return;
                }
                var field = t.GetField(member, k_All | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    field.SetValue(field.IsStatic ? null : target,
                        ArgumentCoercer.Coerce(args[0], field.FieldType));
                    return;
                }
            }
            throw new MissingMethodException(type.FullName, member);
        }

        private static object DoCall(object target, Type type, string member, JArray args, JArray argTypes)
        {
            var candidates = new List<MethodInfo>();
            for (var t = type; t != null; t = t.BaseType)
                candidates.AddRange(t.GetMethods(k_All | BindingFlags.DeclaredOnly)
                    .Where(m => m.Name == member && !m.IsGenericMethodDefinition));
            if (candidates.Count == 0) throw new MissingMethodException(type.FullName, member);

            MethodInfo chosen;
            if (argTypes != null && argTypes.Count > 0)
            {
                var wanted = argTypes.Select(x => x.Value<string>()).ToArray();
                chosen = candidates.FirstOrDefault(m =>
                {
                    var ps = m.GetParameters();
                    return ps.Length == wanted.Length &&
                           ps.Select(p => p.ParameterType.FullName).SequenceEqual(wanted);
                });
                if (chosen == null)
                    throw new MissingMethodException(type.FullName,
                        member + "(" + string.Join(",", wanted) + ")");
            }
            else if (candidates.Count == 1)
            {
                chosen = candidates[0];
            }
            else
            {
                var byCount = candidates.Where(m =>
                {
                    var ps = m.GetParameters();
                    int required = ps.Count(p => !p.HasDefaultValue);
                    return args.Count >= required && args.Count <= ps.Length;
                }).ToList();
                if (byCount.Count == 1) chosen = byCount[0];
                else if (byCount.Count == 0) throw new MissingMethodException(type.FullName, member);
                else throw new AmbiguousMatchException(
                    member + " has " + byCount.Count + " overloads: " +
                    string.Join(" | ", byCount.Select(DescribeHandler.Signature)) +
                    " -- disambiguate with argTypes");
            }

            var pars = chosen.GetParameters();
            var clrArgs = new object[pars.Length];
            for (int i = 0; i < pars.Length; i++)
                clrArgs[i] = i < args.Count
                    ? ArgumentCoercer.Coerce(args[i], pars[i].ParameterType)
                    : pars[i].DefaultValue;

            object result = chosen.Invoke(chosen.IsStatic ? null : target, clrArgs);
            return chosen.ReturnType == typeof(void) ? VoidResult.Instance : result;
        }

        private static object DoIndex(object target, JArray args)
        {
            if (target == null) throw new NullReferenceException("index on null target");
            if (args.Count != 1) throw new ArgumentException("index expects exactly 1 arg");
            if (target is IList list) return list[args[0].Value<int>()];
            if (target is IDictionary dict) return dict[args[0].Value<string>()];
            var indexer = target.GetType().GetProperty("Item", k_All);
            if (indexer != null)
                return indexer.GetValue(target,
                    new[] { ArgumentCoercer.Coerce(args[0], indexer.GetIndexParameters()[0].ParameterType) });
            throw new MissingMethodException(target.GetType().FullName, "Item (indexer)");
        }
    }
}
```
注意：`steps.Last()` 需要 `System.Linq`，已在 using 里。`MissingReferenceException` 来自 `UnityEngine`。

- [ ] **Step 4: 跑测试确认通过**

```powershell
Wait-UnityGone; Run-EditModeTests
```
Expected: 全部 PASS。

- [ ] **Step 5: Commit**

```powershell
git -C $REPO add -A; git -C $REPO commit -m "feat(editor-debug): reflection invoker with chain steps and overload resolution"
```

---

### Task 7: EvalParser（TDD）

**Files:**
- Create: `Packages/com.yoji.editor-debug/Editor/EvalParser.cs`
- Test: `Packages/com.yoji.editor-debug/Tests/Editor/EvalParserTests.cs`

- [ ] **Step 1: 写桩 + 失败测试**

`Editor/EvalParser.cs`（桩）:
```csharp
using System;

namespace Yoji.EditorDebug
{
    internal static class EvalParser
    {
        public static object Evaluate(string code) => throw new NotImplementedException();
    }
}
```

`Tests/Editor/EvalParserTests.cs`:
```csharp
using System;
using NUnit.Framework;

namespace Yoji.EditorDebug.Tests
{
    public class EvalParserTests
    {
        [Test] public void Eval_StaticProperty()
        {
            var r = EvalParser.Evaluate("UnityEditor.EditorApplication.applicationPath");
            Assert.IsInstanceOf<string>(r);
            Assert.IsTrue(((string)r).Length > 0);
        }

        [Test] public void Eval_InternalMethodCall()
        {
            var r = EvalParser.Evaluate("UnityEditor.LogEntries.GetCount()");
            Assert.IsInstanceOf<int>(r);
        }

        [Test] public void Eval_ChainedMemberAfterValue()
        {
            var r = EvalParser.Evaluate("UnityEditor.EditorApplication.applicationPath.Length");
            Assert.IsInstanceOf<int>(r);
            Assert.Greater((int)r, 0);
        }

        [Test] public void Eval_MethodWithIntArgs()
            => Assert.AreEqual(5, EvalParser.Evaluate("Yoji.EditorDebug.Tests.InvokeProbe.Add(2, 3)"));

        [Test] public void Eval_MethodWithStringArgs()
            => Assert.AreEqual("ab", EvalParser.Evaluate("Yoji.EditorDebug.Tests.InvokeProbe.Concat(\"a\", \"b\")"));

        [Test] public void Eval_MethodWithEnumLiteralArg()
            => Assert.AreEqual("A", EvalParser.Evaluate("Yoji.EditorDebug.Tests.InvokeProbe.EnumName(UnityEngine.KeyCode.A)"));

        [Test] public void Eval_UnterminatedString_ThrowsFormatException()
            => Assert.Throws<FormatException>(() =>
                EvalParser.Evaluate("UnityEditor.LogEntries.GetCount(\"unterminated"));

        [Test] public void Eval_TypeofArg_ThrowsFormatException()
            => Assert.Throws<FormatException>(() =>
                EvalParser.Evaluate("Foo.Bar(typeof(UnityEngine.GameObject))"));

        [Test] public void Eval_EmptyExpression_ThrowsFormatException()
            => Assert.Throws<FormatException>(() => EvalParser.Evaluate("   "));

        [Test] public void Eval_DoubleDot_ThrowsFormatException()
            => Assert.Throws<FormatException>(() => EvalParser.Evaluate("UnityEditor..Selection"));

        [Test] public void Eval_BareTypeName_ThrowsFormatException()
            => Assert.Throws<FormatException>(() => EvalParser.Evaluate("UnityEditor.Selection"));

        [Test] public void Eval_UnresolvableRoot_ThrowsTypeAccessException()
            => Assert.Throws<TypeAccessException>(() => EvalParser.Evaluate("Nope.Nothing.Here"));
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

```powershell
Wait-UnityGone; Run-EditModeTests
```
Expected: Eval 用例 FAIL，其余 PASS。

- [ ] **Step 3: 实现**

`Editor/EvalParser.cs`（完整替换）:
```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Yoji.EditorDebug
{
    /// /eval 轻量表达式求值器。仅支持 Type.Member.Member(args) 链式访问；
    /// 实参支持字符串/数字/true/false/null/点分枚举字面量。
    /// 不支持 lambda / new / typeof / 运算符。语法错误抛 System.FormatException。
    /// 类型前缀贪心最长匹配：从全部前导纯标识符段由长到短尝试 TypeResolver。
    internal static class EvalParser
    {
        public static object Evaluate(string code)
        {
            var segs = Parse(code);

            int maxPrefix = 0;
            while (maxPrefix < segs.Count && !segs[maxPrefix].IsCall) maxPrefix++;

            Type rootType = null;
            int typeLen = 0;
            for (int n = maxPrefix; n >= 1; n--)
            {
                var name = string.Join(".", segs.Take(n).Select(x => x.Name));
                if (TypeResolver.TryResolve(name, out rootType)) { typeLen = n; break; }
            }
            if (rootType == null)
                throw new TypeAccessException("cannot resolve a type from expression prefix: " + code);
            if (typeLen == segs.Count)
                throw new FormatException("expression is just a type name, expected member access: " + code);

            object current = null;
            Type currentType = rootType;
            for (int i = typeLen; i < segs.Count; i++)
            {
                var seg = segs[i];
                var step = new JObject
                {
                    ["member"] = seg.Name,
                    ["kind"] = seg.IsCall ? "call" : "get",
                };
                if (seg.IsCall) step["args"] = new JArray(seg.Args.ToArray());
                current = ReflectionInvoker.ExecuteStep(current, currentType, step);
                if (current is VoidResult) return current;
                currentType = current?.GetType() ?? typeof(object);
            }
            return current;
        }

        private sealed class Segment
        {
            public string Name;
            public bool IsCall;
            public readonly List<JToken> Args = new List<JToken>();
        }

        private static List<Segment> Parse(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) throw new FormatException("empty expression");
            int pos = 0;
            var segs = new List<Segment>();
            while (true)
            {
                SkipWs(code, ref pos);
                var seg = new Segment { Name = ReadIdentifier(code, ref pos) };
                SkipWs(code, ref pos);
                if (Peek(code, pos) == '(')
                {
                    pos++;
                    seg.IsCall = true;
                    SkipWs(code, ref pos);
                    if (Peek(code, pos) != ')')
                    {
                        while (true)
                        {
                            seg.Args.Add(ReadLiteral(code, ref pos));
                            SkipWs(code, ref pos);
                            var c = Peek(code, pos);
                            if (c == ',') { pos++; SkipWs(code, ref pos); continue; }
                            if (c == ')') break;
                            throw new FormatException("expected ',' or ')' at offset " + pos);
                        }
                    }
                    pos++; // ')'
                }
                segs.Add(seg);
                SkipWs(code, ref pos);
                if (pos >= code.Length) break;
                if (code[pos] != '.')
                    throw new FormatException("unexpected character '" + code[pos] + "' at offset " + pos);
                pos++;
            }
            return segs;
        }

        private static void SkipWs(string s, ref int pos)
        {
            while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++;
        }

        private static char Peek(string s, int pos) => pos < s.Length ? s[pos] : '\0';

        private static string ReadIdentifier(string s, ref int pos)
        {
            int start = pos;
            if (pos < s.Length && (char.IsLetter(s[pos]) || s[pos] == '_')) pos++;
            while (pos < s.Length && (char.IsLetterOrDigit(s[pos]) || s[pos] == '_')) pos++;
            if (pos == start)
                throw new FormatException("expected identifier at offset " + start);
            return s.Substring(start, pos - start);
        }

        /// 实参字面量：字符串 / 数字 / true / false / null / 点分标识符（枚举等，存为字符串交给 coercer）。
        private static JToken ReadLiteral(string s, ref int pos)
        {
            SkipWs(s, ref pos);
            var c = Peek(s, pos);
            if (c == '"') return new JValue(ReadString(s, ref pos));
            if (c == '-' || char.IsDigit(c)) return ReadNumber(s, ref pos);
            if (char.IsLetter(c) || c == '_')
            {
                var ident = ReadDottedIdentifier(s, ref pos);
                if (Peek(s, pos) == '(')
                    throw new FormatException("call expression not supported as argument: " + ident + "(...)");
                switch (ident)
                {
                    case "true": return new JValue(true);
                    case "false": return new JValue(false);
                    case "null": return JValue.CreateNull();
                    default: return new JValue(ident); // 点分枚举字面量等
                }
            }
            throw new FormatException("cannot parse argument at offset " + pos);
        }

        private static string ReadDottedIdentifier(string s, ref int pos)
        {
            var sb = new StringBuilder(ReadIdentifier(s, ref pos));
            while (Peek(s, pos) == '.')
            {
                pos++;
                sb.Append('.').Append(ReadIdentifier(s, ref pos));
            }
            return sb.ToString();
        }

        private static string ReadString(string s, ref int pos)
        {
            pos++; // 开引号
            var sb = new StringBuilder();
            while (true)
            {
                if (pos >= s.Length) throw new FormatException("unterminated string literal");
                var c = s[pos++];
                if (c == '"') return sb.ToString();
                if (c == '\\')
                {
                    if (pos >= s.Length) throw new FormatException("unterminated escape in string literal");
                    var e = s[pos++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        default: throw new FormatException("unsupported escape \\" + e);
                    }
                }
                else sb.Append(c);
            }
        }

        private static JToken ReadNumber(string s, ref int pos)
        {
            int start = pos;
            if (Peek(s, pos) == '-') pos++;
            while (pos < s.Length && (char.IsDigit(s[pos]) || s[pos] == '.')) pos++;
            var text = s.Substring(start, pos - start);
            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
                return new JValue(l);
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                return new JValue(d);
            throw new FormatException("invalid number literal: " + text);
        }
    }
}
```
注意 `Eval_TypeofArg` 用例走的是 `ReadLiteral` 里 "call expression not supported as argument" 分支（`typeof(...)` 形如实参内调用）。

- [ ] **Step 4: 跑测试确认通过**

```powershell
Wait-UnityGone; Run-EditModeTests
```
Expected: 全部 PASS。

- [ ] **Step 5: Commit**

```powershell
git -C $REPO add -A; git -C $REPO commit -m "feat(editor-debug): /eval lightweight expression parser"
```

---

### Task 8: HTTP 服务（MainThreadDispatcher + EditorDebugMCP + RecompileHandler）

HTTP 生命周期无法用 EditMode 单测可靠覆盖（HttpListener + 线程 + domain reload），本任务只做编译验证，行为由 Task 9 的 e2e 13 条用例验收。

**Files:**
- Create: `Packages/com.yoji.editor-debug/Editor/MainThreadDispatcher.cs`
- Create: `Packages/com.yoji.editor-debug/Editor/RecompileHandler.cs`
- Create: `Packages/com.yoji.editor-debug/Editor/EditorDebugMCP.cs`

- [ ] **Step 1: MainThreadDispatcher**

`Editor/MainThreadDispatcher.cs`:
```csharp
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;
using UnityEditor;

namespace Yoji.EditorDebug
{
    /// 把 HTTP 线程的工作派发到 Editor 主线程执行并阻塞等待结果。
    /// 注意：绝不能在主线程上调用 Run（EditorApplication.update 无法重入，会死锁）。
    internal static class MainThreadDispatcher
    {
        private sealed class Job
        {
            public Func<object> Work;
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
            public object Result;
            public Exception Error;
            public long ElapsedMs;
        }

        private static readonly ConcurrentQueue<Job> s_Queue = new ConcurrentQueue<Job>();
        private static int s_MainThreadId = -1;

        [InitializeOnLoadMethod]
        private static void Install()
        {
            s_MainThreadId = Thread.CurrentThread.ManagedThreadId;
            EditorApplication.update += Pump;
        }

        private static void Pump()
        {
            while (s_Queue.TryDequeue(out var job))
            {
                var sw = Stopwatch.StartNew();
                try { job.Result = job.Work(); }
                catch (Exception e) { job.Error = e; }
                job.ElapsedMs = sw.ElapsedMilliseconds;
                job.Done.Set();
            }
        }

        /// 返回主线程执行结果；主线程抛出的异常原栈重抛。elapsedMs 是主线程内的真实耗时。
        public static object Run(Func<object> work, out long elapsedMs, int timeoutMs = 100000)
        {
            if (Thread.CurrentThread.ManagedThreadId == s_MainThreadId)
            {
                // 已在主线程（理论上只有测试会这么调）：直接执行
                var sw = Stopwatch.StartNew();
                var direct = work();
                elapsedMs = sw.ElapsedMilliseconds;
                return direct;
            }

            var job = new Job { Work = work };
            s_Queue.Enqueue(job);
            if (!job.Done.Wait(timeoutMs))
                throw new TimeoutException("editor main thread did not respond within " + timeoutMs + "ms");
            elapsedMs = job.ElapsedMs;
            if (job.Error != null) ExceptionDispatchInfo.Capture(job.Error).Throw();
            return job.Result;
        }
    }
}
```

- [ ] **Step 2: RecompileHandler**

`Editor/RecompileHandler.cs`:
```csharp
using System;
using System.Diagnostics;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Compilation;

namespace Yoji.EditorDebug
{
    /// /recompile：主线程触发编译，HTTP 线程轮询完成标志后写响应。
    /// 响应发出后 Editor 进入 domain reload，服务短暂下线（客户端职责：轮询 /ping 等恢复）。
    internal static class RecompileHandler
    {
        private static volatile bool s_CompilationStarted;
        private static volatile bool s_CompilationFinished;
        private static volatile bool s_HasErrors;
        private static int s_Pending;

        [InitializeOnLoadMethod]
        private static void Install()
        {
            CompilationPipeline.compilationStarted += _ => s_CompilationStarted = true;
            CompilationPipeline.compilationFinished += _ => s_CompilationFinished = true;
            CompilationPipeline.assemblyCompilationFinished += (_, messages) =>
            {
                foreach (var m in messages)
                    if (m.type == CompilerMessageType.Error) s_HasErrors = true;
            };
        }

        public static JObject Run()
        {
            Interlocked.Increment(ref s_Pending);
            try
            {
                bool alreadyCompiling = false;
                MainThreadDispatcher.Run(() =>
                {
                    if (EditorApplication.isCompiling) { alreadyCompiling = true; return null; }
                    s_CompilationStarted = false;
                    s_CompilationFinished = false;
                    s_HasErrors = false;
                    CompilationPipeline.RequestScriptCompilation();
                    return null;
                }, out _);

                if (alreadyCompiling)
                    return new JObject
                    {
                        ["ok"] = false,
                        ["elapsedMs"] = 0,
                        ["error"] = new JObject
                        {
                            ["type"] = "Conflict",
                            ["message"] = "a script compilation is already in progress",
                        },
                    };

                var sw = Stopwatch.StartNew();
                // 5s 内没进入编译说明没有可编译的变更，直接视为成功
                while (!s_CompilationStarted && sw.ElapsedMilliseconds < 5000) Thread.Sleep(100);
                if (s_CompilationStarted)
                    while (!s_CompilationFinished && sw.ElapsedMilliseconds < 180000) Thread.Sleep(100);

                return new JObject
                {
                    ["ok"] = true,
                    ["elapsedMs"] = sw.ElapsedMilliseconds,
                    ["result"] = new JObject
                    {
                        ["success"] = !s_HasErrors,
                        ["compilationTime"] = Math.Round(sw.Elapsed.TotalSeconds, 1),
                        ["hasErrors"] = s_HasErrors,
                    },
                };
            }
            finally { Interlocked.Decrement(ref s_Pending); }
        }

        /// beforeAssemblyReload 时给尚未写完的 recompile 响应留时间窗。
        public static void WaitForPendingResponse(int maxMs)
        {
            var sw = Stopwatch.StartNew();
            while (Volatile.Read(ref s_Pending) > 0 && sw.ElapsedMilliseconds < maxMs)
                Thread.Sleep(50);
            Thread.Sleep(200); // Run 返回到响应写出之间的微小窗口
        }
    }
}
```
已知妥协：`assemblyCompilationFinished` 在新版可能带 Obsolete 警告，仅警告可接受；若编译报错（API 移除），改用 `CompilationPipeline.compilationFinished` 的 object 参数版本并通过 `EditorUtility.scriptCompilationFailed`（internal，可用本服务自己的 TypeResolver 思路反射读取）获取错误状态，或简化为 `hasErrors:false` 固定值 + 注释说明。

- [ ] **Step 3: EditorDebugMCP（监听 + 路由 + 信封）**

`Editor/EditorDebugMCP.cs`:
```csharp
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Yoji.EditorDebug
{
    /// HTTP+JSON 反射调试服务。只绑 127.0.0.1；HTTP 状态码恒 200，错误走 body.error。
    [InitializeOnLoad]
    internal static class EditorDebugMCP
    {
        private const string k_Version = "0.1.0";
        private const bool c_AllowEval = true; // 关闭 /eval 改成 false
        private const int k_MaxBodyBytes = 4 * 1024 * 1024;
        private static readonly int[] k_Ports = { 21891, 21892, 21893 };

        private static HttpListener s_Listener;
        private static int s_Port;
        private static string s_UnityVersion;
        private static string s_ProjectName;

        static EditorDebugMCP()
        {
            EditorApplication.delayCall += Start;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
        }

        private static void Start()
        {
            if (s_Listener != null) return;
            s_UnityVersion = Application.unityVersion;
            s_ProjectName = Application.productName;

            foreach (var port in k_Ports)
            {
                try
                {
                    var listener = new HttpListener();
                    listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
                    listener.Start();
                    s_Listener = listener;
                    s_Port = port;
                    break;
                }
                catch (Exception)
                {
                    // 端口被占，尝试下一个
                }
            }
            if (s_Listener == null)
            {
                Debug.LogError("[EditorDebugMCP] 21891-21893 全部被占，服务未启动");
                return;
            }
            var thread = new Thread(Loop) { IsBackground = true, Name = "EditorDebugMCP" };
            thread.Start();
            Debug.Log("[EditorDebugMCP] 服务已启动，监听 http://127.0.0.1:" + s_Port + "/");
        }

        private static void Stop()
        {
            if (s_Listener == null) return;
            RecompileHandler.WaitForPendingResponse(2000);
            try { s_Listener.Stop(); s_Listener.Close(); }
            catch (Exception) { }
            s_Listener = null;
        }

        private static void Loop()
        {
            var listener = s_Listener;
            while (listener != null && listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = listener.GetContext(); }
                catch (Exception) { break; } // listener 已关闭
                ThreadPool.QueueUserWorkItem(_ => Handle(ctx));
            }
        }

        private static void Handle(HttpListenerContext ctx)
        {
            JObject envelope;
            try
            {
                string body;
                using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                    body = reader.ReadToEnd();
                var req = string.IsNullOrWhiteSpace(body) ? new JObject() : JObject.Parse(body);
                envelope = Route(ctx.Request.Url.AbsolutePath, req);
            }
            catch (Exception e)
            {
                envelope = ErrorEnvelope(e);
            }
            WriteResponse(ctx, envelope);
        }

        private static JObject Route(string path, JObject req)
        {
            switch (path)
            {
                case "/ping":
                    return new JObject { ["ok"] = true, ["elapsedMs"] = 0, ["result"] = Ping() };
                case "/invoke":
                    return RunOnMain(() => ReflectionInvoker.Execute(req));
                case "/describe":
                    return RunOnMain(() => DescribeHandler.Describe(req.Value<string>("type")));
                case "/eval":
                    if (!c_AllowEval)
                        return ErrorEnvelope(new NotSupportedException("eval is disabled (c_AllowEval=false)"));
                    return RunOnMain(() => EvalParser.Evaluate(req.Value<string>("code")));
                case "/recompile":
                    return RecompileHandler.Run();
                default:
                    return ErrorEnvelope(new ArgumentException("unknown endpoint: " + path));
            }
        }

        private static JObject Ping() => new JObject
        {
            ["service"] = "EditorDebugMCP",
            ["version"] = k_Version,
            ["port"] = s_Port,
            ["unityVersion"] = s_UnityVersion,
            ["projectName"] = s_ProjectName,
        };

        /// 反射执行与结果序列化都必须发生在主线程（结果可能触碰 Unity API）。
        private static JObject RunOnMain(Func<object> work)
        {
            try
            {
                var payload = (JObject)MainThreadDispatcher.Run(() =>
                {
                    var raw = work();
                    var p = new JObject();
                    if (raw is VoidResult) { p["void"] = true; p["result"] = null; }
                    else if (raw is JToken jt) p["result"] = jt; // describe 等已是 JSON
                    else p["result"] = ResultSerializer.ToJson(raw);
                    return p;
                }, out var elapsed);
                payload["ok"] = true;
                payload["elapsedMs"] = elapsed;
                return payload;
            }
            catch (Exception e)
            {
                return ErrorEnvelope(e);
            }
        }

        private static JObject ErrorEnvelope(Exception e) => new JObject
        {
            ["ok"] = false,
            ["elapsedMs"] = 0,
            ["error"] = ErrorJson(e),
        };

        private static JObject ErrorJson(Exception e)
        {
            var o = new JObject
            {
                ["type"] = e.GetType().FullName,
                ["message"] = e.Message,
                ["stack"] = e.StackTrace,
            };
            if (e.InnerException != null) o["inner"] = ErrorJson(e.InnerException);
            return o;
        }

        private static void WriteResponse(HttpListenerContext ctx, JObject envelope)
        {
            var bytes = Encoding.UTF8.GetBytes(envelope.ToString(Formatting.None));
            if (bytes.Length > k_MaxBodyBytes)
            {
                envelope["result"] = new JObject { ["__truncated"] = true, ["reason"] = "bodySize" };
                envelope["__truncated"] = true;
                envelope["fullSize"] = bytes.Length;
                bytes = Encoding.UTF8.GetBytes(envelope.ToString(Formatting.None));
            }
            try
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json; charset=utf-8";
                ctx.Response.ContentLength64 = bytes.Length;
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                ctx.Response.OutputStream.Close();
            }
            catch (Exception)
            {
                // 客户端已断开
            }
        }
    }
}
```

- [ ] **Step 4: 编译验证（单测全量回归）**

```powershell
Wait-UnityGone; Run-EditModeTests
```
Expected: 退出码 0（新增三个文件零编译错误，旧用例全 PASS）。注意测试运行日志里应能看到 `[EditorDebugMCP] 服务已启动` 字样（batchmode 下服务也会拉起，无害）。

- [ ] **Step 5: Commit**

```powershell
git -C $REPO add -A; git -C $REPO commit -m "feat(editor-debug): HTTP listener, main-thread dispatch, recompile endpoint"
```

---

### Task 9: 端到端验收（run-e2e.py 13 条用例）

**Files:**
- 无新文件。使用 `Packages/com.yoji.editor-debug/Agent~/skills/unity-editor-debug-mcp/references/run-e2e.py` 与同目录 `client.py`。

- [ ] **Step 1: 后台启动交互式 Editor（不能用 -batchmode，服务要常驻）**

```powershell
Wait-UnityGone
Start-Process $UNITY -ArgumentList "-projectPath", $TP, "-logFile", "$TP\TestResults\e2e-editor.log"
```

- [ ] **Step 2: 轮询 /ping 等服务上线（首次打开可能要 1-3 分钟）**

```powershell
$skill = "$REPO\Packages\com.yoji.editor-debug\Agent~\skills\unity-editor-debug-mcp"
$deadline = (Get-Date).AddMinutes(5)
do {
    Start-Sleep 5
    $ok = (python "$skill\client.py" ping 2>$null | ConvertFrom-Json).ok
} until ($ok -or (Get-Date) -gt $deadline)
Write-Host "ping ok=$ok"
```
Expected: `ping ok=True`。超时则看 `e2e-editor.log` 是否有编译错误或端口冲突日志。

- [ ] **Step 3: 跑 13 条用例**

```powershell
python "$skill\references\run-e2e.py" --verbose
```
Expected: `== result: 13 passed, 0 failed ==`，退出码 0。
失败时逐条对照 `protocol.md` 修实现（修改 C# 后用 `python "$skill\client.py" recompile --timeout 180` 让 Editor 重新编译，再等 ping 恢复后重跑），不要改 e2e 用例本身。

- [ ] **Step 4: 跑 /recompile 用例**

```powershell
python "$skill\references\run-e2e.py" --include-recompile --verbose
```
Expected: `14 passed, 0 failed`（R1 用例耗时 30-60s，期间服务短暂下线属预期）。

- [ ] **Step 5: 用服务自身关闭 Editor（dogfooding）**

```powershell
python "$skill\client.py" invoke --type UnityEditor.EditorApplication --member Exit --kind call --args 0 --timeout 5
Wait-UnityGone
```
Expected: Editor 退出（HTTP 响应可能因进程退出而收不到，client 报 URLError 属正常）。

- [ ] **Step 6: Commit（如 Step 3/4 期间有实现修正）**

```powershell
git -C $REPO add -A; git -C $REPO commit -m "fix(editor-debug): pass full e2e suite (13+recompile cases)"
```
无修正则跳过本步。

---

### Task 10: 文档同步

**Files:**
- Modify: `README.md`（仓库根）
- Modify: `Packages/com.yoji.editor-debug/Agent~/skills/unity-editor-debug-mcp/references/troubleshooting.md`
- Modify: `Packages/com.yoji.editor-debug/Agent~/skills/unity-editor-debug-mcp/references/manual-verification.md`
- Modify: `Packages/com.yoji.editor-debug/Agent~/skills/unity-editor-debug-mcp/client.py`
- Modify: `Packages/com.yoji.editor-debug/Agent~/skills/unity-editor-debug-mcp/install.ps1`

- [ ] **Step 1: 全仓 grep 旧包名，逐处替换**

```powershell
Get-ChildItem $REPO -Recurse -File -Include *.md,*.py,*.ps1 |
    Select-String -Pattern "com\.tfw\.unity-editor-debug-mcp" | Select-Object Path, LineNumber
```
把所有命中处的 `com.tfw.unity-editor-debug-mcp` 改为 `com.yoji.editor-debug`，包路径 `Packages/com.tfw.unity-editor-debug-mcp/` 改为 `Packages/com.yoji.editor-debug/`。替换后重跑上面命令确认 0 命中。

- [ ] **Step 2: 修正 client.py 过时注释**

`client.py` 中 `cmd_eval` 的 docstring `"""/eval —— 表达式求值。当前阶段：会返回 NotImplemented 错误信封。"""` 改为：
```python
    """/eval —— 轻量表达式求值（链式属性/方法访问；不支持 lambda/new/typeof）。"""
```

- [ ] **Step 3: 修正 manual-verification.md 的工程指向**

前置一节中 `F:\git\x15_client_222\client`、`2022.3.62f2c1`、`Raft War` 等旧工程专属内容，改为指向本仓库 `TestProjects/editor-debug`（Unity 6000.3.16f1，projectName 为该工程 ProjectSettings 的 productName）。其余验证步骤保持不变。同时删去开头"代码已通过 40 条单元测试"句，改为"代码已通过 `Packages/com.yoji.editor-debug/Tests/Editor/` 的 EditMode 单元测试"。

- [ ] **Step 4: 更新仓库 README**

`README.md` 的 unity-editor-debug-mcp 小节补一段安装方式：
```markdown
**Install (Unity side)**: add to your project `Packages/manifest.json`:

    "com.yoji.editor-debug": "file:<path-to-repo>/Packages/com.yoji.editor-debug"

or copy `Packages/com.yoji.editor-debug/` into your project's `Packages/`.
The HTTP service starts automatically with the Editor (look for
`[EditorDebugMCP] 服务已启动` in the Console).
```
并把文中三个工具的目录路径同步为新结构（`Packages/com.yoji.editor-debug/Agent~/skills/unity-editor-debug-mcp/...`）。

- [ ] **Step 5: 验证 install.ps1 在新路径可用**

```powershell
& "$REPO\Packages\com.yoji.editor-debug\Agent~\skills\unity-editor-debug-mcp\install.ps1"
Test-Path "$env:USERPROFILE\.claude\skills\unity-editor-debug-mcp\SKILL.md"
```
Expected: `True`（install.ps1 用 `$PSScriptRoot` 做源目录，移动后无需改逻辑；若注释里仍有旧包名按 Step 1 已替换）。

- [ ] **Step 6: Commit**

```powershell
git -C $REPO add -A; git -C $REPO commit -m "docs(editor-debug): sync package id/paths, eval status, install guide"
```

---

## 验收总清单

1. `Run-EditModeTests` 全 PASS（约 60+ 断言，覆盖 TypeResolver / Describe / Coercer / Serializer / Invoker / Eval）
2. `run-e2e.py` 13 条全 PASS；`--include-recompile` 14 条全 PASS
3. `git grep "com.tfw"` 0 命中；`git grep "CS\."` 不适用（本仓库无 Lua）
4. 仓库根无散落新 .md（计划在 `docs/superpowers/plans/`）

## 已知风险与回退

| 风险 | 信号 | 处理 |
|------|------|------|
| Unity 6 移除/改名 internal API（LogEntries 等） | e2e case 03/08 FAIL | 用 `/describe` 探查新名字，**改的是测试期望对应的协议文档说明，不改用例判定逻辑**；LogEntries 在 Unity 6 仍存在，大概率不触发 |
| `assemblyCompilationFinished` 被移除 | Task 8 编译错误 | 见 Task 8 Step 2 内注的回退方案 |
| recompile 响应与 domain reload 竞态 | R1 用例偶发 URLError | 已有 2s 等待窗 + 客户端轮询 ping 兜底；偶发可接受（cookbook 已声明该行为） |
| Newtonsoft 包解析失败 | Task 1 Step 8 报 missing reference | 检查 manifest 是否联网拉包；离线时把 Unity 自带 `com.unity.nuget.newtonsoft-json` 缓存版本号对齐 |

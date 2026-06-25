from pathlib import Path
import json

ROOT = Path(__file__).resolve().parents[1]
PKG = ROOT / "Packages" / "com.sputnicyoji.u3d-dev-tools-ai"
OLD_PREFIX = "com." + "yoji."
EXPECTED_NAME = "com.sputnicyoji.u3d-dev-tools-ai"
EXPECTED_MODULES = [
    "Editor/Core",
    "Editor/EditorDebug",
    "Editor/TestRunner",
    "Editor/U3DAILinker",
    "Runtime/LuaDeviceDebug",
    "Agent~/skills/test-runner-mcp",
    "Agent~/skills/unity-editor-debug-mcp",
    "Agent~/skills/unity-lua-device-debug",
]


def load_json(path: Path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def test_single_public_package_exists():
    assert PKG.is_dir(), f"missing merged package: {PKG}"
    manifest = load_json(PKG / "package.json")
    assert manifest["name"] == EXPECTED_NAME
    assert manifest["displayName"] == "U3D Dev Tools AI"
    assert manifest["version"] == "0.2.3"
    dependencies = manifest.get("dependencies", {})
    assert "com.unity.nuget.newtonsoft-json" in dependencies
    assert "com.unity.test-framework" in dependencies
    assert all(not name.startswith(OLD_PREFIX) for name in dependencies)


def test_old_package_roots_removed():
    old_roots = sorted(p.name for p in (ROOT / "Packages").iterdir() if p.is_dir() and p.name.startswith(OLD_PREFIX))
    assert old_roots == []


def test_expected_modules_moved_into_single_package():
    for rel in EXPECTED_MODULES:
        assert (PKG / rel).exists(), f"missing module path: {rel}"


def test_test_projects_reference_single_package():
    for manifest_path in sorted((ROOT / "TestProjects").glob("*/Packages/manifest.json")):
        manifest = load_json(manifest_path)
        deps = manifest.get("dependencies", {})
        assert EXPECTED_NAME in deps, f"{manifest_path} does not depend on {EXPECTED_NAME}"
        assert all(not key.startswith(OLD_PREFIX) for key in deps), f"old package dependency in {manifest_path}: {deps}"
        assert deps[EXPECTED_NAME].startswith("file:../../../Packages/com.sputnicyoji.u3d-dev-tools-ai")


def test_readme_uses_single_package_name():
    text = (ROOT / "README.md").read_text(encoding="utf-8-sig")
    assert EXPECTED_NAME in text
    assert OLD_PREFIX not in text


def test_registry_uses_single_package():
    for registry_path in [ROOT / "Registry" / "stable.json", ROOT / "Registry" / "dev.json", PKG / "Registry" / "stable.json", PKG / "Registry" / "dev.json"]:
        registry = load_json(registry_path)
        entries = registry.get("entries", [])
        assert len(entries) == 1, f"{registry_path} should expose one package entry"
        entry = entries[0]
        assert entry["packageName"] == EXPECTED_NAME
        assert entry["packagePath"] == "Packages/com.sputnicyoji.u3d-dev-tools-ai"


def test_single_package_agent_sync_supports_each_skill():
    expected_skills = [
        "test-runner-mcp",
        "unity-editor-debug-mcp",
        "unity-lua-device-debug",
    ]
    for skill in expected_skills:
        assert (PKG / "Agent~" / "skills" / skill / "SKILL.md").is_file(), f"missing skill {skill}"
        assert (PKG / "Agent~" / "fragments" / skill / "CLAUDE.md").is_file(), f"missing Claude fragment for {skill}"
        assert (PKG / "Agent~" / "fragments" / skill / "AGENTS.md").is_file(), f"missing Agents fragment for {skill}"
    resolver = (PKG / "Editor" / "U3DAILinker" / "Settings" / "AgentPackageSourceResolver.cs").read_text(encoding="utf-8-sig")
    assert "Expected exactly one SKILL.md" not in resolver

if __name__ == "__main__":
    for name, fn in sorted(globals().items()):
        if name.startswith("test_") and callable(fn):
            fn()
            print(f"OK {name}")

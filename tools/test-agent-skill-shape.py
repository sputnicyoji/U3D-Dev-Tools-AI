from __future__ import annotations

import re
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SKILLS_ROOT = ROOT / "Packages" / "com.sputnicyoji.u3d-dev-tools-ai" / "Agent~" / "skills"
EXPECTED = {
    "test-runner-mcp",
    "unity-editor-debug-mcp",
    "unity-lua-device-debug",
}
MAX_DESCRIPTION_CHARS = 260


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def fail(message: str) -> None:
    raise AssertionError(message)


def parse_frontmatter(text: str) -> dict[str, str]:
    if not text.startswith("---\n"):
        fail("missing opening frontmatter fence")
    end = text.find("\n---\n", 4)
    if end < 0:
        fail("missing closing frontmatter fence")
    result: dict[str, str] = {}
    for line in text[4:end].splitlines():
        if not line.strip():
            continue
        if ":" not in line:
            fail(f"invalid frontmatter line: {line}")
        key, value = line.split(":", 1)
        result[key.strip()] = value.strip().strip('"')
    return result


def test_exact_skill_directories() -> None:
    actual = {p.name for p in SKILLS_ROOT.iterdir() if (p / "SKILL.md").is_file()}
    assert actual == EXPECTED, f"unexpected skills: {sorted(actual)}"


def test_each_skill_is_operational_and_project_aware() -> None:
    for skill in sorted(EXPECTED):
        path = SKILLS_ROOT / skill / "SKILL.md"
        text = read(path)
        frontmatter = parse_frontmatter(text)
        assert frontmatter.get("name") == skill
        description = frontmatter.get("description", "")
        assert 40 <= len(description) <= MAX_DESCRIPTION_CHARS, f"{skill} description length={len(description)}"
        assert "## Operational loop" in text, f"{skill} missing operational loop"
        assert "## References" in text, f"{skill} missing references"
        assert "Completion:" in text, f"{skill} missing completion criteria"
        assert "--project" in text, f"{skill} must prefer project-aware endpoint resolution"
        assert "## Protocol" not in text, f"{skill} protocol detail belongs in references"


def test_reference_files_exist_for_long_protocols() -> None:
    required = [
        SKILLS_ROOT / "test-runner-mcp" / "references" / "protocol.md",
        SKILLS_ROOT / "test-runner-mcp" / "references" / "troubleshooting.md",
        SKILLS_ROOT / "unity-editor-debug-mcp" / "references" / "protocol.md",
        SKILLS_ROOT / "unity-editor-debug-mcp" / "references" / "troubleshooting.md",
        SKILLS_ROOT / "unity-lua-device-debug" / "references" / "protocol.md",
        SKILLS_ROOT / "unity-lua-device-debug" / "references" / "android-adb-forwarding.md",
    ]
    missing = [str(p.relative_to(ROOT)) for p in required if not p.is_file()]
    assert not missing, "missing references: " + ", ".join(missing)


def test_no_tracked_python_bytecode_under_agent_assets() -> None:
    result = subprocess.run(
        ["git", "ls-files", "Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~"],
        cwd=ROOT,
        check=True,
        text=True,
        capture_output=True,
    )
    offenders = [line for line in result.stdout.splitlines() if re.search(r"(__pycache__|\.pyc$)", line)]
    assert offenders == [], "tracked bytecode under Agent~: " + ", ".join(offenders)


if __name__ == "__main__":
    for test in [
        test_exact_skill_directories,
        test_each_skill_is_operational_and_project_aware,
        test_reference_files_exist_for_long_protocols,
        test_no_tracked_python_bytecode_under_agent_assets,
    ]:
        test()
        print("OK", test.__name__)

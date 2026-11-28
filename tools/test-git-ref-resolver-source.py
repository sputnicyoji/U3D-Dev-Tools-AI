from __future__ import annotations

from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
RESOLVER = ROOT / "Packages/com.sputnicyoji.u3d-dev-tools-ai/Editor/U3DAILinker/Operations/GitLsRemoteResolver.cs"


def _method_body(source: str, signature: str) -> str:
    marker = source.index(signature)
    start = source.index("{", marker)
    depth = 0
    for index in range(start, len(source)):
        char = source[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return source[start + 1:index]
    raise AssertionError(f"method body not found: {signature}")


def main() -> None:
    source = RESOLVER.read_text(encoding="utf-8")
    resolve_tag = _method_body(source, "public string ResolveTagSha(string tag)")
    resolve_ref = _method_body(source, "private string ResolveRefSha(string gitRef")

    assert '"refs/tags/" + tag + "^{}"' in resolve_tag, (
        "annotated tags must resolve the peeled commit ref first"
    )
    assert "allowMissing: true" in resolve_tag, (
        "peeled ref lookup must allow missing output for lightweight tags"
    )
    assert 'ResolveRefSha("refs/tags/" + tag)' in resolve_tag, (
        "lightweight tags must fall back to the plain tag ref"
    )
    assert "allowMissing" in resolve_ref and "return null;" in resolve_ref, (
        "ResolveRefSha must support missing optional refs"
    )

    run = _method_body(source, "private string RunLsRemote(string gitRef)")
    assert 'EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0"' in run, (
        "git must never block on a credential prompt"
    )
    assert "WaitForExit(_timeoutMs)" in run and "WaitForExit()" not in run, (
        "waiting for git must be bounded"
    )
    assert "Kill()" in run, "a timed-out git process must be killed"
    wait_all = next(line for line in run.splitlines() if "Task.WaitAll(" in line)
    assert "_timeoutMs - " in wait_all, (
        "the pipe drain must get only the time the process wait left, not a second full timeout"
    )


if __name__ == "__main__":
    main()

# Upstream Sync Review Reference

## Guardrail scans

Run these on selected upstream diffs before keeping any patch:

```powershell
rtk rg -n "telemetry|analytics|reporting|sentry|posthog|segment|amplitude|mixpanel" .
rtk rg -n "Claude Desktop|Cursor|VSCode|VS Code|Windsurf|Cline|Kiro|Kilo|Gemini|Rider|Trae|Qwen|OpenCode|OpenClaw|Copilot|CodeBuddy|Cherry|Antigravity|Kimi" .
```

Classify false positives explicitly. Pagination `cursor` is not a client.

## Apply mode

1. Create an isolation branch.

```powershell
rtk git switch -c upstream-sync/<date-or-topic>
```

2. Apply only selected commits.

Prefer small cherry-picks or manual patch extraction.

```powershell
rtk git cherry-pick -n <sha>
```

Do not run `git merge upstream/main` into the maintained branch.

3. Prune incompatible surface.

Re-remove upstream content that violates local decisions:

- unsupported clients beyond Claude Code and Codex
- telemetry or analytics
- upstream branding and distribution files
- CI, release, sponsor, website, MCPB material unless explicitly requested

4. Validate.

Minimum checks:

```powershell
rtk git diff --check
rtk python -m json.tool package.json
rtk python -m json.tool Editor/Yoji.UnityMcp.Editor.asmdef
rtk python -m json.tool Runtime/Yoji.UnityMcp.Runtime.asmdef
```

If C# changed and Unity is available, run the relevant Unity/EditMode validation. If Unity is not available, state that boundary.

5. Review before commit.

Re-run the guardrail scans. Confirm the support list and privacy policy still match the fork.

6. Commit.

Use a narrow message:

```text
Sync selected upstream fixes
```

Push only if the user asked for push or this repo's current task flow already requires it.

## Failure modes

- Direct merge reintroduces upstream surface. Stop and revert the merge before continuing.
- Cherry-pick brings a mixed commit. Split manually or abandon it.
- Upstream fix depends on removed clients/reporting. Port the minimal underlying fix, not the whole feature.
- Validation cannot run. Report the exact unavailable tool or Unity path.

---
name: upstream-sync-review
description: Review upstream fixes and features for a private fork before selective sync.
disable-model-invocation: true
---

# Upstream Sync Review

Run a periodic audit of upstream fixes without losing the fork's local decisions.

Default mode is audit-only. Do not change tracked files unless the user explicitly says to apply, sync, fix, or commit selected upstream work.

## Local decisions

For `unity-mcp-yoji`, preserve these decisions:

- package identity: `com.yoji.unity-mcp`, `Yoji Unity MCP`
- supported clients: Claude Code and Codex only
- removed surface: telemetry, analytics, external reporting, upstream website, CI, publishing, sponsor, MCPB packaging
- install mode: Git UPM from the fork URL
- sync strategy: selective porting, not wholesale upstream merge

## Audit workflow

1. Preflight.

   ```powershell
   rtk git status --short --branch
   rtk git remote -v
   rtk git fetch --all --prune
   ```

   Stop if the working tree is dirty unless the user explicitly allows touching dirty changes.

2. Locate upstream.

   Prefer an existing `upstream` remote. If missing, inspect fork metadata or git history. If the URL is still unknown, ask for it. Do not add a remote unless the URL is verified.

3. Compare.

   ```powershell
   rtk git merge-base HEAD upstream/main
   rtk git log --oneline --decorate --cherry-pick --right-only HEAD...upstream/main
   rtk git diff --stat HEAD...upstream/main
   ```

   If upstream uses `beta` or another integration branch, repeat against that branch and name the branch used.

4. Classify every candidate.

   - Take: security fixes, correctness fixes, Unity compatibility, dependency fixes, tests for kept behavior.
   - Investigate: useful features that may fit after pruning upstream clients/reporting.
   - Skip: telemetry, analytics, reporting, unsupported clients, branding, website, CI, publishing, MCPB, release chores.

5. Run guardrail scans before recommending any patch.

   See [REFERENCE.md](REFERENCE.md) for scan commands, apply mode, and failure handling.

6. Report.

   ```text
   upstream: <remote>/<branch> @ <sha>
   local: <branch> @ <sha>
   merge-base: <sha>

   take:
   - <sha> <title> -- <reason>

   investigate:
   - <sha> <title> -- <risk/question>

   skip:
   - <sha> <title> -- <reason>

   recommended next action:
   - <one concrete command or decision>
   ```

Completion: the user can decide what to apply without reading the full upstream log.

## Apply mode

Only enter apply mode when the user explicitly asks to apply selected upstream work.

Use [REFERENCE.md](REFERENCE.md). Completion: selected patches are isolated, guardrails pass, validation results are reported, and no skipped upstream surface remains.

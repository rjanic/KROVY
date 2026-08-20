# KROVY Skill: Git Diff Summary

## Purpose

Use this skill to summarize repository changes clearly before review, commit, push, or handoff.

This skill helps the user understand what changed, why it changed, and what risks remain.

## When to use

Use this skill after implementation, before commit, before push, before pull request, after a coding agent returns a long report, when the user asks what changed, or when preparing a release summary.

## Required inspection

Check:

- branch
- HEAD
- upstream/ahead/behind if available
- working tree status
- changed files
- staged vs unstaged changes
- diff summary
- test result if available
- whether generated files are included
- whether unrelated files were modified

## Recommended commands

```bash
git status --short
git branch --show-current
git rev-parse HEAD
git diff --stat
git diff --check
git diff --name-status
```

Use additional commands only if needed.

## Summary rules

The summary must be factual.

Do not exaggerate.

Do not claim tests passed unless they were actually run.

Do not claim a fix is complete unless validation supports it.

If source code was modified, group changes by area:

- Core
- AutoCAD host
- UI/WPF
- Tests
- Localization
- Scripts/CI
- Documentation
- Generated assets

## Output format

```md
## Git Diff Summary

| Check | Result |
| --- | --- |
| Branch | `<branch>` |
| HEAD | `<commit>` |
| Working tree | `<clean/dirty>` |
| Changed files | `<count>` |
| Diff check | `PASS/FAIL/NOT RUN` |
| Tests | `PASS/FAIL/NOT RUN` |

### Change groups

#### Core
- ...

#### AutoCAD host
- ...

#### UI/WPF
- ...

#### Tests
- ...

#### Localization
- ...

#### Scripts/CI
- ...

#### Documentation
- ...

### Risk notes

- ...

### Suggested commit message

`<message>`

### Next step

`review`, `run tests`, `run HOST test`, `commit`, or `do not commit yet`
```

## Commit message guidance

Use concise imperative English.

Examples:

```text
Add KROVY agent workflow skills
Stabilize roof edit lifecycle
Fix annotation refresh after stretch
Add portable compatibility gate coverage
```

## Human approval

The agent must not commit or push unless explicitly instructed.

The agent may suggest a commit message but must not execute it without approval.

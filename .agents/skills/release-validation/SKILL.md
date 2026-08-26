# KROVY Skill: Release Validation

## Purpose

Use this skill before declaring a KROVY change ready for commit, push, tag, release, or handoff.

This skill validates the final repository state and produces a concise release-readiness report.

## When to use

Use this skill after implementation is complete and before commit, push, tag, GitHub release, pull request ready-for-review, handoff to the user, or declaring a milestone complete.

## Goal

Confirm that the repository is in a known safe state and that all required validation steps have been completed or clearly marked as not applicable.

## Required checks

The agent must check and report:

- branch
- HEAD commit
- upstream status if available
- working tree status
- list of changed files
- Debug build result
- Release build result
- test result
- warning count
- error count
- portable compatibility gate result
- full compatibility gate result, if applicable
- localization parity, if touched
- schema/version changes, if touched
- git diff check
- manual HOST testing status, if applicable

## Validation rules

Do not claim release readiness if any required build fails, any required test fails, warnings are present where warnings-as-errors policy applies, localization parity is broken, schema changes are undocumented, git status is unclear, HOST behavior is claimed without actual HOST validation, or the agent cannot determine what was tested.

## Recommended command groups

Use existing repository scripts when available.

Common examples:

```bash
git status --short
git branch --show-current
git rev-parse HEAD
git diff --check
dotnet build
dotnet test
```

Use repository-specific scripts if they exist:

```bash
pwsh ./scripts/compatibility-gate.ps1 -Portable
pwsh ./scripts/compatibility-gate.ps1 -Full
```

Only run commands that are appropriate for the current environment.

Do not invent scripts.

## HOST testing rule

AutoCAD HOST testing is separate from portable validation.

If HOST testing is required but was not performed, report:

```text
HOST validation: NOT RUN
Release verdict: NOT READY FOR HOST-AFFECTING CHANGE
```

Do not infer HOST PASS from unit tests.

## Required output format

```md
## KROVY Release Validation

| Check | Result |
| --- | --- |
| Branch | `<branch>` |
| HEAD | `<commit>` |
| Upstream | `<ahead/behind or unknown>` |
| Working tree | `<clean/dirty>` |
| Changed files | `<count and summary>` |
| Debug build | `PASS/FAIL/NOT RUN` |
| Release build | `PASS/FAIL/NOT RUN` |
| Tests | `PASS/FAIL/NOT RUN` |
| Portable Gate | `PASS/FAIL/NOT RUN/NOT APPLICABLE` |
| Full Gate | `PASS/FAIL/NOT RUN/NOT APPLICABLE` |
| Localization parity | `PASS/FAIL/NOT RUN/NOT APPLICABLE` |
| Schema/version impact | `NONE/YES - described below/UNKNOWN` |
| Git diff check | `PASS/FAIL/NOT RUN` |
| HOST validation | `PASS/FAIL/NOT RUN/NOT APPLICABLE` |

### Verdict

`READY`, `NOT READY`, or `READY FOR NON-HOST DOCUMENTATION CHANGE ONLY`

### Changed files

- List changed files or summarize by category.

### Notes

- Mention exact commands executed.
- Mention skipped checks and why.
- Mention any manual tests required.
```

## Human approval

The agent must not commit, push, tag, or create a release unless explicitly instructed by the user.

The agent may recommend the next step but must not perform it without approval.

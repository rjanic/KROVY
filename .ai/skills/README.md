# KROVY AI Skills / Agent Workflows

This directory contains standardized agent workflows for the ACAD KROVY project.

The goal is to make AI-assisted development more consistent, safer, and cheaper by avoiding repeated long prompts and by giving agents clear reusable procedures.

## Principles

- Skills are agent-neutral.
- Skills must be usable by ChatGPT, Codex, Cursor, Cloud Agents, and future coding agents.
- Skills are guidance documents, not executable source code.
- Skills must not override human approval.
- Agents must not commit or push unless explicitly approved.
- HOST behavior must not be claimed as validated unless it was actually tested in AutoCAD.

## Available skills

- `portable-compatibility-gate.md`
- `release-validation.md`
- `cad-neutral-core-feature.md`
- `localization-check.md`
- `git-diff-summary.md`
- `host-regression-test.md`
- `roof-timber-lifecycle.md`

## Task-to-skill decision map

| Task | Use this skill | Add another skill when |
| --- | --- | --- |
| Change Core-domain behavior intended to work across CAD hosts | `cad-neutral-core-feature.md` | Run `portable-compatibility-gate.md` after implementation. |
| Validate portable projects, dependency boundaries, and portable tests | `portable-compatibility-gate.md` | Run `localization-check.md` if resources changed. |
| Prepare a commit, push, release, or handoff | `release-validation.md` | Use `git-diff-summary.md` for a focused change summary. |
| Review repository changes before commit, push, or handoff | `git-diff-summary.md` | Use `release-validation.md` when readiness must be declared. |
| Change user-facing strings or resource files | `localization-check.md` | Run `portable-compatibility-gate.md` if portable projects changed. |
| Validate behavior inside AutoCAD | `host-regression-test.md` | Use `roof-timber-lifecycle.md` for roof/timber ownership or lifecycle scenarios. |
| Change roof timber generation, ownership, editing, copy, transforms, or persistence | `roof-timber-lifecycle.md` | Run `host-regression-test.md` when AutoCAD behavior is affected. |

For a build that may load AutoCAD assemblies, close AutoCAD first. Do not build against an active AutoCAD process.

## Recommended use

Point the agent to the relevant skill before starting the task.

Example:

```text
Use .ai/skills/portable-compatibility-gate.md and run the Portable Compatibility Gate.
Do not modify source code.
Report PASS/FAIL using the required format.
```

## Human approval

These skills improve workflow discipline, but they do not replace developer review, AutoCAD HOST testing, manual release approval, or commit/push approval.

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

## Initial skills

- `portable-compatibility-gate.md`
- `release-validation.md`
- `cad-neutral-core-feature.md`
- `localization-check.md`
- `git-diff-summary.md`
- `host-regression-test.md`

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

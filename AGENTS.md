# Repository instructions for AI agents

Before starting a task, read the relevant files in `.ai/`.

Use this source-of-truth order:

1. Current source code.
2. Automated tests.
3. `.ai` architecture rules.
4. `ACAD_KROVY_PROJECT_CONTEXT.md`.
5. `ACAD_KROVY_ROADMAP.md` and `ACAD_KROVY_BACKLOG.md`.

Keep `AcKrovy.Core`, `AcKrovy.Cad.Abstractions`, `AcKrovy.Localization`, and `AcKrovy.Infrastructure` free of Autodesk, BricsCAD, ZWCAD, ODA, and Teigha dependencies.

Close AutoCAD before running any build that may load AutoCAD assemblies. Do not build against an active AutoCAD process.

Commit and push only after all required verification succeeds. Never create a release tag without an explicit user request.

Before prompts that ask Codex to perform substantial work, give the user an explicit model recommendation.

## Core project documents

- [.ai/architecture.md](.ai/architecture.md)
- [.ai/cad-abstractions.md](.ai/cad-abstractions.md)
- [.ai/localization.md](.ai/localization.md)
- [.ai/testing.md](.ai/testing.md)
- [.ai/release-process.md](.ai/release-process.md)
- [.ai/roadmap.md](.ai/roadmap.md)

## KROVY AI Skills / Agent Workflows

This repository contains reusable agent workflows under `.ai/skills/`.

Before starting a repeated workflow, use the matching skill:

- Portable Compatibility Gate: [.ai/skills/portable-compatibility-gate.md](.ai/skills/portable-compatibility-gate.md)
- Release Validation: [.ai/skills/release-validation.md](.ai/skills/release-validation.md)
- CAD-neutral Core Feature: [.ai/skills/cad-neutral-core-feature.md](.ai/skills/cad-neutral-core-feature.md)
- Localization Check: [.ai/skills/localization-check.md](.ai/skills/localization-check.md)
- Git Diff Summary: [.ai/skills/git-diff-summary.md](.ai/skills/git-diff-summary.md)
- HOST Regression Test: [.ai/skills/host-regression-test.md](.ai/skills/host-regression-test.md)
- Roof Timber Lifecycle: [.ai/skills/roof-timber-lifecycle.md](.ai/skills/roof-timber-lifecycle.md)

Hard rules:

- Do not commit or push unless explicitly approved.
- Do not modify unrelated files.
- Do not claim AutoCAD HOST behavior is validated unless it was actually tested in AutoCAD.
- AutoCAD HOST behavior is authoritative for command/event lifecycle. Do not infer ObjectAppended/ObjectErased/CommandEnded ordering solely from source-contract tests or code structure. For lifecycle-sensitive CAD fixes, prove the real HOST event/control-flow sequence before designing persistence, undo/redo, ownership, or recovery behavior.
- Keep CAD-neutral projects free from Autodesk, BricsCAD, ZWCAD, ODA, and Teigha dependencies.
- Prefer existing repository scripts and tests.
- Always report changed files, commands run, test results, PASS/FAIL verdict, and remaining risks.

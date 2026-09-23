# Repository instructions for AI agents

Before starting a task, read the relevant files in `.ai/`.

Use this source-of-truth order:

1. Current source code.
2. Automated tests.
3. `.ai` architecture rules.
4. `ACAD_KROVY_PROJECT_CONTEXT.md`.
5. `ACAD_KROVY_ROADMAP.md` and `ACAD_KROVY_BACKLOG.md`.

## Roof geometry contract (normative)

Before changing roof geometry, purlins, rafters, technical elevations, datums, seating, or related schematic presentation, read:

- [docs/geometry/roof-elevation-contract.md](docs/geometry/roof-elevation-contract.md) — *Geometrický slovník KROVY v1.0* (single SSOT for Cursor and Codex)

Treat that contract as normative. Accepted user-approved physical definitions, verified HOST physical elevation measurements, Core regression tests, the Core implementation, and the contract must stay mutually consistent. On any contradiction, **stop and report** — do not silently prefer whichever source ranks higher in a list, and do not let a current implementation bug override accepted physical HOST geometry. Schematic SVG is never authoritative for technical geometry. Do not mix physical-geometry fixes with presentation-only fixes. A test named `*HostRegression*` is not itself proof of a completed AutoCAD HOST retest. Do not overwrite existing uncommitted WIP. Do not commit or push unless explicitly requested.

Keep `AcKrovy.Core`, `AcKrovy.Cad.Abstractions`, `AcKrovy.Localization`, and `AcKrovy.Infrastructure` free of Autodesk, BricsCAD, ZWCAD, ODA, and Teigha dependencies.

Close AutoCAD before running any build that may load AutoCAD assemblies. Do not build against an active AutoCAD process.

Commit and push only after all required verification succeeds. Never create a release tag without an explicit user request.

Before prompts that ask Codex to perform substantial work, give the user an explicit model recommendation.

## Core project documents

- [docs/geometry/roof-elevation-contract.md](docs/geometry/roof-elevation-contract.md) — Geometrický slovník KROVY v1.0 (normative roof elevation / purlin geometry)
- [.ai/architecture.md](.ai/architecture.md)
- [.ai/cad-abstractions.md](.ai/cad-abstractions.md)
- [.ai/localization.md](.ai/localization.md)
- [.ai/testing.md](.ai/testing.md)
- [.ai/release-process.md](.ai/release-process.md)
- [.ai/roadmap.md](.ai/roadmap.md)

## KROVY AI Skills / Agent Workflows

This repository contains reusable agent workflows under `.agents/skills/`.

Before starting a repeated workflow, use the matching skill:

- Portable Compatibility Gate: [.agents/skills/portable-compatibility-gate/SKILL.md](.agents/skills/portable-compatibility-gate/SKILL.md)
- Release Validation: [.agents/skills/release-validation/SKILL.md](.agents/skills/release-validation/SKILL.md)
- CAD-neutral Core Feature: [.agents/skills/cad-neutral-core-feature/SKILL.md](.agents/skills/cad-neutral-core-feature/SKILL.md)
- Localization Check: [.agents/skills/localization-check/SKILL.md](.agents/skills/localization-check/SKILL.md)
- Git Diff Summary: [.agents/skills/git-diff-summary/SKILL.md](.agents/skills/git-diff-summary/SKILL.md)
- HOST Regression Test: [.agents/skills/host-regression-test/SKILL.md](.agents/skills/host-regression-test/SKILL.md)
- Roof Timber Lifecycle: [.agents/skills/roof-timber-lifecycle/SKILL.md](.agents/skills/roof-timber-lifecycle/SKILL.md)

Hard rules:

- Do not commit or push unless explicitly approved.
- Do not modify unrelated files.
- Do not claim AutoCAD HOST behavior is validated unless it was actually tested in AutoCAD.
- AutoCAD HOST behavior is authoritative for command/event lifecycle. Do not infer ObjectAppended/ObjectErased/CommandEnded ordering solely from source-contract tests or code structure. For lifecycle-sensitive CAD fixes, prove the real HOST event/control-flow sequence before designing persistence, undo/redo, ownership, or recovery behavior.
- Keep CAD-neutral projects free from Autodesk, BricsCAD, ZWCAD, ODA, and Teigha dependencies.
- Prefer existing repository scripts and tests.
- Always report changed files, commands run, test results, PASS/FAIL verdict, and remaining risks.

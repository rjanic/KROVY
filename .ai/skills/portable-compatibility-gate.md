# KROVY Skill: Portable Compatibility Gate

## Purpose

Use this skill to validate that the portable, CAD-neutral parts of ACAD KROVY still build and test correctly without requiring AutoCAD, BricsCAD, ZWCAD, or any Windows-only CAD host.

This gate protects the long-term multi-CAD architecture of KROVY.

## When to use

Use this skill after changes that may affect:

- `AcKrovy.Core`
- `AcKrovy.Cad.Abstractions`
- `AcKrovy.Localization`
- `AcKrovy.Infrastructure`
- portable tests
- domain models
- calculation logic
- serialization or metadata contracts
- release preparation
- architecture refactoring

Use this skill before claiming that a change is safe for the CAD-neutral layer.

## Goal

Confirm that:

1. portable projects build successfully,
2. portable tests pass,
3. CAD API types do not leak into Core or Abstractions,
4. localization resources remain valid where relevant,
5. the result is reported as clear PASS / FAIL.

## Architectural rules

The following projects must remain CAD-neutral:

- `AcKrovy.Core`
- `AcKrovy.Cad.Abstractions`
- `AcKrovy.Localization`
- `AcKrovy.Infrastructure`

These projects must not reference or depend on:

- Autodesk AutoCAD APIs
- BricsCAD APIs
- ZWCAD APIs
- Windows-only UI APIs
- AutoCAD database/entity types
- host-specific command APIs

Domain logic belongs in Core.

Host-specific logic belongs in the relevant CAD adapter / host project.

## Allowed scope

The agent may inspect and test portable project files, portable test projects, CI configuration related to portable compatibility, dependency references, build output, and test output.

The agent must not modify source code unless the user explicitly asked for implementation.

For a pure gate run, the agent must only report findings.

## Recommended commands

Prefer the existing repository scripts if available.

If scripts are not available, use the repository’s current documented build/test commands.

Typical checks may include:

```bash
dotnet restore
dotnet build
dotnet test
```

If the repository has specific gate scripts, use those instead of inventing new ones.

Examples may include:

```bash
pwsh ./scripts/portable-compatibility-gate.ps1
pwsh ./scripts/full-gate.ps1
```

Only use commands that actually exist in the repository.

## Validation checklist

Check and report:

- [ ] Git working tree state before the gate
- [ ] Current branch
- [ ] Current HEAD commit
- [ ] Restore result
- [ ] Build result
- [ ] Portable test result
- [ ] Warning count
- [ ] Error count
- [ ] CAD API dependency leakage check
- [ ] Localization/resource issues, if touched
- [ ] Final PASS / FAIL verdict

## CAD API leakage check

Inspect portable projects for forbidden references or namespaces.

Forbidden examples:

- `Autodesk.AutoCAD`
- `Bricscad`
- `ZwSoft`
- `ZWCAD`
- `ODA`
- `Teigha`
- `AcDb`
- `DatabaseServices`
- `EditorInput`
- `ApplicationServices`
- `Runtime.CommandMethod`

If any of these appear in CAD-neutral projects, report FAIL unless the occurrence is in documentation, comments, or an explicit compatibility note.

## PASS criteria

The gate is PASS only if all of the following are true:

- restore succeeds,
- build succeeds,
- tests pass,
- warnings are acceptable or zero according to repository policy,
- no forbidden CAD API dependency leaks into portable projects,
- no touched localization/resource file is structurally broken,
- the working tree state is clearly reported.

## FAIL criteria

The gate is FAIL if any of the following happens:

- restore fails,
- build fails,
- any portable test fails,
- forbidden CAD API types leak into Core or Abstractions,
- the agent cannot determine whether the correct tests were run,
- the agent skips validation but still claims success,
- the output is incomplete or ambiguous.

## Stop rules

Stop and ask for human guidance if:

- required scripts are missing,
- project structure is unclear,
- test failures appear unrelated to the current change,
- the repository is already dirty before the task and the user did not authorize changes,
- the agent detects CAD API leakage in Core or Abstractions,
- the gate requires AutoCAD GUI or host execution.

Do not guess a PASS.

Do not mark HOST behavior as validated from this gate.

This gate does not replace AutoCAD HOST testing.

## Required output format

```md
## Portable Compatibility Gate

| Check | Result |
| --- | --- |
| Branch | `<branch>` |
| HEAD | `<commit>` |
| Working tree before | `<clean/dirty>` |
| Restore | `PASS/FAIL/NOT RUN` |
| Build | `PASS/FAIL/NOT RUN` |
| Tests | `PASS/FAIL/NOT RUN` |
| Warnings | `<count or unknown>` |
| Errors | `<count or unknown>` |
| CAD API leakage | `PASS/FAIL/NOT CHECKED` |
| Localization/resource check | `PASS/FAIL/NOT APPLICABLE/NOT CHECKED` |

### Verdict

`PASS` or `FAIL`

### Notes

- Short factual notes only.
- Mention skipped checks clearly.
- Mention commands actually executed.
- Mention files changed only if implementation was explicitly requested.
```

## Human approval

The agent must not commit or push after running this gate unless the user explicitly approves it.

The agent must not claim release readiness from this gate alone.

Release readiness requires the Release Validation skill and, where relevant, HOST regression testing.

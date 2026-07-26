# Release process for AI agents

## Purpose

Close a tested milestone with consistent versions, documentation, validation, commit and push while avoiding accidental tags or incomplete releases.

## Hard rules

- Do not push an incomplete or materially changed feature that has not received its required manual host verification.
- After a large verified change, create a focused commit and push it.
- Never create a release tag unless the user explicitly requests one.
- Bump metadata schema only for a real persisted payload-contract change.
- Bump layer-profile version only for a real serialized profile-format change.
- UI preferences use separate local storage and never affect metadata schema.

## Current architecture or workflow

Perform these steps in order:

1. Audit `git status`, the complete diff and untracked files.
2. Remove temporary diagnostics, probes, dead experiments and local paths while retaining production guards/fallbacks.
3. Bump the central application version only in `Directory.Build.props`.
4. Bump `TimberElementDataSchema.CurrentVersion` only if metadata payload structure changed.
5. Bump `ElementLayerProfile.CurrentVersion` only if its serialized format changed; include migration tests.
6. Update all six localization resources and verify key/placeholder parity.
7. Update `README.md`, `README_SK.txt`, project context, roadmap, backlog and changelog if the repository uses one.
8. Run targeted tests.
9. Run the complete test suite.
10. Build Debug x64 and Release x64 with warnings as errors.
11. Run Portable and Full Compatibility Gates.
12. Run `git diff --check`, inspect `git status` and `git diff --stat`.
13. Create one focused commit.
14. Push the intended branch.
15. Verify the working tree is clean.
16. Fetch/compare and verify `HEAD` equals `origin/main` when releasing main.
17. Do not tag unless explicitly instructed.

If any automated check fails, fix only the cause and rerun the relevant check followed by the complete final matrix. If a required manual AutoCAD scenario has not been run, report that fact and do not present the milestone as fully host-verified.

## Common failure modes

- Updating the bundle manifest but not the central version, or vice versa.
- Raising schema/profile versions for UI-only changes.
- Committing generated build artifacts or local diagnostic folders.
- Pushing after targeted tests but before Release or compatibility gates.
- Creating a tag because the product version changed.
- Claiming `HEAD == origin/main` without fetching/comparing the refs.

## Checklist before changing this area

- Confirm branch, remote and intended milestone.
- Determine independently whether product, metadata and profile versions change.
- Identify required manual host scenarios.
- Ensure documentation describes only implemented behavior.
- Complete every validation step and retain exact command outcomes for the report.
- Verify no tag command is part of the workflow.

## Relevant source files and tests

- `Directory.Build.props`
- `deploy/AcKrovy.bundle/PackageContents.xml`
- `src/AcKrovy.Core/Models/TimberElementDataSchema.cs`
- `src/AcKrovy.Cad.Abstractions/Layers/ElementLayerProfile.cs`
- `scripts/compatibility-gate.ps1`
- `docs/COMPATIBILITY_GATE.md`
- `README.md`
- `ACAD_KROVY_PROJECT_CONTEXT.md`
- `.github/workflows/compatibility-gate.yml`

See [testing.md](testing.md) for the validation matrix and [roadmap.md](roadmap.md) for milestone sequencing.

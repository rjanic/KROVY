# Ordinary ERASE / MIRROR rebuild contract — 2026-10-05

Implemented: normal Ordinary ERASE removes the current complete package without a persistent generator exclusion. Accepted AUTO MIRROR erase-source YES removes the current source package and retains the mirrored Independent package; explicit authoritative regeneration recreates the AUTO slot. Independent packages remain outside the roof generator and canonical GROUP. Physical geometry builders, horizontal-width frame and shared confirmation/warning preferences were retained.

## Evidence and scope

Repository: `C:\Users\Roman\Documents\CODEX\C#\CsharpProjects\ACAD_krovy`; branch `main`; HEAD `cb8f2ae225c7d0ddfc9b60ca5fb4e8200ef6493a`. The working tree already contained substantial uncommitted WIP. No commit, push or tag was performed.

The existing HOST log `%LOCALAPPDATA%\ACAD_KROVY\Logs\ACAD_KROVY-20261005.log` establishes the former control flow. At 22:37:51, native ERASE of AUTO line `294F` reached native end before the legacy `ROOF_MANUAL_EDIT_ACCEPT action=suppress` and recovery fallback. At 22:38:00, Solid `2A30` was erased before Line `2950`; native-end Ordinary solid count 26 increased to 27 before later suppression processing. These are observations of the previous implementation, not validation of this fix. They motivate processing the final erased Plan2D package before derived-only recovery.

## Changes

- `src/AcKrovy.AutoCAD/Infrastructure/RoofOrdinaryEraseLifecycleService.cs` (new): command-start package snapshots, once-only terminal processing, complete-package deletion, Plan2D precedence for mixed selection, derived-only restoration/shared warning, exact GROUP and snapshot verification, transaction failure restoration, and DEBUG diagnostics after verified commit.
- `LiveGeometrySynchronizationService.cs`: captures/disposes the ERASE context and filters claimed package events before structural and legacy recovery. No ERASE context is created at UNDO/REDO boundaries.
- `RoofUnsupportedStretchRecoverySnapshotService.cs`, `RoofUnsupportedStretchRecoveryService.cs`, `RoofLiveResizeService.cs`: transient per-member claims prevent a second legacy interpretation/restoration of an already handled package. Claims do not survive command disposal and are not stored in the DWG.
- `RoofGeneratedMemberManualEditService.cs`: removes the former Ordinary ERASE suppression write; annotation-only and existing AttachedManual deletion handling remain available.
- `RoofOrdinaryCopyLifecycleService.cs`: accepted MIRROR source replacement preserves the generator recipe, deletes the current source package, writes no suppression, and reports `suppressionWritten=False`. Existing KROVY NO rollback remains before accepted writes.
- `RoofMirrorCloneDetachService.cs`: the legacy suppression helper rejects Ordinary keys. `RoofIndependentOrdinaryDetachService.cs` preserves the generator recipe before transferring the last AUTO instance; it writes no suppression.
- `RoofGeneratedRafterSetService.cs`: retains the existing generation recipe, supports an empty live AUTO inventory, forces explicit `roof-edit` regeneration even with unchanged geometry, and removes legacy Ordinary suppression from authoritative generator inputs. It does not discover or materialize Independent members.
- `RoofEditCommandWorkflow.cs`: explicit Apply normalizes legacy Ordinary suppression. Source-resize regeneration uses the same Core rule.
- `src/AcKrovy.Core/Services/Roofs/RoofOrdinaryEraseRules.cs` (new): neutral authority/action classification. `RoofOrdinaryRebuildRules.cs` (new): removes legacy Ordinary suppression while leaving other compatible metadata intact.
- `src/AcKrovy.Core/Models/Roofs/RoofDefinitionData.cs`, `RoofDefinitionDataSchema.cs`; `Services/Roofs/RoofDefinitionDataCodec.cs`, `RoofDefinitionPersistence.cs`, `RoofGeneratedMemberOverrideRules.cs`, `RoofGroupGripResizeAdoptionRules.cs`: persist an optional existing `RoofRafterGenerationRecipe` in roof definition schema 6 and retain reading/round-trip of schemas 1–5. Geometry updates and edit-state preservation retain the recipe. Invalid/ambiguous recipes fail rather than invent a configuration.
- `docs/geometry/roof-elevation-contract.md`: normative new deletion/rebuild contract. Previous warning/MIRROR report links to this superseding rule.

Schema 6 is necessary because the previous adapter reconstructed the generation recipe from live AUTO lines. Without retained width/height/spacing/material, deleting or detaching the last AUTO instance and saving/reopening would lose the input needed for regeneration. The recipe contains neither deleted geometry nor Independent identity and is not an exclusion model. Existing older drawings with no remaining AUTO instance and no stored recipe cannot recover an unknown historical recipe; an explicit valid generation configuration is needed for that pre-existing case.

## Automated verification

Verdict: **PASS**. This is compile, Core/domain and source-contract verification; new AutoCAD HOST behavior remains **NOT TESTED**.

Commands run with elevated process checks confirming `acad.exe = NOT RUNNING` before builds:

```powershell
git rev-parse --show-toplevel
git branch --show-current
git rev-parse HEAD
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --configuration Debug --no-restore --logger 'console;verbosity=minimal'
dotnet build src/AcKrovy.AutoCAD/AcKrovy.AutoCAD.csproj --configuration Debug --no-restore -p:Platform=x64 -warnaserror
.\scripts\compatibility-gate.ps1 -Full
git diff --check
```

Final Full Compatibility Gate (including Portable): Core **7567/7567**, WPF **829/829**, no failures/skips, zero build warnings/errors. Architecture dependency checks and restore passed. Standard adapter output is `src/AcKrovy.AutoCAD/bin/x64/Debug/net10.0-windows/AcKrovy.AutoCAD.dll`; no alternate output, copied DLL or runtime/hash/timestamp probe was used. Gate log: `.ai/handoffs/ordinary-erase-rebuild-2026-10-05/full-gate.log`.

`RoofOrdinaryEraseRebuildTests.cs` adds 17 cases for AUTO/Independent Plan2D and mixed/derived-only classification, recipe round-trip without live instances, invalid/malformed recipes, complete current-geometry replay after removing legacy suppression, retained recipe through geometry/edit-state changes, and first-claim/no-suppression adapter guards. `RoofOrdinaryMirrorLifecycleTests.cs`, `RoofGeneratedMemberUnlockedEditSourceContractTests.cs`, `RoofCopyEraseSourceContractTests.cs`, `RoofSplitEraseSourceContractTests.cs`, `RoofOrdinaryRafterSolidSourceContractTests.cs` were adjusted to the new authoritative contract. Existing roof-schema guards now expect 6; explicit schema-5 compatibility tests still assert their unchanged payload format. Initial failures were stale schema-5/current-version assumptions and former suppression assertions; final gate passed after those contracts were corrected. No timber, annotation or AttachedManual schema was bumped.

## Manual HOST sequence and remaining risks

Start AutoCAD Architecture 2027 with `C:\Users\Roman\Documents\3d.dwg`, then stop. No manual `AK_RUNTIME_BUILD`, ERASE, rebuild, save or geometry test is performed by the agent.

1. User erases one untouched AUTO Ordinary Plan2D line. Expect the complete package absent, no detach dialog/Independent, `suppressionWritten=False`, and no immediate regeneration. **STOP and send fresh log.**
2. Separately run explicit `AK_ROOF_EDIT Apply` / authoritative rebuild. Expect current AUTO geometry, Physical3D, annotations and canonical GROUP restored.
3. AUTO MIRROR, native erase-source YES, KROVY accept; verify Independent output and absent source before rebuilding. Rebuild must restore the AUTO slot while leaving the mirrored Independent package unchanged/outside GROUP.
4. AUTO MOVE with accepted detach, then rebuild: original AUTO slot returns; moved Independent stays at its accepted position.

Actual native ERASE event routing, package/annotation inventory, GROUP canonical state, mixed/multi-member operation, derived-only warning ON/OFF, save/reopen, UNDO/REDO and visual behavior remain HOST verification. Source-contract tests do not establish these outcomes.

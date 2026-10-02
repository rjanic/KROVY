# Generated Structural MIRROR clone: narrow fix

Date: 2026-10-01. Branch: `main`. Starting HEAD: `3e0e1337978504b0447bab4c2968ca4691447b12`.
Existing Structural Foundation WIP was retained. No commit, push, tag, deployment or AutoCAD edit was performed.

## Root cause and COPY/MIRROR divergence

The supplied HOST trace proves that native MIRROR No appends `2A39` from Generated Hip `29F4`, owner `2912`, logical key `Hip|1|2`, modifies the clone, and leaves inherited Generated identity at `native-ended`. The clone disappears by `after-maintenance`. These observations are supplied HOST evidence, not a new HOST test of this patch.

The first differing structural decision is in `src/AcKrovy.Core/Services/Roofs/RoofStructuralEditRules.cs`:

- `IsManualCloneAcceptCommand` previously accepted only COPY.
- `IsCloneRejectCommand` explicitly included MIRROR.
- Consequently `Classify(MIRROR, Plan2D, Unlocked)` returned `RejectClone`; COPY returned `AcceptManualClone`.

In `src/AcKrovy.AutoCAD/Infrastructure/RoofStructuralNativeEditService.cs`, `Process` detects an appended Generated Line by absence from the pre-command timber snapshot. COPY takes `TryConvertCloneToAttachedManual`. MIRROR instead took the reject branch, populated `eraseCloneIds`, and erased the native clone before the shared physical rebuild. The structural claim runs after whole-roof rebind and before ordinary clone handling/generic recovery in `LiveGeometrySynchronizationService`.

There is also a necessary placement constraint: `RoofStructuralRafterSolidMaterializationService.TryResolveManualPlacement` previously required `TryMatchRigidPlanCopy`, then translated the source frame. Simply removing MIRROR from the reject list would fail or incorrectly place reflected geometry. The patch addresses this constraint in the existing materializer.

The new MIRROR classification regression was run against the initial production implementation and failed: expected `AcceptManualClone`, actual `RejectClone`. A prefixed command fixture uses uppercase `_.MIRROR`, matching the existing native global-command normalization contract.

## Minimal production changes

1. `RoofStructuralEditRules.IsManualCloneAcceptCommand` now accepts COPY and MIRROR; only ARRAY variants remain in `IsCloneRejectCommand`. Existing creation kind `Mirror` is reused.
2. `RoofStructuralNativeEditService.Process` limits this support to appended Generated Plan clones. Snapshotted sources, in-place MIRROR results, and clones already carrying ManualStructural metadata keep their prior restore/reject policy. COPY routing is unchanged.
3. `RoofStructuralManualPlacementRules.TryReflectFrame` recovers a vertical-plane reflection from corresponding source/final clone Plan endpoints. It reflects the source physical axis and Side/Up vectors, preserves Z and section height, and rejects invalid/non-planar/non-finite/length-changing or glide-reflection inputs. The source frame may extend beyond the logical Plan; its actual physical endpoints are transformed, not replaced by Plan endpoints.
4. `RoofStructuralRafterSolidMaterializationService.TryResolveManualPlacement` selects that reflection only for `CreationKind.Mirror`. COPY retains its original rigid match and translation. The resulting frame is persisted through existing schema-2 placement and rebuilt through existing `TryBuildPrism`/solid materialization. DEBUG reports `placementMode=RigidMirror`.

No schema, enum, persistence mechanism, command event collector, undo mechanism, generated physical builder, ordinary rafter path or UI was added/redesigned.

Generated-to-manual conversion still uses `WriteReplacingGenerated` and its same-transaction live metadata checks. The existing unique identity allocator, source logical-key provenance, Z=0 normalization, physical reconciliation, GROUP sync/finalization and committed key-set verification are reused. No second MIRROR lifecycle exists.

## Tests

New file: `src/AcKrovy.Core.Tests/RoofStructuralMirrorCloneTests.cs` (12 cases).

- Shared unlocked clone classification and locked rejection, including prefixed MIRROR and both Plan/Physical command dispositions.
- Hip and Valley reflection through vertical, horizontal and oblique planes at nonzero WCS coordinates.
- Physical axis and all eight prism corners match an independently computed reflection; Side/Up and section/elevations are preserved appropriately.
- Manual identity/provenance, persisted placement JSON roundtrip, independent key cardinality, Z=0 Plan and repeatable rebuild from persisted placement.
- Parallel/coincident mirror plane cases preserve reflection of the section even when the Plan direction is unchanged.
- Invalid geometry, changed length, non-finite coordinates, non-planar inputs and glide reflections fail closed.
- Adapter wiring uses shared atomic metadata replacement, source guard, physical build and canonical GROUP/committed key-set checks; COPY translation remains wired.

`RoofStructuralFoundationTests` updates the old MIRROR rejection expectation to the requested support and retains every ARRAY rejection/COPY/MOVE/ERASE assertion. No assertion was weakened to hide a failure.

The focused run includes existing AttachedManual/XData replacement, Structural Foundation, Plan override placement and Structural solid source contracts. Source contracts establish adapter wiring; actual entity survival, AutoCAD XData, Solid3d and GROUP behavior still require HOST retest.

## Validation

| Check | Result |
| --- | --- |
| Focused structural/native lifecycle tests | PASS: 137/137, 0 skipped |
| Full Core tests | PASS: 7161/7161, 0 skipped |
| WPF tests | PASS: 806/806, 0 skipped |
| Debug x64 warnings-as-errors build | PASS: Full gate solution build, 0 warnings / 0 errors |
| Release x64 warnings-as-errors build | PASS: solution build, 0 warnings / 0 errors |
| Portable Compatibility Gate | PASS: architecture, version, restore, portable builds and tests |
| Full Compatibility Gate | PASS: portable checks plus adapter build and complete solution tests |
| git diff --check | PASS: exit 0; existing LF/CRLF Git notices only |
| AutoCAD HOST retest | PENDING: user must execute the one-operation procedure below |

Initial sandbox verification: default parallel restore exited 1 without diagnostics. Restore with `-m:1 -nr:false` passed. The serial Portable gate reached all 7161 Core tests: 7160 passed, one unrelated `SafeFileWriterTests` case failed with `UnauthorizedAccessException` at `File.Replace`. The same isolated test failed in sandbox and passed outside sandbox (1/1), without source/test changes. Final gate results are reported separately; the initial failure is not silently discarded.

Final Portable and Full gates ran outside sandbox after automatic approval; both unmodified scripts passed. Core, Abstractions, Localization and Infrastructure dependency checks passed. Full gate built the solution's existing Debug|x64 configuration. AutoCAD process checks found no running acad.exe before adapter/Release builds. Release tests were not rerun; the requested Release build succeeded and the complete Debug test matrix passed. Final verdict: **AUTOMATED PASS; HOST RETEST PENDING**.

Commands executed (repository-root paths):

```powershell
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore -warnaserror -m:1 -nr:false --filter FullyQualifiedName~RoofStructuralMirrorCloneTests
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore -warnaserror -m:1 -nr:false --filter "FullyQualifiedName~RoofStructuralMirrorCloneTests|FullyQualifiedName~RoofStructuralAttachedManual|FullyQualifiedName~RoofStructuralFoundation|FullyQualifiedName~RoofStructuralPlanOverridePhysicalPlacement|FullyQualifiedName~RoofStructuralRafterSolidSourceContract"
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Portable
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Full
dotnet build AcKrovy.sln --no-restore -c Release -p:Platform=x64 -warnaserror -m:1 -nr:false
dotnet restore src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj -m:1 -nr:false --verbosity normal
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-build --no-restore --filter FullyQualifiedName~SafeFileWriterTests.WriteAllBytes_CreatesAndAtomicallyReplacesDestinationWithoutTemporaryFile
git diff --check
git status --short
```

Temporary validation logs and snapshots of the starting WIP for edited files are retained under `C:\Users\Roman\AppData\Local\Temp\krovy-mirror-37aee3b37a1c487d9f067d8267844962`. Comparison against those snapshots confirms only the narrow task delta; existing COPY foundation and source-contract files outside this task were not edited. No Git restoration/stashing operation occurred; HEAD is unchanged.

## Changed files for this task

These are this task's edits, not the entire pre-existing Git diff:

- `src/AcKrovy.Core/Services/Roofs/RoofStructuralEditRules.cs`
- `src/AcKrovy.Core/Services/Roofs/RoofStructuralManualPlacementRules.cs` (pre-existing untracked WIP)
- `src/AcKrovy.AutoCAD/Infrastructure/RoofStructuralNativeEditService.cs`
- `src/AcKrovy.AutoCAD/Infrastructure/RoofStructuralRafterSolidMaterializationService.cs`
- `src/AcKrovy.Core.Tests/RoofStructuralFoundationTests.cs`
- `src/AcKrovy.Core.Tests/RoofStructuralMirrorCloneTests.cs` (new)
- This report (new).

## HOST Test Plan — ONE MIRROR No only

Environment: target AutoCAD 2027; current Debug x64 build (version 0.23.0); branch/HEAD above; user must record actual loaded assembly path/timestamp and test DWG. Use the saved failure drawing, normally `C:\Users\Roman\Documents\3d.dwg`, only if it still contains this exact setup. Existing startup/autoload remains authoritative.

Preconditions:

- Owner `2912`, Hip, Unlocked, 3 Generated structural rafters after the accepted previous ERASE.
- Source Plan2D Line `29F4`, Generated key `Hip|1|2`; source and all Generated endpoints are at Z=0. Record original endpoint coordinates and element identity.
- Record baseline ManualStructural count, physical count, DBMOD and canonical GROUP audit.
- The failed clone `2A39` is absent. The new clone handle must be captured from the new native MAP, not hardcoded as `2A39`.
- Verify `AK_RUNTIME_BUILD` output identifies the new Debug assembly. Use `AK_ROOF_3D_TRACE` only as needed until its output says `enabled=True` (it toggles). Run `AK_ROOF_3D_AUDIT` for baseline.

Steps:

1. Set UCS to World. Temporarily set PICKSTYLE=0 to select only the source Line. Let its start XY be `(Xs,Ys)`; disable object snaps or use `_non` for the two mirror points.
2. Execute exactly one native MIRROR, selecting only `29F4`. Use mirror points `(Xs+1000,Ys-5000,0)` and `(Xs+1000,Ys+5000,0)` (vertical WCS mirror line). Answer **No** to erase source objects.
3. Wait for maintenance; run `AK_ROOF_3D_AUDIT`. Capture the complete command transcript from MIRROR start through native MAP/events, `native-ended`, structural manualization and `after-maintenance`.
4. Inspect the surviving clone and its owned Physical3D; restore the previous PICKSTYLE. No further COPY/MOVE/ERASE/MIRROR operation is part of this retest.

Expected results:

- Source `29F4` retains its original handle, Generated identity/key, Plan endpoints and original physical placement. Generated structural count stays 3.
- New native clone survives and its `after-maintenance` data is not null. Its key is a new unique `ManualStructural:<guid>`, with source provenance `Hip|1|2`, creation kind Mirror, no StructuralGenerated XData, and both Plan Z values exactly 0.
- For each original Plan endpoint `(X,Y,0)`, clone endpoint is `(2*(Xs+1000)-X,Y,0)`. For each source-frame physical axis point `(X,Y,Z)`, manual placement is `(2*(Xs+1000)-X,Y,Z)`; frame vectors reflect X. Compare frame/body geometry, not a translated original solid or schematic display edge.
- DEBUG includes `ROOF_STRUCT_MANUALIZE_PLACEMENT ... placementMode=RigidMirror`, followed by `ROOF_STRUCTURAL_NATIVE_CLAIM ... command=MIRROR ... action=semantic-reconciled result=committed`.
- ManualStructural count increases by one. A corresponding owned manual StructuralRafterSolid exists with matching ManualStructural identity. GROUP finishes `canonical=True`, `expected=actual`, `missing=0`, no duplicate/surplus/foreign members.

Data to capture: AutoCAD/build/path/timestamp/DWG, baseline and final DBMOD/counts/GROUP audit, native source→clone MAP, endpoint and metadata snapshots, full diagnostic transcript, Plan and Physical3D screenshots/coordinates. Persistence/undo/redo and other native operations are outside this single-operation retest.

HOST verdict remains **PENDING / INCONCLUSIVE** until the user performs the test and supplies evidence. Automated PASS does not establish AutoCAD event ordering or HOST physical geometry PASS. Scope does not add support for MIRROR Yes or mirroring an existing ManualStructural child.

## Final git status --short

```text
 M src/AcKrovy.AutoCAD/Infrastructure/LiveGeometrySynchronizationService.cs
 M src/AcKrovy.AutoCAD/Infrastructure/RoofAssemblyGroupMemberCollector.cs
 M src/AcKrovy.AutoCAD/Infrastructure/RoofCommandWorkflow.cs
 M src/AcKrovy.AutoCAD/Infrastructure/RoofDisplayErasePreCommandMapService.cs
 M src/AcKrovy.AutoCAD/Infrastructure/RoofOrdinaryRafterSolidMaterializationService.cs
 M src/AcKrovy.AutoCAD/Infrastructure/RoofPhysical3DHostDiagnostics.cs
 M src/AcKrovy.AutoCAD/Infrastructure/RoofRafterCommandWorkflow.cs
 M src/AcKrovy.AutoCAD/Infrastructure/RoofStructuralGeneratedStore.cs
 M src/AcKrovy.AutoCAD/Infrastructure/RoofStructuralNativeEditService.cs
 M src/AcKrovy.AutoCAD/Infrastructure/RoofStructuralRafterSolidMaterializationService.cs
 M src/AcKrovy.AutoCAD/Infrastructure/RoofUnsupportedStretchRecoveryService.cs
 M src/AcKrovy.AutoCAD/Infrastructure/RoofUnsupportedStretchRecoverySnapshotService.cs
 M src/AcKrovy.AutoCAD/Infrastructure/RoofWholeRoofCopyRebindService.cs
 M src/AcKrovy.AutoCAD/UI/RoofRafterWindow.xaml
 M src/AcKrovy.AutoCAD/UI/RoofRafterWindow.xaml.cs
 M src/AcKrovy.Core.Tests/RoofPhysical3DHostDiagnosticsSourceContractTests.cs
 M src/AcKrovy.Core.Tests/RoofStructuralFoundationSourceContractTests.cs
 M src/AcKrovy.Core.Tests/RoofStructuralFoundationTests.cs
 M src/AcKrovy.Core.Tests/RoofStructuralRafterSolidSourceContractTests.cs
 M src/AcKrovy.Core/Models/Roofs/RoofRafterCreationRequest.cs
 M src/AcKrovy.Core/Models/Roofs/RoofStructuralEditState.cs
 M src/AcKrovy.Core/Services/Roofs/RoofAutomaticRafterPhysicalBuilder.cs
 M src/AcKrovy.Core/Services/Roofs/RoofStructuralEditRules.cs
?? .ai/handoffs/
?? .cursor/rules/acad-build-lock.mdc
?? "ChatGPT Image 23. 8. 2026, 11_10_39.png"
?? "ChatGPT Image 23. 8. 2026, 11_17_14.png"
?? ErrorReports/9d6767a77b9d2cce90e03082e1302957689eeac5/
?? ErrorReports/GroupUneraseProbe/ErrorReports/
?? ErrorReports/Physical3DCloneProbe/
?? KROVY_roof_icons_v1_preview.png
?? docs/ACAD_HOST_WORKFLOW.md
?? docs/ATTACHEDMANUAL_PHYSICAL_LIFECYCLE_2026-09-30.md
?? docs/HEAVY_LIFECYCLE_AUDIT_2026-09-30.md
?? docs/HIP_VALLEY_ATTACHED_MANUAL_STRUCTURAL_2026-10-01.md
?? docs/HIP_VALLEY_NATIVE_LIFECYCLE_AUDIT_2026-10-01.md
?? docs/HIP_VALLEY_STRUCTURAL_FULL_NATIVE_EDITING_2026-10-01.md
?? docs/HIP_VALLEY_STRUCTURAL_MIRROR_CLONE_FIX_2026-10-01.md
?? docs/MEMBER_BREAK_COPY_MIRROR_AUDIT_2026-09-30.md
?? docs/MIXED_PHYSICAL_STRETCH_FIX_2026-09-30.md
?? docs/NATIVE_COPY_LIFECYCLE_FIX_2026-09-30.md
?? docs/NATIVE_MEMBER_REGRESSION_FIX_2026-10-01.md
?? docs/NATIVE_STRETCH_ROUTING_FIX_2026-09-30.md
?? docs/PHYSICAL_3D_CLONE_OWNERSHIP_DIAGNOSIS_2026-09-26.md
?? docs/PHYSICAL_3D_EAVES_RETEST_2026-09-26.md
?? docs/PHYSICAL_3D_LOCKED_ERASE_FIX_2026-09-26.md
?? docs/PHYSICAL_3D_NATIVE_CLONE_FIX_2026-09-26.md
?? scripts/acad-host-workflow.ps1
?? src/AcKrovy.AutoCAD/Infrastructure/RoofStructuralAttachedManualStore.cs
?? src/AcKrovy.Core.Tests/RoofStructuralAttachedManualFoundationTests.cs
?? src/AcKrovy.Core.Tests/RoofStructuralAttachedManualXDataReplaceTests.cs
?? src/AcKrovy.Core.Tests/RoofStructuralMirrorCloneTests.cs
?? src/AcKrovy.Core.Tests/RoofStructuralPlanOverridePhysicalPlacementTests.cs
?? src/AcKrovy.Core.Tests/SimpleGableOrdinaryPhysical3DTests.cs
?? src/AcKrovy.Core/Models/Roofs/RoofStructuralAttachedManualData.cs
?? src/AcKrovy.Core/Services/Roofs/RoofStructuralAttachedManualDataRules.cs
?? src/AcKrovy.Core/Services/Roofs/RoofStructuralManualPlacementRules.cs
?? src/AcKrovy.Core/Services/Roofs/SimpleGableOrdinaryRafterPhysicalAdapter.cs
?? src/AcKrovy.Core/Services/Roofs/SimpleGableRoofTopologyAdapter.cs
```

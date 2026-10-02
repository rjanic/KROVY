# Generated Structural MIRROR No: canonical annotations

Date: 2026-10-02. Branch: `main`. HEAD: `3e0e1337978504b0447bab4c2968ca4691447b12`.

Automated verdict: **PASS**. Actual AutoCAD HOST retest: **PENDING / NOT CHECKED**.
No commit, push, tag, version/schema bump or release was performed. Existing Structural Foundation WIP and the accepted identity/Placement/Physical3D/GROUP fixes were preserved.

## Exact root cause

In `RoofStructuralNativeEditService.Process`, the appended Generated Structural clone branch called `TryConvertCloneToAttachedManual`, then added the clone to `manualizedCloneIds` and `claimed` and continued. It never added the converted MIRROR clone to `mirrorCloneAnnotations`. The working appended ManualStructural branch already did so. Structural claims exclude these entities from the subsequent generic timber refresh, so no later canonical annotation phase repaired this omission.

`TryConvertCloneToAttachedManual` and `WriteReplacingGenerated` successfully replace Generated XData with a new ManualStructural identity and verify the live replacement in the same transaction. Generic timber metadata survives the replacement. The defect was the missing enqueue after that verified metadata write, rather than an identity, metadata serialization or physical placement defect.

Annotation ownership must use the cloned **Plan2D entity handle**. A ManualIdentity, Generated LogicalKey, provenance key, inherited manufacturing ElementId or Solid3d handle is not its annotation SourceHandle. Source and clone may share a manufacturing item number; matching by that number must not adopt the original source's annotation.

## Change scope and ordering

Only 23 production lines were added to the Generated clone success branch, starting at `RoofStructuralNativeEditService.cs:309`:

1. For MIRROR, resolve the original Generated source by exact roof owner, inherited LogicalKey and membership in the pre-command Plan handle snapshot. Require one exact source; no proximity guessing.
2. Use the existing `DeleteStructuralMirrorCloneAnnotations` helper with that original handle and the command's appended annotation IDs. This consumes inherited native annotation clones using the same path as Manual → Manual.
3. Read the converted clone's current generic timber and ManualStructural metadata, and enqueue its ObjectId/data in the existing `mirrorCloneAnnotations` dictionary.
4. The existing shared `TimberCreatedElementAnnotationService.EnsureForCreatedElements(..., copySourcePreservation: true)` creates the canonical set. `TimberAnnotationService` derives SourceHandle from the clone Plan entity. Source preservation disables manufacturing ElementId fallback.
5. Existing ordering remains: metadata conversion/live verification → native annotation cleanup/enqueue → canonical annotation batch → structural Physical3D reconcile → display rebuild → GROUP finalize → transaction commit → committed claim verification.

DEBUG adds `ROOF_STRUCT_GENERATED_MIRROR_CLONE source=... clone=... cloneIdentity=... annotations=canonical result=prepared`. Prepared is pre-commit and is not proof of a successful HOST final state.

No second annotation mechanism was introduced. COPY routing, Manual → Manual routing, identity allocation, Placement calculations, physical builders, GROUP implementation and shared annotation service were not edited by this task. This is the requested Generated MIRROR No scenario; other command variants have not received new HOST verification.

Files changed by this task only:

- `src/AcKrovy.AutoCAD/Infrastructure/RoofStructuralNativeEditService.cs` — missing canonical batch enqueue and existing cleanup call.
- `src/AcKrovy.Core.Tests/RoofStructuralGeneratedMirrorAnnotationTests.cs` — eight regression cases.
- `docs/HIP_VALLEY_GENERATED_MIRROR_ANNOTATION_FIX_2026-10-02.md` — this report and one HOST procedure.

SHA256 comparisons against the task-start backup confirm these six existing WIP files are unchanged: `RoofStructuralRafterSolidMaterializationService.cs`, `RoofStructuralAttachedManualStore.cs`, `RoofStructuralAttachedManualDataRules.cs`, `RoofStructuralManualPlacementRules.cs`, `TimberAnnotationService.cs`, and `RoofMirrorCloneDetachService.cs`. Router comparison against its backup shows exactly the 23-line addition above.

Task-start status and backups, plus gate/build logs, are in:
`C:\Users\Roman\AppData\Local\Temp\krovy-generated-mirror-annotations-2214eeca9840459f9fa8894a8c994eda`.

## Regression coverage and validation

The eight new cases cover:

- One source contract proving the Generated conversion branch enqueues into the existing canonical batch after conversion/cleanup, with exact source resolution and unchanged annotation/physical/GROUP/commit ordering. It failed before the production fix on the missing batch assignment.
- Four canonical annotation modes/styles: dimension text and cutting length, required component roles, slope arrow/angle planning, expected set size, clone-handle matching, source protection, repeated matching without duplicates and no orphan main components.
- Two Hip/Valley cases: original Generated source plus Generated → Manual and subsequent Manual → Manual clones retain one main annotation family per Plan handle after duplicate cleanup; repeating cleanup is idempotent. DimensionsLeader plans three entities per Manual source, hence six for two clones.
- Explicit NoAnnotations remains respected.

These behavioral tests exercise production Core annotation planning, formatting, matching and duplicate/orphan cleanup. The adapter source contract guards the missing enqueue. They do **not** execute AutoCAD annotation materialization or count live MLeader/arrow/angle entities in a DWG; that requires the HOST procedure below.

| Check | Result |
| --- | --- |
| Working tree before | Dirty, authorized existing WIP; preserved |
| Focused tests | PASS: 236 passed, 0 failed, 0 skipped; exit 0 |
| Portable restore/build | PASS; 0 warnings, 0 errors |
| Portable Core tests | PASS: 7,193 passed, 0 failed, 0 skipped |
| CAD-neutral dependency gate | PASS |
| Version/manifest consistency | PASS: 0.23.0 |
| Full solution restore | PASS |
| Debug x64 solution build | PASS; 0 warnings, 0 errors |
| Full Core tests | PASS: 7,193 passed, 0 failed, 0 skipped |
| WPF tests | PASS: 806 passed, 0 failed, 0 skipped |
| Release x64 solution build | PASS; 0 warnings, 0 errors |
| Portable gate | PASS; exit 0 |
| Full gate | PASS; exit 0 |
| Localization/resource changes | Not applicable; none touched |
| git diff --check | PASS; exit 0 |
| AutoCAD during host-dependent builds | Process count 0 before Full and Release |
| AutoCAD annotation/geometry/GROUP HOST result | NOT CHECKED |

Commands executed for final validation:

```powershell
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore -warnaserror -m:1 -nr:false --filter 'FullyQualifiedName~RoofStructuralGeneratedMirrorAnnotationTests|FullyQualifiedName~RoofStructuralManualMirrorCloneTests|FullyQualifiedName~RoofStructuralManualMirrorEditTests|FullyQualifiedName~RoofStructuralMirrorCloneTests|FullyQualifiedName~RoofStructuralAttachedManual|FullyQualifiedName~RoofStructuralFoundation|FullyQualifiedName~RoofStructuralRafterSolidSourceContract|FullyQualifiedName~RoofAutomaticStructuralHipValleyAnnotationTests|FullyQualifiedName~RoofMirrorAnnotationConsume|FullyQualifiedName~TimberElementLabelMatchRulesTests|FullyQualifiedName~TimberElementLabelCleanupRulesTests|FullyQualifiedName~TimberCompositeAnnotationLifecycleRulesTests|FullyQualifiedName~TimberAnnotationRefreshPlannerTests'
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Portable
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Full
dotnet build AcKrovy.sln --no-restore -c Release -p:Platform=x64 -warnaserror -m:1 -nr:false
git diff --check
git status --short
git diff --stat
git branch --show-current
git rev-parse HEAD
```

Full gate runs the original Portable checks, solution restore, warnings-as-errors solution Debug build and solution tests. Output confirms AutoCAD/WPF `bin/x64/Debug` artifacts. Gates/build ran outside the restricted sandbox using approved execution, without weakening tests or gate settings. An intermediate new-test xUnit2031 analyzer error was corrected by using the predicate overload of Assert.Single; the final focused invocation exited 0.

## One HOST retest: Generated Hip → MIRROR / Erase No

Environment to record: actual AutoCAD version, loaded KROVY Debug 0.23.0 DLL/path/time from `AK_RUNTIME_BUILD`, branch/HEAD above, and exact repro DWG. Use a saved copy of the user's repro drawing; confirm the selected member is currently Generated, rather than reusing a previous Manual clone.

Preconditions:

- One Generated Structural Hip N on an Unlocked roof with Physical3D enabled and a working complete annotation set. Use DimensionsLeader or FullLabel with slope arrow/angle enabled so the expected canonical set is three entities, including the dimension/kóta. Record actual timber dimensions, length, item metadata and annotation settings.
- Record roof owner, Generated LogicalKey, original Plan handle, endpoints `(Xs,Ys,0)/(Xe,Ye,0)`, physical key/body placement, annotation SourceHandles/roles/geometry and canonical GROUP baseline. Do not assume historical example handles still apply.
- Record DBMOD and baseline Generated/Manual/physical/annotation counts. Enable `AK_ROOF_3D_TRACE` only as needed until `enabled=True` and run `AK_ROOF_3D_AUDIT`.
- Select the complete individual N member normally with its usual annotation/physical children. Confirm the roof source Polyline itself is not selected.

Steps:

1. Set World UCS. Choose `Xc=max(Xs,Xe)+3000`; disable object snaps or use `_non` for axis points.
2. Execute **exactly one native MIRROR**, using `(Xc,min(Ys,Ye)-5000,0)` and `(Xc,max(Ys,Ye)+5000,0)`. Answer **No** to `Erase source objects?`.
3. Wait for maintenance, then run `AK_ROOF_3D_AUDIT`. Capture the complete native events, maintenance log and the new clone Plan handle/ManualIdentity.
4. Inspect source and clone final metadata, physical positions, annotation roles/SourceHandles/entity counts, readability and GROUP. No second MIRROR or other edit is part of this retest.

Expected final state:

- Original Generated Plan, LogicalKey, metadata, geometry, physical placement and source annotations remain unchanged. Original annotation SourceHandles still point to the original Plan. Physical reconcile may replace solid entity handles; compare semantic key and geometry.
- One new ManualStructural clone with new ManualIdentity, no Generated XData, correct reflected Placement and one Physical3D body at the reflected position. Plan endpoints are `(2*Xc-Xs,Ys,0)/(2*Xc-Xe,Ye,0)` and both Plan Z values are exactly 0.
- DEBUG includes `ROOF_STRUCT_GENERATED_MIRROR_CLONE ... annotations=canonical result=prepared`, the existing reflected-placement diagnostic and subsequent successful committed claim. Verify live after-maintenance metadata/body geometry; prepared alone is insufficient.
- Exactly **one complete canonical clone annotation set**, including its dimension/kóta with actual section dimensions and cutting length. Every component's SourceHandle is the new ManualStructural **Plan** handle. For the stated three-entity mode the final count increases by three. Other supported modes may require a different count: plain DimensionsWithItemNumber uses four, framed combined mode three. Count by configured roles, not a universal constant.
- No inherited native annotation copy remains bound to the original handle in the clone area; no duplicate canonical role or orphan arrow/angle/label remains. Source annotation content/geometry is preserved, including when source and clone share the same manufacturing item number.
- Generated count unchanged, Manual count +1, exactly one Manual physical key/body for the clone, and GROUP canonical with expected=actual, missing=0 and no surplus/foreign members.

Capture: AutoCAD/build/DWG, DBMOD before/after, native appended/modified/erased sequence, final source/clone metadata and frame/body coordinates, all annotation entity handles/SourceHandles/roles/counts, readable dimension screenshot and GROUP audit.

HOST DBMOD, entity lifecycle, live annotations, physical geometry and diagnostics are **NOT CHECKED** here. SAVE/REOPEN and UNDO/REDO are outside this single-operation retest. Later Manual → Manual clone behavior is the user's already accepted baseline and automated regression coverage, not a newly executed HOST run.

## Final git status --short

```text
 M src/AcKrovy.AutoCAD/Infrastructure/LiveGeometrySynchronizationService.cs
 M src/AcKrovy.AutoCAD/Infrastructure/RoofAssemblyGroupMemberCollector.cs
 M src/AcKrovy.AutoCAD/Infrastructure/RoofCommandWorkflow.cs
 M src/AcKrovy.AutoCAD/Infrastructure/RoofDisplayErasePreCommandMapService.cs
 M src/AcKrovy.AutoCAD/Infrastructure/RoofMirrorCloneDetachService.cs
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
?? docs/HIP_VALLEY_GENERATED_MIRROR_ANNOTATION_FIX_2026-10-02.md
?? docs/HIP_VALLEY_NATIVE_LIFECYCLE_AUDIT_2026-10-01.md
?? docs/HIP_VALLEY_STRUCTURAL_FULL_NATIVE_EDITING_2026-10-01.md
?? docs/HIP_VALLEY_STRUCTURAL_MANUAL_MIRROR_CLONE_FIX_2026-10-02.md
?? docs/HIP_VALLEY_STRUCTURAL_MANUAL_MIRROR_FIX_2026-10-02.md
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
?? src/AcKrovy.Core.Tests/RoofStructuralGeneratedMirrorAnnotationTests.cs
?? src/AcKrovy.Core.Tests/RoofStructuralManualMirrorCloneTests.cs
?? src/AcKrovy.Core.Tests/RoofStructuralManualMirrorEditTests.cs
?? src/AcKrovy.Core.Tests/RoofStructuralMirrorCloneTests.cs
?? src/AcKrovy.Core.Tests/RoofStructuralPlanOverridePhysicalPlacementTests.cs
?? src/AcKrovy.Core.Tests/SimpleGableOrdinaryPhysical3DTests.cs
?? src/AcKrovy.Core/Models/Roofs/RoofStructuralAttachedManualData.cs
?? src/AcKrovy.Core/Services/Roofs/RoofStructuralAttachedManualDataRules.cs
?? src/AcKrovy.Core/Services/Roofs/RoofStructuralManualPlacementRules.cs
?? src/AcKrovy.Core/Services/Roofs/SimpleGableOrdinaryRafterPhysicalAdapter.cs
?? src/AcKrovy.Core/Services/Roofs/SimpleGableRoofTopologyAdapter.cs
```

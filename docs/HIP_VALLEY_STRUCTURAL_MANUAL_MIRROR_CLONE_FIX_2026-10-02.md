# Existing ManualStructural MIRROR No — clone identity and placement fix

Date: 2026-10-02. Branch: `main`. HEAD: `3e0e1337978504b0447bab4c2968ca4691447b12` (unchanged).

Automated verdict: **PASS**. AutoCAD HOST acceptance: **PENDING / INCONCLUSIVE**.
Current Structural Foundation WIP and both previous MIRROR fixes were preserved. No commit, push, tag, release, reset, revert, checkout, stash or WIP discard was performed.

## Proven routing failure

The user supplied native MIRROR No evidence for source `ManualStructural:444903c3d3884978a6b19bf97039cd50`, appended Plan Line `2A40` and native solid clone `2A43`. The solid initially shares the source key with `2A3E`. The mirrored copy disappears during maintenance. This task relies on that supplied HOST evidence and current source; it did not execute a new AutoCAD HOST run or independently capture its complete event sequence.

The first divergence is in `RoofStructuralNativeEditService.Process` (`src/AcKrovy.AutoCAD/Infrastructure/RoofStructuralNativeEditService.cs`):

1. The candidate collector recognizes the appended Line through `RoofStructuralAttachedManualStore.Read`. `RoofStructuralEditRules.Classify` correctly returns `AcceptManualClone` for unlocked Plan MIRROR.
2. The previous guard applied to `(before is not null || candidate.Manual)`. An appended Manual clone has `before == null`, but `candidate.Manual == true`, so the guard forces `RejectClone`.
3. The appended Manual branch therefore skips `TryEnsureManualCloneIdentity`, adds the Line to `eraseCloneIds`, and erases it before Physical3D reconciliation. The native inherited UUID never becomes an authoritative new identity. This is a routing rejection, not an identity write performed too late.
4. `RoofStructuralRafterSolidMaterializationService.TryReconcileInTransaction` builds bodies from the surviving Plan members, erases the old/native structural solids, and generates the authoritative set. With the new Plan erased, only the original Manual semantic member survives.
5. The existing `TryEnsureManualCloneIdentity` also created identity-only metadata without Placement. Merely removing the rejection would allow the physical builder's no-placement branch to reconstruct from Generated provenance. A Manual member can already be moved/mirrored away from that source fold, so this would not guarantee reflection of its current stored frame.

Native duplicate Physical3D keys before maintenance are temporary copied metadata and do not alone prove the final failure. The acceptance condition is two unique keys **after maintenance**. Existing `VerifyCommitted` checks the physical key set against the authoritative Plan key set.

Mapping inspection: `RoofNativeCloneSnapshot.GetMemberSourcesByClone` derives its member map from ordinary Generated/AttachedManual entries; StructuralManual is not in that member table. This fix does not expand that snapshot architecture. The inherited Manual UUID plus owner and genuine pre-command Plan snapshot identify the exact surviving source, rather than another member sharing `SourceLogicalKey`.

## Minimal production changes

- The MIRROR source guard now applies only when `before is not null`. Existing Manual sources keep `AcceptPlan`, existing Generated sources keep their restore policy, and appended Manual/Generated clones reach their established allocation branches. Locked commands remain rejected by the original classifier.
- `TryEnsureManualCloneIdentity` accepts the existing assembly snapshot. For Mirror only, it requires exactly one surviving pre-command Manual source with matching inherited UUID, owner, full metadata and unchanged source geometry. Missing/ambiguous/changed sources fail the transaction; there is no proximity or Generated-fold fallback.
- A small CAD-neutral `RoofStructuralAttachedManualDataRules.CreateMirroredClone` composes the existing `TryReflectFrame`, `RoofStructuralAttachedManualIdentityRules.Create` and `Create` validation. It preserves owner/provenance/section/height settings, sets CreationKind Mirror, mints a new UUID and includes explicit reflected Placement. This is the shared allocator, not a second identity system.
- Normalize final clone Plan endpoints to Z=0, derive reflection from the source snapshot and final native clone geometry, and persist new identity + frame via existing `WriteReplacingGenerated` **before** `TryReconcileInTransaction`.
- An unchanged MIRROR No source is claimed without rewriting its Manual metadata or annotations. MIRROR Yes's changed-source frame update is retained.
- `LiveGeometrySynchronizationService` forwards its existing command-scoped appended annotation IDs to the structural router. The router uses a thin internal entry point into the existing `RoofMirrorCloneDetachService.DeleteMirroredCloneAnnotations` policy, then calls `TimberCreatedElementAnnotationService.EnsureForCreatedElements` only for the new Manual MIRROR clone batch with `copySourcePreservation: true`.

The builder, stored-frame early return, reflection math, Manual store/schema, command classifier, Generated conversion, COPY/MOVE/ERASE branches and ordinary annotation algorithm remain unchanged. Hash comparisons confirm the physical builder and reflection helper are byte-for-byte unchanged from task start. The COPY branch of the remint helper preserves its prior identity-only allocation.

## Annotation investigation

Annotation orphaning is not the cause of the missing Plan/solid. The clone's rejection is sufficient to explain disappearance.

The observed `timber-owner-unresolved` comes from `RoofLiveResizeService.TryResolveGeneratedAssemblyOwner`: its annotation-source lookup checks ordinary AttachedManual/Generated and StructuralGenerated, but does not read StructuralManual. This diagnostic coverage gap exists independently of UUID reminting; the warning alone does not prove that a label's source handle is missing.

Separately, native mirrored annotations retain the **original Plan SourceHandle**, and the appended Manual router branch previously continued without adding the clone to its annotation creation batch. A new UUID alone would not rebind these annotation handles. The scoped fix therefore removes redundant native annotation clones and generates the new member's canonical handle-bound set through existing services. The existing consume policy preserves non-appended originals and a sole recreated source survivor; it never uses geometry proximity. `copySourcePreservation` disables ElementId fallback so a clone cannot steal a source annotation with the same item number. No general owner resolver or annotation subsystem was rewritten.

Live annotation survival, ownership and readability remain HOST checks. Do not treat either a generic owner-resolution warning or a canonical GROUP result as complete annotation/geometry proof.

## Regression tests

New `RoofStructuralManualMirrorCloneTests`: **13 cases** (12 behavioral, one adapter source contract).

- Eight cases: Hip/Valley × source CreationKind Copy/Mirror × Automatic/Explicit height. The production clone helper yields A != B, preserves source JSON and provenance/section/settings, writes schema-2 explicit reflected Placement and Mirror creation kind, and survives JSON roundtrip. Rebuilt prism vertices match independently computed reflection; the body differs from the original, and expected Physical3D keys contain exactly A and B.
- Repeated clone allocation: 16 distinct UUIDs from an unchanged source, same reflected frame for the same mirror geometry.
- Missing stored frame, changed segment length and nonplanar Plan input fail without yielding a clone.
- Source contract guards appended routing, exact pre-command source lookup, persisted allocation before physical reconcile, annotation lifecycle IDs, scoped canonical clone annotations and diagnostics.

Numeric fixture combines the supplied new clone endpoints with a representative source/frame from the preceding reproduction. Its independently calculated reflection is `X'=66216.5678895746-X`. It is a portable fixture, not proof that the current HOST drawing's source has those baseline coordinates.

The first routing regression failed before the production change (1/1, old guard found). After the fix, the focused suite passes. Previous `RoofStructuralManualMirrorEditTests` still verifies MIRROR Yes A→A and reflected Placement; `RoofStructuralMirrorCloneTests` retains Generated MIRROR behavior. Their source-guard expectations now explicitly distinguish appended Manual clones from existing sources. No geometry/identity assertion was weakened. Existing annotation consume tests and all Core tests remain green.

## Validation

| Check | Result |
| --- | --- |
| Branch / HEAD | main / 3e0e1337978504b0447bab4c2968ca4691447b12 — unchanged |
| Working tree before | Dirty WIP plus previous fixes — preserved |
| Focused tests | PASS — 167/167, failed 0, skipped 0 |
| Core total | PASS — 7185/7185, failed 0, skipped 0 |
| WPF total | PASS — 806/806, failed 0, skipped 0 |
| Debug x64 build | PASS — solution build in Full gate, warnings-as-errors |
| Release x64 build | PASS — solution build, warnings-as-errors |
| Build warnings / errors | 0 / 0 in both configurations and portable builds |
| Portable Compatibility Gate | PASS — restore/build/tests and architecture checks |
| Full Compatibility Gate | PASS — portable checks, solution restore/Debug build/Core + WPF tests |
| CAD API leakage | PASS — original gate; new Core helper uses CAD-neutral values only |
| Localization/resources | NOT APPLICABLE — no changes; existing checks passed |
| git diff --check | PASS — exit 0 |
| New untracked task-file whitespace check | PASS |
| New HOST behavior | NOT RUN — manual acceptance pending |

AutoCAD was not running before Debug/Release adapter builds. Gates/build ran outside the sandbox because prior verified sandbox limitations blocked the unchanged atomic File.Replace test and parallel restore. No gate/test was weakened. Git prints pre-existing LF/CRLF conversion notices; these are not compiler warnings or whitespace failures.

Commands executed from the repository root:

```powershell
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore -warnaserror -m:1 -nr:false --filter FullyQualifiedName~RoofStructuralManualMirrorCloneTests
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore -warnaserror -m:1 -nr:false --filter "FullyQualifiedName~RoofStructuralManualMirrorCloneTests|FullyQualifiedName~RoofStructuralManualMirrorEditTests|FullyQualifiedName~RoofStructuralMirrorCloneTests|FullyQualifiedName~RoofStructuralAttachedManual|FullyQualifiedName~RoofStructuralFoundation|FullyQualifiedName~RoofStructuralPlanOverridePhysicalPlacement|FullyQualifiedName~RoofStructuralRafterSolidSourceContract|FullyQualifiedName~RoofMirrorAnnotationConsume"
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Portable
Get-Process -Name acad -ErrorAction SilentlyContinue
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Full
dotnet build AcKrovy.sln --no-restore -c Release -p:Platform=x64 -warnaserror -m:1 -nr:false
git diff --check
git status --short
git branch --show-current
git rev-parse HEAD
```

Task-start backups/status and validation logs: `C:\Users\Roman\AppData\Local\Temp\krovy-manual-mirror-clone-505a69c5b3ad4a2b98008ba1ff451de4`. File comparisons isolate this task's delta from the full existing WIP.

## Changed files for this task

- `src/AcKrovy.AutoCAD/Infrastructure/RoofStructuralNativeEditService.cs` — clone routing, source validation, persisted remint/placement, scoped clone annotation batch and source preservation.
- `src/AcKrovy.AutoCAD/Infrastructure/LiveGeometrySynchronizationService.cs` — forward existing appended annotation IDs.
- `src/AcKrovy.AutoCAD/Infrastructure/RoofMirrorCloneDetachService.cs` — thin internal entry point to unchanged consume policy.
- `src/AcKrovy.Core/Services/Roofs/RoofStructuralAttachedManualDataRules.cs` — compose validated reflected clone creation through existing allocator/helper (pre-existing untracked WIP).
- `src/AcKrovy.Core.Tests/RoofStructuralManualMirrorEditTests.cs` — guard expectation refinement, existing MIRROR Yes tests retained.
- `src/AcKrovy.Core.Tests/RoofStructuralMirrorCloneTests.cs` — guard expectation refinement, existing Generated clone tests retained.
- `src/AcKrovy.Core.Tests/RoofStructuralManualMirrorCloneTests.cs` — new regression suite.
- This new report.

## HOST Test Plan — ONE complete-N MIRROR / Erase No

Environment: target AutoCAD 2027; KROVY 0.23.0 Debug x64; branch/HEAD above. Record actual AutoCAD version, loaded assembly path/timestamp from `AK_RUNTIME_BUILD`, and DWG. Use the saved reproduction drawing (normally `C:\Users\Roman\Documents\3d.dwg`) only if it contains the intended fixture. Existing startup/autoload remains authoritative.

Preconditions:

- Start with **one existing ManualStructural Hip** N with Physical3D enabled on an Unlocked roof. Confirm its actual owner, source Plan handle and identity (reported example: Plan `2A27`, A=`444903c3d3884978a6b19bf97039cd50`; verify these are still current).
- Record source Plan endpoints `(Xs,Ys,0)/(Xe,Ye,0)`, full Placement, provenance, width/height/creation settings, physical key/body coordinates/handle, all source annotation SourceHandles/roles/geometry and item metadata. Both Plan Z values must be exactly 0.
- Run `AK_RUNTIME_BUILD`; confirm the new Debug DLL. Enable `AK_ROOF_3D_TRACE` only as needed until `enabled=True` (toggle). Run `AK_ROOF_3D_AUDIT`; record baseline DBMOD, Manual/Generated/physical counts and canonical GROUP.
- Select the **complete N rafter normally**, including its usual owned annotation/physical members. This is not the previous Plan-only test. Confirm the roof source Polyline itself is not selected; this must be an individual-member clone, not a whole-roof MIRROR.

Steps:

1. Set UCS to World. Define a clearly displaced vertical mirror axis `Xc=max(Xs,Xe)+3000`. Disable object snaps or use `_non` for mirror points.
2. Run exactly one native `MIRROR` on that complete N selection, with WCS points `(Xc,min(Ys,Ye)-5000,0)` and `(Xc,max(Ys,Ye)+5000,0)`. Answer **No** to `Erase source objects?`.
3. Wait for maintenance. Run `AK_ROOF_3D_AUDIT`; capture the full before/native-appended/native-ended/after-maintenance transcript and metadata. Read the new clone handle from native events; do not hardcode the previous `2A40`/`2A43` handles.
4. Inspect both N members, frame/body coordinates and annotations. No additional MIRROR/COPY/MOVE/ERASE/UNDO/REDO is part of this retest.

Expected results:

- Original Plan/entity/identity A, metadata/provenance/section/settings/Placement and annotations stay unchanged. Its Physical3D geometry and semantic key stay unchanged. The established structural reconcile replaces solid bodies, so their handles may change; native Solid3d clones are collateral, not authoritative retained bodies.
- Clone Plan survives with B != A, same owner/provenance/width/height settings, CreationKind Mirror, no Generated XData and explicit reflected Placement. Plan endpoints equal `(2*Xc-Xs,Ys,0)` and `(2*Xc-Xe,Ye,0)`, with Z exactly 0.
- Each stored clone frame axis point is `(2*Xc-X,Y,Z)` relative to the source frame; vector X components reflect sign, Y/Z and section height remain unchanged. Rebuilt Physical3D occupies that reflected position, not the old/source Placement.
- Exactly two Manual members and one solid for each key `ManualStructural:A` and `ManualStructural:B`. No duplicate/surplus Manual physical key remains after maintenance. Native pre-maintenance duplicate diagnostics must be distinguished from final-state duplicates.
- DEBUG emits `ROOF_STRUCT_MANUAL_MIRROR_CLONE ... sourceIdentity=ManualStructural:A cloneIdentity=B placementMode=RigidMirror ... result=prepared`, then a committed claim for B. No failure/recovery diagnostic. Prepared alone is not commit proof; verify after-maintenance metadata/body geometry.
- Clone annotations bind to the clone's **Plan handle**, with its current timber item metadata, readable canonical orientation and no orphan/native duplicate helpers. Source annotations remain bound to the source and preserve geometry/content. Capture any `timber-owner-unresolved` alongside the live SourceHandle/owner metadata; a warning alone is insufficient to judge orphaning.
- GROUP finishes canonical with expected=actual, missing=0 and no duplicate/surplus/foreign members. Generated members and roof source stay unchanged. The final `AK_ROOF_3D_AUDIT` shows both ManualStructural physical identities.

Data to capture: AutoCAD/build/DWG, DBMOD before/after, native appended/modified/erased events and source/clone identities, final Plan/frame JSON, all physical keys/body measurements, annotation SourceHandles/roles/ownership/readability and GROUP audit/screenshots.

HOST results: DBMOD, entity lifecycle, live metadata persistence, physical geometry, annotations and diagnostics **NOT CHECKED** by this task. SAVE/REOPEN and UNDO/REDO are outside this single-operation retest. Actual HOST acceptance remains pending; automated/source-contract PASS is not HOST PASS.

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

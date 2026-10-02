# Structural LOCK lifecycle closure

Date: 2026-10-02. Branch: `main`. HEAD: `3e0e1337978504b0447bab4c2968ca4691447b12`.

Automated verdict: **PASS**. AutoCAD HOST acceptance: **PENDING / NOT CHECKED**.
Production change was required. Existing uncommitted WIP was preserved. No reset/revert/stash/checkout, commit/push/tag, schema/version bump or release was performed.

## Product contract and exact root cause

A Locked roof freezes its intelligent roof-owned timber, including ManualStructural Hip/Valley. Native member edits must restore its accepted Plan and stored physical semantics. Ordinary non-roof-owned manual CAD timber is outside this scope. The existing whole-roof rigid translation exception remains authoritative.

Two holes were found in the current source:

1. Locked Structural Plan `STRETCH / GRIP_STRETCH / TRIM / EXTEND` intentionally returns `Unclaimed` from `RoofStructuralEditRules.Classify`. The existing locked recovery owns the restore. `TryRecoverGeneratedMembersOnly` restored snapshotted timber Lines and annotations, erased Generated duplicates and rebuilt display/GROUP, but never invoked `RoofStructuralRafterSolidMaterializationService.TryReconcileInTransaction`. Snapshot restoration includes Plan geometry, not Solid3d geometry. A Plan-only edit need not provide a modified physical ObjectId to the later physical-tamper handler, so that later handler cannot guarantee structural completion.
2. The command snapshot already captures ManualStructural Plans, but `RoofLiveResizeService.TryResolveGeneratedAssemblyOwner` and `ClassifyModifiedGeneratedChildren` did not recognize their structural attached-manual store. A Locked ManualStructural edit left unclaimed by the structural router could therefore miss the recovery route entirely. An annotation SourceHandle resolving to a ManualStructural Plan had the same owner-resolution omission.

This proves missing recovery/reconcile guarantees in the source call-path. It does not assert that a particular AutoCAD callback ordering or live DWG divergence has already been reproduced in HOST.

## Authoritative seam

The existing call-path remains:

`LiveGeometrySynchronizationService.RefreshCandidates`
→ `RoofStructuralNativeEditService.Process` and committed claim filtering
→ `RoofLiveResizeService.Process / Inspect`
→ `RoofGeneratedMemberManualEditService.ProcessOwners / ProcessOwner`
→ existing `!supportedUnlocked` locked recovery branch
→ `RoofUnsupportedStretchRecoveryService.TryRecoverGeneratedMembersOnly`
→ caller transaction commit only for successful recovery.

Within `TryRecoverGeneratedMembersOnly`, completion now follows:

1. Before writes, determine that the owner is Locked, the roof source itself is unchanged, and an unclaimed exact-owner Hip/Valley Plan from the snapshot changed or was erased. Compare endpoints/erasure, never proximity; skip already committed structural claims.
2. Existing snapshot Plan restore, then existing unsnapshot structural-fragment cleanup. In this Locked-only branch the same cleanup also consumes ManualStructural fragments, so duplicate identities cannot block the physical builder. No accepted Unlocked Manual clone is included.
3. Invoke the existing structural reconcile from restored Plan and unchanged Generated edit state / stored ManualStructural Placement. No new physical generator or restore owner was added.
4. Restore exact snapshotted annotations, run existing remaining duplicate cleanup, rebuild display and finalize GROUP through `RoofDisplayService.Rebuild`. Its group ensure verifies canonical multiset membership. A failed required rebuild returns HardFailure.
5. Return Recovered only when all required phases succeeded; existing callers dispose without commit on failure.

The new 12-line adapter `RoofStructuralLockedRecoveryCompletion` sequences the four existing service ports and short-circuits on failure. It allows behavior tests to call the actual production completion code without loading native Autodesk database objects. It contains no geometry, persistence, identity or annotation mechanism.

Erased objects are opened/unerased through the existing recovery helpers only when this Locked structural closure is required, including native TRIM deleting a whole reference or annotations selected with it. Defaults for all other recovery callers remain unchanged.

`RoofLiveResizeService` now resolves structural attached-manual ownership directly and through annotation SourceHandle, and includes those members in child classification **only for a Locked owner**. Non-roof manual timber and Unlocked structural manual paths are unchanged.

DEBUG adds `ROOF_STRUCT_LOCK_RECOVERY owner=... plan=restored physical=canonical result=prepared` (or failure reason). This is pre-commit; actual acceptance requires the final maintained DWG state.

## Preservation of accepted behavior

`Classify` was not changed from Unclaimed to RestorePlan. No competing Plan restore owner was introduced. New completion requires an unchanged roof source; the pre-existing rigid translation acceptance runs before generated-only recovery and was not edited.

SHA256 comparison with the task-start backups confirms these entire files are unchanged:

- `RoofStructuralNativeEditService.cs` — accepted MOVE/COPY/MIRROR/ERASE behavior and previous Generated MIRROR annotations.
- `RoofGeneratedMemberManualEditService.cs` — existing lock branch, Unlocked accept paths and whole-roof rigid translation exception.
- `RoofStructuralRafterSolidMaterializationService.cs` — canonical physical implementation and stored Placement authority.
- `RoofStructuralEditRules.cs` — full Locked/Unlocked classification matrix and suppression/Plan geometry rules.

Recovery completion writes no ManualStructural identity/Placement metadata or Generated edit/suppression state. Topology, physical definitions, numbering algorithms, annotation creation mechanisms and GROUP implementation were not changed.

Backups, initial git status and final gate/build logs:
`C:\Users\Roman\AppData\Local\Temp\krovy-lock-closure-b657a128f05d4812af9784632086f4bf`.

## Files changed by this task

| File | Change |
| --- | --- |
| `src/AcKrovy.AutoCAD/Infrastructure/RoofLiveResizeService.cs` | Locked-only ManualStructural owner/annotation resolution and child classification |
| `src/AcKrovy.AutoCAD/Infrastructure/RoofUnsupportedStretchRecoveryService.cs` | Existing recovery seam completes structural physical state before annotation/display/GROUP finalize; scoped unerase/fragment handling |
| `src/AcKrovy.AutoCAD/Infrastructure/RoofStructuralLockedRecoveryCompletion.cs` | New small production phase sequencer |
| `src/AcKrovy.Core.Tests/RoofStructuralLockedLifecycleTests.cs` | 17 cases |
| `src/AcKrovy.Wpf.Tests/RoofStructuralLockedRecoveryServiceTests.cs` | 20 behavioral service cases |
| `docs/STRUCTURAL_LOCK_LIFECYCLE_CLOSURE_2026-10-02.md` | This report and bounded HOST acceptance plan |

All other status entries below are existing WIP. Recovery file comparison against the task-start backup is 86 additions / 69 deletions, including replacement of the existing phase block; LiveResize adds only Locked ownership/classification support.

## Tests and limits of their evidence

Core adds 16 executable Locked/Unlocked classification cases and one adapter wiring contract. Before production editing, the corrected wiring regression failed on missing `TryCompleteStructuralRecovery`; 16 matrix cases passed. An initial source-marker test error was corrected before that defect-specific red run.

The 20 WPF-hosted service tests call the **actual production completion sequencer** with in-memory ports and real Core physical builders:

- Nine ManualStructural command cases: MOVE, COPY, MIRROR No/Yes, ERASE, STRETCH, GRIP_STRETCH, TRIM and EXTEND. They check restored Plan, no surviving appended clone, same identity/Placement, unchanged Generated state/no suppression, source annotations and canonical physical vertices/GROUP.
- Four Generated geometry-family cases retain pre-existing offset/edit state unchanged and rebuild the same canonical body.
- Four failure cases verify each failed phase prevents every later phase and successful completion.
- One unrelated/Unlocked completion case proves physical reconcile is skipped.
- One repeated completion case checks canonical annotation roles and one physical member in GROUP remain stable.
- One divergence regression runs Plan/annotation/group recovery with physical completion omitted: Plan is restored while damaged physical vertices remain. Enabling structural completion then restores canonical vertices.

Native command changes and database ports are simulated in the fixture. These tests verify phase behavior, Core geometry and metadata invariants; they do not execute `MIRROR`, `Erase(false)`, snapshot MLeader restoration, actual duplicate deletion or Solid3d/GROUP transactions in AutoCAD. Existing working native router paths are preserved and remain subject to HOST evidence. Only the wiring guard is a source-string test; service cases execute production code.

An intermediate test tried calling the neutral phase method on the large CAD-dependent recovery class, which required loading Acdbmgd in the test process. The method was moved into the separate neutral-signature adapter completion class. No Autodesk DLL copying, substitute CAD runtime, weakened test or project dependency change was used. Final service tests pass without native CAD objects.

## Validation

| Check | Result |
| --- | --- |
| Initial working tree | Dirty; current WIP preserved |
| Focused Core LOCK/related lifecycle tests | PASS: 271 passed, 0 failed, 0 skipped |
| Focused production recovery service tests | PASS: 20 passed, 0 failed, 0 skipped |
| Portable restore/build | PASS; 0 warnings / 0 errors |
| Full Core | PASS: 7,210 passed, 0 failed, 0 skipped |
| WPF | PASS: 826 passed, 0 failed, 0 skipped |
| Debug x64 solution build | PASS; 0 warnings / 0 errors |
| Release x64 solution build | PASS; 0 warnings / 0 errors |
| Portable Compatibility Gate | PASS; exit 0 |
| Full Compatibility Gate | PASS; exit 0 |
| CAD-neutral dependency rules | PASS |
| Version/manifest consistency | PASS: 0.23.0 |
| Localization/resources touched | None; not applicable |
| AutoCAD during host-dependent builds | Process count 0 before invocation |
| git diff --check | PASS; exit 0 |
| AutoCAD HOST acceptance | NOT CHECKED / PENDING |

Final commands (gate output saved in the backup folder):

```powershell
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore -warnaserror -m:1 -nr:false --filter 'FullyQualifiedName~RoofStructuralLockedLifecycleTests|FullyQualifiedName~RoofStructuralGeneratedLock|FullyQualifiedName~RoofStructuralFoundation|FullyQualifiedName~RoofStructuralManualMirror|FullyQualifiedName~RoofStructuralGeneratedMirrorAnnotation|FullyQualifiedName~RoofStructuralMirrorClone|FullyQualifiedName~RoofUnsupportedStretchRecovery|FullyQualifiedName~RoofGeneratedMemberLockedTamper|FullyQualifiedName~RoofPhysicalGripStretchRecovery|FullyQualifiedName~RoofLocked|FullyQualifiedName~RoofStructuralGeneratedBreakLeak|FullyQualifiedName~RoofStructuralRestoreNumberingOrder'
dotnet test src/AcKrovy.Wpf.Tests/AcKrovy.Wpf.Tests.csproj --no-restore -p:Platform=x64 -warnaserror -m:1 -nr:false --filter FullyQualifiedName~RoofStructuralLockedRecoveryServiceTests
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Portable
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Full
dotnet build AcKrovy.sln --no-restore -c Release -p:Platform=x64 -warnaserror -m:1 -nr:false
git diff --check
git status --short
git diff --stat
git branch --show-current
git rev-parse HEAD
```

Full gate executes the original Portable checks, solution restore, warnings-as-errors Debug build and all solution tests. Output confirms `bin/x64/Debug` AutoCAD/WPF artifacts. Approved execution outside the restricted sandbox was used for final gates/builds; original script and test requirements were retained. Git LF→CRLF notices on existing WIP are not compiler warnings.

## Minimal HOST acceptance: at most three native edits

Environment: actual AutoCAD version, loaded Debug KROVY 0.23.0 DLL/path/time from `AK_RUNTIME_BUILD`, branch/HEAD above, exact saved repro DWG copy. Do not infer loaded WIP build from version alone.

Preconditions:

- One Locked roof with Physical3D enabled, a Generated Hip and an already existing ManualStructural Hip with valid stored Placement. Use the current repro; do not generate another topology or create a new Manual clone as part of this acceptance.
- Run `AK_RUNTIME_BUILD`, enable `AK_ROOF_3D_TRACE` as needed until enabled=True, and run `AK_ROOF_3D_AUDIT` for the baseline.
- Record owner/EditState, Generated LogicalKey and complete Generated edit/suppression state, Manual Plan handle/ManualIdentity/Placement JSON, both Plan endpoints (Z=0), physical semantic keys and body coordinates, annotation SourceHandles/roles/content/counts, GROUP expected/actual and DBMOD.
- Use World UCS and disable object snaps for explicit displacements. Save PICKSTYLE, temporarily set it to 0, and select the stated member by its exact Plan handle. Keep the roof source Polyline outside all edit selections; otherwise this may test the existing whole-roof exception instead of the forbidden member edit.

Perform only these three native edits, capturing final audit/metadata after each:

1. **Locked Generated Hip COPY.** Select that complete individual member and its usual owned annotation/physical children, excluding the roof source. Make one copy displaced `(2000,0,0)` and finish COPY. After maintenance, all appended structural member/physical/annotation duplicates are consumed; Generated and Manual member counts stay at baseline, original key/state/Plan/body/annotations are unchanged, GROUP is canonical. No ManualStructural clone is accepted.
2. **Locked Generated Hip GRIP_STRETCH.** Select only its Plan Line, activate the upper endpoint grip and move it to `(Bx+500,By,0)` from baseline `(Bx,By,0)`. This intentionally avoids depending on a Solid3d modified event. After maintenance, both endpoints return exactly to baseline with Z=0, Generated edit/suppression state is byte-equivalent, no Suppressed tombstone is added, physical geometry returns to baseline, annotations are canonical and GROUP expected=actual/missing=0. Capture `ROOF_STRUCT_LOCK_RECOVERY ... physical=canonical result=prepared`, the later successful recovery outcome and the final maintained state; prepared alone is not acceptance.
3. **Locked ManualStructural MOVE.** Select only the existing Manual Plan Line and MOVE it by `(1000,500,0)`. After maintenance, the same Plan is restored, same ManualIdentity and complete Placement remain, physical vertices match stored Placement, Generated state is unchanged, annotations are canonical and GROUP is canonical. This exercises the preserved structural router path; the new unclaimed recovery diagnostic need not appear for this already claimed MOVE.

Restore PICKSTYLE after the checks. Capture DBMOD before/after each attempt, actual normalized command names and native/maintenance transcript, source/clone handles, final semantic keys/metadata/body measurements, annotation bindings/counts, GROUP audits and screenshots. Physical body handles may change through existing reconcile; compare semantic identity and geometry, not stable Solid3d handles.

No Valley/L/U/T duplication, additional MIRROR/ERASE/COPY edits, SAVE/REOPEN or UNDO/REDO is requested by this acceptance plan. Topology-specific behavior was not changed. Whole-roof rigid translation is preserved by unchanged code and the completion gate; no new HOST pass for that exception is claimed.

## Remaining risks

Native callback selection/order, actual erased object recovery, MLeader restoration, physical replacement and final GROUP behavior still require the three-step HOST acceptance. Automated PASS does not establish HOST PASS. Failure in the required completion phase intentionally blocks recovery transaction commit rather than reporting a partial recovery as successful.

## Final git status --short

```text
 M src/AcKrovy.AutoCAD/Infrastructure/LiveGeometrySynchronizationService.cs
 M src/AcKrovy.AutoCAD/Infrastructure/RoofAssemblyGroupMemberCollector.cs
 M src/AcKrovy.AutoCAD/Infrastructure/RoofCommandWorkflow.cs
 M src/AcKrovy.AutoCAD/Infrastructure/RoofDisplayErasePreCommandMapService.cs
 M src/AcKrovy.AutoCAD/Infrastructure/RoofLiveResizeService.cs
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
?? docs/STRUCTURAL_LOCK_LIFECYCLE_CLOSURE_2026-10-02.md
?? scripts/acad-host-workflow.ps1
?? src/AcKrovy.AutoCAD/Infrastructure/RoofStructuralAttachedManualStore.cs
?? src/AcKrovy.AutoCAD/Infrastructure/RoofStructuralLockedRecoveryCompletion.cs
?? src/AcKrovy.Core.Tests/RoofStructuralAttachedManualFoundationTests.cs
?? src/AcKrovy.Core.Tests/RoofStructuralAttachedManualXDataReplaceTests.cs
?? src/AcKrovy.Core.Tests/RoofStructuralGeneratedMirrorAnnotationTests.cs
?? src/AcKrovy.Core.Tests/RoofStructuralLockedLifecycleTests.cs
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
?? src/AcKrovy.Wpf.Tests/RoofStructuralLockedRecoveryServiceTests.cs
```

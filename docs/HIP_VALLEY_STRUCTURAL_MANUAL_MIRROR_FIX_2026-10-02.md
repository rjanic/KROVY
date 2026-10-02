# Existing ManualStructural MIRROR / Erase Yes — narrow persistence fix

Date: 2026-10-02. Branch: `main`. HEAD: `3e0e1337978504b0447bab4c2968ca4691447b12` (unchanged).

Automated verification: **PASS**. New AutoCAD HOST acceptance: **PENDING / INCONCLUSIVE**.
No commit, push, tag, release, reset, revert, checkout, stash, or WIP discard was performed.

## Evidence and proven root cause

The supplied HOST reproduction concerns owner `2912`, existing Plan Line `2A27`, ManualIdentity `f3a561b7af1a44dcb8bfd61c99c901a8`, provenance `Hip|1|4`, schema 2 with explicit Placement. The user reports native MIRROR / Erase Yes changes that Line's endpoints while its persisted frame remains unchanged. This is the supplied HOST evidence; no new HOST execution or independently captured event sequence was performed during this task.

Current source confirms the persistence gap in `RoofStructuralNativeEditService.Process` (`src/AcKrovy.AutoCAD/Infrastructure/RoofStructuralNativeEditService.cs`):

1. `RoofStructuralEditRules.Classify` classifies unlocked Plan MIRROR as `AcceptManualClone`, shared with the first Generated clone fix.
2. The first fix's subsequent source guard changed every existing source or Manual MIRROR candidate to `RejectClone`. In the existing Manual branch, that restores snapshot Plan endpoints and leaves Placement unchanged. Therefore the checked-out implementation does not prove that the native mirrored Plan survives maintenance. The supplied final native coordinates establish native reflection; the generic `semantic-reconciled result=committed` message also appears for restoration and cannot prove geometric acceptance.
3. The Manual `AcceptPlan` branch had ERASE and MOVE/Plan-edit handling, but no MIRROR frame update. Simply accepting the native Plan without updating metadata would retain the reported stale frame.
4. `RoofStructuralRafterSolidMaterializationService.TryResolveManualPlacement` returns an existing `manual.Placement` immediately. `TryReconcileInTransaction` then builds the Manual prism from that persisted frame. This authoritative consumption is correct: stale persisted coordinates produce a rebuilt solid at the old position. GROUP synchronization checks membership, not reflected geometric correctness.

Relevant unchanged functions: `RoofStructuralAttachedManualStore.Read/Write`, `RoofStructuralAttachedManualDataRules.WithPlacement`, `RoofStructuralManualPlacementRules.TryReflectFrame/TryBuildPrism`, and `RoofStructuralRafterSolidMaterializationService.TryResolveManualPlacement/TryReconcileInTransaction`.

## Minimal implementation

Only the native semantic router production file changes in this second task:

- In the existing MIRROR guard, an existing Manual candidate with a pre-command snapshot becomes `AcceptPlan`. Generated sources and appended Manual clones retain `RejectClone`; appended Generated clones retain the first fix's Manual conversion path. Locked classification remains rejected.
- In the Manual `AcceptPlan` branch, normalize final native Plan Z to exactly 0. If endpoints changed, reflect the **current persisted frame** using the pre-command Plan endpoints and the final native Plan endpoints through the existing `TryReflectFrame` helper.
- Persist `WithPlacement(manualData, reflected)` on the same Line before existing Physical3D reconciliation, display rebuild, GROUP sync and transaction commit. This preserves owner, ManualIdentity, source role/edge IDs/key, width, height mode, explicit height and CreationKind. It does not remint identity or depend on the original Generated source fold.
- An unchanged Manual source in MIRROR No does not reflect its frame. Missing/invalid explicit frames or invalid reflection fail the transaction rather than introducing a downstream guess.
- DEBUG emits `ROOF_STRUCT_MANUAL_MIRROR_PLACEMENT ... placementMode=InPlaceRigidMirror result=updated` after writing Placement. This pre-commit diagnostic must be accompanied by the committed claim and after-maintenance metadata/geometry evidence.

The physical builder, reflection helper, command classifier and previous COPY/MOVE/ERASE branches were not edited. Hash comparison against task-start backups confirms the first three files are byte-for-byte unchanged. The change handles the supplied existing/same-handle edit; it introduces no replacement-object identity transfer or separate persistence subsystem.

For the supplied coordinates the WCS mirror plane is `X=32334.474595725885`, with transform `(X,Y,Z) -> (64668.94919145177-X,Y,Z)`. Expected frame axis X values become approximately `30633.8288737` and `33720.3887943`; Y/Z stay unchanged, Side X changes sign, and Up stays vertical.

## Tests added / changed

`RoofStructuralManualMirrorEditTests.cs`: 11 cases.

- Two adapter source contracts check existing Manual routing, snapshot/final geometry use, Z=0, identity-preserving metadata write before physical rebuild/GROUP/commit, unchanged stored-frame builder consumption and retained clone/source branches. These verify wiring, not HOST execution.
- Eight numeric cases use the supplied endpoint/frame coordinates across Hip/Valley, Copy/Mirror creation kinds and Automatic/Explicit height modes. They check unchanged identity/provenance/section/settings, changed reflected Placement, independently computed axis/vector reflection, JSON roundtrip, and all eight rebuilt prism vertices against the reflected original. The new body differs from the old placement.
- One repeated reflection case transforms the current persisted frame, preserves identity and returns the rebuilt prism to its original coordinates after the inverse reflection.

The new boundary regression was run before the production fix and failed 1/1 with `Existing Manual MIRROR must update placement at the semantic edit boundary`; it passes after the fix. The first fix's `RoofStructuralMirrorCloneTests` routing assertion now explicitly requires existing Manual `AcceptPlan` while retaining `RejectClone` for other guarded sources. Its geometry/identity assertions were retained.

## Validation results

| Check | Result |
| --- | --- |
| Initial working tree | Dirty Structural Foundation WIP plus first MIRROR fix; preserved |
| Focused tests | PASS — 148/148; failed 0, skipped 0 |
| Core total | PASS — 7172/7172; failed 0, skipped 0 |
| WPF total | PASS — 806/806; failed 0, skipped 0 |
| Debug x64 solution build | PASS — Full gate; warnings-as-errors; 0 warnings / 0 errors |
| Release x64 solution build | PASS — warnings-as-errors; 0 warnings / 0 errors |
| Portable compatibility gate | PASS — restore/build/tests and architecture checks |
| Full compatibility gate | PASS — portable gate, solution restore/Debug build/Core + WPF tests |
| CAD-neutral dependency leakage | PASS — existing gate; no portable production changes |
| Localization/resources | NOT APPLICABLE — no changes; existing checks passed |
| git diff --check | PASS |
| New untracked task-file whitespace check | PASS |
| New HOST test | NOT RUN — manual acceptance pending |

AutoCAD process checks returned no running `acad.exe` before Debug/Release adapter builds. Gates ran outside the sandbox because prior verification proved sandbox failures in the unchanged atomic File.Replace test and parallel restore; no test or gate was weakened. Git may print existing LF/CRLF conversion notices; these are distinct from compiler warnings and whitespace errors.

Commands executed from the repository root:

```powershell
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore -warnaserror -m:1 -nr:false --filter FullyQualifiedName~RoofStructuralManualMirrorEditTests
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore -warnaserror -m:1 -nr:false --filter "FullyQualifiedName~RoofStructuralManualMirrorEditTests|FullyQualifiedName~RoofStructuralMirrorCloneTests|FullyQualifiedName~RoofStructuralAttachedManual|FullyQualifiedName~RoofStructuralFoundation|FullyQualifiedName~RoofStructuralPlanOverridePhysicalPlacement|FullyQualifiedName~RoofStructuralRafterSolidSourceContract"
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Portable
Get-Process -Name acad -ErrorAction SilentlyContinue
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Full
dotnet build AcKrovy.sln --no-restore -c Release -p:Platform=x64 -warnaserror -m:1 -nr:false
git diff --check
git status --short
git branch --show-current
git rev-parse HEAD
```

Backups, initial Git status and validation logs: `C:\Users\Roman\AppData\Local\Temp\krovy-manual-mirror-29976e538a624d699c8fe56a479a8495`. This preserves the starting versions of shared WIP files; comparisons isolate the second task's delta from the full uncommitted diff.

## Changed files for this second task

- `src/AcKrovy.AutoCAD/Infrastructure/RoofStructuralNativeEditService.cs` — guard plus Manual MIRROR placement write.
- `src/AcKrovy.Core.Tests/RoofStructuralMirrorCloneTests.cs` — refine the first fix's guard assertion; existing uncommitted file preserved.
- `src/AcKrovy.Core.Tests/RoofStructuralManualMirrorEditTests.cs` — new regression suite.
- `docs/HIP_VALLEY_STRUCTURAL_MANUAL_MIRROR_FIX_2026-10-02.md` — this new report.

The remaining files in Git status below are pre-existing WIP. The previous MIRROR report is unchanged and describes the earlier task's historical scope.

## HOST Test Plan — exactly ONE MIRROR / Erase Yes

Environment: target AutoCAD 2027, KROVY 0.23.0 Debug x64, branch/HEAD above. Record actual AutoCAD version, loaded DLL path/timestamp from `AK_RUNTIME_BUILD`, and DWG. Use the saved reproduction drawing (normally `C:\Users\Roman\Documents\3d.dwg`) only if it still contains the intended pre-MIRROR fixture. Existing startup/autoload remains authoritative.

Preconditions:

- Owner `2912` is Unlocked and Physical3D is enabled. Select an **existing ManualStructural Hip**, Plan Line `2A27`, identity `f3a561b7af1a44dcb8bfd61c99c901a8`, provenance `Hip|1|4`, schema 2 and explicit Placement. Capture all section/creation settings.
- For the exact numerical fixture, Plan starts `(33991.69391084029,10951.930768028607,0)` and ends `(30991.693910840288,13951.930768028607,0)`. Placement starts `(34035.12031771148,10908.504361157415,-49.567145124853056)` and ends `(30948.56039718791,13995.064281680987,1732.4590558593404)`.
- If the drawing is already mirrored, use its saved pre-operation fixture or record its actual current Plan/frame and compute expected coordinates with the same reflection formula; do not compare against stale baseline numbers.
- Enable DEBUG tracing: use `AK_ROOF_3D_TRACE` only as needed until output says `enabled=True` (it toggles). Run `AK_ROOF_3D_AUDIT`, capture baseline DBMOD, Manual/Generated/physical counts, existing manual solid handle/key/geometry and canonical GROUP.

Steps:

1. Set UCS to World; save PICKSTYLE and set it to 0 to select only the Plan Line. Disable snaps or use `_non` for mirror points.
2. Run exactly one native `MIRROR`, selecting **only Plan Line `2A27`**. Use WCS points `(32334.474595725885,0,0)` and `(32334.474595725885,20000,0)`. Answer **Yes** to `Erase source objects?`.
3. Wait for maintenance. Run `AK_ROOF_3D_AUDIT`. Capture the full MIRROR transcript, native before/events/native-ended/after-maintenance member evidence, metadata JSON, solid geometry and GROUP results. Restore PICKSTYLE.
4. Inspect Plan and physical geometry without another MIRROR/COPY/MOVE/ERASE. No undo/redo or additional mutation is included in this one-operation test.

Expected results:

- Same Plan handle and ManualIdentity; no new Manual identity and no Generated XData on that Line. Owner, provenance, width, height mode, explicit height and CreationKind remain unchanged. Manual and Generated member counts stay unchanged.
- Final Plan start `(30677.255280611484,10951.930768028607,0)` and end `(33677.25528061148,13951.930768028607,0)` for the exact fixture. Z is exactly 0 and geometry is not restored to the snapshot.
- Final persisted Placement axis X values approximately `30633.8288737` and `33720.3887943`, original Y/Z; Side becomes approximately `(0.70710678,-0.70710678,0)` and Up stays `(0,0,1)`. Compare full persisted frame, not only Plan coordinates or GROUP count.
- DEBUG includes `ROOF_STRUCT_MANUAL_MIRROR_PLACEMENT handle=2A27 manualId=f3a561b7af1a44dcb8bfd61c99c901a8 placementMode=InPlaceRigidMirror result=updated`, then a committed ManualStructural MIRROR claim. No `result=failed` or recovery failure. The after-maintenance member evidence must contain the new Placement; the early updated message alone is insufficient.
- Exactly one matching Manual StructuralRafterSolid exists with physical key `ManualStructural:f3a561b7af1a44dcb8bfd61c99c901a8`, rebuilt at the reflected physical coordinates. Its handle may change; its semantic identity must not. Old body must not remain, duplicate or reappear at the old location.
- GROUP finishes canonical with expected=actual, missing=0, no duplicate/surplus/foreign members. Unselected Generated members remain unchanged.

Capture DBMOD before/after, all entity counts/handles/identities, native modified/appended/erased events, endpoint/frame snapshots, command output and physical measurements/screenshots. If the actual HOST creates a replacement Plan handle or the expected fixture/build is not confirmed, report INCONCLUSIVE and preserve the event evidence; this task did not design speculative identity transfer.

HOST result: DBMOD, entity lifecycle, live metadata persistence, visual geometry and diagnostics **NOT CHECKED** in this task. SAVE/REOPEN and UNDO/REDO are outside this single-operation retest. The user reports earlier Generated→Manual MIRROR No and COPY/MOVE/ERASE/SimpleGable paths HOST PASS; this task retained their implementation and automated regressions but did not independently repeat those HOST tests.

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

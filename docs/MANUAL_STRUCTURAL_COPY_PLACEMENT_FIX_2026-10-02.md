# ManualStructural COPY Placement fix — 2026-10-02

## Baseline and scope

- Branch: `main`; HEAD and `origin/main`: `1ce75359c5449184f96a2d282156a010278d6ff4`.
- Published checkpoint: `Finalize structural member lifecycle persistence and locking`.
- Narrow existing ManualStructural → native COPY fix. Generated COPY conversion, MIRROR geometry, LOCK rules, ordinary AttachedManual and the physical materializer are unchanged.
- NO COMMIT. NO PUSH. NO TAG. No reset, revert, stash, checkout or clean.
- All 42 previously untracked files were checked against their initial SHA-256 hashes: 0 changed or removed. The new test and this report are additional files.

## Exact root cause and previous behavior

`RoofStructuralNativeEditService.TryEnsureManualCloneIdentity` previously handled an inherited ManualStructural COPY payload with `RoofStructuralAttachedManualDataRules.Create(...)` without its optional Placement argument. This minted a new UUID but downgraded the clone to identity-only metadata. The source's authoritative current frame was discarded.

Downstream, `RoofStructuralRafterSolidMaterializationService.TryResolveManualPlacement` prefers a stored Placement, but a missing Placement permits reconstruction from the Generated `SourceLogicalKey` fold and a Plan displacement. That reconstruction cannot guarantee the source Manual's persisted orientation/frame, particularly after MIRROR or a sequence of edits. Sharing provenance does not make the Generated fold authoritative for a Manual clone. An uncomplicated MOVE can sometimes produce the same coordinates through that fallback; that coincidence does not preserve the complete stored-frame contract.

The appended Manual COPY branch also did not explicitly queue the clone in the established canonical clone annotation batch. Its native annotation copies can retain the source's handle. The fix uses that existing batch and cleanup service rather than adding another annotation mechanism.

## Authoritative seam and new behavior

The existing command-end structural transaction owns the fix:

1. Resolve the exact pre-command Manual source using inherited UUID and roof owner, never provenance alone. Require exactly one surviving snapshot source, equal live metadata and unchanged source endpoints.
2. Normalize the clone's Plan2D Z to zero, retaining native XY endpoints.
3. Call `RoofStructuralAttachedManualDataRules.CreateCopiedClone`.
4. Validate finite Plan coordinates and use existing `TryMatchRigidPlanCopy` to validate both endpoint displacements and a nondegenerate planar segment.
5. Set `dx = clone.Start.X - source.Start.X`, `dy = clone.Start.Y - source.Start.Y`; persist `Translate(source.Placement, dx, dy, 0)` with a fresh UUID and `CreationKind.Copy` through `WriteReplacingGenerated`.
6. Consume only native appended annotations tied to the exact source handle through the existing clone cleanup. Add the clone to the existing `mirrorCloneAnnotations` canonical batch and claim its new PhysicalKey.
7. Run the existing canonical annotation service with `copySourcePreservation: true`, structural Physical3D reconcile, display rebuild and GROUP sync, then commit and finalize/verify.

The batch retains its existing name; COPY and MIRROR share the same annotation implementation. MIRROR still calls the original reflected-frame helper and retains its diagnostics. COPY source Plan/metadata are claimed without refreshing its existing annotation set.

Translation changes AxisStart and AxisEnd XY only. Axis Z, Side, Up, SectionHeight and width/height semantics stay exactly those of the current source, including an already reflected source. Owner, role, boundary edge IDs and SourceLogicalKey are preserved. Clone UUID and `ManualStructural:<UUID>` PhysicalKey are independent; repeated copies may share provenance.

No materializer change is needed: clone metadata now contains Placement before reconciliation, so its existing stored-frame branch supplies `TryBuildPrism`. The source's semantic frame/body geometry stays unchanged; existing reconciliation may recreate CAD solids as it already did. This does not promise stable Solid3d object IDs.

No new schema was introduced. The existing Placement schema version 2 is used, including JSON persistence.

## Fail-closed fallback

Missing Placement, missing/ambiguous/changed exact source, invalid frame or nonrigid/nonfinite Plan displacement cannot mint an accepted COPY clone. The safest fallback is to reject the appended clone, detach its GROUP membership and erase it in the existing structural transaction, preserving the source. Known-source native appended annotations are consumed with the same cleanup service. No fallback clone is built from Generated provenance.

COPY failure now continues to the existing queued-erasure/finalization phase instead of throwing before that erase. Previously, throwing at that point would roll back the queued cleanup and leave an inherited-UUID native clone available to later recovery. Unknown source handles do not authorize deleting unrelated annotations. A transaction/reconcile failure still rolls back and follows the existing failure diagnostic; native failure behavior requires HOST confirmation.

## Changed files

| File | Change |
| --- | --- |
| `src/AcKrovy.AutoCAD/Infrastructure/RoofStructuralNativeEditService.cs` | COPY exact-source resolution, persisted frame remint, canonical clone annotation batch, source annotation preservation and committed rejection of invalid COPY |
| `src/AcKrovy.Core/Services/Roofs/RoofStructuralAttachedManualDataRules.cs` | `CreateCopiedClone`, reusing existing rigid-copy validation and Translate primitive |
| `src/AcKrovy.Core.Tests/RoofStructuralManualCopyPlacementTests.cs` | 15 new regression cases |
| `docs/MANUAL_STRUCTURAL_COPY_PLACEMENT_FIX_2026-10-02.md` | This report |

## Tests and evidence limits

New suite: 15/15 PASS, comprising 14 behavioral Core cases and one adapter wiring contract:

- Four Hip/Valley × Automatic/Explicit COPY-after-MOVE cases: complete metadata preservation, fresh UUID/physical key, source unchanged, Plan Z=0, JSON round-trip, all eight physical prism vertices translated from the current frame and different from an original-fold-only body.
- Repeated COPY and COPY-of-COPY: 33 independent keys, shared provenance and unchanged source.
- COPY of reflected Manual: preserves reflected Side/Up and translates every physical vertex.
- Seven fail-closed cases: missing frame, changed endpoint, reversed segment, nonplanar Plan, invalid frame, nonfinite XY and nonfinite Z.
- Canonical annotation planning/matching: dimension content, slope roles, independent SourceHandles, unchanged source labels, repeated upsert and absence of duplicate/orphan main labels.
- Adapter wiring: exact source resolution, metadata write before physical reconcile, existing canonical annotation batch and COPY diagnostics.

Before adapter repair, the new suite reported 12 PASS / 1 FAIL: the wiring regression detected the absent production call to `CreateCopiedClone`. The subsequent complete Core suite and gates pass. The initial sandbox MSBuild attempt hung and was interrupted; final tests/builds ran outside the sandbox without weakening scripts or tests.

Existing focused regressions pass: Manual MIRROR clone 13, Manual MIRROR edit 11, Structural MIRROR clone 12, Generated MIRROR annotations 8, Locked Core 17, structural physical placement 36, Manual foundation/XData 16 and Generated-rafter COPY rules/contracts 25. Together with the 15 new cases: 153/153 PASS. Existing WPF Locked service recovery cases (20) pass as part of the full WPF suite; Locked COPY remains RejectClone. Full Core also runs the unchanged Generated Structural COPY foundation contracts.

These are Core builders, ownership/planning rules, service tests and adapter source contracts. No real DWG COPY event sequence, MLeader/dimension entity count, Solid3d geometry or HOST audit was executed. GROUP sync/verification stays in the existing finalization seam; portable GROUP/planning checks are not HOST proof.

## Validation

AutoCAD process checks found no running `acad` before adapter/solution builds. All final build/gate compilation has 0 warnings and 0 errors; all final tests have 0 failures and 0 skipped cases.

| Check | Final result |
| --- | --- |
| Focused COPY/MIRROR/LOCK/physical/annotation/Generated-copy suite | PASS, 153/153 |
| New COPY cases | PASS, 15/15 (included above) |
| Full Core | PASS, 7,225/7,225 in both compatibility gates |
| Full WPF | PASS, 826/826 standalone and Full Gate |
| Debug x64, warnings-as-errors | PASS, 0 warnings / 0 errors |
| Release x64, warnings-as-errors | PASS, 0 warnings / 0 errors |
| Portable Compatibility Gate | PASS: restore/build, 7,225 Core tests, CAD-neutral dependency checks |
| Full Compatibility Gate | PASS: restore/build, 7,225 Core + 826 WPF = 8,051 tests |
| Localization/resource checks | Existing gate/tests PASS; no localization files changed |
| Schema impact | Existing Placement v2 used; no schema/version change |
| `git diff --check` | PASS |
| HEAD/upstream | Unchanged; local refs equal, ahead 0 / behind 0 |
| Unrelated WIP hashes | PASS, 42/42 unchanged |
| HOST validation | NOT RUN |

Commands actually run (PowerShell; logs/TRX in `%TEMP%/krovy-manual-copy-20261002`):

```powershell
git status --short
git rev-parse HEAD origin/main
Get-Process -Name acad -ErrorAction SilentlyContinue
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore -warnaserror -m:1 --filter FullyQualifiedName~RoofStructuralManualCopyPlacementTests
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore -warnaserror -m:1 --logger 'trx;LogFileName=core.trx' --results-directory "$env:TEMP/krovy-manual-copy-20261002"
dotnet build AcKrovy.sln --no-restore -c Debug -p:Platform=x64 -warnaserror -m:1
dotnet build AcKrovy.sln --no-restore -c Release -p:Platform=x64 -warnaserror -m:1
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-build --filter 'FullyQualifiedName~RoofStructuralManualCopyPlacementTests|FullyQualifiedName~RoofStructuralManualMirror|FullyQualifiedName~RoofStructuralMirrorClone|FullyQualifiedName~RoofStructuralGeneratedMirrorAnnotation|FullyQualifiedName~RoofStructuralLocked|FullyQualifiedName~RoofStructuralPlanOverridePhysicalPlacement|FullyQualifiedName~RoofStructuralAttachedManual|FullyQualifiedName~RoofGeneratedRafterCopy' --logger 'trx;LogFileName=copy-focused-final.trx' --results-directory "$env:TEMP/krovy-manual-copy-20261002"
dotnet test src/AcKrovy.Wpf.Tests/AcKrovy.Wpf.Tests.csproj --no-build -p:Platform=x64 --logger 'trx;LogFileName=wpf.trx' --results-directory "$env:TEMP/krovy-manual-copy-20261002"
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Portable
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Full
git diff --check
git diff --stat
```

The standalone full Core run passed 7,224 cases before adding the final nonfinite-Z case; both subsequent full gates execute all 7,225 final cases. Test counts are not added across repeated runs. The TRX overwrite notice on the final focused rerun is a results-file notice, not a compiler warning.

## One minimal HOST acceptance procedure

Use one Hip ManualStructural member A under an Unlocked roof, with normal dimension annotations enabled. Record its identity, Placement and annotation handles. MOVE A by a visible known XY offset, for example `(5000, -2700)`. Record A's resulting Placement. COPY only A's Plan2D member by a different known XY offset, for example `(1300, 2200)`. Run `AK_ROOF_3D_AUDIT` and inspect the two Plan/Physical representations.

Expected: after COPY, A retains its pre-COPY identity, Placement, body geometry and annotation ownership. B has a fresh ManualIdentity, shared provenance and `CreationKind.Copy`; its persisted Placement is A's current Placement plus `(1300, 2200, 0)`, with unchanged Side/Up/height. B Physical3D follows B, and B's complete annotation set including dimensions owns B's Plan handle. No orphan or duplicate annotations. Final GROUP: `duplicates=0`, `missing=0`, `foreign=0`, `canonical=True`. Debug COPY diagnostic: `placementMode=RigidCopy annotations=canonical result=prepared`.

No HOST SAVE/REOPEN cycle or other roof topology is requested.

HOST validation: NOT RUN. Release verdict: NOT READY FOR HOST-AFFECTING CHANGE until this acceptance run passes.

## Current git status

The exact `git status --short` output is appended below. Only the four files listed above belong to this task; all other untracked entries predate it.

```text
 M src/AcKrovy.AutoCAD/Infrastructure/RoofStructuralNativeEditService.cs
 M src/AcKrovy.Core/Services/Roofs/RoofStructuralAttachedManualDataRules.cs
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
?? docs/MANUAL_STRUCTURAL_COPY_PLACEMENT_FIX_2026-10-02.md
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
?? src/AcKrovy.Core.Tests/RoofStructuralManualCopyPlacementTests.cs
?? src/AcKrovy.Core.Tests/SimpleGableOrdinaryPhysical3DTests.cs
?? src/AcKrovy.Core/Services/Roofs/SimpleGableOrdinaryRafterPhysicalAdapter.cs
?? src/AcKrovy.Core/Services/Roofs/SimpleGableRoofTopologyAdapter.cs
```

CODE-SIDE VERDICT: PASS ✅

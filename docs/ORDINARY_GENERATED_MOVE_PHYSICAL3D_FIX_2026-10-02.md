# Ordinary Generated MOVE → Physical3D fix — 2026-10-02

## Scope and baseline

Branch `main`; HEAD and `origin/main` remain `1ce75359c5449184f96a2d282156a010278d6ff4`. This is a narrow persisted pure-XY MOVE fix for Ordinary Generated rafters. No lifecycle framework, metadata schema or coordinate convention was changed.

ManualStructural COPY Placement WIP preserved exactly: both modified production files, its new test and its report retain their initial hashes. All 46 pre-existing modified/untracked files were captured with SHA-256 at task start; no initial file has changed or disappeared.

NO COMMIT. NO PUSH. NO TAG. NO RESET / REVERT / STASH / CLEAN. No native Solid3d MOVE is used.

## Exact root cause

The accepted Plan and persisted override reach physical reconciliation correctly. The loss occurs inside the existing-model rebuild:

```text
RoofGeneratedMemberManualEditService.TryAcceptUnlockedEdits
  → TryClassifyAcceptedMemberEdit / existing override composition
  → RoofDefinitionStore.Write(updated Overrides)
  → RoofOrdinaryRafterSolidMaterializationService.TryReconcileModifiedMembersInTransaction
  → TryReconcileSemanticMembersInTransaction
  → TryBuildExistingModelInTransaction
  → RoofGeneratedMemberReplayPlanner.Create
  → RoofGeneratedMemberDomainRules.OverlapsBoundedPlane
  → RoofAutomaticRafterPhysicalBuilder.TryBuild
  → CreateSolid / ordinary key metadata / erase old body
```

The generic replay planner also implements resize dormancy. A persisted edit entirely outside the regenerated footprint is marked `DormantInvalidDomain`; its `Geometry` is replaced with the canonical Generated geometry. Physical rebuild used that planner without checking the already accepted live Plan. Thus reconciliation really removed/rebuilt `Rafter:Face0:2`, but the model it rebuilt was at the original location. Correct key cardinality alone did not prove correct placement.

A second part of the same MOVE contract was exposed by the smaller vector regression: when a translation overlaps the roof and survives replay, the builder resolves boundary roles at the displaced endpoints and computes new upper points on the original roof plane. That can change Z and end-cut geometry instead of carrying the existing physical body rigidly in XY. In the test fixture, `(37,-19)` produced a roughly 10.97 mm vertex difference before the fix.

The six initial behavioral cases all failed before production changes: large moves lost the accepted Plan through dormancy; smaller moves failed exact translated physical-vertex assertions. No pre-existing expected baseline geometry was changed to make the regressions pass.

## Coordinate interpretation

The working plane is Z=0. For the HOST rafter directed along world +Y, the existing basis is `U=(0,1,0)`, `V=(-1,0,0)` because `V = normal × U`. Therefore:

```text
translation = U * AlongMm + V * LateralMm
            = (0,1,0) * -2700 + (-1,0,0) * -5000
            = (+5000,-2700,0)
```

The persisted signs are correct. The exact HOST Plan endpoints are tested through existing classification and replay math; Along/Lateral signs, rotation and endpoint offsets remain unchanged. The roof creation request contains section/layout settings; it was not the point where the override was lost and was not modified.

## Production change and authoritative seam

1. Existing-model materialization reads live Generated Plan endpoints into an exact `RoofGeneratedMemberKey` map. It uses the persisted owner Overrides after the acceptance write.
2. `RoofGeneratedMemberReplayPlanner.CreateForExistingPhysicalMembers` starts with the unchanged generic replay. Only a dormant, nonsuppressed, pure-translation override with zero rotation and zero endpoint offsets can be restored to `GeometryReplayed`. Both endpoints of the live exact-key Plan must match the geometry reconstructed from the persisted override within the existing 0.01 mm tolerance. Missing, stale, mismatched and nonfinite live geometry does not authorize revival.
3. The physical builder recognizes a replayed pure translation through existing `TryClassifyPureTranslation`. It builds the canonical Core physical member using the existing roof-plane, eave, ridge and structural-side cut solver, then translates the complete Core model in XY. All body vertices, construction prism vertices, clip plane points, cut polygons and structural lower-contact edges move together; normals, Z, dimensions, pitch and physical length remain unchanged.
4. Existing materialization builds a new canonical Solid3d from that Core model. The unchanged exact-key reconciliation removes the old binding and creates exactly one replacement. GROUP and annotation lifecycle remain unchanged.

The live Plan is corroborating evidence that a persisted MOVE was accepted; it does not create Physical3D by transforming a CAD Line or a previously built Solid3d. The persisted override remains geometry authority. The generic resize `Create` method retains its dormant policy; a regenerated canonical live Plan cannot revive an out-of-domain override.

Nontranslation ordinary edits remain on their previous semantic builder path. AttachedManual uses its existing builder. Structural Hip/Valley, ManualStructural, LOCK routing, COPY/MIRROR/ERASE policy and whole-roof command ownership are untouched. Default generated physical geometry still uses the original canonical solver. No annotation source ownership or numbering code was edited.

## Files changed by this task

| File | Purpose |
| --- | --- |
| `src/AcKrovy.AutoCAD/Infrastructure/RoofOrdinaryRafterSolidMaterializationService.cs` | Existing-model read of exact-key live Plan and physical replay selection |
| `src/AcKrovy.Core/Services/Roofs/RoofGeneratedMemberReplayPlanner.cs` | Narrow corroborated existing-physical replay; generic resize replay unchanged |
| `src/AcKrovy.Core/Services/Roofs/RoofAutomaticRafterPhysicalBuilder.cs` | Deterministic rigid translation of the complete Core physical member and clip inputs |
| `src/AcKrovy.Core.Tests/RoofOrdinaryGeneratedMovePhysicalTests.cs` | 15 new regression cases |
| `docs/ORDINARY_GENERATED_MOVE_PHYSICAL3D_FIX_2026-10-02.md` | This report |

The two Structural production modifications and the Manual COPY test/report visible in git status predate this task and were not edited.

## Tests added

New suite: 15/15 PASS, 14 behavioral Core cases plus one adapter wiring check.

- Six tests with `(+5000,-2700)` and `(37,-19)`: three lower-end cut modes, persisted JSON override, exact `Rafter:Face0:2` identity, unchanged ElementId reservation, no AttachedManual conversion/suppression, accepted Z=0 Plan, every physical vertex translated in XY with identical Z, preserved Hip side-cut and horizontal construction/clip geometry, unaffected other members, old physical binding removed, one replacement key and canonical GROUP expected-set rules.
- Four invalid live-Plan cases: missing, regenerated canonical, mismatched endpoint and nonfinite endpoint cannot revive dormant MOVE.
- One suppression/resize guard: existing physical replay accepts corroborated MOVE while ordinary resize replay remains dormant; suppression still has no physical geometry and the persisted override is unchanged.
- One test using the exact supplied HOST Plan coordinates proves the existing Along/Lateral sign convention.
- Two ridge-overlap tests verify translated body/prism/cut polygons and clip plane point with unchanged retained normal for both vectors.
- One wiring test confirms exact-key live Plan plus persisted owner Overrides reach the physical replay after the acceptance metadata write.

The geometric Hip fixture uses the same logical key and world offsets, but its roof dimensions/spacing differ from the live DWG. The separate sign test uses the exact supplied HOST coordinates. None of these tests claims an actual AutoCAD retest.

## Validation

| Check | Result |
| --- | --- |
| New Ordinary MOVE suite | PASS, 15/15 |
| Focused Ordinary/Structural/AttachedManual/whole-roof/GROUP/override regressions | PASS, 648/648 |
| ManualStructural COPY Placement focused suite | PASS, 15/15 included above; all four WIP files unchanged |
| Existing ordinary physical builder tests | PASS, 48/48 included above |
| Existing AttachedManual physical lifecycle + adapter contracts | PASS, 58 + 11 included above |
| Existing Unlocked manual-edit tests | PASS, 28/28 included above |
| Existing replay-domain tests/contracts | PASS, 17 + 6 included above |
| Full Core | PASS, 7,240/7,240, 0 failed / 0 skipped |
| Portable Compatibility Gate | PASS, restore/build, 7,240 Core tests, dependency checks; 0 warnings / 0 errors |
| Full WPF | PASS, 826/826 standalone and Full Gate, 0 failed / 0 skipped |
| Debug x64 | PASS, warnings-as-errors, 0 warnings / 0 errors |
| Release x64 | PASS, warnings-as-errors, 0 warnings / 0 errors |
| Full Compatibility Gate | PASS, restore/build, 7,240 Core + 826 WPF = 8,066 tests, 0 warnings / 0 errors |
| `git diff --check` | PASS |
| HEAD/upstream | Unchanged, ahead 0 / behind 0 |
| Existing WIP hash check | PASS, 46/46 unchanged |
| Schema/localization changes | None |
| New HOST retest | NOT RUN |

The initial six failures were intentional red regressions. A test-fixture compile error referring to `RoofTopologyEdge.Index` was corrected by using the existing indexed-edge enumeration. Final Core builds/tests and Portable Gate have zero compiler warnings/errors.

AutoCAD PID 135096 initially blocked the Debug process guard before any build ran. The user then explicitly authorized automatic closure: request normal close, wait 10 seconds for saving, then close even if unsaved. `CloseMainWindow()` returned true; after the 10-second wait the process still ran and was force-terminated with `Stop-Process`. The first immediate process check still saw termination in progress, so it blocked the build again. A subsequent check confirmed no running AutoCAD before compilation. Debug, Release, WPF and Full Gate then all passed. No DWG save is claimed. No build ran against an active AutoCAD process.

Commands run (logs and TRX: `%TEMP%/krovy-ordinary-move-20261002`):

```powershell
git status --short
git rev-parse HEAD origin/main
git rev-list --left-right --count HEAD...origin/main
Get-Process -Name acad -ErrorAction SilentlyContinue
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore -warnaserror -m:1 --filter FullyQualifiedName~RoofOrdinaryGeneratedMovePhysicalTests
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore -warnaserror -m:1 --filter 'FullyQualifiedName~RoofOrdinaryGeneratedMovePhysicalTests|FullyQualifiedName~RoofAutomaticRafterPhysicalBuilderTests|FullyQualifiedName~RoofGeneratedOverrideReplayDomain'
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore -warnaserror -m:1 --logger 'trx;LogFileName=core.trx' --results-directory "$env:TEMP/krovy-ordinary-move-20261002"
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-build --filter 'FullyQualifiedName~RoofOrdinary|FullyQualifiedName~RoofAutomaticRafterPhysical|FullyQualifiedName~RoofAttachedManualPhysical|FullyQualifiedName~RoofGeneratedMemberUnlocked|FullyQualifiedName~RoofGeneratedOverrideReplay|FullyQualifiedName~RoofStructural|FullyQualifiedName~RoofRigidGroup|FullyQualifiedName~RoofAssemblyGroupMembership' --logger 'trx;LogFileName=focused.trx' --results-directory "$env:TEMP/krovy-ordinary-move-20261002"
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Portable
dotnet build AcKrovy.sln --no-restore -c Debug -p:Platform=x64 -warnaserror -m:1
dotnet build AcKrovy.sln --no-restore -c Release -p:Platform=x64 -warnaserror -m:1
dotnet test src/AcKrovy.Wpf.Tests/AcKrovy.Wpf.Tests.csproj --no-build -p:Platform=x64 --logger 'trx;LogFileName=wpf.trx' --results-directory "$env:TEMP/krovy-ordinary-move-20261002"
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Full
git diff --check
git diff --stat
```

HOST validation: NOT RUN. Release verdict: NOT READY FOR HOST-AFFECTING CHANGE until the one acceptance run below passes. All required code-side validation is complete.

## One minimal HOST retest

Use one Unlocked Ordinary Generated rafter; perform `MOVE @5000,-2700`, then run `AK_ROOF_3D_AUDIT`.

Acceptance: Plan2D moves by `(+5000,-2700)` and remains Z=0; Generated identity and ElementId remain unchanged; exactly one Physical3D body for the same key follows the identical XY translation with unchanged Z. No stale or duplicate body remains and GROUP is canonical. For the supplied original center `(38202.15871521187,11834.655434489285,417.2652807310538)`, the translated center is approximately `(43202.15871521187,9134.655434489285,417.2652807310538)` within floating-point tolerance.

No additional roof type, Structural Hip/Valley retest or SAVE/REOPEN is requested.

## Current git status

Exact output is appended below. No file in the initial WIP hash inventory has changed.

CODE-SIDE VERDICT: PASS ✅

```text
 M src/AcKrovy.AutoCAD/Infrastructure/RoofOrdinaryRafterSolidMaterializationService.cs
 M src/AcKrovy.AutoCAD/Infrastructure/RoofStructuralNativeEditService.cs
 M src/AcKrovy.Core/Services/Roofs/RoofAutomaticRafterPhysicalBuilder.cs
 M src/AcKrovy.Core/Services/Roofs/RoofGeneratedMemberReplayPlanner.cs
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
?? docs/ORDINARY_GENERATED_MOVE_PHYSICAL3D_FIX_2026-10-02.md
?? docs/PHYSICAL_3D_CLONE_OWNERSHIP_DIAGNOSIS_2026-09-26.md
?? docs/PHYSICAL_3D_EAVES_RETEST_2026-09-26.md
?? docs/PHYSICAL_3D_LOCKED_ERASE_FIX_2026-09-26.md
?? docs/PHYSICAL_3D_NATIVE_CLONE_FIX_2026-09-26.md
?? scripts/acad-host-workflow.ps1
?? src/AcKrovy.Core.Tests/RoofOrdinaryGeneratedMovePhysicalTests.cs
?? src/AcKrovy.Core.Tests/RoofStructuralManualCopyPlacementTests.cs
?? src/AcKrovy.Core.Tests/SimpleGableOrdinaryPhysical3DTests.cs
?? src/AcKrovy.Core/Services/Roofs/SimpleGableOrdinaryRafterPhysicalAdapter.cs
?? src/AcKrovy.Core/Services/Roofs/SimpleGableRoofTopologyAdapter.cs
```

CODE-SIDE VERDICT: PASS ✅

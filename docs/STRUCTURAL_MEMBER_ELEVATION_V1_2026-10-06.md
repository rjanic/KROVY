# Implementation Report: Structural Member Elevation v1

**Date:** 2026-10-06  
**Feature:** Structural Member Elevation Contract v1  
**Status:** CODE PASS / HOST PASS / CLOSED (AutoCAD 2027 HOST acceptance completed 2026-10-06/07)

## HOST: Elevation 50° persisted as 45° BRep frame (2026-10-06)

**Symptom:** Elevation Apply logged `slopeAfter=50`, `sectionFrameCaptured=True`, but GRIP trace showed `storedPitchDegrees=50` with `storedFrame L ≈ (0,0.707,0.707)` (45°). After GRIP XY, physical ~44.25°; Elevation panel disappeared (`persisted_elevation_v1_implausible`).

**Root cause:** Independent `TryBuild` treated SectionFrame/PitchDegrees divergence from live roof-node LongitudinalAxis as a plan-direction change and replaced the Elevation frame with a horizontal frame on the **roof** L (still 45° from nodes). TryCapture then persisted that 45° BRep while PitchDegrees stayed 50. Pair:height was already fixed earlier; this is the remaining pitch contract hole.

**Shared fix:**
- Preserve member-owned pitch when SectionFrame already lies on the accepted plan XY and disagrees with roof L pitch.
- When independent L pitch ≠ roof run, place the prism from EaveElevationMm + member pitch (not roof upper axis).
- Elevation Apply validates `targetSpatialPitch ≈ measuredPhysicalPitch ≈ storedPitch ≈ storedFramePitch` before Persist.
- Persist `CalculationMode` (Elevation schema v2); GRIP/LENGTHEN/ROTATE Accept adapts Elevation for Plan2D XY per mode, then patches/rebuilds/validates.
- Timber slope metadata follows Elevation when present.

## HOST: Elevation → GRIP interoperability failure (earlier 2026-10-06)

**Symptom (after legacy Independent migration HOST PASS):** Elevation Apply to 50° succeeded (`independent_applied_after_migration`, `physicalRebuild=pass`). Immediate native `GRIP_STRETCH` on the same Independent member failed:

- `storedStateResolution=valid`, `buildStateSource=persistent_member_xrecord`, `storedPitchDegrees=50`
- `brepLongitudinalSideFaces=2`, `heightFacePairResolved=False`, `Pair:height`
- `result=failed:Ordinary_GRIP_full_physical_build_state_unavailable`, atomic rollback

**Root cause:** Elevation `RebuildPhysical` materialized via `TryBuild(..., useIndependentHorizontalFrame: true)` (body uses `orientation.NewFrame` / `updated.SectionFrame`), but Independent Apply **Persisted the pre-build `patchedState` SectionFrame**. When TryBuild transports/reorients the independent horizontal frame, stored L no longer matches BRep side normals → SectionFrameReader finds only width sides → height pair fails. Defect was in **Elevation Persist path** (not SectionFrameReader itself, not a pitch==50 special case).

**Shared fix:**
- `RebuildPhysical` returns `acceptedState` from post-rebuild `RoofOrdinaryPhysicalSectionFrameReader.TryCapture(solid, updated, …)` (same contract as Detach/GRIP).
- Independent + AUTO-detach Apply Persist `acceptedState`, never bare `patchedState`.
- `AK_EDIT`: successful Elevation-only Apply counts as `upravené 1`; failed Apply does not increment.

## HOST TEST A failure (2026-10-06) — fixed earlier

**Symptom:** Existing AUTO Ordinary opened in `AK_EDIT` showed Elevation section as OS / ±0,000 / ±0,000 / 0,00° while Ordinary slope correctly reported 45°.

**Root cause:** When Elevation XRecord v1 and Build State v2 were absent on the Plan2D Line, `AK_EDIT` invented `CreateSloped(0d, 0d)` (`default_zero`) instead of resolving from the current AUTO physical package.

**Fix:**
- `RoofOrdinaryElevationLifecycleService.TryReadCurrentState` now resolves AUTO via `TryCaptureOrdinaryBuildState` (+ optional measured section frame) and Independent via in-memory package migration.
- Synthetic 0/0/0 is rejected when known pitch is clearly non-zero (`IsPlausibleForKnownPitch`).
- Failure fails loudly; Elevation section stays unavailable (no fake zeros).
- Opening `AK_EDIT` does **not** write Elevation XRecord (display resolve is read-only).
- ViewModel lower/upper are structural Z endpoints (not blind Start/End), preserving reversed fall direction.

## Changed Files

### Core (CAD-Neutral)
- `src/AcKrovy.Core/Models/Roofs/StructuralMemberElevationBehavior.cs`
- `src/AcKrovy.Core/Models/Roofs/StructuralMemberElevationReferenceKind.cs`
- `src/AcKrovy.Core/Models/Roofs/StructuralMemberElevationCalculationMode.cs`
- `src/AcKrovy.Core/Models/Roofs/StructuralMemberElevationState.cs`
- `src/AcKrovy.Core/Models/Roofs/StructuralMemberSpatialAxisResult.cs`
- `src/AcKrovy.Core/Services/Roofs/StructuralMemberElevationRules.cs`
- `src/AcKrovy.Core/Services/Roofs/StructuralMemberSpatialAxisRules.cs`

### AutoCAD (Host Infrastructure)
- `src/AcKrovy.AutoCAD/Infrastructure/StructuralMemberElevationStore.cs`
- `src/AcKrovy.AutoCAD/Infrastructure/RoofOrdinaryElevationLifecycleService.cs`
- `src/AcKrovy.AutoCAD/Infrastructure/RoofOrdinaryElevationCommandWorkflow.cs` (Debug-only standalone)
- `src/AcKrovy.AutoCAD/UI/RoofOrdinaryElevationViewModel.cs`
- `src/AcKrovy.AutoCAD/UI/RoofOrdinaryElevationWindow.cs`
- `src/AcKrovy.AutoCAD/UI/ElementEditWindow.xaml(.cs)` (`AK_EDIT` elevation section)
- `src/AcKrovy.AutoCAD/Commands/AcKrovyCommands.cs` (`AK_EDIT` wiring; no `default_zero`)

### Localization & Tests
- `src/AcKrovy.Localization/Resources/UiStrings.*.resx` + `UiStrings.cs`
- `src/AcKrovy.Core.Tests/StructuralMemberElevationRulesTests.cs`
- `src/AcKrovy.Core.Tests/StructuralMemberElevationLifecycleTests.cs`
- `src/AcKrovy.Core.Tests/StructuralMemberElevationResolverHostRegressionTests.cs`
- `src/AcKrovy.Wpf.Tests/ElementEditElevationTests.cs`
- `src/AcKrovy.Wpf.Tests/RoofOrdinaryElevationViewModelHostRegressionTests.cs`
- `src/AcKrovy.Core.Tests/LocalizationLanguagePackTests.cs` (923 → 947)

## Initialization priority (existing members)

1. `persisted_elevation_v1`
2. `persistent_build_state_v2`
3. AUTO: `auto_current_physical_package` (`TryCaptureOrdinaryBuildState` + optional BRep frame)
4. Independent: `measured_member_package` (in-memory migrate; no write on open)
5. Fail → section unavailable (never invent 0/0/0)

## SH / OS / VH

- `HeightAxisZ = cos(pitch)`
- Reference toggling alone never moves geometry and never detaches AUTO.

## Persistence

- Key: `ACAD_KROVY_MEMBER_ELEVATION_V1`
- Written only on accepted edit; open/`AK_EDIT` display resolve is read-only.

## Test / Gate (after Elevation spatial-pitch + GRIP mode sync)

- Focused Elevation physical-frame / GRIP / migration: PASS
- Core: 7766 PASS
- WPF: 844 PASS
- Portable Compatibility Gate: PASS
- Full Compatibility Gate: PASS (0 warnings, 0 errors)
- `git diff --check`: PASS
- Debug x64 AutoCAD build: PASS (0 warnings, 0 errors)
- AutoCAD 2027 HOST acceptance: **PASS** (manual; Elevation CLOSED)

## JOIN WIP Preservation

- JOIN remains CODE PASS ✅ / HOST OPEN 🟡
- No JOIN HOST tests run

## HOST Acceptance (completed)

Structural Member Elevation was manually HOST validated in AutoCAD 2027 for the accepted scenarios (AUTO resolve, SH/OS/VH, AUTO->Independent detach, legacy Build State v2 migration, Physical Build State v2, AK_EDIT slope + annotation sync, GRIP/STRETCH/LENGTHEN/MOVE/ROTATE slope preservation, IndependentMemberId continuity, positive slope magnitude with separate fall direction, semantic Lower/Upper remapping, crossing elevations, horizontal 0 deg, SAVE/REOPEN, AK_ROOF_EDIT->Pouzit with complete AUTO restoration and Independent coexistence).

Classic geometry edits do **not** intentionally change the accepted rafter slope; slope is changed through AK_EDIT (and later creation workflow). Slope magnitude is positive; fall direction is separate. Endpoint A/B are geometric identities; Lower/Upper are semantic roles.

**Status:**
- **STRUCTURAL MEMBER ELEVATION v1:** CODE: PASS | HOST: PASS | CLOSED
- **JOIN:** CODE: PASS | HOST: OPEN
- **ROOF FACE IDENTITY:** NOT STARTED (next architectural stage)

Checkpoint commit allowed separately. NO TAG. NO RELEASE.

# Independent Ordinary GRIP: horizontal section width

Implementation and automated validation: **PASS**.
New-build visual HOST retest: **NOT CHECKED / INCONCLUSIVE**.

## Approved physical behavior

The user explicitly selected **horizontal width W.Z = 0** after the exact `293F`
calculation showed that minimum 3D transport alone reproduces the rejected frame.
For an accepted Ordinary endpoint GRIP changing its longitudinal direction,
including the AUTO -> Independent acceptance, the section is now constructed as:

```text
L = normalized final Plan-derived physical longitudinal direction
W = normalized cross(WorldZ, L)
H = normalized cross(L, W), H.Z > 0
```

The roof/member reference still lifts the upper longitudinal reference axis and
resolves elevation and end boundaries. It does not re-impose roof-normal roll.
The whole upper face need not lie in that roof plane after Independent yaw.
Width/height remain 80x160 in the tested fixture, normal to the actual section.
Default roof-owned generation retains its approved roof frame. A length edit
without a direction change and rigid translation retain the existing section.
The existing end-cut solver builds the prism using the supplied section W/H,
including lower-end, ridge Meet/Overlap and structural-cut half-spaces.

Minimum 3D transport is retained as a diagnostic reference. Nonzero extra twist
relative to that reference is expected for the chosen horizontal-width convention.

## Exact HOST fixture 293F

Evidence: `C:\Users\Roman\AppData\Local\ACAD_KROVY\Logs\ACAD_KROVY-20261005.log`,
2026-10-05 10:07:01–10:07:04 CEST, line `293F`, solid `2A1F`, owner `2912`, K4.

Before: `(40691.02099635819,11478.696868240506,0)` ->
`(40691.02099635819,14478.696868240506,0)`.

After: `(42132.20804897987,11662.691030875314,0)` ->
`(40691.02099635819,14478.696868240506,0)`.

The log's roof origin is `(36141.02099635819,11478.696868240506,3000)`, normal
`(0,-0.7071067811865474,0.7071067811865477)`, section 80x160, pitch 45 degrees.
The previous final width had Z approximately `-0.2406206507` and user visual FAIL.
The new Core result for those inputs is approximately:

```text
L = (-0.3402889876, 0.6649072886, 0.6649072886)
W = (-0.8901917715,-0.4555860071, 0)
H = ( 0.3029224567,-0.5918949972, 0.7469258983)
minimumTransportAngleDegrees = 19.8944817301
extraTwistAroundNewAxisDegrees = 18.7928739473
```

These are expected Core values, not measurements of a new-build HOST solid.

## Diagnostics and persistence

Command-start capture reads the actual old native BRep section. Accepted member
geometry retains that section frame in the existing member-owned physical build
state. Payload version is now 2; v1 remains readable, with no write during read or
capture. The existing Xrecord name is retained for legacy discovery. Independent
identity and generic timber metadata schemas are unchanged. Frame/axis state is
verified through JSON roundtrip and rebasing after rigid translation.

`ROOF_ORDINARY_PHYSICAL_FRAME` automatically measures the rebuilt final BRep.
It emits old/new physical, width and height axes, minimum transport angle,
extra twist, plan projection error, actual section dimensions and handedness.
Additional fields:

- `rollOracle=worldUpHorizontalWidth` for a changed direction.
- `horizontalWidthErrorDegrees`: measured width's deviation from W.Z=0.
- `rollErrorDegrees`: measured section versus the approved world-up section.
- `rollOracle=preserveUnchangedDirection` when only length/position changes.
- `topFacePlaneErrorMm` and `topNormalDot`: reference information, not Independent
  acceptance criteria. Their roof-plane values need not be zero/one after yaw.
- `extraTwistAroundNewAxisDegrees`: measured section versus the minimum-rotation
  reference; this is informational and no longer required to be zero.

PASS requires correct directed physical axis, Plan projection within 0.001 mm,
section dimensions within 0.001 mm, correct handedness and accepted section roll
within 0.001 degrees. Direction changes additionally require horizontal width
within 0.001 degrees and upward H. Measurement failures remain diagnostic-only
and report INCONCLUSIVE; the existing lifecycle outcome is not overridden.

## Changed files

- `src/AcKrovy.Core/Services/Roofs/RoofOrdinarySectionTransportRules.cs`: frame values,
  horizontal-width frame rule, independent shortest-rotation diagnostic reference.
- `src/AcKrovy.Core/Services/Roofs/RoofOrdinaryGripLifecycleRules.cs`: geometry state
  v2 compatibility and Independent horizontal-frame physical-build input.
- `src/AcKrovy.Core/Services/Roofs/RoofAutomaticRafterPhysicalBuilder.cs`: supplied
  Independent width/height axes used in prism and thickness/end-cut calculations.
- `src/AcKrovy.Core/Services/Roofs/RoofOrdinaryPhysicalFrameRules.cs`: roof reference
  and Independent section separated in the frame description.
- `src/AcKrovy.Core/Models/Roofs/RoofAutomaticRafterPhysicalModel.cs`: transient
  section-orientation diagnostics associated with the resulting physical member.
- `src/AcKrovy.AutoCAD/Infrastructure/RoofOrdinaryPhysicalSectionFrameReader.cs`:
  read-only pre-edit/final BRep section measurement, independent of roof normal.
- `src/AcKrovy.AutoCAD/Infrastructure/RoofOrdinaryGripLifecycleService.cs`: geometry
  input capture and horizontal-frame build option only.
- `src/AcKrovy.AutoCAD/Infrastructure/RoofFinalSolidFrameAudit.cs`: new measured
  world-up diagnostic oracle and minimum-transport comparison fields.
- `src/AcKrovy.Core.Tests/RoofIndependentOrdinaryHorizontalFrameTests.cs`:
  focused physical-frame, end-cut and persisted-state regressions.
- `docs/geometry/roof-elevation-contract.md`: approved Independent exception,
  horizontal-width construction and diagnostic distinction, contract version 1.2.
- `docs/INDEPENDENT_ORDINARY_SECTION_TRANSPORT_CONTRADICTION_2026-10-05.md`:
  marked the earlier contradiction as resolved by the user's horizontal-width choice.
- This report.

Detach, rollback, IndependentMemberId, ElementId, GROUP, prompts, event routing,
terminal handling and existing MOVE implementation were not changed. No new UI,
localization or abstraction changes. Existing unrelated WIP was preserved.
No commit, push or tag.

## Verification

Branch `main`, HEAD `cb8f2ae225c7d0ddfc9b60ca5fb4e8200ef6493a`; dirty before work.

| Check | Result |
| --- | --- |
| Focused horizontal frame plus frozen MOVE/GRIP and roof-yaw regressions | PASS 54/54 |
| Portable Gate: dependency/restore/build and Core tests | PASS 7435/7435 |
| Full Gate: solution restore/build, Core and WPF tests | PASS Core 7435/7435, WPF 828/828 |
| Final Debug x64 rebuild, warnings as errors | PASS, 0 warnings, 0 errors |
| git diff --check | PASS; existing LF/CRLF notices only |

New regressions cover yaw +20/-20/+40 and zero, roof pitch 25/45/60,
rotated roof 0/37 degrees, exact HOST endpoints, horizontal W, upward H,
Plan projection, 80x160 section, handedness, end-cut consistency, reversed axis,
repeat edit/JSON roundtrip, rigid translation, v1 adoption and invalid frames.
An initial test fixture attempted internal topology constructors; it was corrected
to exercise the public Core state builder. Final checks all pass.

Commands:

```powershell
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore -c Debug --filter 'FullyQualifiedName~RoofIndependentOrdinaryHorizontalFrame|FullyQualifiedName~RoofOrdinaryGripLifecycle|FullyQualifiedName~RoofOrdinaryLogicalMove|FullyQualifiedName~RoofOrdinaryMoveCancel|FullyQualifiedName~RoofOrdinaryPhysicalFrameYaw|FullyQualifiedName~RoofOrdinaryRidgeMeetYaw' -m:1 -nr:false
dotnet build src/AcKrovy.AutoCAD/AcKrovy.AutoCAD.csproj --no-restore -c Debug -p:Platform=x64 -warnaserror -m:1 -nr:false
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Portable
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Full
dotnet build src/AcKrovy.AutoCAD/AcKrovy.AutoCAD.csproj --no-restore -t:Rebuild -c Debug -p:Platform=x64 -warnaserror -m:1 -nr:false
git diff --check
```

AutoCAD was closed for all builds. Final rebuild: 8.64 seconds.
DLL timestamp: **2026-10-05 10:49:02 Europe/Bratislava (CEST)**.

```text
C:\Users\Roman\Documents\CODEX\C#\CsharpProjects\ACAD_krovy\src\AcKrovy.AutoCAD\bin\x64\Debug\net10.0-windows\AcKrovy.AutoCAD.dll
SHA256 9D13934F3057CD1E0E17312742C81E6525CDB279677F975879FBA0A9E48D3446
```

## HOST retest and remaining checks

AutoCAD 2027 / .NET 10, above DEBUG build, existing startup/autoload.
Saved workflow DWG is `C:\Users\Roman\Documents\3d.dwg`; use a fresh/canonical
AUTO member for the first-detach test and confirm loaded build at startup.

1. Endpoint GRIP with obvious sideways yaw -> YES -> STOP.
2. Capture lifecycle and `ROOF_ORDINARY_PHYSICAL_FRAME` output.
3. Verify visual roll, matching Plan/physical direction and horizontal width.
4. Confirm `rollOracle=worldUpHorizontalWidth`, W.Z approximately zero,
   `rollErrorDegrees` approximately zero, section 80x160 and correct handedness.
5. A later independent endpoint GRIP should use the member frame without a new
   detach prompt; verify after SAVE/REOPEN as well.

New-build HOST visual result, native section-reader behavior, v2 DWG persistence,
COPY/WBLOCK and UNDO/REDO: **NOT CHECKED in this task**. Prior lifecycle HOST PASS
is preserved as reported evidence; automated tests do not substitute for a new
native test. Interactions remain user-driven under `docs/ACAD_HOST_WORKFLOW.md`.
The HOST Regression workflow explicitly requires "human observation or trusted
AutoCAD execution"; no visual HOST PASS is claimed.

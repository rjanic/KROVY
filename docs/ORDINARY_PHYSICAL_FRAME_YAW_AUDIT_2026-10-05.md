# Ordinary GRIP physical frame / yaw audit

Automated validation: **PASS**. Visual HOST correction: **INCONCLUSIVE / pending retest**.
The reported visual failure is not claimed fixed by this build.

## Evidence and scope

User reported lifecycle PASS for line `2941`, solid `2A75`, AUTO Start GRIP YES,
GROUP `176/176`, ElementId `K4` retained. Visual orientation failed.
That lifecycle evidence remains authoritative; this task does not change detach,
rollback, GROUP, IndependentMemberId or ElementId semantics.

Audited path: captured full builder context -> `TryBuildSemanticMember` ->
`MaterializeOrdinaryMember` -> side-profile region extruded along the width vector ->
existing final cuts -> existing solid `CopyFrom` -> existing BRep vertex verification.
The audited builder already lifted both final XY endpoints onto its resolved roof
plane and used `W = normalize(N x L)`. No old pitched solid plus world-Z yaw or
accumulated Euler rotation was found on this path. The native materializer also
uses the Core prism geometry directly. Autodesk describes this API as extrusion
along the supplied direction vector:
[CreateExtrudedSolid reference](https://help.autodesk.com/cloudhelp/2027/ENU/OARX-RefGuide/files/OARX-RefGuide-AcDb3dSolid__createExtrudedSolid_AcDbEntity__AcGeVector3d__AcDbSweepOptions_.html).

Consequently this is a geometric frame consolidation and diagnostic build, not a
proven correction of the visually reported HOST defect. A new measured HOST result
is needed to distinguish frame error from roof-plane/context or materialization error.

## Files changed in this task

- `src/AcKrovy.Core/Services/Roofs/RoofOrdinaryPhysicalFrameRules.cs`: directed frame
  reconstructed from the final Plan and authoritative upward roof normal; includes
  lifted upper-axis endpoints, normalized L/W/H, and positive height orientation.
- `src/AcKrovy.Core/Services/Roofs/RoofAutomaticRafterPhysicalBuilder.cs`: uses that
  common geometric frame, retaining existing section dimensions and cut solver.
- `src/AcKrovy.AutoCAD/Infrastructure/RoofFinalSolidFrameAudit.cs`: DEBUG read-only
  final BRep measurement and `ROOF_ORDINARY_PHYSICAL_FRAME` output.
- `src/AcKrovy.AutoCAD/Infrastructure/RoofOrdinaryGripLifecycleService.cs`: one
  DEBUG diagnostic call after existing BRep verification; lifecycle logic unchanged.
- `src/AcKrovy.Core.Tests/RoofOrdinaryPhysicalFrameYawTests.cs`: yaw 0/+20/-20/+45,
  roof pitches 25/45/60, roof rotation 0/37, reversed directed axes, exact supplied XY.
- This report.

Existing extensive uncommitted WIP was preserved. No commit, push or tag.

## Diagnostic interpretation

Required fields are emitted automatically on a successful ordinary physical GRIP
rebuild: line, solid, planAxis, physicalAxis, roofNormal, sectionWidthAxis,
sectionHeightAxis, planProjectionErrorMm, topFacePlaneErrorMm,
rollErrorDegrees, result. Measured axes, width and height come from final native
BRep planes, not just expected Core vertices. Additional output identifies the
resolved roof face, roof-plane origin, expected upper-axis endpoints, dimensions,
directed axis agreement and upper-normal agreement.

`planProjectionErrorMm` measures the largest XY distance of final Plan endpoints
from the final BRep upper-face width-centre longitudinal line. It does not require
cut corners or extended source-prism ends to coincide with Plan endpoints.
The upper axis is the roof-surface datum; it is not the section centroid below it.
`topFacePlaneErrorMm` measures all actual upper face vertices against the captured
authoritative plane. `rollErrorDegrees` measures actual width relative to N x L.
PASS requires errors <=0.001 mm / 0.001 degrees, correct directed axis and normal,
and measured section dimensions within 0.001 mm. Missing BRep faces or measurement
exceptions yield `inconclusive`; diagnostics never alter lifecycle decisions.

## Automated verification

Branch `main`, HEAD `cb8f2ae225c7d0ddfc9b60ca5fb4e8200ef6493a`, dirty before work.

| Check | Result |
| --- | --- |
| Focused frame, ridge-yaw, GRIP, MOVE and identity regressions | PASS 67/67 |
| Portable Gate: restore/build/dependency checks and Core tests | PASS 7423/7423 |
| Full Gate: solution restore/build and Core/WPF tests | PASS Core 7423/7423, WPF 828/828 |
| Debug x64 final rebuild, warnings as errors | PASS, 0 warnings, 0 errors |
| git diff --check | PASS (existing LF/CRLF notices only) |

Commands:

```powershell
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore -c Debug --filter 'FullyQualifiedName~RoofOrdinaryPhysicalFrameYaw|FullyQualifiedName~RoofOrdinaryRidgeMeetYaw|FullyQualifiedName~RoofOrdinaryGripLifecycle|FullyQualifiedName~RoofOrdinaryLogicalMove|FullyQualifiedName~RoofOrdinaryMoveCancel|FullyQualifiedName~RoofIndependentOrdinary|FullyQualifiedName~RoofOrdinaryAuthorityTransition' -m:1 -nr:false
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Portable
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Full
dotnet build src/AcKrovy.AutoCAD/AcKrovy.AutoCAD.csproj --no-restore -c Debug -p:Platform=x64 -warnaserror -m:1 -nr:false
dotnet build src/AcKrovy.AutoCAD/AcKrovy.AutoCAD.csproj --no-restore -t:Rebuild -c Debug -p:Platform=x64 -warnaserror -m:1 -nr:false
git diff --check
```

The first rotated fixture assumed source edge 0 survived footprint normalization;
it was corrected to select the face by its geometric roof normal. Final matrix passes.
The exact supplied endpoints are tested on a reconstructed 45-degree Hip fixture
with eave Z=3000 and section 80x160; this is not a readback of the failing DWG's
complete stored topology/recipe and is not proof of HOST behavior.

## Build

AutoCAD was closed for all builds.
DLL timestamp: **2026-10-05 09:45:31 Europe/Bratislava (CEST)**.
Final rebuild duration: 8.21 seconds.

```text
C:\Users\Roman\Documents\CODEX\C#\CsharpProjects\ACAD_krovy\src\AcKrovy.AutoCAD\bin\x64\Debug\net10.0-windows\AcKrovy.AutoCAD.dll
SHA256 6A9CF82F99234628DD2D3F11908A78D28CA3228559596A553F65E36565D1CB9D
```

## HOST retest plan and remaining risk

AutoCAD 2027 / .NET 10, above DEBUG build. Existing startup/autoload is authoritative.
Use a fresh/canonical drawing and a new AUTO ordinary line. The saved workflow
drawing is `C:\Users\Roman\Documents\3d.dwg`; its canonical status must be checked
by the operator before testing. Existing detached members are not substitutes.

1. Verify loaded build via existing `AK_RUNTIME_BUILD` startup output.
2. Endpoint GRIP_STRETCH with obvious sideways yaw; answer YES once; STOP.
3. Capture `ROOF_ORDINARY_PHYSICAL_FRAME` and lifecycle lines.
4. Inspect plan and physical direction, absence of twist, and upper face in roof plane.
5. Retain line/solid handles and roof face/origin/normal if visual failure remains.

New-build HOST visual behavior and new BRep diagnostics: **NOT CHECKED**.
DBMOD, persistence and UNDO/REDO: **NOT CHECKED in this task**.
Native grip interactions remain user-driven under `docs/ACAD_HOST_WORKFLOW.md`.
HOST workflow rule: `.agents/skills/host-regression-test/SKILL.md` says
"HOST regression requires human observation or trusted AutoCAD execution."
No native AutoCAD interaction/visual observation was performed by the agent here.
The previously reported visual failure remains open until this measured retest.

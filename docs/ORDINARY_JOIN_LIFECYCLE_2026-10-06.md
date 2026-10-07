# Ordinary native JOIN lifecycle

Date: 2026-10-06. CODE PASS. HOST OPEN.
No commit, push or tag. Existing uncommitted WIP is preserved.
LENGTHEN is CODE PASS / HOST PASS and CLOSED by the authoritative user matrix;
its existing report has the final status updated without a new LENGTHEN report.

## Observed native event model

Actual AutoCAD 2027 Core Console observations used a disposable copy of the
empty acadiso template. Documents/3d.dwg was not opened or changed by the probe.
The production plugin was not initialized for these native event observations.
They prove the native engine transitions below, not the complete Architecture
UI/plugin lifecycle or product HOST PASS.

| Native case | Observed graph before CommandEnded |
| --- | --- |
| Touching Lines | Secondary erased, primary Line modified and retained |
| Overlapping Lines | Secondary erased, primary Line modified and retained |
| Separated collinear Lines | Secondary erased, primary Line extends across native-supported gap |
| Opposite source direction | Secondary erased, primary remains a Line |
| Fully contained source | Secondary erased, primary modified notification although extents stay identical |
| Three collinear sources | Two secondary erases followed by primary modification |
| Bent Lines, single primary selection | Both Lines unchanged; modified notifications still occur |
| Touching Lines, current multiple selection | Secondary erased, primary remains a Line |
| Bent Lines, current multiple selection | New Polyline appended, both source Lines erased, final Polyline has three vertices |

The current multiple-selection probe explicitly used Editor.InitCommandVersion(2).
Autodesk documents the command-version distinction in
[InitCommandVersion](https://help.autodesk.com/cloudhelp/2026/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-Autodesk_AutoCAD_EditorInput_Editor_InitCommandVersion_int.html)
and the source-type rules in
[JOIN](https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-Core/files/GUID-4D2A28C3-3E7F-4830-BE6A-7C9907C95488.htm).
Evidence is retained locally in .artifacts/ordinary-join-2026-10-06/native-events.log.
Temporary probe projects/drawings were removed from ErrorReports.
A separate attempt to execute the complete production package helper in Core
Console did not reach a result and exited without package diagnostics. It is
UNCONFIRMED, not a passing integration test. Live Architecture testing is required.

The production resolver uses command-start sources and the final native graph;
it does not equate primary ObjectId survival with logical identity. The automatic
DEBUG observer additionally records JOIN_NATIVE_EVENT and native/maintenance
checkpoints for the real user HOST run.

## Command scope and authority

RoofOrdinaryJoinLifecycleService.Context owns fresh source/package snapshots,
raw curve snapshots, GROUP baselines, touched/appended IDs, claimed IDs and
command counters. Begin starts with zero candidates and counters. End/cancel/fail
process at most once; disposal releases clones and clears all transient state.
Undo/Redo retains the existing zero-DB boundary. JOIN joins the existing grouped
undo scope and assembly capture without enabling legacy geometry overrides.

JOIN first claim runs before structural, roof-resize and generic erased-source
maintenance. Claimed object IDs and erased handles are removed from downstream
queues. Plan+paired Solid is one logical source. A native Physical3D-only rejection
needs no artificial solid JOIN. Unchanged native geometry with no consumed source
or new result is a no-op even when modified notifications were emitted.

The accepted authority is one Autodesk Line at Z=0 with the native outer extents.
All source Lines must be on its common infinite axis using existing project
coordinate tolerance. Direction, overlap, containment and native-supported gaps
are handled by projections. The plugin never calculates a replacement gap bridge.
A straight monotonic, open, zero-bulge Polyline can normalize to a Line; bends,
backtracking, arcs, closed shapes and unsupported native types reject atomically.
That normalization has portable validation coverage; a real straight replacement
Polyline variant remains HOST OPEN. Generic or unresolved contributors reject;
they never supply a timber definition.

## Many-to-one physical state and identity

Every contributing source resolves through the shared command-start state path.
Independent uses its own v2 record; legacy uses the existing strict migration,
including MeasuredMemberStructuralCutsVerified. No JOIN migration or schema fork
was introduced. Existing physical bodies additionally replay against the current
source BRep within the unchanged 0.01 mm verification tolerance.

The neutral rules require matching section/material, supported physical axes and
section frames. Projection ordering selects both outer owners, including reversed
input/result directions. Only those owners contribute endpoint boundary edges,
structural side planes and applicable eave/ridge cut settings. Internal boundaries
and structural cuts disappear. Contradictory outer modes or contexts that one
existing builder cannot represent reject; one source's section is never chosen
silently. Final outer vertex clouds must match the respective source outer cuts.
The common builder performs all physical construction and materialization.
Width remains horizontal, height upward, section dimensions exact and frame right
handed. No JOIN extrusion, Euler rotation or physical-frame redesign was added.

AUTO+AUTO and AUTO+Independent use one command-wide standard detach confirmation.
Preference OFF uses its existing automatic YES semantics. Independent-only has no
confirmation. NO restores the entire command, including Independent sources.

Every successful 2+ source JOIN allocates a new IndependentMemberId, even when
native primary geometry did not grow or its ObjectId survives. Origin Joined is
an additive enum value in the existing identity codec; payload schema stays 1.
All old source identities retire. Source roof/key fields are history only.
The result receives a fresh valid v2 record through the common persistence service.
Subsequent edits must read persistent_member_xrecord and retain this new identity.

## Atomic acceptance, cleanup and rollback

One accepted transaction preserves AUTO recipes, removes consumed source package
GROUP membership, transfers the final Line to fresh Independent identity, erases
obsolete Lines/Solids/annotations, creates one new derived Solid, and calls common
acceptance, manufacturing designation reconciliation and annotation services.
Final presentation uses the source plan/physical layers and visibility separately.
The common verifier checks Line/Solid pairing, unique identity, current v2 and
absence of Generated/AttachedManual metadata. Additional checks enforce retired
source packages and exact canonical GROUP membership minus consumed packages.
The result stays outside AUTO GROUP. Erased entities own their obsolete extension
records; a retained carrier's physical record is replaced by the joined v2 state.

NO/failure/cancel/fail restore pre-command native curves and complete source
packages, remove appended native results, restore exact GROUP membership and verify
geometry/identity/designation/physical/annotation baselines. An aborted acceptance
transaction retains no numbering or v2 changes. Rollback uses the existing deferred
refresh. A restoration failure is diagnosed explicitly and never reported PASS.

No successful Suppressed, geometry ManualOverride, AttachedManual, AlongMm,
LateralMm, RotationRadians, StartOffsetMm, EndOffsetMm, PhysicalReferenceSegment
override or HasGeometryOverride writes were added. Explicit Apply regenerates the
complete original AUTO slots from their recipes while joined Independent remains
at its accepted world position. BREAK identity behavior is unchanged.

## Verification

- Focused JOIN plus affected diagnostics/localization: 68/68 PASS, including
  all 25 JOIN cases. Covers touching/overlap/gap/opposite/contained/reversed native
  axis, three sources, outer settings, bent/bulged/closed/backtracking rejection,
  incompatible section/material/elevation/frame/missing state, strict shared
  legacy adoption, v2 round trip and subsequent length edit, native routing,
  atomic first claim, cleanup, persistent identity codec and context disposal.
- Ordinary/Independent/Generated/COPY/MIRROR/ERASE subset: 823/823 PASS before
  the final reversed-native-axis case; the final full suite includes that case.
- Additional measured structural-cut legacy JOIN regression: 1/1 PASS. The
  final full suite includes all 26 JOIN cases and verifies removal of the internal
  seam while retaining the outer Hip cut through MeasuredMemberStructuralCutsVerified.
- Full Core: 7685/7685 PASS, zero failures/skips.
- Full WPF: 831/831 PASS, zero failures/skips.
- Full Compatibility Gate including nested Portable Gate: PASS.
- Final standard Debug x64 net10.0-windows Rebuild: PASS, zero warnings/errors.
  Output: src/AcKrovy.AutoCAD/bin/x64/Debug/net10.0-windows/AcKrovy.AutoCAD.dll.
- git diff --check and new-file whitespace checks: PASS. Nothing staged.
- Native event probe: nine observed cases. Complete package/UI HOST: OPEN.

Portable/source-contract guards prove the corresponding code boundaries; they
do not simulate the detach modal, real DBObject callback integration, AutoCAD
annotation/GROUP reactors, or persistence after reopening a user DWG.

Commands run: dotnet test AcKrovy.Core.Tests with the JOIN/diagnostics/localization
filter, the Ordinary/Independent/Generated/COPY/MIRROR/ERASE filter and the added
MeasuredStructuralLegacyOuterCut filter; pwsh -NoProfile -File
scripts/compatibility-gate.ps1 -Full; dotnet build
src/AcKrovy.AutoCAD/AcKrovy.AutoCAD.csproj -t:Rebuild -c Debug -p:Platform=x64
-warnaserror; git diff --check. Gate/build output is retained in the local
.artifacts/ordinary-join-2026-10-06 directory. AutoCAD was verified OFF before
host builds/gates; no unsaved-work close window was needed. No alternate host
output, copied DLL, version bump, runtime checks or user-DWG save was used.
After the final rebuild, AutoCAD Architecture 2027 was started with the exact
C:\Users\Roman\Documents\3d.dwg path. Its window title confirmed 3d.dwg and
Responding=True. The agent stopped without entering any native command.

## Short user HOST plan — stop at first FAIL

User verifies the loaded build through their normal process. The agent does not
run AK_RUNTIME_BUILD, DLL hash/time/size/version checks or precise native selections.

1. Independent+Independent on one physical axis: JOIN; one new identity/Line/Solid,
   coherent annotations, no old live IDs, compatible physical frame and designation.
2. AUTO+Independent then AUTO+AUTO: YES once per command; both produce new
   Independent identity and canonical GROUP. Repeat one mixed case with NO and
   verify every original package exactly. Preference OFF must mean automatic YES.
3. Three compatible members; test overlap and a native-supported gap.
4. Non-collinear/bent selection, generic contributor, incompatible section/material:
   reject/restore all; no Polyline authority, orphan solid or annotation duplication.
5. BREAK, collinear GRIP/STRETCH adjustment, JOIN: internal seam/cuts removed;
   exactly one continuous physical timber with retained outer cuts.
6. Include a legacy Independent source, then LENGTHEN the result again: valid
   persistent_member_xrecord, no re-migration and stable joined identity. Check
   STRETCH/GRIP/ROTATE similarly as needed.
7. AK_ROOF_EDIT Apply: consumed AUTO slots return; joined Independent stays at
   its world position outside canonical GROUP. No suppression.

Capture ROOF_ORDINARY_JOIN_COMMAND_STATE, ROOF_ORDINARY_JOIN_SOURCE,
ROOF_ORDINARY_JOIN_LIFECYCLE and native checkpoints. Disposed transient counts
must be zero. AUTO rejection must not retain designation changes or create identity.

## Files in this task

New: RoofOrdinaryJoinRules.cs, RoofOrdinaryJoinLifecycleService.cs,
RoofOrdinaryJoinLifecycleTests.cs and this report.
Modified: LiveGeometrySynchronizationService.cs, RoofPhysical3DHostDiagnostics.cs,
RoofGeneratedMemberEditCommandRules.cs, LiveGeometryCommandRules.cs,
RoofIndependentOrdinaryTimberData.cs, UiStrings.cs and six UiStrings.resx packs,
LocalizationLanguagePackTests.cs (new resource count), and the existing LENGTHEN
report final status. No other previous WIP was replaced.

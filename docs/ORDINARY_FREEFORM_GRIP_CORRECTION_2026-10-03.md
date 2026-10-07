# Ordinary freeform endpoint GRIP_STRETCH — 2026-10-03

## Product correction and scope

This task supersedes the axis-lock grip semantics and the grip-specific conclusions
in `ATTACHEDMANUAL_COPY_DURABILITY_AND_GRIP_AXIS_2026-10-03.md` and
`ORDINARY_GRIP_ENDPOINT_CROSSING_CLAMP_2026-10-03.md`. Those reports remain historical
records. Their accepted COPY durability implementation is retained.

Baseline: branch `main`, HEAD `cb8f2ae225c7d0ddfc9b60ca5fb4e8200ef6493a`, dirty working
tree. Before editing, copies of the 12 touched existing files and initial Git status
were saved in `%TEMP%/krovy-freeform-grip-20261003-baseline`.
No reset/revert/stash/clean/commit/push/tag was performed.

The existing HOST log confirms the wrong acceptance: native endpoint lateral
movement was discarded and axisAfter remained axisBefore. The accepted current
product definition now permits Plan yaw. Lifecycle/event routing is retained;
the change replaces endpoint geometry and its physical reconstruction semantics.
COPY durability, roof resize rollback, ElementId recovery, shared-node targeting,
Hip/Valley lifecycle, ManualStructural, LOCK, numbering, TWO-HIP cut algorithms and
the pre-existing pure MOVE roof-plane policy were not modified.

## Accepted Plan geometry and safety guard

`RoofOrdinaryFreeformGripRules.TryAccept` identifies the moved native endpoint
without swapping Start/End. It accepts native XY, normalizes native Z to zero and
leaves the opposite endpoint fixed. The pre-command Plan must be Z=0. Moving both
endpoints remains a separate midpoint-translation case through the existing route.
Invalid/nonfinite input still fails.

The minimum floor remains 500 mm of Plan length, conservatively ensuring an axis
true length of at least 500 mm. Already accepted shorter members retain their
current positive floor. The guard is independent of lateral movement:

- Let `u` be the current pre-command directed axis and `v` its XY perpendicular.
- Resolve longitudinal displacement `d` and lateral displacement `l` of the grip.
- Directed gap `g = L-d` for Start, `g = L+d` for End.
- Required gap `max(2*LengthToleranceMm, sqrt(max(0, minimumLength²-l²)))`.
- If `g` is smaller, correct only the component along `u`; retain the full lateral
  component along `v`. Otherwise accept the native XY point exactly.

Thus a large sideways drag remains fully accepted when its directed gap is legal.
Crossing retains lateral XY and a positive gap, without swapping endpoint identity
or triggering unsupported-stretch recovery. Each subsequent edit uses the current
accepted geometry; Plan yaw intentionally changes.

## Persistence and Physical3D

Generated persists the new yaw, Plan-origin displacement and length through the
existing ManualOverride representation. These Plan-origin displacement terms
cannot be interpreted as a rigid physical placement after a Start grip: doing so
would translate a reconstructed roof-plane shape off its current roof plane.
An optional `PhysicalReferenceSegment` therefore records the accepted grip shape
in the canonical U/V/W basis, using the existing CAD-neutral relative-segment type.
The builder replays that shape on its current resolved face and reconstructs its
physical prism/cuts. Subsequent pure MOVE can carry this newly rebuilt reference.

The override codec still reads/writes the original 10-field representation when no
reference exists. Grip records append six finite U/V/W values (16 fields); the new
codec round-trips both forms and rejects malformed references. Existing records
are not rewritten on read. This is backward reading compatibility; older builds
that only accept 10 fields cannot read the new grip-reference payload.
Normalize/Upsert and existing ElementId reservation synchronization preserve the
new reference. Unaccepted dormant overrides retain their canonical fallback.

AttachedManual retains UUID, ChildIdentity, owner, Origin.Copy and exact anchor key.
It captures RelativeSegment from accepted freeform Plan and makes the new physical
reference describe that edited shape. A genuine no-op retains its previous carried
placement. It remains AttachedManual. Existing COPY/MOVE carry/replay mechanisms
remain unchanged; regression tests include copy-of-copy after freeform grip and
repeated copies of a freeform Generated member.

For each edited physical shape, both XY endpoints are lifted to the authoritative
resolved roof plane. The longitudinal axis is their 3D difference, width is
`normalize(cross(roofNormal, longitudinalAxis))`, and height follows the approved
roof-normal section convention. The former lifted Plan-perpendicular width could
lie in the roof plane while being skew to the 3D longitudinal axis; it is replaced
by this orthogonal frame. The frame checker now measures perpendicularity in 3D.
The existing `SectionOrthogonalToPlan` property name is retained for source
compatibility, with its corrected physical perpendicularity interpretation.

Existing cut solvers resolve roles/planes from current accepted semantic geometry.
Their cut algorithms were not changed. The host builds a new region/prism and
extrudes along the new Core width vector; it does not incrementally yaw an old
Solid3d. The previous pure COPY/MOVE body-carry path remains covered by regressions.

## DEBUG evidence

`ROOF_ORDINARY_GRIP_FREEFORM` replaces the axis-constraint diagnostic. It includes:
kind, owner, identity, endpoint, beforeStart/End, nativeStart/End, acceptedStart/End,
axisBefore/After, planYawDeltaDegrees, roofNormal, physicalAxis,
topFacePlaneErrorMm, minimumLength, clamped and result. There is no normal-path
`lateralRejected` field.

`stage=accepted` records geometry acceptance; physical fields are explicitly
`unavailable` at that stage. `stage=physical-model` follows reconstruction in the
existing physical reconciliation path and measures the Core prism/current face.
It reports failure if plane error, section orthogonality or height convention
exceed their tolerances. These are measurements of the constructed physical model,
not an assertion that native Solid3d faces or the interactive HOST were inspected.
The pending diagnostic context is bounded and DEBUG-only; it does not drive
acceptance, persistence, recovery or reconciliation.

## Changed files — this task only

| File | Change |
| --- | --- |
| `src/AcKrovy.Core/Services/Roofs/RoofOrdinaryFreeformGripRules.cs` | New shared native XY classifier, independent safety guard and Generated composition. |
| `src/AcKrovy.Core/Services/Roofs/RoofOrdinaryRoofPlaneFrameRules.cs` | New measurements of roof normal, physical axis, top plane and section/height. |
| `src/AcKrovy.Core/Services/Roofs/RoofGeneratedMemberOverrideMath.cs` | Removes axis-lock helper; preserves new reference in normalization and translation composition. |
| `src/AcKrovy.Core/Models/Roofs/RoofGeneratedMemberOverride.cs` | Optional canonical physical shape reference. |
| `src/AcKrovy.Core/Services/Roofs/RoofGeneratedMemberOverrideCodec.cs` | Legacy/current reference encoding and finite validation. |
| `src/AcKrovy.Core/Services/Roofs/RoofAttachedManualGripRules.cs` | Freeform acceptance and current edited physical reference, same identity/anchor. |
| `src/AcKrovy.Core/Services/Roofs/RoofAutomaticRafterPhysicalBuilder.cs` | New referenced grip reconstruction and roof-normal cross-product width. |
| `src/AcKrovy.Core/Services/Roofs/RoofOrdinaryPhysicalPlanFrameRules.cs` | Correct 3D section perpendicularity check. |
| `src/AcKrovy.AutoCAD/Infrastructure/RoofGeneratedMemberManualEditService.cs` | Routes endpoint grips to freeform rules, retains midpoint translation and existing finalize/recalc. |
| `src/AcKrovy.AutoCAD/Infrastructure/RoofAttachedManualLifecycleService.cs` | Emits freeform acceptance diagnostics through existing AttachedManual route. |
| `src/AcKrovy.AutoCAD/Infrastructure/RoofGeneratedMemberManualEditDiag.cs` | New DEBUG geometry and physical-model evidence. |
| `src/AcKrovy.AutoCAD/Infrastructure/RoofOrdinaryRafterSolidMaterializationService.cs` | DEBUG measurement hook only; solid/reconcile algorithms unchanged. |
| `src/AcKrovy.Core.Tests/RoofOrdinaryGripAxisConstraintTests.cs` | Replaces superseded tests with `RoofOrdinaryFreeformGripTests`; keeps the existing WIP file path. |
| `src/AcKrovy.Core.Tests/RoofAttachedManualCopyGripDurabilityTests.cs` | Corrects grip assertions; COPY durability tests retained. |
| This report | Scope, evidence, verification and remaining HOST check. |

## Verification

Final focused set covers native Start/End lateral/diagonal/longitudinal editing,
huge sideways movement, 500 mm approach/crossing/coincidence, repeated edits,
MOVE→GRIP, GRIP→MOVE, Generated/AttachedManual persistence, yaw 0°/2°/30°/60°,
Vertical/Perpendicular/Horizontal lower cuts and Meet/Overlap, frame/plane/height,
dormant fallback, codec compatibility, COPY and canonical body/group reconciliation.

Commands executed (PowerShell, repository root):

```powershell
Get-Process acad -ErrorAction SilentlyContinue
dotnet build src/AcKrovy.Core/AcKrovy.Core.csproj --no-restore -warnaserror -m:1 -nr:false
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore -warnaserror -m:1 -nr:false --filter 'FullyQualifiedName~RoofOrdinaryFreeformGripTests|FullyQualifiedName~RoofAttachedManualCopyGripDurabilityTests|FullyQualifiedName~RoofOrdinaryGeneratedMovePhysicalTests|FullyQualifiedName~RoofAutomaticRafterPhysicalBuilderTests|FullyQualifiedName~RoofAttachedManualPhysicalLifecycleTests'
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore -warnaserror -m:1 -nr:false
dotnet build src/AcKrovy.AutoCAD/AcKrovy.AutoCAD.csproj --no-restore -c Debug -p:Platform=x64 -warnaserror -m:1 -nr:false
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Portable
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Full
dotnet build src/AcKrovy.AutoCAD/AcKrovy.AutoCAD.csproj --no-restore -c Release -p:Platform=x64 -warnaserror -m:1 -nr:false
git -c core.safecrlf=false diff --check
git status --short
git branch --show-current
git rev-parse HEAD
```

AutoCAD was initially open; a close/save request citing AGENTS.md was issued.
Subsequent process checks proved it was closed before all host-linked builds.
Tests/gates use ordinary local permissions to avoid the previously diagnosed
sandbox File.Replace permission failure. No production file-writer change was made.

| Check | Result |
| --- | --- |
| Focused final regressions | PASS, 182/182 |
| Full Core, final gates | PASS, 7354/7354 |
| Full WPF, final Full Gate | PASS, 826/826 |
| Debug x64 warnings-as-errors | PASS, 0 warnings, 0 errors |
| Release x64 warnings-as-errors | PASS, 0 warnings, 0 errors |
| Portable Gate | PASS |
| Full Gate, final source state | PASS |
| Restore / neutral-layer dependency rules / manifest | PASS |
| Localization/resource changes | None; existing checks PASS |
| Diff whitespace | PASS |
| Interactive post-fix AutoCAD test | NOT RUN |

## Final code-side verdict

| Requested verdict | Automated result |
| --- | --- |
| FREEFORM LATERAL GRIP | PASS ✅ |
| PLAN YAW PRESERVED | PASS ✅ |
| PHYSICAL3D ROOF-PLANE FRAME | PASS ✅ |
| NO ROLL/TWIST | PASS ✅ |
| 500 MM GUARD WITHOUT AXIS LOCK | PASS ✅ |
| COPY DURABILITY REGRESSION | PASS ✅ |
| CODE-SIDE OVERALL | PASS ✅ |

## Remaining HOST test

Run the current Debug build in a real AutoCAD editor. On Generated and
AttachedManual COPY ordinary members, move Start and End laterally by 1000 mm,
diagonally by 1200 lateral + 700 longitudinal, then repeat from current geometry.
Include MOVE→GRIP, crossing, 500 mm approach, huge sideways drag and repeated COPY.
Verify accepted native XY, fixed opposite endpoint, changed Plan yaw, unchanged
identity/owner/anchor, top face on the current roof plane, orthogonal section, current
cuts, one body per identity and canonical annotations/GROUP. Capture both freeform
diagnostic stages, native solid/visual checks and the existing GROUP/ownership audit.

Core and source-contract tests do not prove interactive HOST callback ordering,
native Solid3d slicing or visual annotation placement. The earlier HOST PASS for
COPY remains baseline evidence; this new freeform build still needs its HOST retest.

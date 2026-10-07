# Ordinary native LENGTHEN lifecycle

Date: 2026-10-06. CODE PASS; HOST PASS confirmed by the user.
LENGTHEN lifecycle and legacy Independent migration defects are CLOSED.
No commit, push or tag. Pre-existing uncommitted WIP is preserved.

## Product contract

Native AutoCAD LENGTHEN remains responsible for Delta, Percent, Total and
Dynamic input and the final Line geometry. The plugin supplies no alternate
command, mode mathematics, cursor-based endpoint choice or UI. It compares the
actual command-start and command-end geometry. A valid accepted edit changes
one endpoint, keeps the other fixed, remains collinear in the same directed
axis, has positive non-degenerate length, and normalizes Plan2D to Z=0.

AUTO Ordinary uses the existing shared detach preference/dialog. YES (including
automatic acceptance when ConfirmAutomaticMemberDetach=false) gives the
member a new IndependentMemberId, clears Generated ownership, rebuilds its
derived Physical3D, persists v2 build state, updates designation/annotations,
and excludes its package from AUTO GROUP. Existing Independent members need
no confirmation and keep their identity.

NO restores the entire affected command set atomically, including Independent
edits in a mixed session, using the same command-wide policy as ROTATE.
Plan2D, physical body, annotations, metadata, Generated identity, ElementId
and GROUP are restored. No Independent acceptance or v2 update survives.
Invalid final geometry also restores the affected set and reports its exact
validation reason. A command cancelled/failed before normal completion uses
the existing EXTEND safe-abort policy: restore retained endpoint changes,
then dispose the snapshot without an acceptance prompt.

A query/no-op or native-rejected selection does not detach, rebuild or change
identity. Ordinary package notifications without Plan change are consumed
before generic unsupported-command recovery. No artificial Solid LENGTHEN
or derived-3D rejection branch was added.

## Architecture reused

CommandWillStart captures the existing shared Ordinary package snapshot;
CommandEnded dispatches LENGTHEN before legacy roof/manual recovery. Logical
members are deduplicated by Plan Line. Mixed Plan + paired Solid evidence
produces one accepted member. One confirmation and one acceptance transaction
cover all affected AUTO and Independent members, across owners.

The handler calls the existing Accept, RecalculateDesignations, Restore,
RestoreGroup and Verify helpers. It reuses the read-only package uniqueness/v2
verifier from ROTATE; ROTATE's default behavior is unchanged. Shared detach,
annotation services, member state persistence and legacy lazy migration retain
their existing implementations. TryRebase, frame mathematics, section rules,
cuts, schema and migration are unchanged.

The length validator observes final native geometry. It does not recreate
geometry from a requested Delta/Total/Percent value. Unexpected changes to
both endpoints are diagnosed and rolled back; selecting both ends of the
same Line in one session is not silently reinterpreted.

Roof source changes retain existing roof routing for AUTO. Independent
provenance does not become a live AUTO-slot dependency. The common builder
resolves boundary/free-end roles, including endpoints moved outside the
original footprint. Independent geometry keeps the existing horizontal
width axis, upward orthogonal height, exact 80x160 section in the tests,
and valid handedness without accumulated Euler rotation or scaling.

ElementId follows current manufacturing signature rules. It stays stable for
the same rounded signature, changes for a new signature, and can reuse an
existing matching designation. IndependentMemberId stays stable across
accepted Independent edits.

Explicit AK_ROOF_EDIT Apply still regenerates the complete AUTO plan,
including the detached member's original slot. The edited Independent remains
at its accepted geometry outside AUTO automation. No persistent slot exclusion,
Suppressed=true, geometry ManualOverride, AttachedManual fallback, or legacy
AlongMm/LateralMm/RotationRadians/StartOffsetMm/EndOffsetMm/
PhysicalReferenceSegment/HasGeometryOverride write is introduced.

## Files changed in this task

| File | Change |
|---|---|
| src/AcKrovy.Core/Services/Roofs/RoofGeneratedMemberEditCommandRules.cs | LENGTHEN capture/first-claim vocabulary and grouped undo scope; no new legacy unlocked-override acceptance |
| src/AcKrovy.Core/Services/Roofs/RoofOrdinaryLengthenRules.cs | Native result endpoint/collinearity validation |
| src/AcKrovy.AutoCAD/Infrastructure/RoofOrdinaryGripLifecycleService.cs | Shared dispatch and command-scoped LENGTHEN diagnostics/count cleanup |
| src/AcKrovy.AutoCAD/Infrastructure/RoofOrdinaryLengthenLifecycleService.cs | Command-wide native reconciliation, atomic acceptance/restore, no-op claim, abort cleanup |
| src/AcKrovy.AutoCAD/Infrastructure/RoofOrdinaryRotateLifecycleService.cs | Make the existing read-only package verifier reusable; ROTATE default unchanged |
| src/AcKrovy.AutoCAD/Infrastructure/LiveGeometrySynchronizationService.cs | LENGTHEN cancellation/failure restoration before disposal |
| src/AcKrovy.AutoCAD/Infrastructure/RoofPhysical3DHostDiagnostics.cs | Include LENGTHEN in existing automatic read-only native event/member checkpoints |
| src/AcKrovy.Core.Tests/RoofOrdinaryLengthenLifecycleTests.cs | Focused portable geometry/physical/signature tests and adapter guards |
| docs/ORDINARY_LENGTHEN_LIFECYCLE_2026-10-06.md | CODE result and sequential manual HOST plan |

## Diagnostics and HOST evidence boundary

ROOF_ORDINARY_LENGTHEN_COMMAND_STATE records begin/collected/processed/end,
cancel/fail and disposed phases with one commandId, candidate/processed,
AUTO/Independent/mixed counts, and pending rollback/refresh counts. At disposed,
all snapshot collections and classification counts are zero. Deferred graphics
requests transfer by value to the existing refresh service rather than staying
in this context.

ROOF_ORDINARY_LENGTHEN_LIFECYCLE records owner, Line/Solid, authority state,
actual Start/End/None endpoint, planChanged, collinear, decision and preference
behavior, detach/rollback/rebuild, before/after IDs and designations, v2 source,
GROUP verification and terminal result. Validation failures identify both-end,
non-collinear or degenerate input. No command-level success is inferred from
a native prompt mode or presumed ObjectModified order.

Existing read-only NATIVE_BEGIN, NATIVE_EVENT_SUMMARY/member checkpoints and
after-maintenance evidence now include LENGTHEN. The actual target-host event
sequence remains to be checked by the user's manual HOST run; portable/source
guards cannot prove native callback ordering, transactions, grip graphics,
DWG persistence, or visual behavior. No persistence/undo architecture was
changed to compensate for an assumed event sequence.

## Original LENGTHEN CODE validation (before the 2942 migration correction)

Root: C:/Users/Roman/Documents/CODEX/C#/CsharpProjects/ACAD_krovy.
Branch main; HEAD cb8f2ae225c7d0ddfc9b60ca5fb4e8200ef6493a.
The working tree was already dirty. Nothing was staged, reverted or committed.

Commands executed:

```powershell
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore --filter FullyQualifiedName~RoofOrdinaryLengthenLifecycleTests -warnaserror -m:1 -nr:false
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore --filter 'FullyQualifiedName~RoofOrdinary|FullyQualifiedName~RoofIndependentOrdinary|FullyQualifiedName~RoofGeneratedMember' -warnaserror -m:1 -nr:false
./scripts/compatibility-gate.ps1 -Full
dotnet build src/AcKrovy.AutoCAD/AcKrovy.AutoCAD.csproj --no-restore -c Debug -p:Platform=x64 -warnaserror -m:1 -nr:false
git -c core.safecrlf=false diff --check
```

| Check | Result |
|---|---|
| Focused LENGTHEN | 28/28 PASS |
| Affected Ordinary/Independent/Generated members, including ROTATE/STRETCH/GRIP | 569/569 PASS |
| Final full Core | 7652/7652 PASS; 0 failed/skipped |
| Final full WPF | 831/831 PASS; 0 failed/skipped |
| Full Compatibility Gate | PASS |
| Portable Compatibility Gate | PASS, executed inside Full Gate |
| Final standard Debug x64 AutoCAD 2027 build | PASS; 0 warnings, 0 errors |
| Diff check after documentation | PASS |
| Central product/package version | 0.23.0, consistent and unchanged by this task |
| Additional schema/version bump | None |

The 28 focused tests validate native endpoint results for all four modes,
fixed endpoints, exact section/frame/projection, free-end boundary semantics,
repeated serialized v2 edits, strict translation-only TryRebase, rejected
both-end/yaw/inversion/degenerate geometry, no-op behavior, warning preference
decisions and designation reuse/split/merge. Adapter guards protect the common
accept/restore/persistence path, logical deduplication, abort/disposal, GROUP
exclusion, complete-generator coexistence and forbidden legacy writes. They
are not automated AutoCAD HOST tests.

## Build/startup protocol

The real acad.exe process list was checked outside the sandbox before build
work, before Full Gate, and immediately before the final adapter build.
AutoCAD was NOT RUNNING; a save grace window was not needed.

Final standard output:
C:/Users/Roman/Documents/CODEX/C#/CsharpProjects/ACAD_krovy/src/AcKrovy.AutoCAD/bin/x64/Debug/net10.0-windows/AcKrovy.AutoCAD.dll

After the successful build, AutoCAD Architecture 2027 was launched with exactly
C:/Users/Roman/Documents/3d.dwg. A passive process check confirmed the
window title AutoCAD Architecture 2027 - [3d.dwg] and Responding=True.
The agent stops without AK_RUNTIME_BUILD,
hash/timestamp/file-length/runtime-version checks or native LENGTHEN input.
Runtime freshness verification and precise native selections belong to the
user for this task.

## HOST failure: legacy Independent 2942 during mixed LENGTHEN

The user's native HOST run changed two members in one command: AUTO 2AD6/2B56
and Independent 2942/2A22, ID 369c1cdb71ad488f870b272942bf670f, K3. One shared
confirmation was shown and YES was selected. Independent state was missing
(missing_extension_dictionary); migration failed with
IndependentPackageMigration:CurrentPhysicalCutsOrPlanPlacementCannotBeReproduced.
Both members rolled back atomically, physicalRebuild=False, and GROUP stayed
canonical. This is the latest observed HOST result: FAIL. That correct atomic
rollback policy remains unchanged.

The read-only existing HOST log contains the current Plan:
(47809.501492803,10609.41891628256,0) →
(47809.501492803,13359.41891628256,0). It also records the member's earlier
45-degree frame/eave origin at (40559.501492803,10609.41891628256,3000), while
the later live roof context has a different placement. Provenance is
Face0/station8. These observations distinguish the current member package from
the current owner roof.

## Shared migration correction

The exact failing Core branch was the member-local fallback in
RoofOrdinaryPhysicalBuildStateMigrationRules.TryRecover. After optional
retained context failed to replay the body, it tried only Free/Eave/Ridge
cut combinations. It could not capture a plumb structural-side cut offset from
the logical Plan endpoint. This is a supported existing Ordinary physical
shape, not an invalid package solely because it moved away from the original
roof.

A portable reproduction uses the supplied 2942 Plan coordinates, 45-degree
member plane, 80x160 section and a current Hip-ended body placed away from the
live owner. Before the fix it failed with the identical HOST reason. It now
passes without owner context and reproduces every current body vertex. This
is a regression for the uncovered failure class; it is not a fresh measurement
of the live Solid 2A22 or a substitute for the exact HOST retest.

The same centralized migration now measures the current inward-facing plumb
end plane and its offset from the logical endpoint. It represents them using
existing v2 member-local topology and structural-side-plane data. The support
edge is centered on the logical endpoint rather than being limited to the
short physical top edge. Elevation and frame come from the current measured
member plane. No live AUTO member/station or historical boundary is required.

Acceptance still requires exact current-body replay within the existing
0.01 mm tolerance. Unsupported, mismatched or ambiguous data is not accepted.
Horizontal width is checked explicitly. Missing/ambiguous pairing, invalid
existing records, unusable frame and zero Plan remain guarded by the existing
capture/migration path. Current Plan, world placement and measured section
remain the baseline. No new persistence system/schema or weakening of TryRebase
was added. Common accepted transactions still persist v2 once, preserve the
Independent ID, and keep the member outside AUTO GROUP.

Diagnostics now include the measured migration frame, upper-axis point,
measured section dimensions, vertex counts and retained-context availability.
The new successful migration resolution is
MeasuredMemberStructuralCutsVerified. The lifecycle source says unavailable
when the snapshot build state is actually unavailable, avoiding a misleading
persistent_member_xrecord label on failure.

## Files changed by this migration correction

| File | Change |
|---|---|
| src/AcKrovy.Core/Services/Roofs/RoofOrdinaryPhysicalBuildStateMigrationRules.cs | Recover measured offset plumb cut in the existing v2 model; retain body replay checks |
| src/AcKrovy.AutoCAD/Infrastructure/RoofIndependentOrdinaryPhysicalStateService.cs | Compact current-package migration evidence |
| src/AcKrovy.Core.Tests/RoofIndependentOrdinaryBuildStateMigrationTests.cs | 2942 failure-class regression, both directed ends, v2 roundtrip and subsequent edits, unsafe inputs |
| src/AcKrovy.AutoCAD/Infrastructure/RoofOrdinaryLengthenLifecycleService.cs | Correct unavailable-state diagnostic label; no lifecycle control-flow change |
| docs/ORDINARY_LENGTHEN_LIFECYCLE_2026-10-06.md | Record HOST FAIL, migration correction, CODE results and minimal retest |

## Latest executed validation after the migration correction

| Check | Result |
|---|---|
| New failure-class test before correction | Expected FAIL: CurrentPhysicalCutsOrPlanPlacementCannotBeReproduced |
| Focused migration, persistence routing and LENGTHEN | 63/63 PASS |
| Final affected Ordinary/Independent/Generated regression set | 576/576 PASS |
| Final full Core | 7659/7659 PASS; zero failed/skipped |
| Final full WPF | 831/831 PASS; zero failed/skipped |
| Full Gate and nested Portable Gate | PASS |
| Final standard Debug x64 AutoCAD 2027 build | PASS; 0 warnings, 0 errors |
| Final diff/whitespace checks | PASS |
| Product/schema bumps in this correction | None |

Executed commands:

```powershell
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore --filter FullyQualifiedName~Host2942_MovedHipEndPackageRecoversWithoutHistoricalRoofBoundary -warnaserror -m:1 -nr:false
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore --filter FullyQualifiedName~RoofIndependentOrdinaryBuildStateMigrationTests -warnaserror -m:1 -nr:false
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore --filter 'FullyQualifiedName~RoofIndependentOrdinaryBuildStateMigrationTests|FullyQualifiedName~RoofIndependentOrdinaryPersistenceRoutingTests|FullyQualifiedName~RoofOrdinaryLengthenLifecycleTests' -warnaserror -m:1 -nr:false
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore --filter 'FullyQualifiedName~RoofOrdinary|FullyQualifiedName~RoofIndependentOrdinary|FullyQualifiedName~RoofGeneratedMember' -warnaserror -m:1 -nr:false
./scripts/compatibility-gate.ps1 -Full
dotnet build src/AcKrovy.AutoCAD/AcKrovy.AutoCAD.csproj --no-restore -c Debug -p:Platform=x64 -warnaserror -m:1 -nr:false
git -c core.safecrlf=false diff --check
```

The regression suite includes previous K15 migration, LENGTHEN, STRETCH,
GRIP and ROTATE. New tests check replay without provenance, offset cut at
either directed end, serialized v2 completion, subsequent LENGTHEN then
STRETCH and a true rigid ROTATE, strict MOVE rebasing, section/frame retention,
and rejection of altered body/section/frame/Plan data. Routing guards cover
shared persistence, identity, GROUP exclusion and mixed command atomicity.
Native DWG persistence and mixed acceptance remain HOST verification items.

AutoCAD was verified NOT RUNNING before the gate and final standard build;
no save window was needed. The existing output path is unchanged. AutoCAD
Architecture 2027 was launched with Documents/3d.dwg; the agent then stopped
without entering native commands.
No AK_RUNTIME_BUILD, hashes, timestamps, file-length or runtime-version checks
are executed. Runtime freshness belongs to the user.

## Minimal HOST retest — stop at first FAIL

User verifies runtime freshness first. Finish the native LENGTHEN session
with Enter.

A. One LENGTHEN Delta +200 session on legacy Independent 2942 plus one untouched
AUTO Ordinary → YES. Expect candidateCount=2, processedCount=2, autoCount=1,
independentCount=1 and one confirmation. 2942 must show
buildStateSource=migrated_member_package, migrationPersisted=True and the same
Independent ID. AUTO receives a new ID. Both must report physicalRebuild=True,
rollback=False, result=pass, with canonical GROUP. The new resolution may be
MeasuredMemberStructuralCutsVerified when the measured offset cut is used.

B. Immediately LENGTHEN 2942 again. Expect storedStateResolution=valid,
buildStateSource=persistent_member_xrecord, no migration, same Independent ID,
and result=pass.

C. AK_ROOF_EDIT → Apply. Complete AUTO output returns; edited Independents
stay at their accepted world positions outside AUTO GROUP. No suppression;
canonical GROUP.

## Current final status

ORDINARY RAFTER LENGTHEN
CODE: PASS ✅
HOST: PASS ✅
Legacy Independent migration defect: CLOSED ✅
Lifecycle defect: CLOSED ✅

Authoritative user HOST closure covers Delta positive/negative, Start/End,
Total, Percent, Dynamic, AUTO YES/NO, repeated Independent edits, mixed
AUTO + Independent with one confirmation, legacy v2 migration followed by
persistent_member_xrecord on the second edit, and AK_ROOF_EDIT rebuild
coexistence. Earlier failure and retest sections above remain historical.

No commit, push or tag.



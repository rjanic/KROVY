# Independent Ordinary physical state — migration and persistence

2026-10-06. CODE PASS; manual AutoCAD HOST checkpoint A–D PASS.
No commit, push or tag. Existing unrelated WIP preserved.

## Confirmed root cause

HOST member `2AFC` / `2B7C`, K15, Independent ID
`53612e706aa8491b81b21b453af91fa8`, DetachedFromAuto, provenance Face0/station5:
no physical-state extension dictionary. Historical AUTO provenance cannot rigidly
rebase to its already-edited axis (endpoint delta mismatch approximately 606 mm).
The working `2AF9` detach retains valid v2 state and subsequent STRETCH succeeds.
This supplied HOST evidence authorizes a member-state persistence/migration fix.

## Final checkpoint contract

The original failure was a real Independent Ordinary edit failure, not a
designation or lifecycle-routing failure. The Plan2D member `2AFC` and paired
solid `2B7C` had a stable Independent ID but no physical build-state Xrecord.
The old recovery path tried to rebase historical AUTO provenance onto the
already-edited Plan2D axis; the approximately 606 mm endpoint mismatch made
that recovery reject the edit and roll it back.

The fix persists one complete version-2 physical build package for every new
Independent Ordinary creation path and performs a read-only lazy migration for
the legacy package above. Migration is accepted only when the existing builder
reproduces the current solid, so it cannot invent a guessed cut or silently
simplify geometry. The migrated package is written once in the accepted edit
transaction; later edits and save/reopen use the persistent package directly.

The lifecycle contract is: Plan2D is authoritative, Physical3D is derived,
Independent ownership never returns to the AUTO slot, provenance is recovery
context only, `IndependentMemberId` is stable instance identity, and
`ElementId` remains signature-derived. Independent members stay outside the
canonical AUTO GROUP. No `Suppressed`, geometry `ManualOverride`, legacy
`AttachedManual` fallback, or new identity is introduced by migration.

## Creation-path audit

Y = present, N = missing. The before columns describe the source before this change,
not a claim that all paths were independently HOST-retested.

| Creation path | Independent identity before | Physical state before | Accepted Plan before | Section/frame before | Final persistence route/result |
|---|---|---|---|---|---|
| MOVE detach | Y | N | N | N | Detach captures original full context, rigidly rebases accepted MOVE, reads actual section, common Persist: v2 complete |
| GRIP_STRETCH detach | Y | Y | Y | Y | Accept supplies rebuilt state to Detach; common Persist: v2 complete |
| STRETCH detach | Y | Y | Y | Y | Same Accept/Detach route: v2 complete |
| single-end TRIM detach | Y | Y | Y | Y | Same Accept/Detach route: v2 complete |
| EXTEND detach | Y | Y | Y | Y | Same Accept/Detach route: v2 complete |
| split TRIM, retained and new parts | Y | Y | Y | Y | ClaimSplitLine/CreateSplitMember → Accept for each result → common Persist |
| BREAK, retained and new parts | Y | Y | Y | Y | Same split/Accept engine → common Persist |
| BREAKATPOINT, both parts | Y | Y | Y | Y | Same engine with touching pieces → common Persist |
| COPY, AUTO or Independent source | Y | Y | Y | Y | CreateMember/prepared copy → Accept → common Persist |
| MIRROR Erase No | Y | Y | Y | Y | Shared clone engine/prepared reflection → Accept → common Persist |
| MIRROR Erase Yes, clone replacement or in-place | Y | Y | Y | Y | Same accepted clone engine; source-erasure/event routing unchanged → common Persist |
| whole-roof clone/rebind and legacy AttachedManual recovery | N/A | N/A | N/A | N/A | Do not create new Ordinary Independent identity; existing ownership routing unchanged |
| old/incomplete Independent package | already Y | N | N | N | New read-only package reconstruction, then common Persist in accepted geometry transaction |

Audit of all production `RoofIndependentOrdinaryTimberStore.Write/TransferFromRoof`
call sites found three Plan identity creation engines: Detach, ordinary split and
ordinary copy/mirror. Remaining writes bind derived Solids/annotations or refresh
existing annotation identity. No other production Independent creation/migration
writer was found. Native cloning itself is not treated as authoritative state completion.

## One existing v2 persistence model

`RoofIndependentOrdinaryPhysicalStateService.Persist` is now the only production
caller writing the physical build-state store. It checks Plan identity, absence of
Generated/AttachedManual ownership, current timber section and accepted Plan axis;
it completes v2/frame, writes the existing Xrecord, checks readback and unchanged
Independent identity. No competing schema/record was introduced.

The Xrecord retains topology/plane/pitch/eave datum, section, accepted axis, measured
frame, boundary/cut settings and structural cut sources. Material remains in existing
generic timber metadata because the physical builder does not consume material.

MOVE detach receives its original Plan by subtracting the accepted translation,
captures the full state and uses the unchanged rigid `TryRebase`. Physical geometry
and ownership transfer retain their existing routes. Other detach commands supply
their already-rebuilt accepted state. Existing Independent MOVE also saves its rebased
state and actual BRep frame; designation and persistent ID remain unchanged.

## Conservative lazy migration

1. Only Independent Plan members without the specific build-state Xrecord enter migration.
   An unrelated extension dictionary is allowed; corrupt/unsupported existing physical
   records fail with `ExistingXRecordInvalid` rather than being silently overwritten.
2. Capture remains read-only. Require a unique Solid with the same Independent ID,
   current section dimensions and no conflicting Generated/AttachedManual ownership.
3. Read opposite BRep face pairs at width/height dimensions. Select the uniquely
   directed orthonormal frame whose longitudinal projection matches current Plan;
   height points upward. No current AUTO station supplies the frame.
4. Optional persisted owner topology/elevation/cuts are candidate context only.
   They do not require the current AUTO inventory, recipe or station. A candidate
   is fitted to measured member elevation/frame/current Plan, then accepted only
   when the existing physical builder reproduces current body vertices within 0.01 mm.
5. When owner context is absent or incompatible, reconstruct a member-local plane
   and measured end boundaries using the same v2 topology/cut model. Conservative
   free/perpendicular, vertical and ridge-meet end combinations are checked by exact
   current-body replay. Complex cuts require safely recoverable verified context.
6. No reconstruction branch calls or relaxes provenance `TryRebase`.
7. On successful accepted edit, normal physical rebuild runs and common Persist
   writes the **final accepted** v2 state in that transaction. Decline/cancel/failure
   does not commit a migration. Next command and save/reopen read the Xrecord normally.

Failures name missing identity/pair, invalid record, BRep/frame/projection ambiguity,
unavailable complex cut context or non-reproducible physical cuts/Plan placement.
No guessed Solid or silently simplified cuts are persisted.

## Diagnostics

`ROOF_ORDINARY_PHYSICAL_BUILD_STATE` now shows, for migrated geometry edits:

```text
storedStateResolution=missing_extension_dictionary|missing_xrecord
buildStateSource=migrated_member_package
migrationResolution=RetainedContextVerifiedAgainstCurrentPackage|MeasuredMemberPlaneAndCutsVerified
migrationPrepared=True
migrationPersisted=True
fullBuildStateResolved=True
sectionFrameResolved=True
failure=none
scope=build-state-resolution
result=pass
```

`migrationPersisted=True` is emitted inside the accepted transaction; the later normal
lifecycle diagnostic proves commit/rollback outcome. It alone is not a HOST product verdict.
Subsequent commands must show `storedStateResolution=valid`, `storedVersion=2` and
`buildStateSource=persistent_member_xrecord`. Actual frame, axis, cuts and diagnostic
inputs remain available from the previous instrumented path.

## Files changed for this task

| File | Change |
|---|---|
| Core/Services/Roofs/RoofOrdinaryPhysicalBuildStateMigrationRules.cs | New neutral completion and conservative current-body replay recovery |
| AutoCAD/Infrastructure/RoofIndependentOrdinaryPhysicalStateService.cs | New common v2 persistence and read-only migration adapter |
| AutoCAD/Infrastructure/RoofIndependentOrdinaryDetachService.cs | Capture/save missing MOVE state; accept supplied rebuilt state |
| AutoCAD/Infrastructure/RoofOrdinaryGripLifecycleService.cs | Independent migration branch, accepted shared persistence, migration diagnostics |
| AutoCAD/Infrastructure/RoofOrdinaryLogicalMoveService.cs | Persist rebased actual frame after accepted Independent MOVE |
| AutoCAD/Infrastructure/RoofOrdinaryPhysicalSectionFrameReader.cs | Hint-free, measured legacy Independent frame resolution |
| AutoCAD/Infrastructure/RoofOrdinaryPhysicalBuildStateStore.cs | Read-only HasRecord distinguishes absence from invalid records |
| AutoCAD/Infrastructure/RoofOrdinaryRafterSolidMaterializationService.cs | Expose existing structural context resolver internally for optional verified cut evidence |
| Core.Tests/RoofIndependentOrdinaryBuildStateMigrationTests.cs | 18 neutral geometry/recovery/roundtrip/creation cases |
| Core.Tests/RoofIndependentOrdinaryPersistenceRoutingTests.cs | 10 host-routing guards, identity/group/legacy-state boundaries |
| Core.Tests/RoofOrdinaryGripLifecycleSourceContractTests.cs | Follow centralized physical-state writer |
| docs/INDEPENDENT_ORDINARY_BUILD_STATE_MIGRATION_2026-10-06.md | This audit/report |
| .ai/handoffs/independent-build-state-migration-2026-10-06/full-gate.log | Final gate evidence |

Source entries are under `src/AcKrovy.*`. The Core `TryRebase`, horizontal-width
frame rules, canonical GROUP collector, designation rules and command routing are unchanged.

## Verification

Working copy: `C:/Users/Roman/Documents/CODEX/C#/CsharpProjects/ACAD_krovy`.
Branch main; HEAD `cb8f2ae225c7d0ddfc9b60ca5fb4e8200ef6493a`.

- Initial focused migration cases: 18/18 PASS.
- Focused Ordinary/Independent lifecycle group: 189/189 PASS.
- Final Full Compatibility Gate, including Portable Gate: PASS.
- Full Core: 7605/7605 PASS, zero skips/failures.
- Full WPF: 831/831 PASS, zero skips/failures.
- Warnings-as-errors build: 0 warnings, 0 errors.
- `git diff --check`: PASS.
- AutoCAD process absence checked before every build/gate. No 15-second save window
  was required because no acad.exe process was running.

Commands run:

```powershell
git rev-parse --show-toplevel
git branch --show-current
git rev-parse HEAD
Get-Process -Name acad -ErrorAction SilentlyContinue
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore --filter FullyQualifiedName~RoofIndependentOrdinaryBuildStateMigrationTests -warnaserror -m:1 -nr:false
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore --filter 'FullyQualifiedName~RoofIndependentOrdinary|FullyQualifiedName~RoofOrdinaryGripLifecycle|FullyQualifiedName~RoofOrdinaryCopyLifecycle|FullyQualifiedName~RoofOrdinaryMirrorLifecycle|FullyQualifiedName~RoofOrdinaryLogicalMove|FullyQualifiedName~RoofOrdinaryTrim|FullyQualifiedName~RoofOrdinaryBreak|FullyQualifiedName~RoofOrdinaryExtend' -warnaserror -m:1 -nr:false
./scripts/compatibility-gate.ps1 -Full
dotnet build src/AcKrovy.AutoCAD/AcKrovy.AutoCAD.csproj --no-restore -c Debug -p:Platform=x64 -warnaserror -m:1 -nr:false
git -c core.safecrlf=false diff --check
```

## HOST validation supplied for this checkpoint

The following results are manual HOST observations supplied by the user from
AutoCAD Architecture 2027. They are recorded as HOST evidence; they are not
inferred from source-contract tests.

| Case | Evidence | Result |
|---|---|---|
| A. Legacy `GRIP_STRETCH` on `2AFC`/`2B7C` | `storedStateResolution=missing_extension_dictionary`; `buildStateSource=migrated_member_package`; `migrationResolution=RetainedContextVerifiedAgainstCurrentPackage`; `migrationPrepared=True`; `migrationPersisted=True`; `storedVersion=2`; `fullBuildStateResolved=True`; `failure=none`; `state=Independent`; `decision=NONE`; `rollback=False`; `physicalRebuild=True`; same ID `53612e706aa8491b81b21b453af91fa8` | PASS |
| B. Subsequent normal `STRETCH` | `storedStateResolution=valid`; `buildStateSource=persistent_member_xrecord`; `storedVersion=2`; `storedRebaseResolved=True`; `fullBuildStateResolved=True`; `failure=none`; `rollback=False`; `physicalRebuild=True` | PASS; no second migration |
| C. Opposite-side non-rigid `STRETCH` | Frame/rebuild pass; `ElementId` `K15→K16` from the changed signature; Independent ID stable; `designationChanged=True`; GROUP `166/166`, duplicates `0`, missing `0`, foreign `0`, canonical `True` | PASS |
| D. SAVE/REOPEN then `GRIP_STRETCH` | `storedStateResolution=valid`; `buildStateSource=persistent_member_xrecord`; `storedVersion=2`; `fullBuildStateResolved=True`; `failure=none`; `rollback=False`; `physicalRebuild=True`; no second migration; ID stable | PASS |

These cases establish the reported legacy migration, subsequent persistent
editing, signature-based designation change, GROUP canonicality, and reopen
durability for the affected member. They do not claim that every unrelated
TRIM, EXTEND, BREAK, COPY, MIRROR, ERASE, or whole-roof workflow received a
fresh native HOST run in this checkpoint; those paths are covered here by the
creation-path/source audit and automated routing tests.

## Remaining OPEN items

There is no open defect for the reported `2AFC` legacy package or the A–D
checkpoint. Broader native HOST coverage for every creation path, native BRep
edge/cut variants outside the verified migration cases, and independent
UNDO/REDO behavior remain separate validation work. Unsupported or ambiguous
legacy cuts continue to fail conservatively with a diagnosed reason while
preserving the member geometry and identity.

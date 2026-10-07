# Ordinary native ROTATE lifecycle — implementation report

2026-10-06. CODE PASS. The HOST lifecycle is now CLOSED with PASS; see the
[final checkpoint](ORDINARY_RAFTER_ROTATE_LIFECYCLE_2026-10-06.md) for the
user-accepted HOST matrix and final product contract.
No commit, push or tag. Earlier unrelated uncommitted WIP is preserved.

## Contract and architecture

Native AutoCAD ROTATE supplies the final Line geometry. The existing command-start
Ordinary snapshot captures Plan2D, paired Physical3D, owned annotations, metadata,
full physical build state, and canonical GROUP baseline. At CommandEnded the
Ordinary first claim runs before legacy roof/manual edit recovery. No custom
ROTATE command, geometry UX, persistence record, schema, or migration is introduced.

`RoofOrdinaryRotateLifecycleService` collects actual final Plan geometry and
native modified IDs. It groups by logical Plan member, so mixed Plan2D + Solid
selection rebuilds one body. AUTO members with a changed roof source remain on
the existing whole-roof path; Independent edits keep member authority even when
their provenance roof changes in the same command. The adapter does not assume ObjectModified order; command-state
and lifecycle diagnostics expose the actual affected member set on the HOST run.

All affected members, across roof owners, use one shared detach decision and one
acceptance transaction. The existing `MemberWarningPreferenceService` supplies
the approved wood dialog, do-not-show-again behavior, and automatic YES when
`ConfirmAutomaticMemberDetach=false`. Existing Independent members do not prompt.
Locked AUTO roofs remain protected by the existing lock semantics.

Accepted geometry calls `RoofOrdinaryGripLifecycleService.Accept`, then shared
designation recalculation and package/GROUP verification before commit. AUTO
receives a new Independent ID through the existing detach service; Independent
keeps its ID. The common physical-state service writes the existing v2 Xrecord.
Legacy missing state uses the existing read-only package migration. `TryRebase`
is unchanged and is not made to accept rotations.

Plan is normalized to Z=0. The existing full physical builder receives the
final Plan axis and persistent member context, maintaining the approved
horizontal width, upward height, exact section, and right-handed frame.
The adapter never world-Z-rotates a pitched Solid. A planar rotation can change
the resulting true/cutting length on the member plane; normal signature rules
handle that change. An unchanged manufacturing signature preserves ElementId.

NO restores every native-mutated package and exact GROUP baseline atomically.
No acceptance, designation, Independent identity, or v2 write is committed.
If any acceptance step fails, that transaction aborts and a separate atomic
rollback restores all affected packages. The claim remains terminal and does
not fall through to legacy override or recovery paths.

Physical3D-only ROTATE restores the package without detach, Plan changes,
identity changes, or GROUP repair. Warning ON/OFF changes only post-commit UX.
The existing deferred refresh is reused after rollback. ROTATE's historical
full-drawing annotation fallback is disabled when Ordinary first claim has
handled a package, preventing a NO snapshot from being refreshed again.

Canonical AUTO GROUP excludes every Independent package. No suppression or
geometry ManualOverride is written. Existing generator/rebuild behavior stays
authoritative: explicit Apply recreates the original AUTO slot and leaves the
rotated Independent where the user placed it.

## Files changed by this task

| File | Change |
|---|---|
| `src/AcKrovy.Core/Services/Roofs/RoofGeneratedMemberEditCommandRules.cs` | Route ROTATE through Ordinary snapshot/first claim and physical reconciliation |
| `src/AcKrovy.Core/Services/Roofs/RoofOrdinaryRotateRules.cs` | Neutral final-Plan length/rotation validation; no base-point reconstruction |
| `src/AcKrovy.AutoCAD/Infrastructure/RoofOrdinaryGripLifecycleService.cs` | Shared snapshot dispatch, ROTATE counts, end/cancel/fail/disposed cleanup |
| `src/AcKrovy.AutoCAD/Infrastructure/RoofOrdinaryRotateLifecycleService.cs` | Command-wide decision, atomic accepted/rollback processing, compact diagnostics |
| `src/AcKrovy.AutoCAD/Infrastructure/RoofAssemblyGroupSyncService.cs` | Expose existing read-only canonicality check internally; no repair change |
| `src/AcKrovy.AutoCAD/Infrastructure/LiveGeometrySynchronizationService.cs` | Prevent historical annotation fallback from revisiting terminal Ordinary ROTATE |
| `src/AcKrovy.Core.Tests/RoofOrdinaryRotateLifecycleTests.cs` | Focused neutral geometry/signature/preference tests and adapter source guards |
| `docs/ORDINARY_ROTATE_LIFECYCLE_2026-10-06.md` | Report and manual HOST plan |

No change to product/schema versions, warning UX/resources, physical frame math,
v2 migration, AUTO recipe, suppression policy, or existing command acceptance.

## Automated coverage and its limits

19 focused ROTATE tests cover 15°, 37°, 90°, -20°, 180°, off-axis native base
points, final Z normalization, actual prism section dimensions, directed
physical/Plan alignment, horizontal width, upward right-handed height,
repeated v2 updates and serialization roundtrip, strict unchanged translation
rebase, unchanged/changed manufacturing signature, and warning preference rules.

Source guards cover AUTO/Independent/mixed/physical-only dispatch, one command
confirmation, multi-owner transaction scope, exact NO/failure snapshot restore,
common detach/persistence/migration, package uniqueness, GROUP exclusion,
terminal first claim, disposed transient counts, generator recipe authority,
and forbidden legacy override writes. They guard production routing; they are
not native AutoCAD DB/event/Undo or visual proof. Existing Ordinary tests supply
the shared builder, detach, designation, rebuild coexistence, and migration
regressions. Actual HOST cases A–M remain subject to the manual plan below.

## CODE validation

Working root: `C:/Users/Roman/Documents/CODEX/C#/CsharpProjects/ACAD_krovy`.
Branch `main`; HEAD `cb8f2ae225c7d0ddfc9b60ca5fb4e8200ef6493a`.
Working tree was already dirty; this task does not stage or revert earlier WIP.

Commands executed:

```powershell
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore --filter FullyQualifiedName~RoofOrdinaryRotateLifecycleTests -warnaserror -m:1 -nr:false
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore --filter 'FullyQualifiedName~RoofOrdinary|FullyQualifiedName~RoofIndependentOrdinary|FullyQualifiedName~RoofGeneratedMember' -warnaserror -m:1 -nr:false
./scripts/compatibility-gate.ps1 -Full
dotnet build src/AcKrovy.AutoCAD/AcKrovy.AutoCAD.csproj --no-restore -c Debug -p:Platform=x64 -warnaserror -m:1 -nr:false
git -c core.safecrlf=false diff --check
```

The first sandboxed focused test run could not connect to testhost and aborted.
The same tests were then run with authorized host-process access and passed.
No failed test assertion was hidden. Full Gate includes the Portable Gate.

| Check | Final result |
|---|---|
| Focused ROTATE tests | 19/19 PASS |
| Affected Ordinary/Independent/Generated member tests | 541/541 PASS |
| Final full Core | 7624/7624 PASS; zero failures/skips |
| Final full WPF | 831/831 PASS; zero failures/skips |
| Full Gate including Portable Gate | PASS; architecture/restore/build/tests/version checks |
| Standard Debug x64 AutoCAD 2027 build | PASS; 0 warnings, 0 errors |
| Diff check | PASS |
| Schema/product version changes | None |
| AutoCAD absence before each host build/gate | Verified acad.exe NOT RUNNING; no save window needed |

The final build output is exactly
`C:/Users/Roman/Documents/CODEX/C#/CsharpProjects/ACAD_krovy/src/AcKrovy.AutoCAD/bin/x64/Debug/net10.0-windows/AcKrovy.AutoCAD.dll`.
AutoCAD was launched afterward with the exact requested `Documents/3d.dwg`.
No runtime/hash/timestamp/file-length verification or native ROTATE was performed
by the agent. Runtime freshness and the native HOST results remain user-owned.

## Historical manual HOST plan

The following was the pre-test plan. Its former OPEN verdict is superseded by
the final checkpoint linked above, which records the user's completed HOST
results. Retain this plan as historical implementation context.

Environment: AutoCAD Architecture 2027, standard Debug x64 net10 output,
`C:/Users/Roman/Documents/3d.dwg`. User validates runtime freshness before edits.
The agent does not execute native HOST ROTATE, AK_RUNTIME_BUILD, hash/timestamp,
or file-length checks. Do not save the test DWG unless the user intends to.

Use an unlocked roof for AUTO acceptance. Record the target Line/Solid handles,
Independent ID/ElementId where present, geometry and GROUP before each case.
Stop the sequence on the first FAIL.

1. AUTO Plan2D ROTATE -> YES: rotate one Line by 37° around a clear native base
   point. Check final XY/Z=0, one Independent package/new ID, valid persisted v2,
   rebuilt 80x160 body with horizontal W, annotations, correct designation, and
   canonical GROUP with the whole Independent package excluded. Also test
   confirmation OFF: no modal, `automaticConfirm=True`, same accepted behavior.
2. Another AUTO Plan2D ROTATE -> NO: compare exact pre-command geometry, physical
   body, annotations, Generated metadata, ElementId and GROUP. No Independent or
   v2 Independent state survives. Check visible rollback without manual REGEN.
3. Existing Independent ROTATE: no detach modal; same ID, persistent_member_xrecord,
   valid updated v2 and one body. Repeat negative/90° rotation and inspect section,
   annotations, designation and GROUP exclusion. Include a legacy missing-v2
   member if available; migration occurs once through the shared path.
4. Physical3D-only ROTATE: check exact Solid restore, unchanged Plan/ID/GROUP and
   no detach. Repeat with WarnDerived3DEdit ON and OFF; final model must match.
5. Mixed Plan2D + paired Solid ROTATE: final Plan wins, one lifecycle record/member,
   one rebuild/body and no Physical3D-only warning for that member.
6. Multi-member ROTATE: select several AUTO plus an Independent, preferably across
   owners. One AUTO confirmation; YES gives unique new IDs and preserves existing
   Independent ID. Repeat on other AUTO members with NO: exact atomic rollback.
   Verify one record per member and disposed transient counts zero.
7. AK_ROOF_EDIT -> Apply: the original AUTO slot is regenerated and the rotated
   Independent remains exactly in its accepted position, outside AUTO GROUP.
   Verify no suppression, overrides, or AttachedManual fallback.

Expected diagnostics: `ROOF_ORDINARY_ROTATE_COMMAND_STATE` begin/collected/processed/
end/disposed, and one `ROOF_ORDINARY_ROTATE_LIFECYCLE` per changed logical member.
Accepted: physicalRebuild=True, rollback=False, correct identity/designation,
groupCanonical=True, all forbidden-write fields False, terminalHandled=True,
result=pass. NO: detached=False, rollback=True, physicalRebuild=False,
unchanged identity/designation. Physical-only: decision=NONE, no detach or rebuild.
At disposed all candidate/processed/classification counts must be zero.

## Historical scope notes

The implementation-time plan did not claim SAVE/REOPEN or UNDO/REDO verification
from portable tests. Broader UNDO/REDO and the native ROTATE Copy option remain
separate from the in-place lifecycle HOST closure; see the final checkpoint.

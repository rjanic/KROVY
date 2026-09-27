# Locked source ERASE: duplicate GROUP slot — 2026-09-27

Implementation, automated validation, and AutoCAD Architecture 2027 HOST acceptance complete.
Existing WIP is retained, including AK_ROOF_PURLINS. No commit/push/reset/stash/clean.

## HOST evidence and its limits

Local primary evidence: `%LOCALAPPDATA%/ACAD_KROVY/Logs/ACAD_KROVY-20260927.log`.
The user also supplied AutoCAD command diagnostics not written to that file.

| Time / checkpoint | Owner / group handle | Raw / unique / source slots |
| --- | --- | --- |
| 09:45:43 manual audit before FIRST rejected ERASE | 293C / 2950 | 6 / 6 / 1 |
| 09:46:22 manual audit after FIRST rejected ERASE | 293C / 2950 | 7 / 6 / 2 |
| 09:57:39 CommandWillStart, SECOND rejected ERASE | 293C / 2950 | 7 / 6 / 2 |
| 09:57:39 CommandEnded-before-maintenance | 293C / 2950 | 6 / 6 / 1; sourceLive=False |
| Repair GROUP_SYNC_POST / SOURCE_ERASE_REPAIR (user transcript) | 293C / 2950 | 6 / 6 / 1; same source ObjectId/handle |
| 09:57:39.7075702 locked-source-repair-committed | 293C / 2950 | 7 / 6 / 2; sourceLive=True |
| 09:57:39.9670304 CommandEnded-after-maintenance | 293C / 2950 | 7 / 6 / 2 |

The definitive fresh drawing trace subsequently establishes the FIRST duplicate:

| Clean drawing checkpoint | Owner / canonical group | Raw / unique / source slots |
| --- | --- | --- |
| 10:11:40.7050823 native-clone-rebind-committed:COPY | 293C / 2950 | 6 / 6 / 1 |
| 10:11:41.0708775 CommandEnded-after-maintenance:COPY | 293C / 2950 | 6 / 6 / 1 |
| 10:11:48.4343000 manual audit | 293C / 2950 | 6 / 6 / 1 |
| 10:12:22.9011504 CommandWillStart:FIRST ERASE | 293C / 2950 | 6 / 6 / 1 |
| In-transaction GROUP_SYNC_POST / persistence / SOURCE_ERASE_REPAIR (user transcript) | 293C / 2950 | 6 / 6 / 1; same source ObjectId/handle |
| 10:12:23.2397404 locked-source-repair-committed | 293C / 2950 | 7 / 6 / 2 |
| 10:12:23.3523237 CommandEnded-after-maintenance:ERASE | 293C / 2950 | 7 / 6 / 2 |
| 10:12:29.4000275 manual audit | 293C / 2950 | 7 / 6 / 2 |

These are separate drawing runs; reused handles do not mean the second run started
with the earlier drawing's duplicate. The clean trace proves the FIRST extra slot is
present immediately after the source repair transaction commits, before deferred
maintenance. The repeated-ERASE trace independently shows the same boundary. The
confirmed application defect is validating and reporting a provisional group snapshot
as final, without validating/correcting the committed membership.

The managed repair path calls source `Erase(false)`, display/group synchronization and
`transaction.Commit()`. The observations do not isolate which internal Group/entity
reactor operation produces the extra slot. We do not claim that `Group.Append` or
`Erase(false)` alone is proven to do so. A minimal standalone probe was built to compare
unerase-only, opening Group ForWrite, and synchronization, but AutoCAD 2027 CoreConsole
returned ErrorStatus=53 during startup before the command ran. Its API reproduction is
INCONCLUSIVE; no exact internal Autodesk callback mechanism is asserted. Probe source
and script remain available for an isolated new GUI drawing test.

The same persistent group handle excludes canonical group replacement. Duplicate owner
group names in the audit are produced when its group-to-member index adds the same
name once per raw member slot. They are not evidence of two dictionary groups.
Physical children remain 4 Face + 4 Eave + 4 Hip + 1 Ridge and display remains five Lines
for both roofs. The user accepted the prior physical-3D correction; it is not changed here.

## HOST 9 acceptance matrix

The final acceptance was run in real AutoCAD Architecture 2027 against the accepted
Debug x64 build. The internal AutoCAD API callback that creates the transient extra raw
slot remains unproven and is intentionally not named as a confirmed cause.

| Scenario | Result | Stable evidence |
| --- | --- | --- |
| Clean whole-roof COPY | PASS | New owner has one canonical group, 6/6/1, five display Lines and 13 physical children; original unchanged |
| Clean whole-roof MIRROR | PASS | New owner has one canonical group and independent ownership; original unchanged |
| First rejected locked-source ERASE after COPY | PASS | Source ObjectId/handle and groupId=2950 preserved; committed transient 7/6/2 is reduced by indexed source-slot removal to stable 6/6/1 |
| First rejected locked-source ERASE after MIRROR | PASS | Same committed 7/6/2 → 6/6/1 correction; `Recovered|ok`; five display Lines and 13 physical children |
| Repeated rejected ERASE | PASS | Stable 6/6/1 and `Recovered|ok` on subsequent attempts; no additional raw slot |
| EDIT after rejected ERASE | PASS | Copy and mirrored owner edit successfully with one current generated set and no cross-owner deletion |
| COPY EDIT 45° → U → REDO | PASS | Verified separately; source roof remains unchanged |
| Original roof during all copy/mirror/recovery cases | PASS | Source identities, physical set, and ownership remain unchanged |
| MIRROR EDIT 45° → U → REDO | Not reported | Requires a separate HOST run |
| Save/reopen after this fix | Not reported | Requires a separate HOST run |
| Plan2D/Both/Model3D plus purlin fixture in this final run | Not reported | Earlier physical/eaves acceptance remains historical evidence; repeat if release coverage requires it |

The accepted stable invariant is one persistent canonical group per owner with raw
members=6, uniqueMembers=6, sourceSlots=1 for the no-timber fixture. The production
collector still supports larger groups with purlins, timber, and annotations.

## Narrow correction

The existing recovery validates membership while the source opened for write is being
unerased inside a transaction. Its canonical six-member view is insufficient evidence
of the committed database state. The finalizer therefore runs only for successfully
restored locked source ObjectIds, after that transaction has committed AND been disposed,
while the original document lock and native ERASE undo scope remain active.

It reads the committed canonical GROUP through the existing collector/inspection rules.
The existing canonical group ObjectId is captured and must remain unchanged. For the
confirmed source-only duplication, the unique member set must already exactly equal
the full collector's expected set and every surplus slot must reference that source.
Only the source's surplus indices are removed in descending order with `Group.RemoveAt`.
The first source slot and every unique member are retained, without any Append or Clear.
The resulting raw membership is checked, the group transaction is closed, and a fresh
read transaction checks actual persisted membership. No Idle callback or undo/redo
repair is scheduled. The finalizer explicitly rejects UNDO/REDO and non-ERASE commands.
An already canonical group receives no membership writes or commit.

Missing/foreign unique members or other duplicated children fail closed without commit;
this narrowly scoped correction does not remove a unique member or repair unrelated
damage. The generic COPY/MIRROR/EDIT `EnsureGroup` production membership logic remains
unchanged. The index-specific removal contract is documented by Autodesk's
[Group.RemoveAt overload reference](https://help.autodesk.com/cloudhelp/2019/ENU/OARX-ManagedRefGuide/files/OREFNET-__OVERLOADED_RemoveAt_Autodesk_AutoCAD_DatabaseServices_Group.html)
and the method compiled against the installed AutoCAD 2027 SDK. HOST execution of the
correction is still required, particularly for repeated ERASE and native U/REDO.

The expected set comes from the existing full assembly collector, including owned
purlins, timber and annotations. Six is the no-timber fixture expectation, not a new
global hardcoded limit. Definition schema5, elevation store schema2 and all physical
geometry/elevation/visibility data remain unchanged.

The earlier `ROOF_SOURCE_ERASE_REPAIR` is now explicitly `Recovered|provisional-group`.
Only successful verification after the group transaction has closed emits `Recovered|ok`.
An internal committed-but-not-finalized snapshot may show7/6/2; accepted stable command
completion and explicit audit must show6/6/1. Programmatic group work stays inside the
existing LiveResize suppression scope, so it cannot queue maintenance recursion.

## Instrumentation

Opt-in `AK_ROOF_3D_TRACE` now records exact Append/Remove group and member handles and
raw/distinct member handle lists. Checkpoints include transaction entry/before unerase,
after unerase, group sync before mutation/before append/after sync, before commit,
immediately after source repair commit, group finalization before/after/committed and
after deferred command maintenance. Manual `AK_ROOF_3D_AUDIT` remains read-only.
Canonical groups are also audited when their sole source slot is temporarily absent,
using the exact dictionary key rather than requiring source membership for discovery.

Database-scoped trace lookup now compares native database identities (`UnmanagedObject`),
not managed wrapper reference identity. The enabled HOST trace previously showed only
the Document overload's post-commit counts; internal transaction checkpoints were absent.
The wrapper check could skip them. This diagnostic correction is DEBUG-only and makes
no database mutations. Every final count must be checked after transaction closure.

## Exact files changed by this task

- `src/AcKrovy.AutoCAD/Infrastructure/RoofLiveResizeService.cs`: group finalization after repair disposal, current ERASE scope.
- `src/AcKrovy.AutoCAD/Infrastructure/RoofAssemblyGroupSyncService.cs`: indexed source-slot finalizer, preserved group ID and committed verification.
- `src/AcKrovy.AutoCAD/Infrastructure/RoofDisplayGroupService.cs`: opt-in mutation/group checkpoints only; production membership policy unchanged.
- `src/AcKrovy.AutoCAD/Infrastructure/RoofPhysical3DHostDiagnostics.cs`: native DB identity and exact group member/mutation trace.
- `src/AcKrovy.Core/Services/Roofs/RoofAssemblyGroupMembershipRules.cs`: CAD-neutral exact-source surplus index planner.
- `src/AcKrovy.Core.Tests/RoofLockedSourceGroupFinalizationTests.cs`: provisional-versus-committed arithmetic, repeated recovery, indexed removal and full assembly retention.
- `src/AcKrovy.Core.Tests/RoofLockedSourceGroupFinalizationSourceContractTests.cs`: closed-transaction verification, command guards, no geometry generator and trace contracts.
- `docs/ROOF_GROUP_LOCKED_ERASE_FIX_2026-09-27.md`: this handoff.
- `ErrorReports/GroupUneraseProbe/Probe.cs`, `Probe.csproj`, `probe.scr`: isolated API diagnostic source/script; final build PASS, runtime INCONCLUSIVE before command execution.

## Validation

| Check | Result |
| --- | --- |
| Branch / HEAD / upstream | main / d4d4fcff0cdfb3c548af40f17aac6ae00c6029f9 / origin/main; no ahead/behind shown |
| Working tree | Dirty before task; existing WIP preserved |
| Focused Core | PASS 78/78 |
| Full Core | PASS6667/6667 |
| Focused / full WPF | PASS463/463 and803/803 |
| Debug / Release x64 | PASS, 0 warnings/errors |
| Portable Gate | PASS: restore/build/tests/architecture, 0 warnings/errors |
| Full Gate | PASS: restore/solution build/Core6667/WPF803, 0 warnings/errors |
| Localization / schemas | No changes |
| Git diff check | PASS; line-ending conversion notices only |
| HOST correction acceptance | PASS: AutoCAD Architecture 2027 COPY/MIRROR, first/repeated locked-source ERASE, EDIT, and COPY EDIT → U → REDO |
| Isolated native API reproduction | INCONCLUSIVE: CoreConsole startup ErrorStatus=53 before probe command |

Core model/source-contract tests do not execute native ERASE/COPY/MIRROR or prove
AutoCAD callback ordering. AutoCAD/CoreConsole were checked closed before CAD builds.

Commands run:

```powershell
dotnet build AcKrovy.sln -c Debug -p:Platform=x64 -warnaserror -m:1 --no-restore
dotnet build AcKrovy.sln -c Release -p:Platform=x64 -warnaserror -m:1 --no-restore
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj -c Debug --no-build --no-restore --filter "FullyQualifiedName~RoofLockedSourceGroupFinalization|FullyQualifiedName~RoofAssemblyGroupMembership|FullyQualifiedName~RoofPhysical3DLockedSourceErase|FullyQualifiedName~RoofCanonicalGroup|FullyQualifiedName~RoofDisplayGroup|FullyQualifiedName~RoofWholeRoofMirrorGroup" --logger "trx;LogFileName=group-erase-focused-core.trx"
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj -c Debug --no-build --no-restore --logger "trx;LogFileName=group-erase-full-core.trx"
dotnet test src/AcKrovy.Wpf.Tests/AcKrovy.Wpf.Tests.csproj -c Debug -p:Platform=x64 --no-build --no-restore --filter "FullyQualifiedName~AutomaticPurlin|FullyQualifiedName~HipRoof" --logger "trx;LogFileName=group-erase-focused-wpf.trx"
dotnet test src/AcKrovy.Wpf.Tests/AcKrovy.Wpf.Tests.csproj -c Debug -p:Platform=x64 --no-build --no-restore --logger "trx;LogFileName=group-erase-full-wpf.trx"
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Portable
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Full
git diff --check
dotnet build ErrorReports/GroupUneraseProbe/Probe.csproj -c Debug -warnaserror -m:1 --no-restore
# Diagnostic attempt, from ErrorReports/GroupUneraseProbe:
& 'C:\Program Files\Autodesk\AutoCAD 2027\accoreconsole.exe' /s 'C:\Users\Roman\Documents\CODEX\C#\CsharpProjects\ACAD_krovy\ErrorReports\GroupUneraseProbe\probe.scr' /l en-US
```

## Minimal AutoCAD 2027 HOST retest of the final build

Restart AutoCAD and NETLOAD:
`C:/Users/Roman/Documents/CODEX/C#/CsharpProjects/ACAD_krovy/src/AcKrovy.AutoCAD/bin/x64/Debug/net10.0-windows/AcKrovy.AutoCAD.dll`.
Use Debug for the opt-in audit/trace. Final SHA256:
`E8963FE035448FACF6F229F348110D821EC944B145C288E99511775CEF59455F`.
Host-local timestamp: 2026-09-27 10:26:14; length6710784 bytes.

1. Restart AutoCAD with the new Debug x64 DLL. Enable TRACE before CREATE/COPY, not
   after the first failure. Create a supported 10000×6000 roof at30°, Both, physical
   enabled; audit O. COPY the whole roof and audit C. MIRROR No to create M, audit.
   Each owner has one canonical group, raw6/unique6/sourceSlots1, five plan Lines and
   13 physical children. Record source ObjectIds/handles, group handles and all child
   handles/signatures. Use an eave WCS Z3000 fixture to retain the established geometry
   checks (ridge≈4732.050808 at30°, Z6000 at45°, length4000).
2. Lock C using `AK_ROOF_LOCK`, select only its source with `AK_ROOF_SELECT_SOURCE`,
   perform FIRST ERASE and audit. Repeat two more times. Capture raw membership before/
   after unerase, `GROUP_MUTATION`, pre-commit and immediately committed group states,
   `locked-source-group-finalize-before`, `...after:provisional`, `...committed`, and
   `CommandEnded-after-maintenance:ERASE`. Final/manual counts must be6/6/1 and native
   source and group identities unchanged. Any transitional7/6/2 must be corrected by
   the current command; no duplicate may persist. Repeat on M and directly created O.
   `GROUP_MUTATION operation=remove-at-source-slot` must target only the restored
   source's surplus index. Confirm the final `ROOF_SOURCE_ERASE_REPAIR result=Recovered|ok`
   follows committed finalization; the earlier marker is intentionally provisional.
3. EDIT only C and M 30→45→30→45; audit each time. Verify group6/6/1, one current physical
   set13, five flat plan children, exact independent owner XData and no stale signatures.
   Other roofs retain their exact pre-command physical handles/geometry/visibility.
4. Exercise U/UNDO/REDO/MREDO after protected ERASE, COPY/MIRROR and EDIT. Capture source/
   group identity, group counts, child counts and DBMOD/REDO availability at completed
   boundaries. No plugin repair/sync/diagnostic database access at native undo/redo
   boundaries. Explicit manual audits run only after each boundary finishes.
5. Repeat Plan2D/Both/Model3D, save/reopen, and a fixture with existing AK_ROOF_PURLINS.
   Native source grips/explicit selection and all physical eaves remain correct. The
   full collector's expected membership (possibly greater than six with timber) must
   retain purlins and annotations. Inspect group IDs to distinguish extra groups from
   repeated slots. No shared layers, other owners or geometric-guess cleanup may change.

Verdict: automated validation PASS and HOST 9 acceptance PASS for the scenarios listed
above. The exact internal Autodesk trigger remains unproven; the committed-state
finalizer is the confirmed correction. The combinations marked “Not reported” remain
outside this acceptance evidence.

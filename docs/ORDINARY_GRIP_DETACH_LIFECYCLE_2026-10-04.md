# Ordinary GRIP_STRETCH detach lifecycle — 2026-10-04

Automated verdict: **PASS**. AutoCAD HOST retest: **PENDING / INCONCLUSIVE**.
No commit, push, tag or version change. Existing repository WIP was retained.

## Evidence and defect

Source: `C:\Users\Roman\AppData\Local\ACAD_KROVY\Logs\ACAD_KROVY-20261004.log`,
21:39:44–21:39:46, owner `2912`, line `2940`, `Rafter:Face0:6`.
The command-start/native-ended checkpoints prove a non-rigid Start endpoint edit:

- Before: `(41591.02099635819,11478.696868240506,0)` → `(41591.02099635819,14478.696868240506,0)`.
- Native: `(43234.902248851795,10749.501585525788,0)` → unchanged End.
- Native `ObjectModified` summary: 3 events; source geometry remained unchanged.
- Legacy `ROOF_ORDINARY_GRIP_FREEFORM`, recalc, accept and identity sync then ran.
- `K4` became `K8`; a geometry override was persisted; GROUP lost canonical membership.

The previous first-detach handler could decline ownership of a batch and fall through
to legacy acceptance. Its GRIP acceptance also explicitly invoked that legacy path.
The new route claims completed Ordinary GRIP edits before roof inspection and general
refresh. MOVE acceptance and physical correction implementations were not changed.

## Changes in this task

| File | Change |
| --- | --- |
| `src/AcKrovy.AutoCAD/Infrastructure/LiveGeometrySynchronizationService.cs` | Capture/dispose GRIP package snapshots; route and filter claimed packages before legacy resize/refresh. |
| `src/AcKrovy.AutoCAD/Infrastructure/RoofOrdinaryGripLifecycleService.cs` | New complete-command classifier, detach confirmation, direct-physical rejection, atomic snapshot rollback, terminal handling and diagnostics. |
| `src/AcKrovy.AutoCAD/Infrastructure/RoofOrdinaryPhysicalBuildStateStore.cs` | Versioned Xrecord on the Plan line, preserving full independent builder inputs without XData size constraints. |
| `src/AcKrovy.AutoCAD/Infrastructure/RoofOrdinaryRafterSolidMaterializationService.cs` | Read-only capture of existing Ordinary physical inputs and access to the existing solid materializer. |
| `src/AcKrovy.AutoCAD/Infrastructure/RoofGeneratedMemberManualEditService.cs` | Remove GRIP from MOVE's first-detach acceptance; fail closed for an unclaimed Ordinary GRIP instead of legacy freeform acceptance. AttachedManual and other command support remain. |
| `src/AcKrovy.Core/Services/Roofs/RoofOrdinaryGripLifecycleRules.cs` | CAD-neutral final-geometry classifier and persisted topology/section/plane/cut inputs; rebuild through `TryBuildSemanticMember`. |
| `src/AcKrovy.Core.Tests/RoofOrdinaryGripLifecycleTests.cs` | HOST numeric fixture; repeated independent endpoint edits; rigid frame rebasing; persisted cuts; invalid geometry; physical authority. |
| `src/AcKrovy.Core.Tests/RoofOrdinaryGripLifecycleSourceContractTests.cs` | First claim, no legacy acceptance/renumbering/recovery, rollback, identity, GROUP exclusion and warning routing guards. |
| `src/AcKrovy.Core.Tests/RoofIndependentOrdinaryHostSourceContractTests.cs` | Align outdated source assertions with separate GRIP routing and the existing MOVE implementation. |
| `src/AcKrovy.Core.Tests/RoofOrdinaryMoveCancelRollbackSourceContractTests.cs` | Correct source-method delimiters after removing GRIP acceptance; retain MOVE rollback assertions. |
| This report | Verification and the staged HOST retest plan. |

YES rebuilds Physical3D from complete Ordinary inputs, preserves the Plan handle and
ElementId, transfers ownership/annotations through the existing detach service, stores
member-owned build inputs and excludes exactly that package from GROUP. It does not
create a ManualOverride or perform numbering reconciliation. The new materialized BRep
vertices are checked against the Core physical member before commit.

NO restores captured entities, their XData and exact GROUP membership in one transaction.
Independent endpoint edits use the stored inputs without a detach prompt. A rigid final
translation rebases the entire physical context, including cut planes. Direct Physical3D
grips restore the command-start body and produce one aggregate wood-style warning.
An acceptance failure aborts its writes, attempts an exact snapshot rollback and claims
the package terminally, without generic recovery.

`ROOF_ORDINARY_GRIP_LIFECYCLE` records owner, line, solid, initial state, final edit
classification, planChanged, decision, detached, rollback, physicalRebuild,
manualOverrideWritten, terminalHandled and result.

## Commands and automated results

Branch: `main`. HEAD: `cb8f2ae225c7d0ddfc9b60ca5fb4e8200ef6493a`.
Working tree before and after: dirty with substantial pre-existing WIP.

```powershell
git status --short
dotnet build src/AcKrovy.AutoCAD/AcKrovy.AutoCAD.csproj --no-restore -c Debug -p:Platform=x64 -warnaserror -m:1 -nr:false
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore -warnaserror -m:1 -nr:false --filter 'FullyQualifiedName~RoofOrdinaryGripLifecycle|FullyQualifiedName~RoofOrdinaryLogicalMove|FullyQualifiedName~RoofOrdinaryMoveCancel|FullyQualifiedName~RoofIndependentOrdinary|FullyQualifiedName~RoofOrdinaryAuthorityTransition'
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Portable
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Full
git diff --check
```

| Check | Result |
| --- | --- |
| Focused GRIP/Independent/MOVE regressions | PASS, 53/53 |
| Debug x64 adapter build | PASS, 0 warnings / 0 errors |
| Portable Gate | PASS; Core 7415/7415, restore/build/dependency/version checks pass |
| Full Gate | PASS; Core 7415/7415, WPF 828/828, solution restore/build pass |
| Neutral-layer CAD dependency checks | PASS |
| Whitespace diff check | PASS; Git emitted only existing LF/CRLF conversion notices |
| AutoCAD closed during all builds/gates | Confirmed before execution |
| Release build | Not run; this task does not publish a release |
| Actual AutoCAD endpoint-GRIP YES retest | PENDING |

An initial sandboxed test run aborted because vstest could not connect to testhost.
The authorized tests were rerun with normal process communication. The initial build
API error and initial source-contract/fixture expectations were corrected before the
passing gates. No failed check is counted as PASS.

## HOST test plan — first YES only

Environment: AutoCAD 2027, Debug x64 KROVY 0.23.0, saved
`C:\Users\Roman\Documents\3d.dwg`, branch/HEAD above. Existing startup loader points
to `src\AcKrovy.AutoCAD\bin\x64\Debug\net10.0-windows\AcKrovy.AutoCAD.dll` and invokes
`AK_RUNTIME_BUILD` plus `AK_ROOF_3D_TRACE`. Do not load a second copy.

DLL built 2026-10-04 22:08:55 local time; SHA256:
`FE066120A8833B2DE7E56211A7E5C877C2147144D94CA0A115F68BEB573CC602`.

AutoCAD Architecture 2027 was opened with the saved drawing (PID 56420). Startup
log at 22:12:54–22:12:55 confirms plugin initialization, host version `26.0.0.0`,
runtime `.NET 10.0.12`, NETLOAD completion and `TRACE enabled=True build=0.23.0.0`.
The endpoint test and its clean canonical starting inventory have not yet been observed.

1. Confirm the loaded runtime path/build and a clean canonical GROUP (`181/181` for
   the supplied fixture). Record DBMOD and the chosen AUTO line/solid/ElementId.
2. Select one AUTO Ordinary Plan2D line, and drag exactly one endpoint so length
   and direction change. Do not select the entire roof GROUP or use MOVE.
3. Choose YES in the single existing wood-style detach dialog.
4. **STOP.** Do not run NO, Independent, middle-grip or direct-Physical3D tests yet.

Expected: native final XY retained, both Plan endpoint Z values zero, Independent
ownership on line/body/annotations with one shared IndependentMemberId, original
ElementId retained, rebuilt Physical3D matching the Core model, exact package exclusion
and canonical remaining GROUP, no new geometry overrides, no legacy recalc/accept/
identity-sync or generic recovery for the member. The detached package's size determines
the GROUP count reduction; do not assume a fixed number of annotation helpers.

Capture runtime identity, command line, `ROOF_ORDINARY_GRIP_LIFECYCLE`, existing detach
readbacks, before/after GROUP diagnostics, Plan endpoints, Physical3D BRep/frame audit,
annotations and DBMOD. A missing confirmation or failed physical/group readback is FAIL.

## Remaining risks and status limits

- Native GRIP callback timing, transient clones, `Solid3d.CopyFrom`, Xrecord persistence,
  annotation geometry and actual GROUP behavior require the staged AutoCAD retest.
  Automated source contracts and Core/WPF tests do not prove HOST PASS.
- NO, Independent, middle-grip, direct-Physical3D, SAVE/REOPEN and UNDO/REDO remain
  HOST untested and must wait for the first YES result. Undo boundaries perform no DB
  capture/rebuild; package snapshots are disposed on end/cancel/failure/disposal.
- An older Independent member without stored build inputs can be adopted only when
  its historical roof/key resolves an exact rigid canonical reference. Otherwise GRIP
  fails closed and restores the package; it does not guess a frame or use live native
  Solid geometry as semantic authority.
- Existing WIP remains uncommitted. No changes to the normative geometry contract,
  target frameworks, MOVE correction algorithm, release versions or localization.

HOST result: **INCONCLUSIVE / awaiting first YES execution**.

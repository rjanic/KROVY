# AttachedManual COPY durability and endpoint GRIP axis constraint

Date: 2026-10-03. Branch: `main`. HEAD: `cb8f2ae225c7d0ddfc9b60ca5fb4e8200ef6493a`.
The working tree already contained substantial WIP. This change preserves it; no reset,
revert, stash, clean, commit, push or tag was performed.

## COPY causal trace

Evidence: the user's supplied HOST sequence and the existing
`%LOCALAPPDATA%\ACAD_KROVY\Logs\ACAD_KROVY-20261003.1.log`.

The fresh log contains these observations:

- 14:25:35: source Plan `2942` is copied to `2A56`, and the native Solid has the
  same temporary Generated physical key `Rafter:Face0:8` as its source.
- The native `2A56` Plan has both endpoints at `Z=-162.1840056343167`.
- `ROOF_GENERATED_COPY ... clone=2A56 ... result=ok` appears before final maintenance.
- 14:25:36: `ROOF_MEMBER_ROLLBACK command=COPY ... error=InvalidOperationException`.
  The final checkpoint for `2A56` is `after=null`.
- 14:25:41: the subsequent copy `2A8E` has Plan `Z=0`; its final checkpoint retains
  AttachedManual UUID `26f4bf4318ed4f99a73ee0de473ff441` and a physical reference.

Source/control-flow reconstruction:

1. `CommandWillStart` captures the current COPY member mapping and assembly snapshot.
   `CaptureForCommand` clears the previous snapshot. COPY deliberately belongs to
   `IsAssemblySnapshotCommand` for clone ownership; this does not make it a supported
   Generated manual edit. No previous STRETCH pending state is needed.
2. `CommandEnded` runs `ProcessNativeMemberClones`. COPY promotion services commit nested
   transactions inside the outer member transaction. Their early acceptance/commit
   diagnostics do not prove that the outer transaction will survive.
3. The outer transaction builds the semantic physical model. In the observed raw-Z state,
   `RoofAttachedManualPhysicalBuilder.TryAppend` rejects the reference-bearing child:
   Plan endpoints must be at Z=0. The new portable regression explicitly reproduces
   this rejection and proves that accepting the same XY placement at Z=0 succeeds.
   The old HOST log records the exception type, not its exact message; identifying this
   particular validation as the cause is the code-side reconstruction, not a newly
   measured HOST exception message.
4. A failed physical reconcile aborts the outer transaction and invokes
   `RecoverNativeMemberFailure`. That method erases command-appended owned entities,
   including the new clone, then calls `TryRecoverGeneratedMembersOnly` using the COPY
   snapshot. That call emits the generated snapshot lookup/recovery probes. This is a
   synchronous failure path, not a separately scheduled native STRETCH command.
5. There was also an overly broad second route: residual Generated member notifications
   could enter `RoofLiveResizeService`'s manual-tamper branch because it used
   `IsAssemblySnapshotCommand`. `ProcessOwner` then treats COPY as unsupported and can
   run snapshot recovery. This gate now excludes COPY, with a second defensive guard at
   `ProcessOwners` before any owner transaction.

The fix normalizes and validates ordinary native COPY geometry before either promotion
service captures RelativeSegment or PhysicalReferenceSegment. Only appended clone plans
are written; retained source plans are skipped. The existing semantic reconciliation
then removes native copied solids as command collateral and creates one body under each
fresh AttachedManual UUID. Emergency handling of genuinely failed transactions remains;
STRETCH, GRIP_STRETCH, TRIM and EXTEND keep their existing recovery paths.

## AttachedManual GRIP acceptance

The same log shows `2A8E` changing from a vertical Plan to a diagonal Plan during
GRIP_STRETCH. The old refresh path accepted the native geometry and cleared its
PhysicalReferenceSegment.

`RoofAttachedManualGripRules.TryAccept` calls the existing
`TryClassifyAxisConstrainedEndpointGrip`. It projects the moved endpoint onto the
pre-command current axis, keeps the opposite endpoint fixed and discards lateral motion.
Generated override persistence and AttachedManual identity persistence stay separate.

The adapter retrieves the existing pre-command assembly snapshot and applies the accepted
Plan before numbering, annotations and physical reconciliation. The Core rule recomputes
RelativeSegment in the current exact anchor basis and applies the same endpoint delta to
the existing PhysicalReferenceSegment. Its displacement to the copied/moved Plan therefore
remains unchanged. UUID, child binding, owner, Origin and anchor key are preserved. An
already absent legacy physical reference remains absent; an existing reference is retained.
Midpoint rigid grips retain their placement path, and classic freeform STRETCH is unchanged.

Physical3D is rebuilt by the existing semantic solver, without rotating an existing solid.
Regressions compare Plan direction, Physical3D longitudinal direction, pitch and transverse
section frame before and after accepted grips, including repeated grips after MOVE.

## Changed files for this task

Existing WIP files extended:

- `src/AcKrovy.AutoCAD/Infrastructure/LiveGeometrySynchronizationService.cs`: COPY Plan
  normalization before promotion; final read-only DEBUG durability audit and failure reason.
- `src/AcKrovy.AutoCAD/Infrastructure/RoofLiveResizeService.cs`: exclude COPY from manual-tamper recovery.
- `src/AcKrovy.AutoCAD/Infrastructure/RoofGeneratedMemberManualEditService.cs`: defensive COPY guard;
  route failed AttachedManual acceptance through existing owner recovery.
- `src/AcKrovy.AutoCAD/Infrastructure/RoofAttachedManualLifecycleService.cs`: endpoint projection
  from the command snapshot; preserve/update reference; preserve identity and anchor.
- `src/AcKrovy.AutoCAD/Infrastructure/RoofGeneratedMemberManualEditDiag.cs`: kind, identity and
  axisBefore/axisAfter fields on `ROOF_ORDINARY_GRIP_CONSTRAINT`.

New files:

- `src/AcKrovy.Core/Services/Roofs/RoofOrdinaryCopyPlanRules.cs`.
- `src/AcKrovy.Core/Services/Roofs/RoofAttachedManualGripRules.cs`.
- `src/AcKrovy.Core.Tests/RoofAttachedManualCopyGripDurabilityTests.cs`.
- This report.

No schema, version, localization, CAD abstraction or runtime-target changes.

## Commands and automated verification

Commands ran from the repository root. `dotnet test`/Compatibility Gates were retried
outside the sandbox after a temporary-file `File.Replace` access failure was isolated.
The one isolated SafeFileWriter test passed there, followed by the complete suite. No
product file-writer change was needed. AutoCAD was confirmed closed before adapter builds.

```powershell
git status --short
git branch --show-current
git rev-parse HEAD
Get-Process acad -ErrorAction SilentlyContinue
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore -warnaserror -m:1 -nr:false --filter 'FullyQualifiedName~RoofAttachedManualCopyGripDurabilityTests|FullyQualifiedName~RoofOrdinaryGripAxisConstraintTests|FullyQualifiedName~RoofAttachedManualAnchorStabilityTests|FullyQualifiedName~RoofAttachedManualPhysicalLifecycleTests|FullyQualifiedName~RoofNativeCopyRoutingTests'
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-build --no-restore -warnaserror -m:1 -nr:false --filter 'FullyQualifiedName~RoofAttachedManual|FullyQualifiedName~RoofGeneratedRafterCopy|FullyQualifiedName~RoofNativeCopy|FullyQualifiedName~RoofOrdinaryGripAxis|FullyQualifiedName~RoofOrdinaryPhysicalReconciliation|FullyQualifiedName~RoofGeneratedMemberClassicStretch'
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore -warnaserror -m:1 -nr:false --logger 'trx;LogFileName=copy-grip-core-full.trx'
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-build --no-restore -warnaserror -m:1 -nr:false --filter 'FullyQualifiedName~SafeFileWriterTests'
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-build --no-restore -warnaserror -m:1 -nr:false --logger 'trx;LogFileName=copy-grip-core-full.trx'
dotnet test src/AcKrovy.Wpf.Tests/AcKrovy.Wpf.Tests.csproj --no-restore -p:Platform=x64 -warnaserror -m:1 -nr:false --logger 'trx;LogFileName=copy-grip-wpf-full.trx'
dotnet build src/AcKrovy.AutoCAD/AcKrovy.AutoCAD.csproj --no-restore -c Debug -p:Platform=x64 -warnaserror -m:1 -nr:false
dotnet build src/AcKrovy.AutoCAD/AcKrovy.AutoCAD.csproj --no-restore -c Release -p:Platform=x64 -warnaserror -m:1 -nr:false
pwsh -NoProfile -File scripts/acad-host-workflow.ps1 -Configuration Release -SkipTests -NoLaunch
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Portable
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Full
git diff --check
```

| Check | Result |
| --- | --- |
| Initial focused Core | PASS, 94/94 |
| Expanded COPY / AttachedManual / Generated GRIP / anchor / reconciliation / classic STRETCH focus | PASS, 188/188 |
| Core full | PASS, 7318/7318 after sandbox issue resolved |
| WPF full | PASS, 826/826 |
| Debug x64 warnings-as-errors | PASS, 0 warnings / 0 errors |
| Release x64 warnings-as-errors | PASS, 0 warnings / 0 errors |
| Portable Gate restore/build/tests/dependency leakage | PASS |
| Full Gate restore/build/solution tests | PASS; Core 7318/7318, WPF 826/826; 0 build warnings / 0 errors |
| git diff --check | PASS; Git also reports pre-existing LF/CRLF conversion notices |
| HOST retest of this build | NOT RUN |

## HOST test plan

Environment: AutoCAD Architecture 2027; current Debug x64 KROVY build, version 0.23.0;
saved fixture `C:\Users\Roman\Documents\3d.dwg`; branch/HEAD above. Use a separate
test drawing copy and verify `AK_RUNTIME_BUILD` before interpreting results.

Preconditions: unlocked ordinary Generated rafter, Physical3D enabled, native COPY
selection containing its Plan and associated Solid, DEBUG 3D tracing enabled.

1. COPY the Generated rafter once with the logged XY displacement and nonzero native Z.
2. Repeat COPY from the same Generated source. Verify source + both independent clones.
3. COPY an AttachedManual child, then MOVE that child and repeat diagonal endpoint grips.
4. Test END longitudinal, END diagonal, START diagonal and pure lateral endpoint grips.
5. Recheck Generated endpoint grips, classic freeform STRETCH and anchor-stability cases.

Expected results: every clone has a fresh UUID, independent Plan/annotations and exactly
one physical body; the source retains its identity/body. Final uniqueKeys=True and GROUP
canonical=True; no temporary native duplicate Generated physical key remains. Every
COPY final diagnostic reports planSurvived=True, physicalCount=1, recoveryClaimed=False,
result=ok. Grip axisAfter equals axisBefore, the fixed endpoint stays fixed, accepted
length follows the longitudinal projection, physical slope/section frame and moved
placement remain consistent. UUID, owner, Origin and anchor key do not change during grip.

Capture: DBMOD before/after, command/event trace through CommandEnded, `ROOF_COPY_FINAL`,
`ROOF_ORDINARY_GRIP_CONSTRAINT kind=AttachedManual`, final entity/annotation/physical counts,
GROUP audit and visual/coordinate checks for yaw and section twist. The old HOST log proves
the reported defects; it does not verify the modified build. The existing Core Console probe
documents that the full UI-bound document tracker cannot start there on this installation,
so it is not used to claim live command/grip validation.

## Verdict

The following verdicts describe automated Core/source-contract/build verification.
They do not claim post-fix AutoCAD command execution or visual HOST validation.

| Requested verdict | Code-side result |
| --- | --- |
| COPY ONE-COMMAND DURABILITY | PASS |
| REPEATED COPY | PASS |
| ATTACHEDMANUAL GRIP AXIS CONSTRAINT | PASS |
| ATTACHEDMANUAL PHYSICAL3D NO-YAW | PASS |
| GENERATED GRIP REGRESSION | PASS |
| CODE-SIDE OVERALL | PASS |

HOST is INCONCLUSIVE / NOT RUN until the plan above is executed with this build.

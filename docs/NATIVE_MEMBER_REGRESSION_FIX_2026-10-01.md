# Native member regression package — 2026-10-01

Code-side validation: PASS. New HOST validation: NOT RUN. No commit, push, tag,
release, or DWG writes were performed. Existing unrelated WIP was preserved.

## Final report

1. **Root cause and actual HOST evidence.** Supported native member results were
   not consistently classified/claimed before fallback. In the existing
   `%LOCALAPPDATA%\ACAD_KROVY\Logs\ACAD_KROVY-20261001.log`, MIRROR mappings at
   08:34 precede native append/modify events, but `RoofLiveResizeService.Process`
   invokes generated-tamper repair and `command-misclassified` before the later
   mirror handler. Separately, the first BREAK at 08:35:25 is on an AttachedManual
   Copy member: its appended segment retains the source UUID and no split accept
   occurs. `ClassifyModifiedGeneratedChildren` resolved that owner but counted
   only Generated/StructuralGenerated timber changes, excluding AttachedManual.
   These are two concrete gaps in existing routing, rather than a new framework.

2. **COPY causality.** The previous COPY correction gave COPY early ownership but
   left MIRROR after generic recovery. That incomplete ordering explains MIRROR's
   observed loss. It does not establish that COPY changed BREAK command gates:
   both BREAK names already share the supported split gate. The subsequent
   Generated BREAK at 08:35:36 reaches `ROOF_GENERATED_SPLIT result=ok` and semantic
   accept, then restore fails with snapshot K4/live K8. Inference from the log and
   strict builder validation: the prior unhandled AttachedManual split's duplicate
   UUID prevents physical reconcile, and fallback follows numbering. Subsequent
   BREAKATPOINT rejects the already duplicated Generated Face0:7 key left by that
   failed recovery. No assertion of a new HOST pass follows from this diagnosis.

3. **Before/after ordering.** Before: native snapshot/mapping → whole-roof rebind
   → early COPY → live resize/semantic member-or-generic recovery → timber refresh
   → late MIRROR. After: snapshot/mapping → whole-roof rebind → shared early
   COPY/MIRROR `ProcessNativeMemberClones` → remove claimed results → live resize
   for remaining changes (including existing supported BREAK/member handling) →
   generic refresh/fallback for remaining work. Whole-roof source geometry retains
   authority; member clone handling requires unchanged source geometry.

4. **Claiming results.** The existing command snapshot consumption remains keyed
   by clone ObjectId. The common route returns owned appended results, unchanged
   mapped sources, accepted modified ordinary Plan2D members, restored Hip/Valley
   references, restored derived bodies, and exact erased replacement sources.
   Modified/appended queues and erased-source handle queues are filtered before
   generic processing. MIRROR inputs are restricted to the proven unchanged-source
   owner scope. Erased derived bodies are claimed only for known changed semantic
   keys; their geometry is never read as authority. Missing-source annotations
   are cleaned inside the same transaction. A failed handler still invokes the
   existing snapshot recovery; genuine tamper protection is not disabled.

5. **COPY preservation.** Existing AttachedManual reinitialization, Generated
   promotion, copy-preserving presentation, unchanged-source validation,
   Physical3D planning, and GROUP verification remain in the same transaction.
   Single, mixed, Multiple, repeated-event, and Copy-of-Copy/Split tests pass.
   Native commands cannot execute clipboard adoption again. User-provided HOST
   COPY success is the reference evidence; COPY was not rerun in HOST here.

6. **MIRROR No.** Reuses `RoofMirrorCloneDetachService` before fallback. Generated
   source/key/geometry remain; final mirrored clone becomes a new AttachedManual
   UUID with a derived body. Native copied solids are collateral. Unchanged source
   body bindings remain; ElementId renumbering is not treated as logical identity.
   Automated routing/model coverage passes; native retest remains required.

7. **MIRROR Yes.** Existing exact, unambiguous source→replacement mapping and
   `TryRebindGeneratedReplacement` retain the original Generated logical key and
   persist its geometry override. Existing in-place ordinary handling is reused.
   Claimed source erasures cannot fall through to recovery and resurrect the old
   member/body. Original AttachedManual UUID retention remains supported. Ambiguous
   replacement mappings still fail safely instead of inventing a second identity.

8. **BREAK one point.** Existing deterministic Generated retained-fragment rule,
   AttachedManual promotion and shared physical reconciliation remain. Owned
   AttachedManual edits now reach the same service as Generated edits. No Solid3d
   is accepted as source. An AttachedManual fragment uses its inherited exact
   pre-command ChildIdentity binding when selecting its source, rather than a
   collinear Generated sibling. Retained UUID survives repeated splits.

9. **BREAKATPOINT.** Same `IsBreakCommand` / `IsSplitCommand`, snapshot, promotion,
   and physical reconciliation route as BREAK. Explicit BREAKATPOINT and native
   prefix regression cases pass. There is no second implementation.

10. **BREAK two points.** Existing gap semantics remain; both final segments have
    distinct semantic physical identities and the removed middle portion does
    not leave the original full-length body. Tests include Generated and
    AttachedManual Copy/Split sources. Unchanged AttachedManual candidates are
    skipped, avoiding promotion/physical work from incidental modification events.

11. **Physical3D invariant.** Plan2D → persisted semantic state → existing builder
    → common reconciliation planner. Both retained and added split members have
    one body; changed/replaced old bodies and collateral copies are removed.
    Unchanged Generated model members/body bindings are preserved in focused tests.
    No physical elevation, cuts, ridge overlap, or Hip/Valley geometry was changed.

12. **Unique keys.** Duplicate inherited UUIDs remain invalid for physical
    materialization until semantic split promotion assigns the fresh UUID. New
    tests explicitly reject this transient input and prove canonical final keys.
    Existing duplicate Generated/Structural/physical protection is preserved.

13. **GROUP.** Existing owner-scoped sync and physical/GROUP verification remain
    inside command transactions. Tests verify final membership and physical key
    canonicality separately; an unchanged baseline count alone is insufficient.
    No HOST member/entity counts or handles are hardcoded in production logic.

14. **Undo/Redo.** Existing command undo grouping, snapshot cancellation, and
    zero-database-access U/UNDO/REDO/MREDO guards are preserved. Codec/planner tests
    replay the same serialized identities and accepted geometries, including
    repeated AttachedManual splits. These are semantic snapshot tests, not native
    AutoCAD Undo/Redo execution. Native undo/redo is part of remaining retest.

15. **Compact TRACE.** Existing native event summaries, mappings, changed member
    checkpoints, physical reconciliation, uniqueKeys and GROUP summaries remain.
    The existing clone accept line adds classification and claimed-result counts.
    Full explicit `AK_ROOF_3D_AUDIT` stays available. Successful supported routes
    bypass generic command-misclassified/unsupported recovery; fallback diagnostics
    remain for actual failures. Normal trace inventory verbosity was not expanded.

16. **Files changed by this task (prior WIP in these files is retained).**
    - `src/AcKrovy.AutoCAD/Infrastructure/LiveGeometrySynchronizationService.cs`
    - `src/AcKrovy.AutoCAD/Infrastructure/RoofLiveResizeService.cs`
    - `src/AcKrovy.AutoCAD/Infrastructure/RoofGeneratedMemberManualEditService.cs`
    - `src/AcKrovy.Core.Tests/RoofNativeCopyRoutingTests.cs`
    - `src/AcKrovy.Core.Tests/RoofSupportedNativeMemberRoutingTests.cs` (new)
    - `src/AcKrovy.Core.Tests/RoofAttachedManualPhysicalLifecycleTests.cs`
    - `src/AcKrovy.Core.Tests/MonopitchRafterStage2D4AClipboardTests.cs`
    - `src/AcKrovy.Core.Tests/MonopitchRafterStage2D4BClipboardTests.cs`
    - This report. No Core production sources, trace inventory service, mirror
      semantic engine, UI, build helper, or unrelated WIP files changed in this task.

17. **New/changed tests.** Thirteen new cases: seven adapter-route/clipboard-gate
    cases and six AttachedManual split model/UUID/body/reload cases. Existing COPY
    routing expectations now protect common COPY/MIRROR early ownership. Three
    clipboard source assertions were updated from the removed `else if` spelling
    to the existing proven clipboard decision gate. Tests read real adapter routes;
    they do not simulate native AutoCAD transactions/events. Initial route run was
    RED (8 failed/2 passed). Initial full Core run found three stale clipboard
    assertions; final complete Core is green.

18. **Focused counts.** Final focused routing/split/physical/structural/clipboard
    set: 164/164 PASS. Expanded relevant lifecycle regressions: 796/796 PASS,
    including MOVE/TRIM/ERASE/STRETCH/GRIP_STRETCH, direct derived recovery,
    Hip/Valley protection, source resize, whole-roof COPY/MIRROR, numbering and
    undo guards. No skips.

19. **Core/WPF.** Core 6966/6966 PASS; WPF 806/806 PASS. Full Gate repeats Core
    successfully. The gate runs outside the sandbox for existing atomic temporary
    file replacement tests; those tests and their implementation are unchanged.

20. **Builds/gates/diff.** Final Debug x64 and Release x64 PASS, zero build
    warnings/errors. Full Compatibility Gate PASS, including its required Portable
    Gate, restores, dependency leakage checks and solution tests. `git diff --check`
    PASS; new files separately checked for whitespace. Branch main, HEAD
    `4f03788f7afb6f26e95dceff52e6e960e088706d`, local origin/main comparison 0/0.
    Dirty before/after, nothing staged or published. Localization/schema version
    changes: none in this task. Git LF→CRLF normalization notices are not
    diff whitespace errors.

21. **Remaining limitations.** New native HOST behavior has not been executed.
    Ambiguous replacements and malformed/pre-damaged metadata still fail closed.
    This fix does not silently repair previously corrupted saved DWGs or weaken
    restore identity checks. The secondary restore failure after numbering was
    observed in the dirty sequence; the primary missing AttachedManual split route
    is fixed, but recovery from unrelated damaged prestate remains a HOST risk.
    For this host-affecting change release verdict remains NOT READY pending HOST
    retest; publishing was not requested.

22. **Minimal sequential HOST retest.** Launch the existing saved
    `C:\Users\Roman\Documents\3d.dwg` using the existing Debug autoload workflow
    (AK_RUNTIME_BUILD, AK_ROOF_3D_TRACE). Test separately, in order:
    BREAKATPOINT → BREAK → MIRROR No → MIRROR Yes. Use Generated ordinary members
    for the requested primary sequence. Verify retained logical key/new split UUID,
    two canonical bodies after split, source retained/new UUID for Mirror No,
    original key/one body for Mirror Yes, uniqueKeys=True and canonical GROUP.
    Expect no command-misclassified or unsupported recovery on successful commands.
    Follow with COPY (single/mixed/Multiple), repeated BREAK of its AttachedManual
    result, and native UNDO/REDO. Request manual AK_ROOF_3D_AUDIT only if compact
    automatic summaries are missing/inconsistent. Do not save failing test state
    over the clean fixture. Launching is not a HOST PASS.

## Validation commands

```powershell
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore --filter 'FullyQualifiedName~RoofSupportedNativeMemberRoutingTests|FullyQualifiedName~RoofNativeCopyRoutingTests|FullyQualifiedName~RoofAttachedManualPhysicalLifecycleTests|FullyQualifiedName~RoofBreakAtPointRoutingTests|FullyQualifiedName~RoofStructuralRestoreNumberingOrderTests|FullyQualifiedName~RoofAttachedManualPhysicalAdapterSourceContractTests|FullyQualifiedName~MonopitchRafterStage2D4AClipboardTests|FullyQualifiedName~MonopitchRafterStage2D4BClipboardTests' -warnaserror -m:1 -nr:false
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-build --filter 'FullyQualifiedName~RoofSupportedNativeMemberRouting|FullyQualifiedName~RoofNativeCopyRouting|FullyQualifiedName~RoofStructuralRestoreNumberingOrder|FullyQualifiedName~RoofBreakAtPoint|FullyQualifiedName~RoofAttachedManual|FullyQualifiedName~RoofGeneratedMember|FullyQualifiedName~RoofMixedPhysicalStretch|FullyQualifiedName~RoofOrdinaryPhysicalStretch|FullyQualifiedName~RoofPhysical3D|FullyQualifiedName~RoofStructuralGenerated|FullyQualifiedName~RoofMirror|FullyQualifiedName~RoofWholeRoofMirror|FullyQualifiedName~RoofHipLiveResize|FullyQualifiedName~RoofWholeRoofCopy|FullyQualifiedName~RoofGeneratedRafterCopy|FullyQualifiedName~RoofAssemblyGroup|FullyQualifiedName~RoofUndo|FullyQualifiedName~LiveGeometry|FullyQualifiedName~TimberElementItemNumbering|FullyQualifiedName~MonopitchRafterStage2D4AClipboard|FullyQualifiedName~MonopitchRafterStage2D4BClipboard' -m:1 -nr:false
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-build -m:1 -nr:false
pwsh -NoProfile -File scripts/acad-host-workflow.ps1 -SkipTests -NoLaunch
dotnet build src/AcKrovy.AutoCAD/AcKrovy.AutoCAD.csproj --no-restore -c Release -p:Platform=x64 -warnaserror -m:1 -nr:false
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Full
git diff --check
git diff --no-index --check -- /dev/null src/AcKrovy.Core.Tests/RoofSupportedNativeMemberRoutingTests.cs
git diff --no-index --check -- /dev/null docs/NATIVE_MEMBER_REGRESSION_FIX_2026-10-01.md
pwsh -NoProfile -File scripts/acad-host-workflow.ps1 -SkipTests
```

The final Release build was repeated after the compact accept-line change.
`-Full` runs the Portable Gate internally; a separate duplicate run is unnecessary.

## Portable Compatibility Gate / Release Validation

| Check | Result |
| --- | --- |
| Branch / HEAD | main / 4f03788f7afb6f26e95dceff52e6e960e088706d |
| Upstream comparison | Local origin/main: ahead/behind 0/0 |
| Working tree before/after | Dirty; prior WIP retained; no staging/publication |
| Focused / expanded regressions | 164/164 / 796/796 PASS |
| Core / WPF | 6966/6966 / 806/806 PASS |
| Debug / Release x64 | PASS; zero build warnings/errors |
| Restore / portable dependency leakage | PASS |
| Portable / Full Gate | PASS; Portable included in Full |
| Localization/resource / new schema changes | Not applicable / none |
| Diff whitespace | PASS |
| New HOST execution / native Undo/Redo | NOT RUN |
| Release verdict | NOT READY pending actual HOST retest |

Existing dated audit reports, handoffs, ErrorReports, images, `.cursor` WIP and
earlier lifecycle source changes remain untouched and uncommitted. The saved HOST
DWG is opened after validation; no test roof or new drawing is created.

Launch confirmation: existing `acad-host-workflow.ps1 -SkipTests` completed its
Debug x64 build with zero warnings/errors. The initial sandboxed process had no
window/autoload activity and was closed by the helper. The helper was repeated
outside the sandbox: PID 60932 has window title `AutoCAD Architecture 2027 -
[3d.dwg]`; the new session log confirms NETLOAD and `TRACE enabled=True`.
No native member operation was executed by this task.

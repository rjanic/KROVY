# Ordinary BREAK Stage 1 — 2026-10-05

## Change

BREAK and BREAKATPOINT now enter the existing Ordinary Plan2D first-claim lifecycle. Both retained and appended native lines are read at command completion; no break-point/intersection geometry is recomputed. Appended lineage uses the same source metadata matching as the accepted TRIM split. All appended fragments are transferred to distinct Independent identities before retained-source acceptance can synchronize roof GROUP.

Each accepted result uses the existing full physical builder, horizontal-width section frame, annotation synchronization and current-signature designation reconciliation. AUTO single result detaches; AUTO split creates distinct Independent members. Independent retained result preserves its identity; appended results receive new identities. NO erases appended native results and restores the exact package snapshot. Cancel/fail uses the existing split rollback helper, including single-result endpoint changes, before context disposal.

The shared split validator accepts touching intervals only for BREAK, so BREAKATPOINT's two line entities become two timber pieces. TRIM's disconnected-interval rule remains intact. Overlapping/degenerate/out-of-source results are rejected. No persistence schema, undo architecture, physical frame or legacy ManualOverride contract was introduced.

Diagnostics: `ROOF_ORDINARY_BREAK_LIFECYCLE`, `ROOF_ORDINARY_BREAK_SPLIT`, `ROOF_ORDINARY_BREAK_COMMAND_STATE`. Split rejection reports `split=True`, one final member and no new Independent ids. Command-state reports appended evidence and clears it before `phase=disposed`. Native TRIM collection/helper names are retained internally to minimize changes to the accepted shared implementation.

## Changed files in this task

- `src/AcKrovy.Core/Services/Roofs/RoofGeneratedMemberEditCommandRules.cs`
- `src/AcKrovy.Core/Services/Roofs/RoofOrdinaryTrimSplitRules.cs`
- `src/AcKrovy.AutoCAD/Infrastructure/LiveGeometrySynchronizationService.cs`
- `src/AcKrovy.AutoCAD/Infrastructure/RoofOrdinaryGripLifecycleService.cs`
- `src/AcKrovy.Core.Tests/RoofOrdinaryBreakLifecycleTests.cs`
- `src/AcKrovy.Core.Tests/RoofOrdinaryTrimLifecycleTests.cs`
- `src/AcKrovy.Core.Tests/RoofIndependentOrdinaryHorizontalFrameTests.cs`
- `docs/geometry/roof-elevation-contract.md` — accepted two-piece BREAK product definition
- this report

Pre-existing uncommitted WIP is preserved. Repository root: `C:\Users\Roman\Documents\CODEX\C#\CsharpProjects\ACAD_krovy`; branch `main`; HEAD `cb8f2ae225c7d0ddfc9b60ca5fb4e8200ef6493a`.

## Commands and results

```powershell
git rev-parse --show-toplevel
git status --short
git branch --show-current
git rev-parse HEAD
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore --filter 'FullyQualifiedName~RoofOrdinaryBreak|FullyQualifiedName~RoofOrdinaryExtend|FullyQualifiedName~RoofOrdinaryTrim|FullyQualifiedName~RoofOrdinaryGripLifecycle|FullyQualifiedName~RoofIndependentOrdinary|FullyQualifiedName~RoofOrdinaryLogicalMove' -warnaserror -m:1 -nr:false
dotnet build src/AcKrovy.AutoCAD/AcKrovy.AutoCAD.csproj --no-restore -c Debug -p:Platform=x64 -warnaserror -m:1 -nr:false
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Portable
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Full
git diff --check
```

Targeted regressions: PASS 127/127. Geometry tests include both directions, gap/zero-gap split, invalid fragments and repeated Independent physical splitting on a sloped/yawed member. Routing/source guards cover AUTO/Independent accept, single/split rejection, capture/claim ordering, unique identities, cancel/fail disposal, repeated context creation and legacy exclusion. Source guards are not native HOST proofs.

## Portable Compatibility Gate

| Check | Result |
| --- | --- |
| Branch | `main` |
| HEAD | `cb8f2ae225c7d0ddfc9b60ca5fb4e8200ef6493a` |
| Working tree before | Dirty, prior authorized WIP |
| Restore | PASS |
| Build | PASS |
| Tests | PASS 7496/7496 |
| Warnings / errors | 0 / 0 |
| CAD API leakage | PASS |
| Localization changes | NOT APPLICABLE |

Verdict: PASS. Whitespace check: PASS.

Full Compatibility Gate: PASS. Solution restore/build succeeded with zero warnings/errors; Core 7496/7496 and WPF 828/828 passed, no skipped tests.

## Standard HOST build

External process checks confirmed `acad.exe = NOT RUNNING` before all builds. No close/save prompt occurred; no 15-second force-close was needed. Standard output only, no alternate directory/copied DLL.

```text
assemblyLocation=C:\Users\Roman\Documents\CODEX\C#\CsharpProjects\ACAD_krovy\src\AcKrovy.AutoCAD\bin\x64\Debug\net10.0-windows\AcKrovy.AutoCAD.dll
LastWriteTime=2026-10-05 20:31:38.063 +02:00
assemblyLastWriteTimeUtc=2026-10-05 18:31:38.063 UTC
assemblySha256=F5F1E5F48C172497E261086CF772238FD40ACD57922D99853801820B82976562
configuration=DEBUG x64 net10.0-windows
```

Previous HOST DLL: `2026-10-05 17:50:37.293 UTC`, SHA256 `779E11630B9A897C7C863AEB39F2ED87E84F37DFACD984A1EE46374002C07A24`. New timestamp and SHA differ.

## HOST Test Plan

AutoCAD Architecture 2027, clean `C:\Users\Roman\Documents\3d.dwg`, runtime must match the build above. Computer Use only prepares/reads runtime; precise BREAK selection remains manual.

User: BREAK one untouched AUTO Ordinary Plan2D rafter, choose two interior points to remove a middle interval, choose YES at detach, STOP. Expect two 2D segments and two corresponding 3D pieces with a gap, no third piece or AUTO recreation. Capture lifecycle/split diagnostics, identities, designations, section/frame, canonical GROUP and command-state disposed with all transient counts zero.

Required split diagnostic: `resultMemberCount=2`, `physicalRebuildCount=2`, `duplicateGeneratedIdentityCount=0`, `groupCanonical=True`, `manualOverrideWritten=false`, `result=pass`.

Runtime verification: PASS. AutoCAD Architecture 2027 opened `C:\Users\Roman\Documents\3d.dwg` and startup executed `AK_RUNTIME_BUILD` after NETLOAD. Computer Use read the new output: exact standard assembly location, `2026-10-05 18:31:38.063 UTC`, SHA256 `F5F1E5F48C172497E261086CF772238FD40ACD57922D99853801820B82976562`, `configuration=DEBUG`, `result=ok`, process ID 21736. No runtime mismatch. DWG disk timestamp remained `2026-10-04 20:26:41`, length 475612 bytes.

BREAK HOST verdict: INCONCLUSIVE / not yet run. NO/single-result/Independent/zero-gap/cancel/repeated native scenarios remain HOST-unconfirmed until executed. Persistence and UNDO/REDO are not tested by this preparation. No drawing save, commit, push or tag. READY FOR USER BREAK TEST.

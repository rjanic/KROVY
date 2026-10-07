# Ordinary MIRROR Independent lifecycle — 2026-10-05

Historical implementation report. The later approved detach-confirmation and central-warning rules supersede the no-dialog statements below; see [current report](ORDINARY_MIRROR_CONFIRMATION_WARNINGS_2026-10-05.md).

## Result and scope

Individual Ordinary MIRROR uses the existing COPY package engine, accepted full Ordinary physical builder, annotations and current designation reconciliation. It creates a fresh Independent UUID with informational `MirroredFromAuto=4` / `MirroredFromIndependent=5` provenance in the existing schema. No parallel ownership record, schema layout change, detach dialog or new AttachedManual creation.

Native clone mapping has first priority after whole-roof/structural claims and before legacy MIRROR/generated/erase recovery. Claimed clones, source package IDs and erased source handles are removed from downstream legacy candidates. Every line is claimed and generated ownership cleared before native derived clones are discarded and rebuilt.

MIRROR No preserves the complete source snapshot and GROUP. Native mirrored XY wins; Z endpoints normalize to zero. The full member physical context is reflected (topology, member plane, structural cut sources and anchor). Final native direction determines endpoint roles; the shared semantic resolver detects the reversed eave/ridge roles. Section width is horizontal, height upward and the frame right-handed. Mixed Line+Solid produces one Independent package; physical-only clones are rejected with the existing wood-style warning, once per operation. Physical-only in-place/erased source bodies are restored from the accepted package snapshot.

MIRROR Yes handles both clone+erased-source and the repository HOST-proven same-ID in-place transform. Both receive a NEW Independent UUID. Original source bodies/annotations are removed before canonical clone annotations are created. AUTO replacement explicitly reuses only the existing suppression writer (`Suppressed=true`, zero geometry offsets); Independent replacement removes the old package without AUTO suppression. This runs in the same transaction as claim/rebuild/numbering/GROUP, with readback checks for removed source packages and suppression. Failure restores source packages, owner definition and original GROUP and discards only exact mapped native clones. It never falls through generic ERASE.

Fresh MIRROR context uses the common clone context. Begin/end/cancel/fail/disposed diagnostics identify MIRROR. Cancellation restores affected snapshots and discards surviving mapped member clones before disposal. All transient sets clear; no DB work was added at undo/redo boundaries. Whole-roof/owner mappings and raw owner transformations are excluded from Ordinary processing.

## Changed files

- `src/AcKrovy.Core/Services/Roofs/RoofOrdinaryMirrorRules.cs` (new: neutral reflection, native endpoint correspondence, full physical context and provenance)
- `src/AcKrovy.Core/Models/Roofs/RoofIndependentOrdinaryTimberData.cs` (two stable origin enum values)
- `src/AcKrovy.AutoCAD/Infrastructure/RoofOrdinaryCopyLifecycleService.cs` (shared COPY/MIRROR engine, replacement, cancellation and verification)
- `src/AcKrovy.AutoCAD/Infrastructure/LiveGeometrySynchronizationService.cs` (fresh MIRROR scope and first claim/filtering)
- `src/AcKrovy.AutoCAD/Infrastructure/RoofOrdinaryGripLifecycleService.cs` (expose existing restore helpers; optional replaced-package GROUP exclusion in shared verification)
- `src/AcKrovy.AutoCAD/Infrastructure/RoofMirrorCloneDetachService.cs` (expose existing suppression writer only)
- `src/AcKrovy.Core.Tests/RoofOrdinaryMirrorLifecycleTests.cs` (new)
- `src/AcKrovy.Core.Tests/RoofIndependentOrdinaryHorizontalFrameTests.cs` (share existing test fixture)
- `docs/geometry/roof-elevation-contract.md` (approved MIRROR product contract)
- this report

Existing uncommitted WIP is preserved. Root `C:\Users\Roman\Documents\CODEX\C#\CsharpProjects\ACAD_krovy`, branch `main`, HEAD `cb8f2ae225c7d0ddfc9b60ca5fb4e8200ef6493a`. No commit/push/tag.

## Commands and checks

```powershell
git status --short
git rev-parse --show-toplevel
git branch --show-current
git rev-parse HEAD
Get-Process -Name acad -ErrorAction SilentlyContinue
dotnet build src/AcKrovy.AutoCAD/AcKrovy.AutoCAD.csproj --no-restore -c Debug -p:Platform=x64 -warnaserror -m:1 -nr:false
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore --filter 'FullyQualifiedName~RoofOrdinaryMirror|FullyQualifiedName~RoofOrdinaryCopy|FullyQualifiedName~RoofIndependentOrdinaryHorizontal' -warnaserror -m:1 -nr:false
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore --filter 'FullyQualifiedName~RoofOrdinaryMirror' -warnaserror -m:1 -nr:false
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore --filter 'FullyQualifiedName~RoofOrdinaryMirror|FullyQualifiedName~RoofOrdinaryCopy|FullyQualifiedName~RoofOrdinaryBreak|FullyQualifiedName~RoofOrdinaryExtend|FullyQualifiedName~RoofOrdinaryTrim|FullyQualifiedName~RoofOrdinaryGripLifecycle|FullyQualifiedName~RoofIndependentOrdinary|FullyQualifiedName~RoofOrdinaryLogicalMove|FullyQualifiedName~RoofWholeRoofCopy|FullyQualifiedName~RoofNativeCopyRouting|FullyQualifiedName~RoofMirrorAnnotation' -warnaserror -m:1 -nr:false
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Portable
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Full
git diff --check
```

Targeted COPY/MIRROR/frame: PASS 52/52. Expanded lifecycle regressions: PASS 228/228. Final focused MIRROR: PASS 18/18. An added test initially used a nonexistent `SourceSegment` property; corrected to the actual persisted/final `Anchor` before the passing run. No failing check is left open.

Real neutral tests cover AUTO/Independent provenance and fresh identity, exact Line-only/mixed/physical-only/multiple/owner mappings, XY/Z normalization, 35-degree roof slope with 37-degree roof yaw, mirror axes at 0/30/90/-20 degrees with both endpoint directions, exact 80x160 section, upward/right-handed horizontal frame, reflected whole solid vertex sets, end-cut planes and eave/ridge role inversion. Invalid length/nonfinite/ambiguous reflections are rejected. Adapter source guards cover claim order, same-ID replacement, suppression, source removal, GROUP/duplicate checks and cancel/fail/disposal; these are not actual native HOST lifecycle tests.

## Portable Compatibility Gate

| Check | Result |
| --- | --- |
| Branch / HEAD | main / cb8f2ae225c7d0ddfc9b60ca5fb4e8200ef6493a |
| Working tree before | Dirty, authorized prior WIP |
| Restore / build | PASS / PASS |
| Tests | PASS 7530/7530 |
| Warnings / errors | 0 / 0 |
| CAD API leakage | PASS |
| Localization/resources | NOT APPLICABLE |

Verdict: PASS. Full Gate: PASS — solution restore/build with zero warnings/errors, Core 7530/7530 and WPF 828/828, no failed/skipped tests. The final adapter was built to the standard Debug x64 output. Final whitespace check: PASS.

## Simplified HOST startup

External checks confirmed AutoCAD was already OFF before builds/gates. No save prompt, 15-second window or force-close was needed. Standard Debug x64 output only:

`C:\Users\Roman\Documents\CODEX\C#\CsharpProjects\ACAD_krovy\src\AcKrovy.AutoCAD\bin\x64\Debug\net10.0-windows\AcKrovy.AutoCAD.dll`

Startup: PASS. AutoCAD Architecture 2027 was launched with exactly `C:\Users\Roman\Documents\3d.dwg`; the unique returned window title and drawing canvas confirmed it loaded without an error dialog. The agent entered no AutoCAD command, performed no MIRROR or geometry edit and did not save the DWG. The current explicit request supersedes the previous mandatory runtime identity protocol: no agent-invoked `AK_RUNTIME_BUILD`, SHA-256, DLL timestamp/size calculations or runtime metadata comparison. The user verifies build timing manually. The existing automatic AutoCAD startup still emitted its runtime/trace text; the agent did not invoke or compare that output and did not change the user's startup configuration.

## HOST plan and remaining limitations

FIRST TEST IS MANUAL: select ONE untouched AUTO Ordinary Plan2D line, MIRROR, two mirror-axis points, answer NO to erase source, STOP. Expected: unchanged AUTO source in canonical GROUP, one new Independent mirrored Plan2D/Physical3D/annotation package outside GROUP, no roll, no detach dialog, no AttachedManual, lifecycle result=pass and disposed command state with zero counts. Capture the fresh command log and inspect source/clone.

HOST verdict: NOT RUN / INCONCLUSIVE. Both erase-source shapes, physical-only warning, multiple members, cancellation, whole-roof regressions, native UNDO/REDO and SAVE/REOPEN need HOST confirmation for this build. Existing repository evidence establishes the prior same-ID MIRROR Yes event shape; it does not prove this new implementation.

Known geometric limitation: a single source/clone Line pair with coincident midpoints can fit two different reflection planes when endpoint reversal is allowed. Its actual reflected roof plane/cut correspondence cannot be recovered uniquely from that pair alone. This case currently fails closed (source restored, native clones discarded, result=fail) rather than selecting an arbitrary plane. General support for that ambiguity remains open and would require authoritative native mirror-axis/transform evidence. Do not call the implementation complete for every possible MIRROR geometry or label automated tests as HOST PASS.

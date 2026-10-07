# Ordinary native TRIM split

## Implemented scope

HOST evidence supplied for source 293F: ObjectModified retains the first segment;
ObjectAppended creates 2ACD with the same Generated identity. The previous Ordinary
first-claim path considered only snapshot packages, so the clone survived into generic
GROUP processing. This change records exact command-scoped appended ids and resolves
their inherited source identity before retained-source detach can synchronize GROUP.

AUTO acceptance detaches the retained source once and gives each appended Plan2D
segment a fresh IndependentMemberId. Independent acceptance preserves the retained
identity and assigns new identities to appended segments. Each segment receives its
own Solid3d through the existing full physical builder and its own source-bound
annotations. Manufacturing designation is reconciled after all bodies are complete.
The approved horizontal-width frame and existing metadata schema are reused.

NO erases native appended lines before restoring original packages and exact GROUP
from the pre-command snapshot. Cancel/fail cleans surviving native split products
before disposing the command context. No new ManualOverrides are written.

Core validates collinear, nondegenerate, disconnected intervals contained in the
original Plan2D axis. Source lineage is matched by inherited identity, not proximity.
Verification checks unique Independent identities, one plan/physical pair per piece,
cleared Generated ownership, designations, annotations and exact roof GROUP membership.

## Files changed for this task

- src/AcKrovy.AutoCAD/Infrastructure/LiveGeometrySynchronizationService.cs
- src/AcKrovy.AutoCAD/Infrastructure/RoofOrdinaryGripLifecycleService.cs
- src/AcKrovy.Core/Services/Roofs/RoofOrdinaryTrimSplitRules.cs
- src/AcKrovy.Core.Tests/RoofOrdinaryTrimSplitTests.cs
- src/AcKrovy.Core.Tests/RoofIndependentOrdinaryHorizontalFrameTests.cs
- src/AcKrovy.Core.Tests/RoofIndependentOrdinaryDesignationTests.cs
- this report

Existing unrelated WIP was preserved. Root is
C:\Users\Roman\Documents\CODEX\C#\CsharpProjects\ACAD_krovy,
branch main, HEAD cb8f2ae225c7d0ddfc9b60ca5fb4e8200ef6493a.

## Verification

- Targeted dotnet test (OrdinaryTrim, OrdinaryGripLifecycle, IndependentOrdinary):
  PASS, 72 tests.
- dotnet build src/AcKrovy.AutoCAD/AcKrovy.AutoCAD.csproj --no-restore -c Debug
  -p:Platform=x64 -warnaserror -m:1 -nr:false: PASS, zero warnings/errors.
- pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Full: PASS;
  Core 7466 and WPF 828. Four additional physical/designation tests were subsequently
  added and passed in the targeted run and final Portable Gate.
- pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Portable: PASS;
  final Core 7470, zero warnings/errors, neutral dependency checks PASS.
- git diff --check: PASS; existing line-ending conversion warnings only.

Initial sandbox restore/testhost attempts failed; the same tests/gates passed outside
the sandbox. No source changes were made to work around those environment failures.

## Standard HOST build and runtime

acad.exe was not running before the builds. No alternate output path was used.

DLL:
C:\Users\Roman\Documents\CODEX\C#\CsharpProjects\ACAD_krovy\src\AcKrovy.AutoCAD\bin\x64\Debug\net10.0-windows\AcKrovy.AutoCAD.dll

LastWriteTimeUtc: 2026-10-05 16:44:25.285 UTC

SHA256: 25BCDE8964C8FDE1EDE0F7905AF59C1922F6D9C4EA9AF79D69F5169B7FF2067D

Configuration: DEBUG x64 net10.0-windows

AutoCAD Architecture 2027 was restarted with C:\Users\Roman\Documents\3d.dwg.
Computer Use observed the fresh startup AK_RUNTIME_BUILD output: assemblyLocation,
timestamp, SHA256 and DEBUG matched the DLL above; result=ok.
Runtime verification PASS. No failed drawing state was saved.

## First HOST retest pending

Use the established hybrid workflow: user performs native TRIM using two cutting edges,
removes the middle interval of one AUTO Ordinary Plan2D rafter, accepts YES and stops.
Then inspect two Plan2D segments, two matching Physical3D pieces and new diagnostics:
ROOF_ORDINARY_TRIM_SPLIT, ROOF_ORDINARY_TRIM_LIFECYCLE and
ROOF_ORDINARY_TRIM_COMMAND_STATE phase=disposed with zero transient counts.
Expected split result: unique independent ids, no duplicate active Generated identity,
physicalRebuildCount=2 and canonical GROUP. Validate NO and Independent split separately.

HOST split lifecycle/visual verdict: NOT TESTED. Automated guards and runtime match
do not prove native TRIM acceptance or rollback. No commit, push or tag.

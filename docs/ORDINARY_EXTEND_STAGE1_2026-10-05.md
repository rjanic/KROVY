# Ordinary EXTEND Stage 1 — 2026-10-05

## Implementation

Native EXTEND now enters the existing Ordinary Plan2D first-claim lifecycle. The final native line is authoritative; no boundary intersection is recomputed. AUTO acceptance, Independent rebuild, designation recalculation, annotation synchronization and GROUP exclusion reuse the accepted shared implementation and horizontal-width physical frame. Rejection uses the shared exact package snapshot restoration. Cancel/fail restores remaining native endpoint changes before disposing the command snapshot.

Added EXTEND lifecycle and command-state diagnostics. Each command gets the existing fresh snapshot/context; unchanged and non-Ordinary entities are excluded and changed logical members are processed once.

Files changed in this task (other pre-existing WIP preserved):

- `src/AcKrovy.Core/Services/Roofs/RoofGeneratedMemberEditCommandRules.cs`
- `src/AcKrovy.AutoCAD/Infrastructure/RoofOrdinaryGripLifecycleService.cs`
- `src/AcKrovy.AutoCAD/Infrastructure/LiveGeometrySynchronizationService.cs`
- `src/AcKrovy.Core.Tests/RoofOrdinaryExtendLifecycleTests.cs`
- `src/AcKrovy.Core.Tests/RoofOrdinaryTrimLifecycleTests.cs`
- `src/AcKrovy.Core.Tests/RoofIndependentOrdinaryHorizontalFrameTests.cs`
- this report

## Verification

Repository: `C:\Users\Roman\Documents\CODEX\C#\CsharpProjects\ACAD_krovy`; branch `main`; HEAD `cb8f2ae225c7d0ddfc9b60ca5fb4e8200ef6493a`. Working tree was already dirty.

Commands run:

```powershell
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore --filter 'FullyQualifiedName~RoofOrdinaryExtend|FullyQualifiedName~RoofOrdinaryTrim|FullyQualifiedName~RoofOrdinaryGripLifecycle|FullyQualifiedName~RoofIndependentOrdinary|FullyQualifiedName~RoofOrdinaryLogicalMove' -warnaserror -m:1 -nr:false
dotnet build src/AcKrovy.AutoCAD/AcKrovy.AutoCAD.csproj --no-restore -c Debug -p:Platform=x64 -warnaserror -m:1 -nr:false
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Portable
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Full
git diff --check
```

Targeted tests: PASS 110/110. Portable Gate: PASS, Core 7479/7479. Full Gate: PASS, Core 7479/7479 and WPF 828/828. Builds: zero warnings/errors. Architecture dependency checks: PASS. Whitespace check: PASS. No localization changes in this task.

New tests cover EXTEND command routing, Start/End classification from the final native line, unchanged exclusion, shared accept/reject/Independent routes, cancellation/failure restoration order, fresh context and once-per-member processing. Physical tests extend either endpoint on a sloped/yawed member and verify longer full body, end geometry, section dimensions/handedness and approved horizontal-width frame. Source guards are not proof of native HOST ordering or DWG rollback.

## Standard HOST DLL

AutoCAD was absent in Computer Use and `acad.exe` process checks before all builds. No forced close or save-window was necessary. No alternate output or copied DLL used.

```text
assemblyLocation=C:\Users\Roman\Documents\CODEX\C#\CsharpProjects\ACAD_krovy\src\AcKrovy.AutoCAD\bin\x64\Debug\net10.0-windows\AcKrovy.AutoCAD.dll
LastWriteTime=2026-10-05 19:50:37.293 +02:00
assemblyLastWriteTimeUtc=2026-10-05 17:50:37.293 UTC
assemblySha256=779E11630B9A897C7C863AEB39F2ED87E84F37DFACD984A1EE46374002C07A24
configuration=DEBUG (x64, net10.0-windows)
```

Previous DLL: `2026-10-05 16:44:25.285 UTC`, SHA256 `25BCDE8964C8FDE1EDE0F7905AF59C1922F6D9C4EA9AF79D69F5169B7FF2067D`. Both timestamp and hash changed.

## HOST status

Runtime verification: PASS. AutoCAD Architecture 2027 opened `C:\Users\Roman\Documents\3d.dwg`; startup executed `AK_RUNTIME_BUILD`. Computer Use read the new output: exact assembly location/time/SHA above, `configuration=DEBUG`, `result=ok`, process ID 83220. F2 history was closed after inspection. Native EXTEND test not performed by the agent. No drawing save, commit, push or tag.

Startup required recovery: the first process remained hidden and held the drawing while the second offered read-only opening. The second window closed; after the announced 15-second save window the remaining hidden process was terminated. An external process check confirmed `acad.exe = NOT RUNNING` before the final single visible launch. No read-only HOST test was attempted.

After runtime match, user test: EXTEND, one suitable boundary, Enter, endpoint side of one untouched AUTO Ordinary Plan2D rafter, visible extension, detach YES, STOP. Inspect new EXTEND diagnostics, physical section, Independent identity/designation, full package GROUP exclusion and canonical roof GROUP. Native multi-member/cancel/repeated-command behavior remains HOST-unconfirmed until executed.

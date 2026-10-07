# Ordinary COPY Independent lifecycle — 2026-10-05

## Implementation

Native individual Ordinary COPY now receives first claim after whole-roof rebind and before legacy member-clone handling and generic resize/recovery. The existing native IdMapping is retained; no matching by geometry, proximity or selection order. The existing Ordinary command-start package snapshot supplies the source physical context and exact source verification. Owner-copy mappings and consumed whole-roof mappings stay with the existing roof lifecycle.

All copied Plan2D lines receive new Independent UUIDs before any group reconciliation. AUTO sources stay AUTO; Independent sources retain their UUID. Copied lines preserve native XY and normalize both Z endpoints to zero. The existing provenance field gains stable `CopiedFromAuto=2` and `CopiedFromIndependent=3` values; the record/serialized field layout and schema version stay unchanged. Legacy DetachedFromAuto payloads still round-trip.

Mapped native solids and annotations are discarded. A copied Plan2D member gets one new physical body from the accepted full Ordinary builder, own annotations and current-signature designation reconciliation. The complete member context is translated, including roof-plane elevation/reference and end-cut inputs, then uses horizontal width/upward height. It does not resolve placement against an active roof. Native solid clones never become a second logical member. Source package metadata, geometry, solid vertices, annotation inventory and exact GROUP membership must match the pre-command snapshot before commit. Duplicate Generated/physical keys and duplicate Independent plan/body pairing are rejected.

Physical-only copies are erased without new member identity and use the existing wood-style derived-3D warning once. COPY never requests detach confirmation. Processing failure aborts plugin writes and discards the exact mapped native clones; it does not route them to AttachedManual. Existing AttachedManual compatibility and MIRROR behavior remain in place for historical data.

Fresh COPY context counts sources, line/solid clones, claimed clones, processed lines and physical-only rejection. Context disposal clears all collections. Multiple destinations are indexed by clone and deduplicated. Cancel/fail processes surviving mapped placements before disposal, so completed native COPY Multiple placements can receive valid packages; no geometry is invented for erased native results. This completion path still requires real HOST confirmation.

## Changed files

- `src/AcKrovy.AutoCAD/Infrastructure/RoofOrdinaryCopyLifecycleService.cs` (new)
- `src/AcKrovy.AutoCAD/Infrastructure/LiveGeometrySynchronizationService.cs`
- `src/AcKrovy.AutoCAD/Infrastructure/RoofNativeCloneSnapshot.cs`
- `src/AcKrovy.AutoCAD/Infrastructure/RoofOrdinaryGripLifecycleService.cs` (expose existing Accept, designation and Verify helpers only)
- `src/AcKrovy.Core/Services/Roofs/RoofOrdinaryCopyCloneRules.cs` (new)
- `src/AcKrovy.Core/Models/Roofs/RoofIndependentOrdinaryTimberData.cs`
- `src/AcKrovy.Core/Services/Roofs/RoofIndependentOrdinaryTimberDataCodec.cs`
- `src/AcKrovy.Core.Tests/RoofOrdinaryCopyLifecycleTests.cs` (new)
- `src/AcKrovy.Core.Tests/RoofIndependentOrdinaryHorizontalFrameTests.cs`
- `docs/geometry/roof-elevation-contract.md`
- this report

Existing dirty WIP preserved. Root: `C:\Users\Roman\Documents\CODEX\C#\CsharpProjects\ACAD_krovy`; branch `main`; HEAD `cb8f2ae225c7d0ddfc9b60ca5fb4e8200ef6493a`. No commit/push/tag.

## Commands and verification

```powershell
git rev-parse --show-toplevel
git status --short
git branch --show-current
git rev-parse HEAD
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore --filter 'FullyQualifiedName~RoofOrdinaryCopy|FullyQualifiedName~RoofOrdinaryBreak|FullyQualifiedName~RoofOrdinaryExtend|FullyQualifiedName~RoofOrdinaryTrim|FullyQualifiedName~RoofOrdinaryGripLifecycle|FullyQualifiedName~RoofIndependentOrdinary|FullyQualifiedName~RoofOrdinaryLogicalMove|FullyQualifiedName~RoofWholeRoofCopy|FullyQualifiedName~RoofNativeCopyRouting' -warnaserror -m:1 -nr:false
dotnet build src/AcKrovy.AutoCAD/AcKrovy.AutoCAD.csproj --no-restore -c Debug -p:Platform=x64 -warnaserror -m:1 -nr:false
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Portable
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Full
git diff --check
```

Targeted regressions: PASS 197/197. Real neutral tests cover AUTO/Independent identities and provenance, line-only/mixed/physical-only map classification, multiple sources/destinations, whole-owner exclusion, invalid/pre-existing mappings, planar Z normalization, current manufacturing grouping, sloped/yawed full physical copies and preservation of source inputs. Adapter source guards protect ordering, source snapshot checks, shared accepted builder and command-context disposal; they are not proof of AutoCAD native callbacks.

## Portable Compatibility Gate

| Check | Result |
| --- | --- |
| Branch / HEAD | main / cb8f2ae225c7d0ddfc9b60ca5fb4e8200ef6493a |
| Working tree before | Dirty, authorized previous WIP |
| Restore / build | PASS / PASS |
| Tests | PASS 7512/7512 |
| Warnings / errors | 0 / 0 |
| CAD API leakage | PASS |
| Localization/resource changes | NOT APPLICABLE |

Verdict: PASS. Full Gate also PASS: solution restore/build, Core 7512/7512 and WPF 828/828; no failures/skipped tests or compiler warnings/errors. Final adapter sources were compiled by Full Gate to the standard Debug x64 host output. Whitespace check PASS.

## Mandatory HOST build

External process checks confirmed `acad.exe = NOT RUNNING` before every build/gate. AutoCAD was already off; no unsaved drawing prompt and no force-close/save-window were required. No alternate output directory or copied DLL.

```text
assemblyLocation=C:\Users\Roman\Documents\CODEX\C#\CsharpProjects\ACAD_krovy\src\AcKrovy.AutoCAD\bin\x64\Debug\net10.0-windows\AcKrovy.AutoCAD.dll
LastWriteTime=2026-10-05 21:13:43.196 +02:00
assemblyLastWriteTimeUtc=2026-10-05 19:13:43.196 UTC
assemblySha256=08129B41C90FD86AFA072DB9A1D51A405E513665E3F304946CAEFF1DDD15DCFF
configuration=DEBUG x64 net10.0-windows
```

Both timestamp and hash differ from the preceding BREAK HOST DLL (`2026-10-05 18:31:38.063 UTC`, `F5F1E5F48C172497E261086CF772238FD40ACD57922D99853801820B82976562`).

Runtime verification: PASS. AutoCAD Architecture 2027 was restarted with `C:\Users\Roman\Documents\3d.dwg`. A fresh `AK_RUNTIME_BUILD` was entered through the actual bottom command-line text input using Computer Use. Its new output confirmed the exact standard assemblyLocation, `2026-10-05 19:13:43.196 UTC`, SHA-256 `08129B41C90FD86AFA072DB9A1D51A405E513665E3F304946CAEFF1DDD15DCFF`, `configuration=DEBUG`, `result=ok`, processId `77576`. AutoCAD remained responsive without an error dialog. Expanded command history was closed; no COPY, geometry edit or drawing save was performed by the agent.

## First HOST test plan

AutoCAD Architecture 2027; clean `C:\Users\Roman\Documents\3d.dwg`; exact runtime identity above. User manually selects one untouched AUTO Ordinary Plan2D Line only, native COPY, base point, one destination in XY, STOP. No detach dialog expected. Agent does not execute precise native selection.

Expected: source unchanged/AUTO/in canonical roof GROUP; one new Independent clone with fresh UUID, Plan2D Z=0, one valid 3D body, horizontal section width, own annotations and normally the same designation. Clone package outside roof GROUP. Capture `ROOF_ORDINARY_COPY_LIFECYCLE` with `attachedManualWritten=false`, duplicate Generated/physical counts zero, `groupCanonical=True`, `terminalHandled=true`, `result=pass`, plus COPY command-state disposed with every transient count zero. Legacy AttachedManual creation/anchor diagnostics must not execute for this handled clone.

HOST COPY verdict: INCONCLUSIVE / NOT RUN. Mixed, Independent, physical-only warning, multiple placements, cancel/fail, whole-roof, SAVE/REOPEN and native UNDO/REDO remain HOST-unconfirmed for this build. Automated PASS does not establish native HOST behavior. DWG disk state before startup: length 475612 bytes, LastWriteTimeUtc `2026-10-04 18:26:41`. No drawing save.

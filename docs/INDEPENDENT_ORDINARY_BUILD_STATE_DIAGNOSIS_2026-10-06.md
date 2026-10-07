# Independent Ordinary build-state diagnosis — 2026-10-06

Status: instrumentation implemented; actual missing HOST state remains OPEN.
No lifecycle, geometry, persistence schema, ownership, designation or GROUP repair changed.
No commit, push or tag.

## Located failure path

`RoofOrdinaryGripLifecycleService.Accept` throws
`Ordinary GRIP full physical build state unavailable.` when the command snapshot's
`Member.BuildState` is null (or accepted rigid-translation rebase fails).

Command-start `Capture` resolves the state through these existing branches:

1. Read the Plan Line's `ACAD_KROVY_ORDINARY_PHYSICAL_BUILD_V1` Xrecord.
2. If valid, rebase that persistent state against the command-start Plan axis.
   A failed rebase discards the state; the existing branch does not attempt provenance recovery afterward.
3. If no valid stored state was read, recover from roof owner, live Generated recipe,
   physical elevation, layout, source face/station and structural context.
   Historical Independent recovery must rebase the recovered AUTO anchor against the Independent axis.
4. Apply current timber section dimensions and capture the paired Solid's actual BRep frame.
   Failure to resolve that frame also discards the state.

The provenance recovery branch depends on current AUTO inventory/layout.
The persisted member-state branch does not need that inventory. This is a code-path finding,
not proof of which branch failed for the reported HOST member.

## HOST comparison available before instrumentation

Source: `%LOCALAPPDATA%/ACAD_KROVY/Logs/ACAD_KROVY-20261006.log`.

| Member | Solid | Evidence |
|---|---|---|
| 2AFC | 2B7C | Independent STRETCH Start at 09:03:04 and subsequent GRIP/STRETCH fail with build state unavailable. |
| 2B0C | 2B8C | Independent STRETCH Start at 09:02:55 and 09:02:58: physicalRebuild=True, result=pass. |
| 2AFB | 2B7B | Independent STRETCH Start at 09:02:18: physicalRebuild=True, result=pass. |

Both compared Independent members resolve roof owner 2912 in the existing lifecycle output.
The old log does not include Independent build-state Xrecord contents, rebase deltas or
BRep frame sub-resolver results. The first material persistent-state difference is UNCONFIRMED.
The 2AFC endpoint=None/planChanged=False physical-only rejection record is not evidence
of a successful Independent rebuild.

## Changed files and diagnostic scope

- `RoofOrdinaryGripLifecycleService.cs`: command-start trace retained with each member;
  one `ROOF_ORDINARY_PHYSICAL_BUILD_STATE` record emitted for an accepted edit's build-state resolution.
  Successful state resolution is distinct from later lifecycle/model validation.
- `RoofOrdinaryPhysicalBuildStateTrace.cs`: new transient DEBUG evidence collector and invariant formatting.
- `RoofOrdinaryPhysicalBuildStateStore.cs`: Xrecord read outcome, decoded inputs, JSON path and exact invalid component.
- `RoofOrdinaryPhysicalSectionFrameReader.cs`: hint origin, face-pair results,
  directed frame comparison and BRep exception/error status.
- `RoofOrdinaryRafterSolidMaterializationService.cs`: provenance fallback inputs,
  roof/face/elevation/layout resolution, historical rebase and exact structural sub-resolver failure.
- `RoofGeneratedRafterSetService.cs`: recipe recovery source failure or incompatible observed recipe inputs.

The compact diagnostic includes member identity/provenance, paired Solid count, section/material,
current and accepted Plan axes, stored topology/face plane nodes, eave datum/pitch/cuts,
stored/measured section frames, rebase endpoint deltas and the exact failing resolver.
Uncalled live roof resolvers are labeled `not_attempted`; they are not required by the stored-state path.
No full raw JSON or unrelated members are printed. Trace collection is enabled only in DEBUG.

## Verification and remaining HOST work

Commands:

```powershell
git rev-parse --show-toplevel
git branch --show-current
git rev-parse HEAD
git diff --check
Get-Process -Name acad -ErrorAction SilentlyContinue
dotnet build src/AcKrovy.AutoCAD/AcKrovy.AutoCAD.csproj --no-restore -c Debug -p:Platform=x64 -warnaserror -m:1 -nr:false
```

Confirmed working copy: `C:/Users/Roman/Documents/CODEX/C#/CsharpProjects/ACAD_krovy`.
Branch `main`, HEAD `cb8f2ae225c7d0ddfc9b60ca5fb4e8200ef6493a`.
Existing uncommitted WIP was preserved.

Final Debug x64 adapter build: PASS, 0 warnings, 0 errors (10.07 seconds).
`acad.exe = NOT RUNNING` was explicitly checked immediately before each build.
Standard output: `src/AcKrovy.AutoCAD/bin/x64/Debug/net10.0-windows/AcKrovy.AutoCAD.dll`.
`git diff --check`: PASS; only existing line-ending notices appeared in the full-worktree check.
AutoCAD Architecture 2027 restarted with the requested `C:/Users/Roman/Documents/3d.dwg`;
process 95032 showed `[3d.dwg]` and `Responding=True`. No CAD edit command was issued.
Runtime build matching and the new member comparison are not claimed as HOST-validated.

Regression tests and compatibility gates are NOT RUN: the request explicitly defers focused
tests until the actual missing HOST state is identified. Compilation is not a HOST verdict.
After diagnostic build/startup, manually reproduce endpoint STRETCH on 2AFC and on one
still-working Independent (prefer 2B0C or 2AFB); compare their new build-state records.
Do not design a semantic repair until those records identify the first divergence.

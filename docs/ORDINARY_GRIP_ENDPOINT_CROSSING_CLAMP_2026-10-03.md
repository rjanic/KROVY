# Ordinary endpoint GRIP crossing clamp — 2026-10-03

## Scope and HOST evidence

All pre-existing WIP is retained; no commit, push or tag. Baseline: `main`,
`cb8f2ae225c7d0ddfc9b60ca5fb4e8200ef6493a` (dirty working tree).
Copies of the seven touched pre-existing files and initial status were saved to
`%TEMP%/krovy-crossing-grip-20261003-baseline` before edits.

The user confirms fresh HOST PASS for repeated COPY, AttachedManual diagonal
axis-constrained GRIP and Physical3D no-yaw. The current task changes only the
shared endpoint classifier and its projection telemetry. COPY promotion, lifecycle
routing, recovery, metadata persistence algorithms, physical builders, annotations
and GROUP services retain the previous implementation.

Existing HOST log `%LOCALAPPDATA%/ACAD_KROVY/Logs/ACAD_KROVY-20261003.1.log`
contains repeated `ROOF_COPY_FINAL ... physicalCount=1 recoveryClaimed=False result=ok`
and successful AttachedManual projected grips. At 15:02:14.848 the crossing case
enters `ROOF_ORDINARY_GRIP_CONSTRAINT ... endpoint=None ... result=fail`.
These are pre-fix HOST observations, not a retest of this change.

## Cause and accepted geometry

The grip classifier aligned the observed axis to the pre-command direction before
detecting which endpoint moved. Crossing the fixed endpoint reversed that observed
axis, so alignment swapped Start/End and made both endpoints appear moved. Exact
endpoint coincidence was also rejected as zero length before classifying the grip.

The classifier now retains native endpoint semantics, identifies the moved
endpoint, projects its displacement onto the original directed axis, and clamps
the signed length before passing positive geometry to the existing endpoint-offset
replay. The fixed endpoint is unchanged. Neither RotationRadians nor endpoint-role
swapping is introduced. Repeated attempts at the clamp return accepted no-change;
the existing Generated no-change path restores the native line to the baseline.
AttachedManual retains its existing identity/anchor/reference update and replay.

Let `L` be the baseline Plan length and `d` the signed cursor displacement along
Start→End. The projected length is `L-d` for Start and `L+d` for End. The accepted
length is `max(minimumLength, projectedLength)`.

The interactive Plan floor reuses
`RoofRafterLengthRules.DefaultMinimumAutomaticLengthMm = 500 mm`, conservatively:
the slope-corrected length of the member axis is at least its Plan length. The
existing automatic-layout threshold remains a **true-length** setting; this change
does not redefine that setting or derive Plan geometry from a physical cut. For an
already accepted shorter manual member, the floor is its current positive length
(`min(L,500)`), avoiding growth caused by pure lateral or crossing grips.

For the rounded HOST input supplied in the request:

| Value | Result |
| --- | --- |
| Before Start | `(48115.221,10292.763,0)` |
| Fixed End | `(48115.221,13441.329,0)` |
| Native Start | `(49421.594,13886.318,0)` |
| Endpoint | `Start` |
| projectedDelta | `3593.555 mm` |
| projectedLength | `-444.989 mm` |
| minimumLength | `500 mm` |
| Accepted Start | `(48115.221,12941.329,0)` |
| Accepted longitudinalDelta | `2648.566 mm` |
| clamped | `True` |

DEBUG `ROOF_ORDINARY_GRIP_CONSTRAINT` now includes `projectedDelta`,
`projectedLength`, `minimumLength`, `clamped=True|False` for Generated and
AttachedManual endpoint acceptance. `longitudinalDelta` is the accepted movement;
`projectedDelta` is the cursor projection before clamping.

## Changed files (this task)

- `src/AcKrovy.Core/Services/Roofs/RoofGeneratedMemberOverrideMath.cs`: shared clamp,
  finite native input guard, CAD-neutral projection result and compatible overload.
- `src/AcKrovy.Core/Services/Roofs/RoofAttachedManualGripRules.cs`: carries the shared
  projection result; identity/anchor/reference algorithms unchanged.
- `src/AcKrovy.AutoCAD/Infrastructure/RoofGeneratedMemberManualEditService.cs`:
  passes projection telemetry through existing grip acceptance diagnostics.
- `src/AcKrovy.AutoCAD/Infrastructure/RoofGeneratedMemberManualEditDiag.cs`:
  adds the four DEBUG fields.
- `src/AcKrovy.AutoCAD/Infrastructure/RoofAttachedManualLifecycleService.cs`:
  adds projection to the existing success diagnostic only.
- `src/AcKrovy.Core.Tests/RoofOrdinaryGripAxisConstraintTests.cs`: Start/End crossing,
  exact coincidence, huge diagonal drag, repeated clamp, reported HOST coordinates,
  Generated override replay/body-frame/reconciliation and short-member lateral no-op.
- `src/AcKrovy.Core.Tests/RoofAttachedManualCopyGripDurabilityTests.cs`: repeated
  Start/End crossing with codec/replay, unchanged UUID/handle/owner/anchor/Origin,
  physical frame and one-body reconciliation; unclamped normal grips; DEBUG routing.
- This report.

## Validation

Commands executed from the repository root:

```powershell
Get-Process acad -ErrorAction SilentlyContinue
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore -warnaserror -m:1 -nr:false --filter 'FullyQualifiedName~RoofOrdinaryGripAxisConstraintTests|FullyQualifiedName~RoofAttachedManualCopyGripDurabilityTests'
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Portable
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Full
dotnet build src/AcKrovy.AutoCAD/AcKrovy.AutoCAD.csproj --no-restore -c Release -p:Platform=x64 -warnaserror -m:1 -nr:false
git -c core.safecrlf=false diff --check
git status --short
git branch --show-current
git rev-parse HEAD
```

The gates run with ordinary local permissions because the earlier sandbox run of
the existing SafeFileWriter File.Replace test failed with UnauthorizedAccessException.
No file-writer production change was made. AutoCAD was confirmed closed before
host-linked builds.

| Check | Result |
| --- | --- |
| Branch / HEAD / initial working tree | main / cb8f2ae225c7d0ddfc9b60ca5fb4e8200ef6493a / dirty |
| Restore | PASS, Portable and Full Gate |
| Focused Generated/AttachedManual grip and COPY tests | PASS, 39/39 |
| Portable Gate / full Core suite | PASS, 7334/7334, 0 warnings, 0 errors |
| Neutral-layer dependencies / manifest | PASS |
| Full Gate / Debug x64 / WPF | PASS, Core 7334/7334, WPF 826/826, 0 warnings, 0 errors |
| Release x64 warnings-as-errors | PASS, 0 warnings, 0 errors |
| Localization/resource edits | NOT APPLICABLE, no resources changed; existing tests PASS |
| Diff whitespace | PASS |
| Post-fix interactive AutoCAD crossing retest | NOT RUN |

Automated verdict: **PASS**. All requested crossing/normal-grip/COPY regression
cases pass. HOST verdict for the new crossing clamp remains **NOT RUN**.

## Remaining HOST check

With the new Debug build, drag Start past End and End past Start on both Generated
ordinary and AttachedManual Origin.Copy ordinary members. Include exact coincidence,
very large diagonal drags and repeated attempts. Verify endpoint=Start/End,
projectedLength<=0, minimumLength=500 (or current shorter member length), clamped=True,
result=ok; same directed axis, fixed endpoint, UUID/anchor, no yaw/roll, one physical
body and canonical annotations/GROUP. Normal grips should have clamped=False.
Recheck repeated COPY. No unsupported-stretch recovery should occur for valid
endpoint crossing.

Automated geometry/frame/source-contract tests cannot prove real HOST callback
ordering or visual annotations/GROUP. The new crossing behavior requires this
interactive HOST retest before declaring HOST PASS.

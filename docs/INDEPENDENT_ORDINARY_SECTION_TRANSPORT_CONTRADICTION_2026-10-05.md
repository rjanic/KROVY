# Independent Ordinary section transport: product-rule contradiction

Historical report: the contradiction below was resolved by the user's subsequent
explicit selection of **horizontal width W.Z = 0**. Minimum transport is now a
diagnostic reference only. See the horizontal-width implementation report for
the final implementation and validation.

Status at the time of this report: **INCONCLUSIVE, awaiting clarification**. No corrected HOST behavior claimed.
Implementation is a compiling prototype; focused new transport regressions and final
compatibility gates have not been run. Do not treat the prototype as a validated fix.

## Exact HOST evidence

Log: `C:\Users\Roman\AppData\Local\ACAD_KROVY\Logs\ACAD_KROVY-20261005.log`,
2026-10-05 10:07:01–10:07:04 CEST.
Line `293F`, solid `2A1F`, owner `2912`, ElementId `K4`.
Lifecycle is reported PASS and is outside the requested change.

Before Plan:
`(40691.02099635819,11478.696868240506,0)` ->
`(40691.02099635819,14478.696868240506,0)`.

After Plan:
`(42132.20804897987,11662.691030875314,0)` ->
`(40691.02099635819,14478.696868240506,0)`.

Authoritative roof plane: origin
`(36141.02099635819,11478.696868240506,3000)`,
normal `(0,-0.7071067811865474,0.7071067811865477)`.
Measured final section was 80x160, with W approximately
`(-0.9403209052771455,-0.2406206507126782,-0.24062065071264543)`
and H approximately `(0,-0.7071067811865491,0.7071067811865459)`.
User reports this visual result FAIL.

## Geometric conflict

For the canonical pre-edit roof-owned frame produced by the existing builder:

```text
L0 = (0,0.7071067811865476,0.7071067811865476)
W0 = (-1,0,0)
H0 = (0,-0.7071067811865475,0.7071067811865475)
L1 = (-0.3402889876248827,0.6649072886129435,0.6649072886129435)
```

Both axes lie in the same roof plane. Therefore the minimum rotation axis
`normalize(L0 x L1)` is the roof normal. The minimum angle is
`19.89448173013032` degrees. Applying that same Rodrigues rotation to W0/H0 gives:

```text
W1 = (-0.9403209052771465,-0.24062065071265973,-0.24062065071265973)
H1 = (0,-0.7071067811865475,0.7071067811865475)
```

These match the existing final HOST frame within numerical tolerance.
Thus minimum 3D transport and the roof-normal rule produce the same result for
this canonical fixture. A transport oracle would report zero extra twist too.
For a pre-edit section already having its own roll, minimum transport can differ;
the proposed native reader captures that actual frame. But this does not establish
that the new rule will remove the reported visual defect for a canonical AUTO member.

This is a contradiction between the requested algorithm and the expected visual
correction, not an Euler implementation defect. The normative geometry contract
requires stopping and reporting contradictions; implementation must not silently
substitute a different rule.

## Reviewable prototype changes

- `src/AcKrovy.Core/Services/Roofs/RoofOrdinarySectionTransportRules.cs`: shortest
  proper rotation, shared W/H transport, tolerance-only orthonormalization, explicit
  deterministic antiparallel case, signed extra-twist comparison.
- `src/AcKrovy.Core/Services/Roofs/RoofOrdinaryGripLifecycleRules.cs`: geometry-only
  optional section frame in build state v2, backward reading of v1, optional
  `preserveSectionRoll` physical-build input, accepted frame retained for subsequent edits.
- `src/AcKrovy.Core/Services/Roofs/RoofAutomaticRafterPhysicalBuilder.cs`: supplied
  Independent W/H used by prism and end-cut construction; default AUTO path unchanged.
- `src/AcKrovy.Core/Services/Roofs/RoofOrdinaryPhysicalFrameRules.cs`: roof reference
  separated from supplied Independent section axes.
- `src/AcKrovy.Core/Models/Roofs/RoofAutomaticRafterPhysicalModel.cs`: optional transport
  description for geometry diagnostics.
- `src/AcKrovy.AutoCAD/Infrastructure/RoofOrdinaryPhysicalSectionFrameReader.cs`:
  read-only native BRep section-plane measurement before the edit.
- `src/AcKrovy.AutoCAD/Infrastructure/RoofOrdinaryGripLifecycleService.cs`: geometry
  input capture and transport option only; no detach, identity, GROUP, rollback,
  prompts, routing, or terminal handling changes.
- `src/AcKrovy.AutoCAD/Infrastructure/RoofFinalSolidFrameAudit.cs`: transport-relative
  diagnostic oracle and old/new axes, minimum angle, extra twist. Roof-plane top
  distance remains informational for Independent.
- `docs/geometry/roof-elevation-contract.md`: explicit newly approved distinction
  between AUTO roof frame and Independent section frame, currently pending resolution
  of the visual expectation described above.
- This report.

The existing Xrecord name is retained for legacy discovery; its geometry payload
has explicit version 2 and accepts version 1. Identity metadata schemas are unchanged.
No pre-acceptance persistence writes were added. Existing WIP was preserved.
No commit, push or tag.

## Commands and results

```powershell
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore -c Debug --filter 'FullyQualifiedName~RoofOrdinaryGripLifecycle|FullyQualifiedName~RoofOrdinaryPhysicalFrameYaw' -m:1 -nr:false
dotnet build src/AcKrovy.AutoCAD/AcKrovy.AutoCAD.csproj --no-restore -c Debug -p:Platform=x64 -warnaserror -m:1 -nr:false
```

Existing focused tests: **PASS 21/21**. Prototype DEBUG x64 build:
**PASS, 0 warnings, 0 errors**, AutoCAD closed, 14.36 seconds.
An independent numerical Rodrigues calculation produced the values above.
New required transport tests, Portable Gate, Full Gate and new-build HOST:
**NOT RUN**, stopped upon identifying the product-rule contradiction.

## Required clarification

Either retain minimum 3D transport and accept that this canonical HOST case keeps
the same frame, or specify a different desired section convention. For example,
horizontal width (`W.Z = 0`) with height obtained by projecting world-Z perpendicular
to L1 would produce a different result, but it is not minimum 3D transport and has
not been authorized as a replacement algorithm.

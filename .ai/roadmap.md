# AI implementation roadmap

> This file does not replace ACAD_KROVY_ROADMAP.md or ACAD_KROVY_BACKLOG.md. It summarizes constraints and sequencing for AI agents.

## Purpose

Give AI agents a compact sequencing and constraint snapshot. Product priorities remain authoritative in the root roadmap and backlog.

## Hard rules

- Treat the milestone information below as a snapshot, then confirm it against current code and Git history.
- Do not redesign stable metadata, SourceHandle ownership, numbering, annotation lifecycle or Settings workflows without an explicit task.
- Do not present planned AutoCAD/BricsCAD/ZWCAD features as implemented.
- Preserve the portable Core/adapter boundary throughout future work.

## Current architecture or workflow

Snapshot after successful v0.18.0 closure: Settings Fashion Look provides Light/Dark design tokens, section-specific apply workflows, editable/hydrated layer selection with idempotent suffix resolution, the ACI 1-255 picker, six runtime languages, ten PNG annotation presets, NoAnnotations, and separate standalone versus combined framed annotation lifecycles. The release also introduces `.ai` developer guidance.

Near-term themes must be taken from `ACAD_KROVY_BACKLOG.md` and sequenced through `ACAD_KROVY_ROADMAP.md`. At this snapshot those documents remain the source for roof automation, reporting/manufacturing workflow, multilingual completion and compatibility expansion; implement only items that are still present and explicitly selected.

Architectural prerequisites for later stages:

- Keep calculations and persisted models CAD-neutral.
- Stabilize host-neutral contracts before adding another adapter.
- Preserve schema migration and update-on-write behavior.
- Require portable tests plus real-host probes for geometry, MLeader and database lifecycle.
- Keep user-entered and technical CAD values language-neutral.

Do not rework without explicit instruction:

- metadata schema 4 and its backward-compatible normalization,
- layer profile version 3 and suffix-family/idempotency rules,
- `ElementId` versus `SourceHandle` ownership,
- stable item numbering by `TimberElementSignature`,
- standalone versus combined framed workflows,
- NoAnnotations preservation of timber data,
- runtime localization preservation of pending Settings edits.

Known technical debt/limitations:

- COPY/COPYCLIP/WBLOCK/SAVE-REOPEN and interactive STRETCH still require real AutoCAD release smoke tests.
- The current production adapter targets AutoCAD 2027/.NET 10; other host/version targets need deliberate adapter/build work.
- Some architecture regressions are guarded by source-contract tests and must not be mistaken for runtime API coverage.

Compatibility path: retain shared Core and abstractions, then add explicitly targeted AutoCAD 2021-2027 build adapters as required. BricsCAD and ZWCAD require separate host adapters for transactions, events, XData and geometry conversions, with the same portable contracts. Do not introduce vendor API types into Core to accelerate this work.

## Common failure modes

- Treating this summary as a second backlog and adding unscheduled features.
- Reopening stabilized v0.18.0 workflows while implementing an unrelated milestone.
- Mixing future multi-CAD abstractions with speculative vendor-specific APIs.
- Marking a manual-host limitation complete based only on unit tests.

## Checklist before changing this area

- Read the current root roadmap and backlog.
- Verify the selected item is still planned and explicitly in scope.
- Identify architectural prerequisites and protected stable workflows.
- Separate implemented facts from future intent.
- Update this snapshot only when milestone sequencing materially changes.

## Relevant source files and tests

- `ACAD_KROVY_ROADMAP.md`
- `ACAD_KROVY_BACKLOG.md`
- `ACAD_KROVY_PROJECT_CONTEXT.md`
- `src/AcKrovy.Core`
- `src/AcKrovy.Cad.Abstractions`
- `src/AcKrovy.AutoCAD`
- `src/AcKrovy.Core.Tests`
- `src/AcKrovy.Wpf.Tests`

See [architecture.md](architecture.md), [cad-abstractions.md](cad-abstractions.md), and [release-process.md](release-process.md).

# KROVY Skill: CAD-Neutral Core Feature

## Purpose

Use this skill when designing or implementing a new KROVY feature that should live primarily in the CAD-neutral domain layer.

This skill protects the long-term plan to support AutoCAD, BricsCAD, ZWCAD, and multiple CAD versions through separate host adapters.

## When to use

Use this skill for new domain calculations, timber element behavior, roof geometry logic, metadata contracts, serialization rules, validation rules, report calculations, unit-testable business logic, and features intended to work across more than one CAD host.

## Goal

Keep the feature split cleanly between CAD-neutral Core logic, CAD abstractions, host-specific adapter logic, UI/command layer, and AutoCAD-only behavior.

## Architecture rule

Core must know nothing about AutoCAD.

Core may define:

- domain models
- calculations
- validation rules
- immutable snapshots
- serialization contracts
- deterministic algorithms
- host-independent geometry abstractions
- testable workflows

Core must not define or reference:

- AutoCAD DBObject
- ObjectId
- Entity
- Polyline
- Transaction
- Document
- Editor
- CommandMethod
- SelectionSet
- PromptResult
- LayerTableRecord
- MLeader
- Dimension
- BlockReference
- host UI types

Host adapters may translate between CAD APIs and Core types.

## Design checklist

Before implementation, define:

- [ ] feature purpose
- [ ] user-visible behavior
- [ ] Core models affected
- [ ] CAD abstraction types required
- [ ] AutoCAD adapter responsibilities
- [ ] persistence/schema impact
- [ ] localization impact
- [ ] tests required
- [ ] backward compatibility impact
- [ ] BricsCAD/ZWCAD risk
- [ ] manual HOST test plan, if needed

## Implementation rules

Prefer this flow:

1. Add or update Core model/calculation.
2. Add portable tests.
3. Add abstraction if host interaction is needed.
4. Add AutoCAD adapter implementation.
5. Add UI/command integration.
6. Add localization.
7. Run Portable Compatibility Gate.
8. Run Full Gate or HOST validation where applicable.

Do not start by placing domain logic inside AutoCAD commands.

## Forbidden shortcuts

Do not put calculations directly into AutoCAD command classes, pass AutoCAD ObjectId into Core, store host-specific database types in Core snapshots, make Core depend on Windows UI, hardcode AutoCAD-only behavior where an abstraction is required, change schema/version silently, or mix large refactor and feature implementation unless explicitly approved.

## Output format for design-only work

```md
## CAD-Neutral Feature Design

### Purpose

### Core changes

### Abstractions

### AutoCAD adapter changes

### Persistence/schema impact

### Localization impact

### Test plan

### HOST test plan

### Risks

### Verdict
`SAFE TO IMPLEMENT`, `NEEDS CLARIFICATION`, or `NOT CAD-NEUTRAL`
```

## Output format for implementation work

```md
## CAD-Neutral Feature Implementation Report

| Area | Result |
| --- | --- |
| Core | `<changed/not changed>` |
| Abstractions | `<changed/not changed>` |
| AutoCAD adapter | `<changed/not changed>` |
| UI/commands | `<changed/not changed>` |
| Persistence/schema | `<none/changed>` |
| Localization | `<none/changed>` |
| Tests | `<added/updated/not run>` |
| Portable Gate | `<PASS/FAIL/NOT RUN>` |
| HOST validation | `<PASS/FAIL/NOT RUN/NOT APPLICABLE>` |

### Changed files

### Notes

### Risks
```

## Human approval

If the feature affects persistence, identity, schema, COPY/PASTE, WBLOCK, UNDO/REDO, generated entities, or existing DWG compatibility, stop and require human review before finalizing.

# KROVY Skill: Roof Timber Lifecycle

## Purpose

Use this skill when changing roof timber behavior that depends on ownership, generated-versus-manual identity, geometry synchronization, editing, transforms, persistence, or drawing lifecycle events.

The goal is to keep roof sources, generated timber, attached manual timber, annotations, display groups, and persisted data consistent through AutoCAD operations.

## When to use

Use this skill for changes involving:

- roof-generated rafters or other generated timber
- manually attached roof timber
- source/display ownership or selection behavior
- COPY, PASTE, WBLOCK, MIRROR, MOVE, ROTATE, STRETCH, GRIP edit, ERASE, UNDO, or REDO
- SAVE/REOPEN persistence or schema changes
- roof-group membership, copy rehydration, detach, rollback, or orphan handling
- targeted recalculation, annotation refresh, or live geometry synchronization

## Core lifecycle rules

- Keep domain rules and persistence contracts CAD-neutral; put AutoCAD database operations in the AutoCAD adapter.
- Preserve stable source, roof, and generated-member identity whenever the operation supports it.
- Treat generated timber, attached manual timber, annotations, and display entities as distinct lifecycle participants with explicit ownership.
- Never silently turn a user-edited or detached member back into generated output without an explicit lifecycle rule.
- Make COPY, WBLOCK, and MIRROR create independent ownership where required; do not retain references to the original roof unless the behavior is intentional and tested.
- Handle unsupported edits predictably: preserve recoverable data, provide a user-visible outcome where applicable, and avoid partial ownership state.
- Any persistence/schema change must be versioned, backward-compatible, and covered by tests.

## Design checklist

Before implementation, define:

- [ ] source roof and child ownership model
- [ ] generated, attached-manual, detached, erased, and orphan states
- [ ] identity behavior for COPY, WBLOCK, MIRROR, and clone operations
- [ ] transform behavior for MOVE, ROTATE, STRETCH, and GRIP edits
- [ ] recalculation scope and annotation/display refresh behavior
- [ ] SAVE/REOPEN and UNDO/REDO expectations
- [ ] recovery behavior for unsupported edits or partial failures
- [ ] Core, abstraction, AutoCAD adapter, localization, and schema impact
- [ ] portable, source-contract, and AutoCAD HOST test coverage

## Implementation sequence

1. Define or update CAD-neutral lifecycle rules, models, and persistence contracts.
2. Add portable unit tests for identity, ownership, transforms, and serialization rules.
3. Add or update AutoCAD adapter handling for database writes and command/event integration.
4. Add source-contract tests for required adapter integration points.
5. Run the Portable Compatibility Gate.
6. Close AutoCAD before any build that may load AutoCAD assemblies.
7. Run the required compatibility build and execute a HOST Regression Test for affected AutoCAD behavior.

## Required validation

Report each applicable operation as `PASS`, `FAIL`, `NOT RUN`, or `NOT APPLICABLE`:

| Area | Checks |
| --- | --- |
| Ownership | Source, generated, attached-manual, detached, and orphan behavior remain consistent. |
| Copy and clone | COPY, PASTE, WBLOCK, and MIRROR do not create accidental cross-roof ownership. |
| Editing | MOVE, ROTATE, STRETCH, GRIP, trim, and manual edits follow the defined recalculation or detach policy. |
| Persistence | SAVE/REOPEN preserves required lifecycle state and schema compatibility. |
| Recovery | Unsupported or failed edits do not leave partial generated/display state. |
| AutoCAD behavior | DBMOD, command output, entity lifecycle, and visual state are verified by actual HOST testing. |

## Stop rules

Stop and request human review before finalizing if the change affects persistence, identity, schema, copy/clone semantics, WBLOCK, UNDO/REDO, generated-member ownership, or existing DWG compatibility and the intended behavior is not explicit.

Do not claim AutoCAD behavior is validated from source inspection or portable tests alone.

## Output format

```md
## Roof Timber Lifecycle Report

| Area | Result |
| --- | --- |
| Ownership | `PASS/FAIL/NOT RUN` |
| Copy and clone | `PASS/FAIL/NOT RUN/NOT APPLICABLE` |
| Editing and transforms | `PASS/FAIL/NOT RUN/NOT APPLICABLE` |
| Persistence | `PASS/FAIL/NOT RUN/NOT APPLICABLE` |
| Recovery | `PASS/FAIL/NOT RUN/NOT APPLICABLE` |
| Portable validation | `PASS/FAIL/NOT RUN` |
| HOST validation | `PASS/FAIL/NOT RUN/NOT APPLICABLE` |

### Risks

- ...

### Verdict

`SAFE TO IMPLEMENT`, `NEEDS HUMAN REVIEW`, or `NOT VALIDATED`
```

## Human approval

Do not commit or push unless explicitly approved. Require human review for lifecycle decisions that affect existing drawings or user-authored timber.

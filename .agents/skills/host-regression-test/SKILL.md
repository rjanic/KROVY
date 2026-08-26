# KROVY Skill: HOST Regression Test

## Purpose

Use this skill to define and report manual or semi-automated AutoCAD HOST regression testing for ACAD KROVY.

This skill is for behavior that cannot be proven by portable unit tests alone.

## When to use

Use this skill after changes that may affect AutoCAD commands, DBMOD behavior, XData persistence, drawing database writes, generated entities, annotations, COPY/PASTE, WBLOCK, SAVE/REOPEN, UNDO/REDO, STRETCH/GRIP/MOVE/ROTATE/ERASE, transient preview, AutoCAD UI interactions, source/display ownership, or roof lifecycle behavior.

## Important rule

Portable tests do not replace HOST testing.

Do not claim HOST PASS unless the described AutoCAD test was actually executed.

## Goals

Confirm that the AutoCAD command behaves correctly, source entities and generated entities remain consistent, DBMOD behavior is expected, persistence survives SAVE/REOPEN where required, UNDO/REDO behavior is not broken, generated/display entities do not leak or duplicate, and user-visible behavior matches the expected workflow.

## Test plan format

Before running the test, define:

```md
## HOST Test Plan

### Environment

- AutoCAD version:
- KROVY build:
- DWG/test file:
- Branch:
- HEAD:

### Preconditions

### Steps

1. ...
2. ...
3. ...

### Expected results

- ...

### Data to capture

- DBMOD before/after
- command line messages
- entity counts if relevant
- screenshots if relevant
- diagnostics/log output if relevant
```

## Result format

After running the test, report:

```md
## HOST Regression Test Result

| Check | Result |
| --- | --- |
| AutoCAD version | `<version>` |
| KROVY build | `<build/config>` |
| Branch | `<branch>` |
| HEAD | `<commit>` |
| DWG/test file | `<name>` |
| Test scope | `<short scope>` |
| DBMOD behavior | `PASS/FAIL/NOT CHECKED` |
| Entity lifecycle | `PASS/FAIL/NOT CHECKED` |
| Persistence | `PASS/FAIL/NOT APPLICABLE/NOT CHECKED` |
| UNDO/REDO | `PASS/FAIL/NOT APPLICABLE/NOT CHECKED` |
| Visual behavior | `PASS/FAIL/NOT CHECKED` |
| Diagnostics | `PASS/FAIL/NOT CHECKED` |

### Verdict

`PASS`, `FAIL`, or `INCONCLUSIVE`

### Observations

- ...

### Evidence

- command output
- screenshots
- logs
- measured values

### Follow-up

- ...
```

## PASS criteria

HOST PASS requires actual AutoCAD execution, expected user-visible behavior, expected DBMOD behavior where relevant, no leaked or duplicated generated/display entities, persistence verified if the feature stores data, no unexpected command-line errors, and no unsupported inferred behavior.

## FAIL criteria

HOST FAIL if AutoCAD throws unexpected errors, DBMOD changes unexpectedly, generated entities duplicate or leak, metadata ownership breaks, display/entities go stale, SAVE/REOPEN loses required data, UNDO/REDO behavior regresses, or test steps cannot be completed.

## Stop rules

Stop and report INCONCLUSIVE if the DWG/test setup is unclear, the build is not the intended build, the test cannot be reproduced, manual input deviated from the plan, AutoCAD crashed or test environment is unstable, or required screenshots/logs are missing for a visual regression.

## Human approval

HOST regression requires human observation or trusted AutoCAD execution.

The agent must not mark a HOST test as passed from repository inspection alone.

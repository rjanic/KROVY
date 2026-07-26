# Repository instructions for AI agents

Before starting a task, read the relevant files in `.ai/`.

Use this source-of-truth order:

1. Current source code.
2. Automated tests.
3. `.ai` architecture rules.
4. `ACAD_KROVY_PROJECT_CONTEXT.md`.
5. `ACAD_KROVY_ROADMAP.md` and `ACAD_KROVY_BACKLOG.md`.

Keep `AcKrovy.Core` free of Autodesk dependencies. Commit and push only after all required verification succeeds. Never create a release tag without an explicit user request. Before prompts that ask Codex to perform substantial work, give the user an explicit model recommendation.

- [.ai/architecture.md](.ai/architecture.md)
- [.ai/cad-abstractions.md](.ai/cad-abstractions.md)
- [.ai/localization.md](.ai/localization.md)
- [.ai/testing.md](.ai/testing.md)
- [.ai/release-process.md](.ai/release-process.md)
- [.ai/roadmap.md](.ai/roadmap.md)

# KROVY Skill: Localization Check

## Purpose

Use this skill to validate localization resources in ACAD KROVY.

KROVY currently uses multiple languages and must keep localization keys, placeholders, and UI intent consistent.

## When to use

Use this skill after changes that affect UI text, command messages, validation messages, settings labels, tooltips, report text, error messages, release notes shown inside the product, or localization resource files.

## Languages

Check all supported project languages.

Known current language set:

- Slovak
- Czech
- English
- German
- Polish
- French

If the repository contains additional languages, include them.

## Goals

Confirm that all languages contain the same key set, placeholders match across languages, formatting tokens are preserved, no translation is obviously in the wrong language, no accidental duplicate or orphaned key was introduced, and command names and technical identifiers remain consistent.

## Placeholder rules

The following must be preserved exactly across languages:

- `{0}`, `{1}`, `{2}`
- `%s`, `%d`
- `{Name}` style tokens
- command names such as `AK_ROOF`, `AK_SETTINGS`
- schema identifiers
- file extensions
- units where intentionally fixed

Do not translate code identifiers.

## Validation checklist

- [ ] Identify localization files
- [ ] Compare key sets across languages
- [ ] Compare placeholder sets across matching keys
- [ ] Check touched keys for obvious wrong-language text
- [ ] Check missing translations
- [ ] Check duplicate keys if format allows
- [ ] Check resource loading/build result if available
- [ ] Report exact affected files

## PASS criteria

Localization check is PASS only if key sets match, placeholders match, resource files parse/build, no touched translation is obviously broken, and all missing translations are either fixed or explicitly reported.

## FAIL criteria

Localization check is FAIL if any language is missing a required key, placeholders differ, resource file format is invalid, translations are shifted into the wrong language, or the agent cannot determine parity but claims success.

## Output format

```md
## Localization Check

| Check | Result |
| --- | --- |
| Languages checked | `<list>` |
| Files checked | `<count/list>` |
| Key parity | `PASS/FAIL/NOT CHECKED` |
| Placeholder parity | `PASS/FAIL/NOT CHECKED` |
| Resource format/build | `PASS/FAIL/NOT RUN` |
| Wrong-language scan | `PASS/FAIL/NOT CHECKED` |

### Verdict

`PASS` or `FAIL`

### Issues

- List exact key/file/language problems.

### Notes

- Mention commands or scripts used.
- Mention skipped checks clearly.
```

## Human approval

Do not invent translations for user-facing text if the meaning is uncertain.

For ambiguous product terminology, ask for human review.

# Localization guidance for AI agents

## Purpose

Keep all user-facing UI consistently localized while preserving stable domain values and user-entered CAD data.

## Hard rules

- Supported language codes are exactly `sk`, `cs`, `en`, `de`, `pl`, and `fr`.
- Use `UiStrings` resources for visible text, tooltips, accessibility names, validation messages and status banners.
- Every invariant key must exist in all six language resources with identical composite-format placeholders.
- Persist stable enums/IDs, never `DisplayName`.
- Do not translate user-persisted `Material`, layer names, linetype names or `CustomElementName`.
- Do not translate technical CAD names.
- Runtime culture switching must preserve pending settings values.
- Replace localized ComboBox `ItemsSource` atomically; do not expose a transient `Clear()` state that can reset selection.

## Current architecture or workflow

`AppLanguageService` defines the supported cultures, sets the active culture and publishes runtime changes. `UiStrings.resx` is the invariant Slovak resource; `UiStrings.cs.resx`, `.en`, `.de`, `.pl` and `.fr` are peers.

Display-name providers convert stable enum values to resources at the UI boundary. Settings rebuilds localized navigation, annotation presets, layer-color labels and language-card accessibility text without changing the underlying selected enums, codes or edited row values. Language cards use stable culture codes and embedded flag resources.

Settings language selection uses a one-way presentation binding and the
`ApplicationLanguageWorkflow` as the single persistence path. Loading,
binding initialization and runtime refresh never save
`application-settings.json`; only a real user change applies, persists and
refreshes localized host UI once.

Productivity command windows use the same six resource packs and Light/Dark theme dictionaries. `TimberCsvLocalizationProvider` supplies localized headers and display values at the localization boundary; the Core CSV formatter receives typed localization data and never references resources directly. Command names and canonical material values remain technical and invariant.

To add a key:

1. Add it to invariant `UiStrings.resx`.
2. Add the same key to all five satellite resources.
3. Preserve exactly the same `{0}`, `{1}`, and other placeholders.
4. Consume it through the generated `UiStrings` API or the established resource lookup.
5. Refresh it in the relevant runtime-culture handler.
6. Add/update parity and runtime-switch tests.

## Common failure modes

- A raw resource key appears because one satellite resource is missing.
- Placeholder order/count differs and formatting fails only in one culture.
- Replacing `DisplayName` also replaces or persists the domain enum.
- Calling `Items.Clear()` fires selection changes and destroys pending edits.
- Hardcoded WPF `Content`, `Text`, tooltip, automation name or banner bypasses resources.
- Translating a layer or linetype makes an existing DWG/profile reference invalid.

## Checklist before changing this area

- Add the invariant key.
- Add all six translations.
- Verify placeholder parity.
- Verify runtime refresh and preservation of pending values.
- Search for hardcoded user-facing UI text.
- Confirm no translated value is persisted.
- Test every supported culture and raw-key detection.

## Relevant source files and tests

- `src/AcKrovy.Localization/AppLanguageService.cs`
- `src/AcKrovy.Localization/Resources/UiStrings.resx`
- `src/AcKrovy.Localization/Resources/UiStrings.cs.resx`
- `src/AcKrovy.Localization/Resources/UiStrings.en.resx`
- `src/AcKrovy.Localization/Resources/UiStrings.de.resx`
- `src/AcKrovy.Localization/Resources/UiStrings.pl.resx`
- `src/AcKrovy.Localization/Resources/UiStrings.fr.resx`
- `src/AcKrovy.Localization/TimberAnnotationModeDisplayNameProvider.cs`
- `src/AcKrovy.Localization/SettingsAnnotationPresetDisplayNameProvider.cs`
- `src/AcKrovy.Localization/TimberCsvLocalizationProvider.cs`
- `src/AcKrovy.AutoCAD/UI/LayerSettingsWindow.xaml.cs`
- `src/AcKrovy.Core.Tests/LocalizationFoundationTests.cs`
- `src/AcKrovy.Core.Tests/LocalizationLanguagePackTests.cs`
- `src/AcKrovy.Core.Tests/SettingsRuntimeLocalizationTests.cs`

See [testing.md](testing.md) for required localization regressions.

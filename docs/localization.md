# Localization (UI languages)

ModBuilder's interface text lives in standard .NET resource files. A translation is a data file: no Razor component changes to add or
update one.

## Supported languages

| Id | Language | Resources | Notes |
| --- | --- | --- | --- |
| `en` | English | `Strings.resx` (neutral) | Every key exists here. Anything missing elsewhere falls back to it. |
| `zh-Hans` | Simplified Chinese · 简体中文 | `Strings.zh-Hans.resx` | Originated from the translation contributed by **@CChusky** in [GitHub issue #1](https://github.com/SkyeShade/HD2Runtime-ModBuilder/issues/1). |

**Settings & SDK → Language** offers:

- **System default**, which follows the OS UI language (`zh-CN` and `zh-SG` map to `zh-Hans`; anything unsupported uses English);
- **English**;
- **简体中文 · Simplified Chinese**.

The choice is stored as `system`, `en` or `zh-Hans` in `preferences.json` in the data folder (`%LOCALAPPDATA%\HD2RuntimeGUI`, or `HD2RUNTIMEGUI_DATA_ROOT`).

- **At start-up:** the preference is applied in `MauiProgram` before anything renders. It sets the UI culture of the process and its threads.
- **When you change it:** every component re-renders in place (`LocalizedComponentBase`, cascaded from `Routes.razor`). The open project, unsaved drafts (including custom Lua), the bound SDK and page state are unchanged.
- **The page:** `<html lang>` and `dir` follow the language (`hd2Ui.setLanguage` in `wwwroot/i18n.js`), so a future right-to-left language only needs `RightToLeft: true` in its registry entry.

Only the UI language changes. Dates shown in the UI use the language's pattern (`*.DateTime.Format`); number formatting keeps the system culture. Nothing ModBuilder generates depends on either: Lua,
project JSON and exported ZIPs are byte-identical in every language, including under a decimal-comma formatting culture. The
`LocalizationTests` check this.

## Where the text lives

```
HD2RuntimeGUI.Core/Resources/Strings/Strings.resx           English (neutral), NeutralLanguage=en
HD2RuntimeGUI.Core/Resources/Strings/Strings.<culture>.resx  one per translation → <culture>/HD2RuntimeModBuilder.Core.resources.dll
HD2RuntimeGUI.Core/Localization/UiLanguages.cs               the registry of offered languages (+ plural rules)
HD2RuntimeGUI.Core/Localization/Text.cs                      IUiText / UiText / CoreText / TextResources
HD2RuntimeGUI.Core/Localization/LanguageService.cs           the preference (load, save, apply, change event)
```

**In Razor components,** `T` is injected everywhere through `_Imports.razor`:

| Call | For |
| --- | --- |
| `@T["Area.Element"]` | plain text |
| `@T.Format("Area.Element", a, b)` | text with `{0}`, `{1}` placeholders |
| `@T.Plural("Area.Element", count, …)` | counted text (`Area.Element.One` / `.Other`; the count is `{0}`) |
| `@T.Or("Area.Element", sdkText)` | an optional translation of text that may come from the SDK; unknown values show as published |
| `@T.Markup("Area.Element.Html", …)` | a sentence with inline `<code>`, `<strong>`, `<em>` or `<br>`; arguments are HTML-encoded |

**Core code** that produces UI text (workspace and build messages, navigation labels, snippet descriptions, asset status) uses
`CoreText.Get / Format / Plural / Or`, which reads the same resources in the current UI language.

**Fallback:**

- A key a translation lacks, or leaves empty, shows the English text.
- A key no resource has shows the key itself, so a label is never blank. The tests make both cases a build failure.
- A translated template whose placeholders don't fit the arguments shows the English sentence.

## Key naming

- Keys are stable semantic ids, never derived from the English text: `Area.Element` or `Area.Sub.Element`, PascalCase segments. For example:
  - `Settings.Language.Title`
  - `CustomLua.Reload`
  - `DropPod.LivePair`
  - `Messages.Build.Composition.AmbiguousExplosionSource`
- Roles by suffix: `.Title` (heading), `.Help` / `.Description` (paragraph), `.Label`, `.Placeholder`, `.Tooltip`, `.Empty`, `.Error`, `.Warning`.
- `Common.*` holds the few truly generic words (Save, Cancel, Read-only, …). Context-specific text gets its own key, even if its English is the same, so a translation can differ.
- Counted text: `Key.One` and `Key.Other`. Other CLDR categories (`Zero`, `Two`, `Few`, `Many`) are used when a language's rule in `UiLanguages` returns them.
- `.Html` keys are the only ones that may contain markup, and only `<code>`, `<strong>`, `<em>` and `<br>`.
- `*.DateTime.Format` values are .NET date and time format patterns, not sentences: a translation gives its natural order (`yyyy年M月d日 HH:mm`).
- Keys whose English equals another key's are deliberate when the context differs; technical words such as SDK, Lua, API, ID and Schema stay as written in translations.
- `Messages.*` are messages produced by Core (workspace, project, update, build and validation messages).

## What is never translated

These are shown exactly as published or written:

- **Game data:** weapon, stratagem, enemy and pickup names, field display names, units, evidence and provenance text, Runtime reasons, and native or debug names.
- **Identifiers:** semantic ids (`enemy/v1/…`, `output/v1/…`), hashes, package and resource ids, Lua identifiers, `hd2.*` API names, field constants and opt-in flags (`allow_shared`, …).
- **Generated content:** everything ModBuilder writes into a mod or project. That includes Lua code, custom Lua snippet *code* (their titles and descriptions are translated), the custom Lua starter text, and mod option texts that end up in the generated mod.
- **Names and technical tokens:** product names (HD2Runtime, ModBuilder, Bingus), file names (`src/addon.lua`), command-line flags and environment variables.
- **SDK diagnostics:** the SDK readers' validation messages ("Malformed …"). They are for bug reports.

SDK text that ModBuilder has no key for is simply shown, so a newer SDK with unknown fields or tiers never breaks the UI.

## Placeholders

- Use composite format placeholders `{0}`, `{1}`, … A literal brace is `{{` / `}}`.
- A translation must use exactly the same *set* of placeholders as English; the order may change. The tests check every key.
- Never build a sentence from separately translated fragments. Put the whole sentence in one resource with placeholders, and describe each placeholder in the English entry's `<comment>`.

## Adding a language

1. Copy `Strings.resx` to `Strings.<culture>.resx`, using a .NET culture name such as `de`, `fr`, `ja` or `pt-BR`. Or run `node tools/i18n/resx.mjs new <culture>`, which creates the file with every key.
2. Translate the `<value>` of every entry. Keep the keys, placeholders and `.Html` tags.
3. Register it with one line in `UiLanguages.Supported` (`HD2RuntimeGUI.Core/Localization/UiLanguages.cs`). Give its id, English name and native name, plus `RightToLeft: true` for a right-to-left script. If its plural rule differs from English's one/other, add the rule.
4. Build. The satellite assembly is produced automatically, and the language appears in Settings.
5. Test: `dotnet test HD2RuntimeGUI.Tests --filter LocalizationTests`. It reports missing keys, empty values and placeholder mismatches. `node tools/i18n/resx.mjs check` does the same without building.

A new English key makes `Every_supported_language_has_every_key_with_the_same_placeholders` fail until each translation has it.
`node tools/i18n/resx.mjs missing zh-Hans` lists what to translate.

## Testing a translation

- **Unit tests:** `LocalizationTests` cover:
  - resource integrity (keys, placeholders, markup);
  - every key used by the source exists, and every resource is used;
  - fallback, plural rules and the preference;
  - identical Lua, JSON and ZIP output across languages.
- **In the app:** Settings & SDK → Language switches immediately. Check long labels in the sidebar, tabs, buttons, badges and banners.
- **Desktop smoke:** `tools/language-smoke.mjs` switches to zh-Hans and back and checks that the main screens re-render (see the file header for how to launch the app).

## zh-Hans provenance

`Strings.zh-Hans.resx` records where each translation came from in its `<comment>`:

- `issue #1`: the contributor's translation (@CChusky, GitHub issue #1, extracted from v1.3.0), used verbatim.
- `issue #1 (adapted)`: the contributor's translation, adjusted minimally where the English was reworded, split or merged since v1.3.0.
- `new: needs native review`: text added after v1.3.0 (Runtime 0.28 work, enemies, custom Lua and others), translated with the contributor's terminology, awaiting review by a native speaker.

The contributor's bundle keys were derived from the English text. ModBuilder uses stable semantic keys instead. The Chinese values
were matched by their English source text, not by the old keys.

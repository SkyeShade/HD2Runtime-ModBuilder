using System.Globalization;

namespace HD2RuntimeGUI.Core.Localization;

// CLDR plural categories. Resource keys for counted text end in the category name (Key.One, Key.Other, …); every counted key has
// Key.Other. English uses One / Other, Simplified Chinese only Other; a future language adds its rule to its registry entry.
public enum PluralCategory { Zero, One, Two, Few, Many, Other }

/// <summary>One UI language ModBuilder ships resources for.</summary>
/// <param name="Id">Culture name of its resource file (Strings.&lt;Id&gt;.resx) and of the html lang attribute.</param>
/// <param name="EnglishName">Name in English (Settings shows it next to the native name).</param>
/// <param name="NativeName">Name in the language itself.</param>
/// <param name="RightToLeft">Sets dir="rtl" on the page.</param>
/// <param name="Plural">Plural category for a count; English's one/other rule when omitted.</param>
public sealed record UiLanguage(string Id, string EnglishName, string NativeName, bool RightToLeft = false, Func<long, PluralCategory>? Plural = null)
{
    public CultureInfo Culture => CultureInfo.GetCultureInfo(Id);
    public PluralCategory PluralOf(long count) => (Plural ?? UiLanguages.EnglishPlural)(count);
    public string Direction => RightToLeft ? "rtl" : "ltr";
}

// The languages ModBuilder offers. Adding a language: add Strings.<culture>.resx next to Strings.resx and one entry here
// (see docs/localization.md). English is the neutral language: every key exists in Strings.resx, and any key a translation
// lacks (or leaves empty) is shown in English.
public static class UiLanguages
{
    public const string System = "system", English = "en", SimplifiedChinese = "zh-Hans";
    internal static PluralCategory EnglishPlural(long n) => n == 1 ? PluralCategory.One : PluralCategory.Other;

    public static readonly IReadOnlyList<UiLanguage> Supported =
    [
        new(English, "English", "English", Plural: EnglishPlural),
        new(SimplifiedChinese, "Simplified Chinese", "简体中文", Plural: _ => PluralCategory.Other),
    ];
    public static UiLanguage Neutral => Supported[0];

    public static UiLanguage? Find(string? id) => Supported.FirstOrDefault(l => string.Equals(l.Id, id, StringComparison.OrdinalIgnoreCase));
    /// <summary>A saved preference ("system", "en", "zh-Hans"; anything else counts as "system") is valid as stored.</summary>
    public static string NormalizePreference(string? preference) => Find(preference)?.Id ?? System;
    /// <summary>The language for a preference: an explicit supported language, or for "system" the first supported language on the
    /// culture's parent chain (zh-CN → zh-Hans). Anything unsupported falls back to English.</summary>
    public static UiLanguage Resolve(string? preference, CultureInfo systemUiCulture)
    {
        if (Find(preference) is { } chosen) return chosen;
        for (var culture = systemUiCulture; !string.IsNullOrEmpty(culture.Name); culture = culture.Parent)
            if (Find(culture.Name) is { } match) return match;
        return Neutral;
    }
}

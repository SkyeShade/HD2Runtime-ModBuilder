using System.Globalization;
using System.Net;
using System.Resources;

namespace HD2RuntimeGUI.Core.Localization;

/// <summary>ModBuilder's UI text, from Resources/Strings/Strings.resx (English, neutral) and Strings.&lt;culture&gt;.resx.</summary>
/// <remarks>
/// Keys are stable semantic ids (Area.Element), never the English text. Lookups fall back from the UI language to English and, for a
/// key no resource has, to the key itself, so a label is never blank. Counted text uses Key.One / Key.Other (CLDR categories) with the
/// count as {0}. Keys ending in ".Html" may contain &lt;code&gt;, &lt;strong&gt;, &lt;em&gt; and &lt;br&gt;; their arguments are
/// HTML-encoded. SDK-provided text (field names, evidence, reasons) is not a key: use <see cref="Or"/> where ModBuilder owns an optional
/// translation, so unknown SDK text still shows as published.
/// </remarks>
public interface IUiText
{
    string this[string key] { get; }
    string Format(string key, params object?[] args);
    string Plural(string key, long count, params object?[] args);
    /// <summary>The translation of an optional key, or <paramref name="fallback"/> (for example SDK text) when no resource has it.</summary>
    string Or(string key, string fallback);
    /// <summary>A ".Html" key formatted with HTML-encoded arguments, for rendering as markup.</summary>
    string Html(string key, params object?[] args);
    UiLanguage Language { get; }
    CultureInfo Culture { get; }
}

public static class TextResources
{
    public const string BaseName = "HD2RuntimeGUI.Core.Resources.Strings.Strings";
    public static readonly ResourceManager Manager = new(BaseName, typeof(TextResources).Assembly);

    /// <summary>The text for a key in a culture (falling back to English when the translation lacks it or leaves it empty), or null.</summary>
    public static string? Find(string key, CultureInfo culture)
    {
        var value = Manager.GetString(key, culture);
        if (string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(culture.Name)) value = Manager.GetString(key, CultureInfo.InvariantCulture);
        return string.IsNullOrEmpty(value) ? null : value;
    }
    public static string Get(string key, CultureInfo culture) => Find(key, culture) ?? key;
    public static string Format(string key, CultureInfo culture, object?[] args) =>
        SafeFormat(culture, Get(key, culture), Find(key, CultureInfo.InvariantCulture) ?? key, args);
    /// <summary>Formats a translated template; one whose placeholders do not fit the arguments (the tests prevent this) shows the English
    /// sentence instead, and an unusable English template shows as written.</summary>
    public static string SafeFormat(CultureInfo culture, string template, string neutral, object?[] args)
    {
        try { return string.Format(culture, template, args); }
        catch (FormatException)
        {
            try { return string.Format(culture, neutral, args); } catch (FormatException) { return neutral; }
        }
    }
    public static string Plural(string key, UiLanguage language, CultureInfo culture, long count, object?[] args)
    {
        var category = language.PluralOf(count);
        var chosen = category != PluralCategory.Other && Find(key + "." + category, culture) != null ? key + "." + category : key + ".Other";
        return Format(chosen, culture, [count, ..args]);
    }
    public static string Html(string key, CultureInfo culture, object?[] args) =>
        Format(key, culture, args.Select(a => (object?)WebUtility.HtmlEncode(Convert.ToString(a, culture) ?? "")).ToArray());
}

/// <summary>The current UI language for everything that renders text: set at start-up and on every language change.</summary>
public static class UiCulture
{
    private static UiLanguage global = UiLanguages.Neutral;
    private static readonly AsyncLocal<UiLanguage?> scoped = new();
    public static UiLanguage Language => scoped.Value ?? global;
    public static CultureInfo Culture => Language.Culture;
    /// <summary>The UI language of the process: UI text, and the UI culture of this and new threads.</summary>
    public static void Apply(UiLanguage value)
    {
        global = value;
        CultureInfo.DefaultThreadCurrentUICulture = value.Culture;
        CultureInfo.CurrentUICulture = value.Culture;
    }
    /// <summary>A UI language for this async flow only (tests and tooling), without touching the process language.</summary>
    public static IDisposable Use(UiLanguage value)
    {
        var previous = scoped.Value; scoped.Value = value;
        return new Scope(() => scoped.Value = previous);
    }
    private sealed class Scope(Action restore) : IDisposable { public void Dispose() => restore(); }
}

/// <summary>The IUiText service: always the current UI language, so a component re-rendered after a language change shows it.</summary>
public sealed class UiText : IUiText
{
    public UiLanguage Language => UiCulture.Language;
    public CultureInfo Culture => UiCulture.Culture;
    public string this[string key] => TextResources.Get(key, Culture);
    public string Format(string key, params object?[] args) => TextResources.Format(key, Culture, args);
    public string Plural(string key, long count, params object?[] args) => TextResources.Plural(key, Language, Culture, count, args);
    public string Or(string key, string fallback) => TextResources.Find(key, Culture) ?? fallback;
    public string Html(string key, params object?[] args) => TextResources.Html(key, Culture, args);
}

/// <summary>UI text for Core code that is not a component (messages shown to the user). Same resources and language as IUiText.</summary>
public static class CoreText
{
    private static readonly UiText Text = new();
    public static string Get(string key) => Text[key];
    public static string Format(string key, params object?[] args) => Text.Format(key, args);
    public static string Plural(string key, long count, params object?[] args) => Text.Plural(key, count, args);
    public static string Or(string key, string fallback) => Text.Or(key, fallback);
}

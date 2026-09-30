using System.Globalization;
using System.Text.Json;
using HD2RuntimeGUI.Core.Storage;

namespace HD2RuntimeGUI.Core.Localization;

/// <summary>Settings → Language: "system", "en" or "zh-Hans", stored in preferences.json in the data root.</summary>
public sealed record UiPreferences(string Language = UiLanguages.System);

/// <summary>The UI language preference: loaded and applied before the first render, changed live from Settings.</summary>
/// <remarks>Only the UI language changes. Number and date formatting keep the system's culture, and nothing ModBuilder generates
/// (Lua, project JSON, exported mods) depends on either.</remarks>
public sealed class LanguageService
{
    public const string FileName = "preferences.json";
    private readonly AppPaths paths;
    private readonly CultureInfo system;
    private readonly Action<UiLanguage> apply;
    /// <param name="apply">Makes a language current; the process-wide <see cref="UiCulture.Apply"/> unless a test supplies its own.</param>
    public LanguageService(AppPaths paths, CultureInfo systemUiCulture, Action<UiLanguage>? apply = null)
    {
        this.paths = paths; system = systemUiCulture; this.apply = apply ?? UiCulture.Apply;
        Preference = Load(paths).Language;
        Language = UiLanguages.Resolve(Preference, system);
    }
    /// <summary>The saved choice: "system", "en" or "zh-Hans".</summary>
    public string Preference { get; private set; }
    /// <summary>The language shown: the explicit choice, or for "system" the OS UI language when ModBuilder supports it, else English.</summary>
    public UiLanguage Language { get; private set; }
    /// <summary>The OS UI language ModBuilder started with (what "System default" follows).</summary>
    public CultureInfo SystemCulture => system;
    public event Action? Changed;

    public static string SettingsPath(AppPaths paths) => Path.Combine(paths.Root, FileName);
    /// <summary>Reads the saved preference. A missing or unreadable file (or an unsupported language) is "system", so start-up never fails.</summary>
    public static UiPreferences Load(AppPaths paths)
    {
        try
        {
            var file = SettingsPath(paths);
            if (!File.Exists(file) || new FileInfo(file).Length > 64 * 1024) return new();
            using var json = JsonDocument.Parse(File.ReadAllBytes(file));
            var language = json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("language", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() : null;
            return new(UiLanguages.NormalizePreference(language));
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    /// <summary>Makes the current language the UI language (UI text, and the UI culture of this and new threads).</summary>
    public void Apply() => apply(Language);
    /// <summary>Saves a new preference and applies it; listeners re-render. The file is written before anything changes.</summary>
    public async Task SetPreferenceAsync(string preference)
    {
        var normalized = UiLanguages.NormalizePreference(preference);
        await JsonStorage.WriteAtomicAsync(SettingsPath(paths), new UiPreferences(normalized));
        Preference = normalized; Language = UiLanguages.Resolve(normalized, system);
        Apply();
        Changed?.Invoke();
    }
}

using System.Text.Json;
using HD2RuntimeGUI.Core.Localization;

namespace HD2RuntimeGUI.Core.Metadata;

// Runtime 0.26.0 fire modes and third-person reticle (player and support weapons).
// fire_mode.modes is the weapon's native four-slot FireMode list as an ordered set of named modes: the first entry is the
// default; empty slots are None. Only Automatic (1), Single (2) and Burst (3) are named; charge/safety values (4-8) are
// never offered. A weapon with a bound selector takes up to maxModes entries; a single-mode weapon takes exactly one.
public static class FireModes
{
    public const string ModesField = "fire_mode.modes", BurstField = "fire_mode.burst_rounds", ReticleField = "weapon.third_person_reticle";
    public static readonly IReadOnlyDictionary<string, int> Native = new Dictionary<string, int> { ["automatic"] = 1, ["single"] = 2, ["burst"] = 3 };
    public static readonly string[] States = ["selectable", "single_mode", "blocked", "absent"];
    // Label is language-neutral: WeaponCapability.Format uses it, and that text is compared and re-parsed. DisplayLabel is the same
    // name in the UI language, for display only; an unnamed mode token shows as published.
    public static string Label(string mode) => mode switch { "automatic" => "Automatic", "single" => "Single", "burst" => "Burst", _ => mode.Replace('_', ' ') };
    public static string DisplayLabel(string mode) => mode switch
    {
        "automatic" => CoreText.Get("FireMode.Mode.Automatic"), "single" => CoreText.Get("FireMode.Mode.Single"), "burst" => CoreText.Get("FireMode.Mode.Burst"),
        _ => Label(mode),
    };

    public static IReadOnlyList<string> Modes(JsonElement value) =>
        value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()! : throw new InvalidDataException("Fire modes are named modes.")).ToArray()
            : throw new InvalidDataException("Fire modes are an ordered list of modes.");

    // Published capability: baseline, allowed modes, native slot encoding and selector agree (editable fields only carry a baseline).
    public static void ValidateCapability(string? state, JsonElement baseline, bool editable, IReadOnlyList<string>? allowed, IReadOnlyDictionary<string, int>? values,
        int? maxModes, IReadOnlyList<int>? slots)
    {
        if (state is not null && !States.Contains(state)) throw new InvalidDataException("Unknown fire-mode state.");
        if (!editable && baseline.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return;
        if (allowed is not { Count: > 0 } || values == null || allowed.Any(m => !Native.TryGetValue(m, out var n) || values.GetValueOrDefault(m) != n)
            || maxModes is not (>= 1 and <= 4) || slots is not { Count: 4 } || state is not ("selectable" or "single_mode") || (state == "single_mode") != (maxModes == 1))
            throw new InvalidDataException("Inconsistent fire-mode capability.");
        var modes = Modes(baseline);
        if (modes.Count < 1 || modes.Count > maxModes || modes.Distinct().Count() != modes.Count || modes.Any(m => !allowed.Contains(m))
            || !slots.SequenceEqual(modes.Select(m => values[m]).Concat(Enumerable.Repeat(0, 4 - modes.Count))))
            throw new InvalidDataException("Fire-mode baseline disagrees with its native slots.");
    }
    public static void ValidateCapability(WeaponCapability f) =>
        ValidateCapability(f.FireModeState, f.CurrentDefault, f.Editable, f.AllowedModes, f.ModeValues, f.MaxModes, f.NativeSlots);

    // A desired mode set: 1..maxModes distinct allowed modes. The order is the in-game order; the first is the default.
    public static void ValidateValue(JsonElement value, IReadOnlyList<string> allowed, int maxModes)
    {
        var modes = Modes(value);
        if (modes.Count < 1) throw new InvalidDataException(CoreText.Get("FireMode.Error.AtLeastOne"));
        if (modes.Count > maxModes) throw new InvalidDataException(maxModes == 1 ? CoreText.Get("FireMode.Error.SingleMode") : CoreText.Plural("FireMode.Error.AtMost", maxModes));
        if (modes.Distinct().Count() != modes.Count) throw new InvalidDataException(CoreText.Get("FireMode.Error.Duplicate"));
        if (modes.FirstOrDefault(m => !allowed.Contains(m)) is { } bad) throw new InvalidDataException(CoreText.Format("FireMode.Error.NotAllowed", DisplayLabel(bad)));
    }
    public static bool Equal(JsonElement a, JsonElement b) => a.ValueKind == JsonValueKind.Array && b.ValueKind == JsonValueKind.Array && Modes(a).SequenceEqual(Modes(b));
    public static string Lua(JsonElement value) => "{" + string.Join(",", Modes(value).Select(m => "'" + m + "'")) + "}";
    public static string Text(JsonElement value) => string.Join(", ", Modes(value).Select(Label));
    public static string DisplayText(JsonElement value) => string.Join(", ", Modes(value).Select(DisplayLabel));
}

// WeaponFireModeCapabilities.json (hd2runtime.weapon.fire_modes.v1): every player and support weapon's mode set and state, including
// the observed modes of read-only weapons. Its writable set must agree exactly with the editable fire_mode.modes fields.
// Modes of read-only weapons may include unnamed native values (4-8, charge/safety states): they are shown, never offered.
public sealed record WeaponFireModeEntry(string Kind, string Weapon, string State, JsonElement[]? Modes, JsonElement? DefaultMode, int? MaxModes, int? BurstRounds,
    bool Writable, string? Reason, bool? SelectorBound = null)
{
    // Display only (the observed modes of a read-only weapon), in the UI language.
    public IEnumerable<string> ModeLabels => (Modes ?? []).Select(m => m.ValueKind == JsonValueKind.String ? FireModes.DisplayLabel(m.GetString()!) : CoreText.Format("FireMode.NativeValue", m.GetRawText()));
    public IEnumerable<string> NamedModes => (Modes ?? []).Where(m => m.ValueKind == JsonValueKind.String && FireModes.Native.ContainsKey(m.GetString()!)).Select(m => m.GetString()!);
}
public sealed record WeaponFireModeCatalog(string Contract, int SchemaVersion, string Hd2RuntimeVersion, WeaponFireModeEntry[] Weapons)
{
    public WeaponFireModeEntry? Find(string kind, string weapon) => Weapons.FirstOrDefault(w => w.Kind == kind && w.Weapon == weapon);
}
public static class WeaponFireModeReader
{
    public const string FileName = "WeaponFireModeCapabilities.json", Contract = "hd2runtime.weapon.fire_modes.v1";
    public const int MaxBytes = 2 * 1024 * 1024;
    public static WeaponFireModeCatalog Read(byte[] bytes, string version, PlayerWeaponCatalog players, SupportAuthoringCatalog? support)
    {
        void Check(bool valid) { if (!valid) throw new InvalidDataException("Inconsistent weapon fire-mode capability metadata."); }
        try
        {
            if (bytes.Length > MaxBytes) throw new InvalidDataException("Fire-mode capability file exceeds size limit.");
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 }); MetadataReader.RejectDuplicates(doc.RootElement);
            if (doc.RootElement.GetProperty("contract").GetString() != Contract || doc.RootElement.GetProperty("schemaVersion").GetInt32() != 1)
                throw new UnsupportedSdkException("Unsupported weapon fire-mode contract.");
            var c = JsonSerializer.Deserialize<WeaponFireModeCatalog>(bytes, new JsonSerializerOptions(Storage.JsonStorage.Options) { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Skip })!;
            Check(c.Hd2RuntimeVersion == version && c.Weapons.Select(w => (w.Kind, w.Weapon)).Distinct().Count() == c.Weapons.Length);
            foreach (var w in c.Weapons)
            {
                Check(w.Kind is "player" or "support" && FireModes.States.Contains(w.State) && (w.Modes ?? []).All(m => w.Writable ? m.ValueKind == JsonValueKind.String && FireModes.Native.ContainsKey(m.GetString()!) : m.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                    && (w.Writable ? w.State is "selectable" or "single_mode" && w.Reason == null : !string.IsNullOrWhiteSpace(w.Reason)));
                // The field catalogs publish the same decision: writable here exactly where fire_mode.modes is editable there.
                var editable = w.Kind == "player"
                    ? players.Weapons.FirstOrDefault(p => p.Name == w.Weapon)?.Fields.FirstOrDefault(f => f.SemanticFieldId == FireModes.ModesField) is { Editable: true } pf
                        && FireModes.Modes(pf.CurrentDefault).SequenceEqual(w.NamedModes)
                    : support?.FieldInstances.FirstOrDefault(f => f.SupportWeapon == w.Weapon && f.SemanticFieldId == FireModes.ModesField) is { Writable: true } sf
                        && FireModes.Modes(sf.Value.Baseline).SequenceEqual(w.NamedModes);
                Check(editable == w.Writable);
            }
            return c;
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or NullReferenceException or ArgumentException or InvalidOperationException)
        { throw new InvalidDataException("Malformed weapon fire-mode capability metadata.", e); }
    }
}

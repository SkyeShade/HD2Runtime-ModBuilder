using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Generation;

// In-game options (HD2Runtime 0.25.0 hd2.options, displayed by CowboyBingus Mod Options Menu, an optional dependency).
// A row binds one edited field's value; Runtime only lets an option bind `value` inside hd2.ensure, and a toggle only `enabled`.
// One master toggle is the `enabled` of every operation that contains a bound value. Everything else about the operation
// (target, expect, allow_* flags, transaction/plan grouping) is generated exactly as without options.
public sealed record OptionEnumValue(string Name, double Value);
public sealed record OptionTarget(string Key, string Domain, string Owner, string DisplayName, string Type, string? Unit, double Baseline, double Desired,
    bool Active, bool Ensure, double? RangeMin, double? RangeMax, IReadOnlyList<OptionEnumValue>? EnumValues, string? Blocker, Action<double> Check)
{
    public bool Eligible => Blocker == null;
    public bool Integer => Type == "integer";
    public bool PublishedRange => RangeMin != null || RangeMax != null;
    public string Title => Owner + " · " + DisplayName;
}

public static class ModOptionsService
{
    public const string Slider = "slider", Choice = "choice", MasterId = "enabled";
    // Mod Options Menu v1 limits as enforced by HD2Runtime 0.25.0 api/options.lua.
    public const int MaxRows = 32, MaxIdLength = 96, MaxPageIdLength = 40, MaxTitleBytes = 40, MaxLabelBytes = 64, MaxDescriptionBytes = 400,
        MinChoices = 2, MaxChoices = 16, MaxChoiceBytes = 48, MaxDecimals = 3;
    private static readonly Regex OptionId = new(@"\A[A-Za-z0-9_-]+\z", RegexOptions.CultureInvariant);
    public static readonly object OptionalDependency = new { min_version = "1.0.0", api = 1, bingus_min_release = 18 };
    // 0.25.1: hd2.options({..., fallback='default' or 'disable'}) (metadata HD2OptionsRequest.fallback?). Without Mod Options Menu,
    // 'default' (Runtime's default, recommended for generated mods) applies each option's declared default; 'disable' keeps the
    // bound operations inactive. 0.25.0 has no fallback and always keeps them inactive.
    public const string FallbackDefault = "default", FallbackDisable = "disable";
    public static bool FallbackSupported(SdkMetadata? sdk) => sdk != null && Models.SemVersion.Parse(sdk.Version).CompareTo(Models.SemVersion.Parse("0.25.1")) >= 0;
    public static bool UsesDefaultsWithoutMenu(SdkMetadata sdk, ModOptionsSettings s) => FallbackSupported(sdk) && s.Fallback != FallbackDisable;
    // Manager description note, worded as HD2Runtime's own mod builder.
    public static string DependencyNote(SdkMetadata sdk, ModOptionsSettings s) => "Optional: CowboyBingus Mod Options Menu v1+ (needs Bingus Shared Loader v18+) for in-game settings; "
        + (UsesDefaultsWithoutMenu(sdk, s) ? "without it the settings use their defaults." : "without it the configurable settings stay inactive.");

    public static bool Supported(SdkMetadata? sdk) => sdk != null && Models.SemVersion.Parse(sdk.Version).CompareTo(Models.SemVersion.Parse("0.25.0")) >= 0;
    // Binding keys: one per edited field, stable across saves.
    public static string WeaponKey(string weapon, string field) => "weapon:" + weapon + "|" + field;
    public static string ObjectKey(CompositionChange c) => "object:" + c.Weapon + "|" + c.AttackRole + "|" + c.Kind + "|" + c.Phase + "|" + c.Scalar!.SemanticFieldId;
    public static string EntityKey(string instance) => "entity:" + instance;
    public static string SupportKey(string instance) => "support:" + instance;
    public static string StratagemKey(string instance) => "stratagem:" + instance;

    public static ModOptionsSettings Defaults(ModProject p) => new()
    {
        PageId = Truncate(Regex.Replace(p.ResourceId.Split('/').Last(), "[^A-Za-z0-9_-]", "_"), MaxPageIdLength),
        Title = TruncateBytes(p.DisplayName.Trim(), MaxTitleBytes),
    };
    // Active, eligible rows; the only rows that generate Lua.
    public static IReadOnlyList<(ModOptionRow Row, OptionTarget Target)> ActiveRows(ModProject p, SdkMetadata sdk)
    {
        if (p.ModOptions is not { Enabled: true } settings || settings.Rows.Count == 0 || !Supported(sdk)) return [];
        var targets = Targets(p, sdk).ToDictionary(t => t.Key, StringComparer.Ordinal);
        return settings.Rows.Where(r => targets.TryGetValue(r.Key, out var t) && t.Eligible && t.Active).Select(r => (r, targets[r.Key])).ToArray();
    }

    public static IReadOnlyList<OptionTarget> Targets(ModProject p, SdkMetadata sdk)
    {
        var result = new List<OptionTarget>();
        if (!Supported(sdk)) return result;
        if (sdk.PlayerWeapons != null)
        {
            foreach (var g in WeaponAliasResolver.Group(sdk, p.WeaponChanges))
            {
                var f = sdk.PlayerWeapons.FindCanonicalField(g.Weapon, g.FieldId); if (f == null) continue;
                var c = g.Representative;
                result.Add(Weapon(WeaponKey(g.Weapon, g.FieldId), "weapon", g.Weapon, f, c.ExpectedValue, c.DesiredValue,
                    g.Enabled && g.Conflict == null && !WeaponScalar.IsNoOp(sdk, c), c.EnsureEnabled));
            }
            foreach (var c in p.CompositionChanges.Where(c => c.Scalar != null))
            {
                WeaponCapability f; try { f = sdk.PlayerWeapons.Field(c.Scalar!.Weapon, c.Scalar.SemanticFieldId); } catch (InvalidDataException) { continue; }
                result.Add(Weapon(ObjectKey(c), "object", c.Weapon, f, c.Scalar!.ExpectedValue, c.Scalar.DesiredValue, c.Enabled, c.EnsureEnabled));
            }
        }
        if (sdk.Entities != null)
            foreach (var c in p.EntityChanges)
            {
                EntityField f; try { f = EntityChangeService.Resolve(sdk.Entities, c); } catch (InvalidDataException) { continue; }
                var blocker = f.IsReference ? "Reference swaps cannot be bound to in-game options." : f.IsPickup ? "Drop-pod contents are pickup references and cannot be in-game options." : Scalar(f.Type);
                // Magazine attachments are identified by semantic ID; show their published name.
                var owner = f.Target.Attachment is { } a ? sdk.Entities.Attachments?.Attachment(a)?.Name ?? a : f.Target.Entity;
                result.Add(Numeric(EntityKey(c.InstanceKey), "entity", owner, f.DisplayName, f.Type, f.Unit, c.ExpectedValue, c.DesiredValue,
                    c.Enabled, c.EnsureEnabled, Finite(f.EffectiveRange?.Min), Finite(f.EffectiveRange?.Max), blocker, v => EntityScalar.CheckRange(f, Json(v))));
            }
        if (sdk.SupportAuthoring != null)
            foreach (var c in p.SupportChanges)
            {
                var f = sdk.SupportAuthoring.FieldInstances.FirstOrDefault(x => x.InstanceKey == c.InstanceKey); if (f == null) continue;
                result.Add(Numeric(SupportKey(c.InstanceKey), "support", f.SupportWeapon, f.Display.Name, f.Value.Type, f.Value.Unit, c.ExpectedValue, c.DesiredValue,
                    c.Enabled, c.EnsureEnabled, null, null, Scalar(f.Value.Type), v => SupportScalar.Normalize(f, Json(v))));
            }
        if (sdk.Stratagems != null)
            foreach (var c in p.StratagemChanges)
            {
                StratagemField f; try { f = StratagemChangeService.Resolve(sdk.Stratagems, c); } catch (InvalidDataException) { continue; }
                // 0.26.0 mission uses: a finite count Runtime lets change to other finite counts maps to an integer slider in the published
                // range; Unlimited is a token, not a number, so an edit to or from it cannot be an option.
                if (f.Type == StratagemUses.Type)
                {
                    var finite = !StratagemUses.IsUnlimited(c.ExpectedValue) && !StratagemUses.IsUnlimited(c.DesiredValue) && f.Transitions?.Contains(StratagemUses.FiniteToFinite) == true;
                    result.Add(Numeric(StratagemKey(c.InstanceKey), "stratagem", f.Target.Stratagem, f.DisplayName, "integer", f.Unit, c.ExpectedValue, c.DesiredValue,
                        c.Enabled, c.EnsureEnabled, Finite(f.Min), Finite(f.Max), finite ? null : "Unlimited mission uses are a token, not a number, so only a finite-to-finite use count can be an in-game option.",
                        v => StratagemUses.CheckTransition(f, c.ExpectedValue, StratagemUses.Normalize(f, Json(v)))));
                    continue;
                }
                result.Add(Numeric(StratagemKey(c.InstanceKey), "stratagem", f.Target.Stratagem, f.DisplayName, f.Type, f.Unit, c.ExpectedValue, c.DesiredValue,
                    c.Enabled, c.EnsureEnabled, null, null, Scalar(f.Type), v => StratagemScalar.Normalize(f, Json(v))));
            }
        return result;
    }
    public static OptionTarget? Target(ModProject p, SdkMetadata sdk, string key) => Targets(p, sdk).FirstOrDefault(t => t.Key == key);
    private static double? Finite(double? v) => v is double d && double.IsFinite(d) ? d : null;
    private static string? Scalar(string type) => type switch
    {
        "integer" or "number" => null,
        "boolean" => "Runtime binds toggle options only to an operation's enabled state, never to a field value, so boolean edits cannot be in-game options.",
        WeaponCapability.FireModeSet => "A fire-mode list is not a number, so it cannot be an in-game option.",
        _ => "Only numeric and enum fields can be in-game options.",
    };
    private static JsonElement Json(double v) => JsonSerializer.SerializeToElement(v);
    private static double Number(JsonElement v) => v.ValueKind == JsonValueKind.Number ? v.GetDouble() : double.NaN;
    // Number fields are float32: use the shortest float spelling (0.35, not 0.3499999940395355) so defaults sit on a decimal step.
    private static double Clean(string type, double v) => type == "number" && double.IsFinite(v) ? double.Parse(((float)v).ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) : v;
    private static OptionTarget Numeric(string key, string domain, string owner, string name, string type, string? unit, JsonElement expected, JsonElement desired,
        bool active, bool ensure, double? min, double? max, string? blocker, Action<double> check) =>
        new(key, domain, owner, name, type, unit, Clean(type, Number(expected)), Clean(type, Number(desired)), active, ensure, min, max, null, blocker, check);
    private static OptionTarget Weapon(string key, string domain, string owner, WeaponCapability f, JsonElement expected, JsonElement desired, bool active, bool ensure)
    {
        if (f.Type == "enum")
        {
            // Enum constants are numbers (hd2.enums.*); a choice's values are those numbers, restricted to the weapon's allowed modes.
            var values = f.EnumValues?.Where(p => p.Value.ValueKind == JsonValueKind.Number && (f.AllowedValues == null || f.AllowedValues.Contains(p.Value.GetInt32())))
                .Select(p => new OptionEnumValue(Label(p.Key), p.Value.GetDouble())).ToArray() ?? [];
            return new(key, domain, owner, f.DisplayName, "enum", f.Unit, Number(expected), Number(desired), active, ensure, null, null, values,
                values.Length >= MinChoices ? null : "This field publishes fewer than two selectable values.", v => WeaponChangeService.ValidateValue(f, Json(v)));
        }
        return Numeric(key, domain, owner, f.DisplayName, f.Type, f.Unit, expected, desired, active, ensure, f.Min, f.Max,
            f.Type is "projectile_reference" or "explosion_reference" ? "Reference swaps cannot be bound to in-game options." : Scalar(f.Type),
            v => WeaponChangeService.ValidateValue(f, Json(v)));
    }
    private static string Label(string name) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.Replace('_', ' '));

    // A sensible first row from the current edit: slider from the baseline up, default = the edited value, step on a 1-2-5 grid.
    public static ModOptionRow Suggest(ModProject p, OptionTarget t)
    {
        var settings = p.ModOptions ?? Defaults(p);
        var label = TruncateBytes(t.Owner + " " + t.DisplayName, MaxLabelBytes);
        var row = new ModOptionRow { Key = t.Key, Label = label, Id = UniqueId(settings, Slug(t.Owner + "_" + t.DisplayName)) };
        if (t.EnumValues != null)
        {
            row.Kind = Choice; row.Choices = t.EnumValues.Select(v => TruncateBytes(v.Name, MaxChoiceBytes)).ToList(); row.Values = t.EnumValues.Select(v => v.Value).ToList();
            row.DefaultIndex = Math.Max(1, row.Values.IndexOf(t.Desired) + 1); return row;
        }
        double lo = Math.Min(t.Baseline, t.Desired), hi = Math.Max(t.Baseline, t.Desired);
        var min = t.RangeMin is double rmin ? Math.Max(rmin, lo == hi ? rmin : lo) : lo;
        var max = t.RangeMax ?? (hi == 0 ? (t.Integer ? 10 : 1) : Math.Abs(hi) * 2 + (hi < 0 ? hi * 2 : 0));
        if (max <= min) max = min + (t.Integer ? 10 : 1);
        var step = StepFor(t.Integer, min, max, t.Desired);
        if (step == null) { min = t.Desired; step = StepFor(t.Integer, min, max, t.Desired) ?? (t.Integer ? 1 : 0.001); }
        row.Min = min; row.Step = step.Value; row.Default = t.Desired;
        row.Max = Round(min + Math.Floor((max - min) / step.Value + 1e-9) * step.Value);
        if (row.Max <= row.Default && row.Default < max) row.Max = Round(row.Default + step.Value);
        return row;
    }
    private static double? StepFor(bool integer, double min, double max, double target)
    {
        var raw = (max - min) / 50; var candidates = new List<double>();
        for (var e = 6; e >= -MaxDecimals; e--) foreach (var m in new[] { 5.0, 2.0, 1.0 }) candidates.Add(m * Math.Pow(10, e));
        foreach (var s in candidates.Where(s => s <= Math.Max(raw, integer ? 1 : 0.001) && (!integer || s >= 1) && s <= max - min))
        {
            var n = (target - min) / s;
            if (Math.Abs(n - Math.Round(n)) < 1e-6) return Round(s);
        }
        return null;
    }
    private static double Round(double v) => Math.Round(v, MaxDecimals + 3);
    public static string Slug(string text)
    {
        var slug = Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
        return string.IsNullOrEmpty(slug) ? "option" : Truncate(slug, 48);
    }
    public static string UniqueId(ModOptionsSettings settings, string id)
    {
        var taken = settings.Rows.Select(r => r.Id).Append(MasterId).ToHashSet(StringComparer.Ordinal);
        var candidate = id; for (var n = 2; taken.Contains(candidate); n++) candidate = Truncate(id, 44) + "_" + n;
        return candidate;
    }
    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
    public static string TruncateBytes(string s, int max)
    {
        while (Encoding.UTF8.GetByteCount(s) > max) s = s[..^1];
        return s.TrimEnd();
    }

    // Mod Options Menu / HD2Runtime rules. Build blockers for active rows; stale rows (edit removed, disabled or ineligible) are skipped.
    public static IReadOnlyList<string> Issues(ModProject p, SdkMetadata sdk)
    {
        var issues = new List<string>();
        if (p.ModOptions is not { Enabled: true } s || s.Rows.Count == 0) return issues;
        if (!Supported(sdk)) { issues.Add($"In-game options need HD2Runtime SDK 0.25.0 or newer; this project is bound to {sdk.Version}."); return issues; }
        issues.AddRange(PageIssues(s));
        if (s.Fallback is not (FallbackDefault or FallbackDisable)) issues.Add($"Unknown fallback '{s.Fallback}'.");
        else if (s.Fallback == FallbackDisable && !FallbackSupported(sdk)) issues.Add($"Keeping option-bound edits inactive without Mod Options Menu (fallback='disable') needs HD2Runtime SDK 0.25.1; SDK {sdk.Version} already keeps them inactive. Rebind to 0.25.1 or choose the default.");
        foreach (var dup in s.Rows.GroupBy(r => r.Id, StringComparer.Ordinal).Where(g => g.Count() > 1)) issues.Add($"Option id '{dup.Key}' is used more than once.");
        foreach (var dup in s.Rows.GroupBy(r => r.Key, StringComparer.Ordinal).Where(g => g.Count() > 1)) issues.Add("One edited field has more than one in-game option.");
        var active = ActiveRows(p, sdk);
        if (active.Count + 1 > MaxRows) issues.Add($"Mod Options Menu shows at most {MaxRows} options per mod (the master toggle is one); this project has {active.Count + 1}.");
        foreach (var (row, t) in active) Row(issues, s, row, t);
        return issues;
    }
    public static IReadOnlyList<string> PageIssues(ModOptionsSettings s)
    {
        var issues = new List<string>();
        if (!OptionId.IsMatch(s.PageId) || s.PageId.Length > MaxPageIdLength) issues.Add($"Options page id must be 1 to {MaxPageIdLength} letters, digits, _ or -.");
        Plain(issues, "Options page title", s.Title, MaxTitleBytes);
        Plain(issues, "Master toggle label", s.MasterLabel, MaxLabelBytes);
        if (s.MasterDescription != null) Plain(issues, "Master toggle description", s.MasterDescription, MaxDescriptionBytes);
        return issues;
    }
    public static IReadOnlyList<string> RowIssues(ModOptionsSettings s, ModOptionRow row, OptionTarget t) { var issues = new List<string>(); Row(issues, s, row, t); return issues; }
    private static void Row(List<string> issues, ModOptionsSettings s, ModOptionRow row, OptionTarget t)
    {
        var name = "Option '" + (string.IsNullOrWhiteSpace(row.Label) ? row.Id : row.Label) + "'";
        if (!OptionId.IsMatch(row.Id) || row.Id == MasterId || s.PageId.Length + 1 + row.Id.Length > MaxIdLength)
            issues.Add($"{name}: id must use letters, digits, _ or - (not '{MasterId}'), at most {MaxIdLength - s.PageId.Length - 1} characters.");
        Plain(issues, name + " label", row.Label, MaxLabelBytes);
        if (row.Description != null) Plain(issues, name + " description", row.Description, MaxDescriptionBytes);
        if (!t.Ensure) issues.Add($"{name}: in-game options need Ensure; this edit is set to apply once.");
        void Sample(double v)
        {
            // The published safe range first (clearer), then the field's own validation, as Runtime validates every sample at declaration.
            if (t.RangeMin is double lo && v < lo || t.RangeMax is double hi && v > hi) { issues.Add($"{name}: value {Format(v)} is outside the Runtime-published safe range {Format(t.RangeMin)}–{Format(t.RangeMax)}."); return; }
            try { t.Check(v); } catch (InvalidDataException e) { issues.Add($"{name}: value {Format(v)} is not accepted by {t.DisplayName}: {e.Message}"); }
        }
        if (row.Kind == Slider)
        {
            if (t.EnumValues != null) { issues.Add($"{name}: enum fields use a choice control."); return; }
            if (!double.IsFinite(row.Min) || !double.IsFinite(row.Max) || !double.IsFinite(row.Step) || !double.IsFinite(row.Default)) { issues.Add($"{name}: slider values must be finite numbers."); return; }
            if (!(row.Min < row.Max && row.Step > 0 && row.Step <= row.Max - row.Min)) { issues.Add($"{name}: slider needs min < max and 0 < step ≤ max − min."); return; }
            if (Decimals(row.Step) is not int decimals) { issues.Add($"{name}: slider step may have at most {MaxDecimals} decimal places."); return; }
            if (t.Integer && new[] { row.Min, row.Max, row.Step, row.Default }.Any(v => v != Math.Truncate(v))) issues.Add($"{name}: {t.DisplayName} is an integer field; min, max, step and default must be whole numbers.");
            if (row.Default < row.Min || row.Default > row.Max) issues.Add($"{name}: default must lie between min and max.");
            else if (Snap(row, decimals) != row.Default) issues.Add($"{name}: default must sit on a step (min + n × step).");
            foreach (var v in new[] { row.Min, row.Max, row.Default }.Concat(row.Min + row.Step < row.Max ? [Snap(row, decimals, row.Min + row.Step)] : []).Distinct()) Sample(v);
        }
        else if (row.Kind == Choice)
        {
            if (row.Choices.Count < MinChoices || row.Choices.Count > MaxChoices) issues.Add($"{name}: a choice needs {MinChoices} to {MaxChoices} entries.");
            if (row.Values.Count != row.Choices.Count) { issues.Add($"{name}: every choice needs exactly one value."); return; }
            foreach (var c in row.Choices) Plain(issues, name + " choice", c, MaxChoiceBytes);
            if (row.DefaultIndex < 1 || row.DefaultIndex > row.Choices.Count) issues.Add($"{name}: default must be one of the choices.");
            foreach (var v in row.Values) if (!double.IsFinite(v)) issues.Add($"{name}: choice values must be finite numbers."); else Sample(v);
            if (t.EnumValues != null && row.Values.Any(v => t.EnumValues.All(e => e.Value != v))) issues.Add($"{name}: choose only values this field allows.");
        }
        else issues.Add($"{name}: unknown control '{row.Kind}'.");
    }
    private static void Plain(List<string> issues, string name, string? text, int maxBytes)
    {
        if (string.IsNullOrWhiteSpace(text) || Encoding.UTF8.GetByteCount(text) > maxBytes || text.Any(char.IsControl))
            issues.Add($"{name} must be plain text of 1 to {maxBytes} bytes on one line.");
    }
    // Same step precision and snapping as HD2Runtime / Mod Options Menu.
    public static int? Decimals(double step)
    {
        for (var places = 0; places <= MaxDecimals; places++) { var scaled = step * Math.Pow(10, places); if (Math.Abs(scaled - Math.Floor(scaled + 0.5)) <= 1e-6) return places; }
        return null;
    }
    private static double Snap(ModOptionRow r, int decimals, double? value = null)
    {
        if (r.Min % 1 != 0) decimals = Math.Max(decimals, 1);
        var v = value ?? r.Default; var steps = Math.Floor((v - r.Min) / r.Step + 0.5);
        return Math.Round(Math.Min(r.Max, Math.Max(r.Min, r.Min + steps * r.Step)), decimals);
    }
    public static string Format(double? v) => v is double d ? d.ToString("R", CultureInfo.InvariantCulture) : "—";

    // Lua for the options page and the handle each bound field uses; null when nothing is bound (Lua is then unchanged).
    public static OptionBindings? Bindings(ModProject p, SdkMetadata sdk)
    {
        if (p.ModOptions is not { Enabled: true } s || s.Rows.Count == 0) return null;
        if (Issues(p, sdk) is { Count: > 0 } issues) throw new InvalidDataException("In-game options: " + issues[0]);
        var active = ActiveRows(p, sdk);
        if (active.Count == 0) return null;
        var lua = new StringBuilder();
        // The default fallback is Runtime's own default and emits no key (unchanged output); strict mode is explicit.
        lua.Append("local options=hd2.options({id=").Append(LuaGenerator.Quote(s.PageId)).Append(",title=").Append(LuaGenerator.Quote(s.Title))
            .Append(s.Fallback == FallbackDisable ? ",fallback='disable'" : "").Append("})\n");
        lua.Append("local ").Append(OptionBindings.Master).Append("=options:toggle({id=").Append(LuaGenerator.Quote(MasterId)).Append(",label=").Append(LuaGenerator.Quote(s.MasterLabel)).Append(",default=true");
        if (s.MasterDescription != null) lua.Append(",description=").Append(LuaGenerator.Quote(s.MasterDescription));
        lua.Append("})\n");
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (row, _) in active)
        {
            var variable = "option_" + row.Id.Replace('-', '_'); variables[row.Key] = variable;
            lua.Append("local ").Append(variable).Append("=options:").Append(row.Kind).Append("({id=").Append(LuaGenerator.Quote(row.Id)).Append(",label=").Append(LuaGenerator.Quote(row.Label));
            if (row.Kind == Slider) lua.Append(",min=").Append(Format(row.Min)).Append(",max=").Append(Format(row.Max)).Append(",step=").Append(Format(row.Step)).Append(",default=").Append(Format(row.Default));
            else lua.Append(",choices={").Append(string.Join(",", row.Choices.Select(LuaGenerator.Quote))).Append("},values={").Append(string.Join(",", row.Values.Select(v => Format(v))))
                .Append("},default=").Append(row.DefaultIndex.ToString(CultureInfo.InvariantCulture));
            if (row.Description != null) lua.Append(",description=").Append(LuaGenerator.Quote(row.Description));
            if (row.Gap) lua.Append(",gap=true");
            lua.Append("})\n");
        }
        return new OptionBindings(lua.ToString(), variables);
    }
}

// Handle lookup used by every Lua emitter. Binding only replaces `value=` and wraps the same operation in hd2.ensure with
// enabled=<master toggle>; nothing is split, merged or re-grouped.
public sealed class OptionBindings(string header, IReadOnlyDictionary<string, string> variables)
{
    public const string Master = "enabled";
    public string Header { get; } = header;
    public IReadOnlyDictionary<string, string> Variables { get; } = variables;
    // Several edits that Runtime writes as one value (shared objects): at most one of them may be bound.
    public string Value(IEnumerable<string?> keys, string literal)
    {
        var bound = keys.Where(k => k != null && Variables.ContainsKey(k)).Select(k => Variables[k!]).Distinct().ToArray();
        if (bound.Length > 1) throw new InvalidDataException("In-game options: two options control one shared Runtime value. Keep one of them.");
        return bound.Length == 1 ? bound[0] : literal;
    }
    public string Value(string? key, string literal) => Value([key], literal);
    public bool Bound(IEnumerable<string?> keys) => keys.Any(k => k != null && Variables.ContainsKey(k));
    // Wraps one existing request. Runtime lets only hd2.ensure own option values, so a bound operation must already be an ensure.
    public static string Wrap(OptionBindings? options, string kind, string request, bool ensure, IEnumerable<string?> keys)
    {
        var body = request.Replace("\n", "\n    ");
        if (options != null && options.Bound(keys))
        {
            if (!ensure) throw new InvalidDataException("In-game options need Ensure: an option-bound edit shares its Runtime operation with an edit set to apply once.");
            return "hd2.ensure({\n    enabled=" + Master + ",\n    " + kind + "=" + body + "\n})";
        }
        return ensure ? "hd2.ensure({\n    " + kind + "=" + body + "\n})" : "hd2." + kind + "(" + request + ")";
    }
}

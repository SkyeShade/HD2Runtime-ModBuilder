using System.Text.Json;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Storage;

namespace HD2RuntimeGUI.Core.Services;

// In-game options authoring (HD2Runtime 0.25.0+). Settings live on the project; Lua and export read them through ModOptionsService.
public sealed partial class BuilderWorkspace
{
    public bool OptionsSupported => Project != null && ModOptionsService.Supported(Metadata);
    public bool OptionsEnabled => OptionsSupported && Project!.ModOptions?.Enabled == true;
    // Cached per preview refresh (every saved edit refreshes the preview), so each field row can look up its target cheaply.
    private IReadOnlyList<OptionTarget>? optionTargets; private (object? Project, object? Metadata) optionTargetsFor;
    public IReadOnlyList<OptionTarget> OptionTargets
    {
        get
        {
            if (!OptionsSupported) return [];
            if (optionTargets == null || !ReferenceEquals(optionTargetsFor.Project, Project) || !ReferenceEquals(optionTargetsFor.Metadata, Metadata))
            {
                (optionTargets, optionTargetsFor) = (ModOptionsService.Targets(Project!, Metadata!), (Project, Metadata));
                optionCandidates.Clear();
            }
            return optionTargets;
        }
    }
    public OptionTarget? OptionTarget(string key) => OptionTargets.FirstOrDefault(t => t.Key == key);
    public ModOptionRow? OptionRow(string key) => Project?.ModOptions?.Rows.FirstOrDefault(r => r.Key == key);
    public IReadOnlyList<string> OptionIssues => Project == null || Metadata == null ? [] : ModOptionsService.Issues(Project, Metadata);
    // Rows are generated only when their edit is present, enabled and eligible.
    public bool OptionRowActive(ModOptionRow row) => OptionTarget(row.Key) is { Eligible: true, Active: true };

    // 1.6.0: a field without an edit can be exposed too. Saving its option adds an option-only edit: the field's vanilla value, which only the
    // in-game option changes. Its target is that edit's (baseline = desired = vanilla); null for a field that has an edit, is not editable or
    // cannot be an option. Cached with OptionTargets: field rows ask on every render.
    private readonly Dictionary<string, VanillaOption?> optionCandidates = new(StringComparer.Ordinal);
    private sealed record VanillaOption(OptionTarget Target, Action<ModProject> Add);
    public OptionTarget? OptionCandidate(string key)
    {
        if (!OptionsSupported || OptionTarget(key) != null) return null;
        if (!optionCandidates.TryGetValue(key, out var candidate)) optionCandidates[key] = candidate = Vanilla(key) is { Target.Eligible: true } v ? v : null;
        return candidate?.Target;
    }
    // An option-only edit: an edit kept at its vanilla value for its in-game option. Field rows show the field as unedited, with its option.
    public bool OptionOnly(EntityChange c) => Metadata != null && EntityChangeService.NoOp(Metadata, c);
    public bool OptionOnly(SupportChange c) => Metadata != null && SupportChangeService.NoOp(Metadata, c);
    public bool OptionOnly(StratagemChange c) => Metadata != null && StratagemChangeService.NoOp(Metadata, c);
    public bool OptionOnly(CompositionChange c) => Metadata != null && c.Scalar != null && compositionChanges.IsNoOp(Metadata, c);
    // The option-only edit a field would get (created exactly as typing its vanilla value into the field), or null.
    private VanillaOption? Vanilla(string key)
    {
        var (sdk, p) = (Metadata!, Project!); var (domain, parts) = ModOptionsService.ParseKey(key);
        try
        {
            switch (domain)
            {
                // Keys name the canonical field (an alias's edit is keyed by its canonical field, so its row would not keep it).
                case "weapon" when sdk.PlayerWeapons?.FindCanonicalField(parts[0], parts[1]) is { } f && f.SemanticFieldId == parts[1]:
                {
                    var value = f.CurrentDefault.GetRawText(); var c = weaponChanges.Create(sdk, parts[0], parts[1], value, true);
                    // Without its row the edit would be a no-op; as an option it is active.
                    return ModOptionsService.WeaponTarget(p, sdk, WeaponAliasResolver.Group(sdk, [c]).Single()) is { } t
                        ? new(t with { Active = true }, x => ApplyWeapon(x, parts[0], parts[1], value, true, "Gameplay", null)) : null;
                }
                case "object":
                {
                    var (weapon, role, kind, phase, field) = (parts[0], parts[1], parts[2], parts[3].Length == 0 ? null : parts[3], parts[4]);
                    if (compositionChanges.Fields(p, sdk, weapon, role, kind, phase).FirstOrDefault(f => f.SemanticFieldId == field) is not { } f) return null;
                    var value = f.CurrentDefault.GetRawText();
                    CompositionChange Create(ModProject x) => compositionChanges.CreateScalar(x, sdk, weapon, role, kind, phase, field, value, acknowledge: true);
                    return ModOptionsService.ObjectTarget(sdk, Create(p)) is { } t ? new(t, x => ApplyComposition(x, Create(x), false)) : null;
                }
                case "entity" when EntityChangeService.Catalog(sdk).Field(parts[0]) is { } f:
                {
                    var value = f.CurrentDefault.GetRawText();
                    return ModOptionsService.EntityTarget(sdk, entityChanges.Create(sdk, parts[0], value)) is { } t ? new(t, x => ApplyEntity(x, parts[0], value, false)) : null;
                }
                case "support":
                {
                    var value = SupportChangeService.Catalog(sdk).Field(parts[0]).Value.Baseline.GetRawText();
                    return ModOptionsService.SupportTarget(sdk, supportChanges.Create(sdk, parts[0], value)) is { } t ? new(t, x => ApplySupportValue(x, parts[0], value, false)) : null;
                }
                case "stratagem":
                {
                    var value = StratagemChangeService.Catalog(sdk).Field(parts[0]).CurrentDefault.GetRawText();
                    return ModOptionsService.StratagemTarget(sdk, stratagemChanges.Create(sdk, parts[0], value)) is { } t ? new(t, x => ApplyStratagem(x, parts[0], value, false)) : null;
                }
            }
        }
        // Not an editable field (read-only, unpublished, or rejected by its own rules): it cannot get an option either.
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException or KeyNotFoundException or IndexOutOfRangeException) { }
        return null;
    }
    // Removing a field's option also removes its option-only edit (the edit was kept only for the option).
    private void RemoveOptionOnlyEdit(ModProject p, string key)
    {
        var sdk = Metadata!;
        var weapons = WeaponAliasResolver.Group(sdk, p.WeaponChanges).Where(g => g.Conflict == null && ModOptionsService.WeaponKey(g.Weapon, g.FieldId) == key)
            .SelectMany(g => g.Sources).Where(c => WeaponScalar.IsNoOp(sdk, c)).ToHashSet();
        p.WeaponChanges.RemoveAll(weapons.Contains);
        p.CompositionChanges.RemoveAll(c => c.Scalar != null && ModOptionsService.ObjectKey(c) == key && compositionChanges.IsNoOp(sdk, c));
        p.EntityChanges.RemoveAll(c => ModOptionsService.EntityKey(c.InstanceKey) == key && EntityChangeService.NoOp(sdk, c));
        p.SupportChanges.RemoveAll(c => ModOptionsService.SupportKey(c.InstanceKey) == key && SupportChangeService.NoOp(sdk, c));
        p.StratagemChanges.RemoveAll(c => ModOptionsService.StratagemKey(c.InstanceKey) == key && StratagemChangeService.NoOp(sdk, c));
    }

    private Task EditOptionsAsync(Action<ModOptionsSettings> edit) => EditOptionsAsync((_, s) => edit(s));
    // edit may also change the project's edits (an option-only edit is added or removed with its option); they are rolled back together.
    private async Task EditOptionsAsync(Action<ModProject, ModOptionsSettings> edit)
    {
        var project = Project; await weaponEditGate.WaitAsync();
        try
        {
            if (project == null || !ReferenceEquals(project, Project)) throw new InvalidOperationException(CoreText.Get("Messages.Workspace.ProjectChanged"));
            if (!ModOptionsService.Supported(Metadata)) throw new InvalidDataException(CoreText.Get("Messages.Options.NeedsSdk"));
            var previous = project.ModOptions == null ? null : JsonSerializer.Deserialize<ModOptionsSettings>(JsonSerializer.Serialize(project.ModOptions, JsonStorage.Options), JsonStorage.Options);
            var format = project.FormatVersion;
            var (weapons, objects, entities, support, stratagems) = (project.WeaponChanges.ToList(), project.CompositionChanges.ToList(), project.EntityChanges.ToList(),
                project.SupportChanges.ToList(), project.StratagemChanges.ToList());
            try
            {
                project.ModOptions ??= ModOptionsService.Defaults(project);
                edit(project, project.ModOptions);
                // Format 7 adds in-game options.
                project.FormatVersion = Math.Max(project.FormatVersion, 7);
                await SaveChangesAsync();
            }
            catch
            {
                project.ModOptions = previous; project.FormatVersion = format;
                (project.WeaponChanges, project.CompositionChanges, project.EntityChanges, project.SupportChanges, project.StratagemChanges) = (weapons, objects, entities, support, stratagems);
                throw;
            }
        }
        finally { weaponEditGate.Release(); }
    }
    public Task SetModOptionsEnabledAsync(bool enabled) => EditOptionsAsync(s => s.Enabled = enabled);
    public Task SaveModOptionsPageAsync(string pageId, string title, string masterLabel, string? masterDescription) => EditOptionsAsync(s =>
    {
        s.PageId = pageId.Trim(); s.Title = title.Trim(); s.MasterLabel = masterLabel.Trim();
        s.MasterDescription = string.IsNullOrWhiteSpace(masterDescription) ? null : masterDescription.Trim();
        if (ModOptionsService.PageIssues(s).FirstOrDefault() is { } issue)
            throw new InvalidDataException(issue);
    });
    // Adds or replaces the option of one field after validating it against Runtime's limits and the field's own rules. A field without an edit
    // also gets its option-only edit, in the same save.
    public Task SaveOptionRowAsync(ModOptionRow row) => EditOptionsAsync((p, s) =>
    {
        var edited = OptionTarget(row.Key);
        var vanilla = edited == null ? Vanilla(row.Key) : null;
        var target = edited ?? vanilla?.Target ?? throw new InvalidDataException(CoreText.Get("Messages.Options.EditMissing"));
        if (!target.Eligible) throw new InvalidDataException(target.Blocker!);
        row.Label = row.Label.Trim(); row.Id = row.Id.Trim(); row.Description = string.IsNullOrWhiteSpace(row.Description) ? null : row.Description.Trim();
        if (s.Rows.Any(r => r.Key != row.Key && r.Id == row.Id)) throw new InvalidDataException(CoreText.Format("Messages.Options.IdInUse", row.Id));
        if (ModOptionsService.RowIssues(s, row, target).FirstOrDefault() is { } issue) throw new InvalidDataException(issue);
        var index = s.Rows.FindIndex(r => r.Key == row.Key);
        // A new row, or the inactive row of a removed edit that this save brings back, takes one of the menu's rows.
        if ((index < 0 || vanilla != null) && s.Rows.Count(r => r.Key != row.Key && OptionTarget(r.Key) is { Eligible: true, Active: true }) + 2 > ModOptionsService.MaxRows)
            throw new InvalidDataException(CoreText.Format("Messages.Options.TooMany", ModOptionsService.MaxRows));
        if (index >= 0) s.Rows[index] = row; else s.Rows.Add(row);
        // The row first: it is what keeps the vanilla-valued edit.
        vanilla?.Add(p);
    });
    // 0.25.1+: what option-bound edits do without Mod Options Menu (declared defaults, or inactive).
    public Task SetOptionsFallbackAsync(string fallback) => EditOptionsAsync(s =>
    {
        if (fallback is not (ModOptionsService.FallbackDefault or ModOptionsService.FallbackDisable)) throw new InvalidDataException("Unknown fallback.");
        if (fallback == ModOptionsService.FallbackDisable && !ModOptionsService.FallbackSupported(Metadata)) throw new InvalidDataException(CoreText.Get("Messages.Options.DisableFallbackNeedsSdk"));
        s.Fallback = fallback;
    });
    public Task RemoveOptionRowAsync(string key) => EditOptionsAsync((p, s) => { s.Rows.RemoveAll(r => r.Key == key); RemoveOptionOnlyEdit(p, key); });
    // Mod Options Menu shows rows in registration order.
    public Task MoveOptionRowAsync(string key, int delta) => EditOptionsAsync(s =>
    {
        var index = s.Rows.FindIndex(r => r.Key == key); var next = index + delta;
        if (index < 0 || next < 0 || next >= s.Rows.Count) return;
        (s.Rows[index], s.Rows[next]) = (s.Rows[next], s.Rows[index]);
    });
    public ModOptionRow SuggestOptionRow(OptionTarget target) => ModOptionsService.Suggest(Project!, target);
}

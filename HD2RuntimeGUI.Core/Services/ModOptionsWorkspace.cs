using System.Text.Json;
using HD2RuntimeGUI.Core.Generation;
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
                (optionTargets, optionTargetsFor) = (ModOptionsService.Targets(Project!, Metadata!), (Project, Metadata));
            return optionTargets;
        }
    }
    public OptionTarget? OptionTarget(string key) => OptionTargets.FirstOrDefault(t => t.Key == key);
    public ModOptionRow? OptionRow(string key) => Project?.ModOptions?.Rows.FirstOrDefault(r => r.Key == key);
    public IReadOnlyList<string> OptionIssues => Project == null || Metadata == null ? [] : ModOptionsService.Issues(Project, Metadata);
    // Rows are generated only when their edit is present, enabled and eligible.
    public bool OptionRowActive(ModOptionRow row) => OptionTarget(row.Key) is { Eligible: true, Active: true };

    private async Task EditOptionsAsync(Action<ModOptionsSettings> edit)
    {
        var project = Project; await weaponEditGate.WaitAsync();
        try
        {
            if (project == null || !ReferenceEquals(project, Project)) throw new InvalidOperationException("Active project changed.");
            if (!ModOptionsService.Supported(Metadata)) throw new InvalidDataException("In-game options need HD2Runtime SDK 0.25.0 or newer. Rebind the project first.");
            var previous = project.ModOptions == null ? null : JsonSerializer.Deserialize<ModOptionsSettings>(JsonSerializer.Serialize(project.ModOptions, JsonStorage.Options), JsonStorage.Options);
            var format = project.FormatVersion;
            try
            {
                project.ModOptions ??= ModOptionsService.Defaults(project);
                edit(project.ModOptions);
                // Format 7 adds in-game options.
                project.FormatVersion = Math.Max(project.FormatVersion, 7);
                await SaveChangesAsync();
            }
            catch { project.ModOptions = previous; project.FormatVersion = format; throw; }
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
    // Adds or replaces the option of one edited field after validating it against Runtime's limits and the field's own rules.
    public Task SaveOptionRowAsync(ModOptionRow row) => EditOptionsAsync(s =>
    {
        var target = OptionTarget(row.Key) ?? throw new InvalidDataException("This edit no longer exists.");
        if (!target.Eligible) throw new InvalidDataException(target.Blocker!);
        row.Label = row.Label.Trim(); row.Id = row.Id.Trim(); row.Description = string.IsNullOrWhiteSpace(row.Description) ? null : row.Description.Trim();
        if (s.Rows.Any(r => r.Key != row.Key && r.Id == row.Id)) throw new InvalidDataException($"Option id '{row.Id}' is already used.");
        if (ModOptionsService.RowIssues(s, row, target).FirstOrDefault() is { } issue) throw new InvalidDataException(issue);
        var index = s.Rows.FindIndex(r => r.Key == row.Key);
        if (index >= 0) s.Rows[index] = row;
        else
        {
            if (s.Rows.Count(r => OptionTarget(r.Key) is { Eligible: true, Active: true }) + 2 > ModOptionsService.MaxRows)
                throw new InvalidDataException($"Mod Options Menu shows at most {ModOptionsService.MaxRows} options per mod, including the master toggle.");
            s.Rows.Add(row);
        }
    });
    // 0.25.1+: what option-bound edits do without Mod Options Menu (declared defaults, or inactive).
    public Task SetOptionsFallbackAsync(string fallback) => EditOptionsAsync(s =>
    {
        if (fallback is not (ModOptionsService.FallbackDefault or ModOptionsService.FallbackDisable)) throw new InvalidDataException("Unknown fallback.");
        if (fallback == ModOptionsService.FallbackDisable && !ModOptionsService.FallbackSupported(Metadata)) throw new InvalidDataException("fallback='disable' needs HD2Runtime SDK 0.25.1 or newer.");
        s.Fallback = fallback;
    });
    public Task RemoveOptionRowAsync(string key) => EditOptionsAsync(s => s.Rows.RemoveAll(r => r.Key == key));
    // Mod Options Menu shows rows in registration order.
    public Task MoveOptionRowAsync(string key, int delta) => EditOptionsAsync(s =>
    {
        var index = s.Rows.FindIndex(r => r.Key == key); var next = index + delta;
        if (index < 0 || next < 0 || next >= s.Rows.Count) return;
        (s.Rows[index], s.Rows[next]) = (s.Rows[next], s.Rows[index]);
    });
    public ModOptionRow SuggestOptionRow(OptionTarget target) => ModOptionsService.Suggest(Project!, target);
}

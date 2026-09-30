using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Services;

/// <summary>One flight value of a projectile row (velocity, drag, penetration slowdown) with the published field that edits it where it is
/// authored: a player projectile object field (with its attack role), a support, mounted-weapon or stratagem field. Note says why it cannot
/// be edited here (its owner fires another row in this project).</summary>
public sealed record RowScalar(string Key, string Label, System.Text.Json.JsonElement Baseline, WeaponCapability? Player = null, string? PlayerRole = null,
    SupportField? Support = null, EntityField? Mount = null, StratagemField? Stratagem = null, string? Note = null);

// 0.28.0 unified projectile hosts and the projectile builder. A host swap changes which row a host fires (player, support or mounted weapon,
// a Guard Dog drone gun included); a row write changes a row's slots or mode presentation for every entity that fires it. The two are separate
// operations: a row write never follows a swap, so an effect on a swapped host is written on the donor's row (every other weapon firing that
// row changes too). Player hosts keep their own path (SetAttackOutputAsync, with classic same-class swaps).
public sealed partial class BuilderWorkspace
{
    public AttackOutputHost? ProjectileHost(string kind, string weapon, string role, out string reason)
    {
        reason = CoreText.Get("Messages.Output.NoSources");
        return Metadata == null ? null : ProjectileHosts.Host(Metadata, kind, weapon, role, out reason);
    }
    // Mounts that are the same weapon entity as this one (the Gunner FRV and the Super Earth FRV gun): one write changes them all.
    public IReadOnlyList<string> SameEntity(string kind, string weapon, string role) =>
        Metadata?.AttackOutputs?.Source(kind, weapon, role)?.SharedEntity ?? [];
    // The saved swap of a host, or of a mount that is the same weapon entity.
    public AttackOutputChange? HostOutput(string kind, string weapon, string role)
    {
        if (Project?.AttackOutputChanges is not { } list) return null;
        var same = SameEntity(kind, weapon, role);
        return list.FirstOrDefault(c => c.Kind == kind && c.AttackRole == role && (c.Weapon == weapon || same.Contains(c.Weapon)));
    }
    /// <summary>Makes a support or mounted attack fire a catalogued output (null restores its own projectile). A mount that is the same weapon
    /// entity as another is one host: a swap set through either replaces the other's. Player hosts go through SetAttackOutputAsync.</summary>
    public async Task SetHostOutputAsync(string kind, string weapon, string role, string? output)
    {
        if (kind == AttackOutputChange.PlayerHost) { await SetAttackOutputAsync(weapon, role, output); return; }
        var project = Project; await weaponEditGate.WaitAsync();
        try
        {
            if (project == null || !ReferenceEquals(project, Project)) throw new InvalidOperationException(CoreText.Get("Messages.Workspace.ProjectChanged"));
            var (previous, format) = (project.AttackOutputChanges?.ToList(), project.FormatVersion);
            try
            {
                var list = project.AttackOutputChanges ?? [];
                var same = SameEntity(kind, weapon, role);
                bool Match(AttackOutputChange c) => c.Kind == kind && c.AttackRole == role && (c.Weapon == weapon || same.Contains(c.Weapon));
                var old = list.FirstOrDefault(Match);
                list.RemoveAll(Match);
                if (output != null)
                {
                    var next = AttackOutputChangeService.Create(Metadata!, kind, weapon, role, output);
                    if (old != null) next = next with { Id = old.Id, Enabled = old.Enabled, EnsureEnabled = old.EnsureEnabled, Group = old.Group, Notes = old.Notes };
                    list.Add(next);
                }
                project.AttackOutputChanges = list.Count > 0 ? list : null;
                await SaveChangesAsync();
            }
            catch { project.AttackOutputChanges = previous; project.FormatVersion = format; throw; }
        }
        finally { weaponEditGate.Release(); }
    }
    // What a host fires now in this project: the donor row of its swap, else its own row.
    public AttackOutput? FiredRow(string kind, string weapon, string role)
    {
        if (Metadata?.AttackOutputs is not { } catalog) return null;
        if (HostOutput(kind, weapon, role) is { Enabled: true } swap) return catalog.Output(swap.Output);
        if (kind == AttackOutputChange.PlayerHost && Project?.ProjectileChanges.FirstOrDefault(c => c.Enabled && c.Weapon == weapon && c.AttackRole == role) is { } classic
            && catalog.OwnedBy(classic.ReplacementProjectile.Weapon) is { } donor) return donor;
        return catalog.OwnedBy(weapon);
    }
    // Hosts this project swaps onto a row (they fire it too; Runtime's allow_shared count covers only the row's native consumers).
    public IReadOnlyList<string> HostsSwappedOnto(string output)
    {
        if (Project == null || Metadata?.AttackOutputs is not { } catalog) return [];
        return [.. (Project.AttackOutputChanges ?? []).Where(c => c.Enabled && c.Output == output).Select(c => c.Weapon)
            .Concat(Project.ProjectileChanges.Where(c => c.Enabled && catalog.OwnedBy(c.ReplacementProjectile.Weapon)?.SemanticId == output).Select(c => c.Weapon))
            .Distinct().Order(StringComparer.OrdinalIgnoreCase)];
    }

    // ---- a row's flight values (not builder slots) --------------------------------------------------------------------------------------
    // Velocity, drag and penetration slowdown are the row's own ProjectileSettings members. They are not slots: they are the projectile fields
    // its owner publishes (hd2.weapon(W):attack(role):projectile(), the support or mounted attack projectile, the stratagem attack), edited
    // where they are authored. Each comes back with the published field its existing editor takes, or a note when none can edit it here.
    public static readonly string[] RowScalarKeys = ["velocity", "drag", "penetration_slowdown"];
    public IReadOnlyList<RowScalar> RowScalars(string output)
    {
        if (Metadata?.AttackOutputs?.Output(output) is not { } row) return [];
        var owner = row.Owner.Name; var list = new List<RowScalar>();
        switch (row.Owner.Kind)
        {
            case AttackProjectileSource.PlayerKind when Metadata.PlayerWeapons?.Weapons.FirstOrDefault(w => w.Name == owner) is { } weapon:
            {
                var role = Metadata.AttackOutputs.ProjectileSources.FirstOrDefault(s => s.HostKind == AttackProjectileSource.PlayerKind && s.Weapon == owner)?.Attack ?? "primary";
                var branch = role.StartsWith("feed_", StringComparison.Ordinal) ? role[5..] : role;
                // Object edits write the projectile the owner's attack fires; only while that is still its own row do they reach this row.
                var own = Project != null && Metadata.Advanced != null && EffectiveProjectile(owner, role) == new ProjectileReference(owner, role)
                    && CompositionChangeService.FiresOutput(Project, owner, role) == null;
                var editable = own ? ObjectFields(owner, role, "projectile") : [];
                foreach (var key in RowScalarKeys)
                    if (weapon.Fields.FirstOrDefault(f => f.IsPreferred && f.SemanticFieldId == "projectile." + key && f.Backing?.Branch == branch) is { } f)
                        list.Add(new(key, f.DisplayName, f.CurrentDefault, Player: editable.FirstOrDefault(x => x.SemanticFieldId == f.SemanticFieldId), PlayerRole: role,
                            Note: own || Project == null ? null : CoreText.Format("Messages.Row.Scalar.OwnerFiresAnother", owner)));
                break;
            }
            case AttackProjectileSource.SupportKind:
                foreach (var key in RowScalarKeys)
                    if (Metadata.SupportAuthoring?.FieldInstances.Where(f => f.SupportWeapon == owner && f.Target.Path == "projectile_reference" && f.SemanticFieldId == "projectile." + key)
                            .OrderBy(f => f.Target.AttackRole == "primary" ? 0 : 1).FirstOrDefault() is { } f)
                        list.Add(new(key, f.Display.Name, f.Value.Baseline, Support: f));
                break;
            case AttackProjectileSource.VehicleKind:
                foreach (var key in RowScalarKeys)
                    if (Metadata.Entities?.VehicleWeapons?.FieldInstances.Where(f => f.Target.Weapon == owner && f.Target.Path == "projectile_reference"
                            && f.SemanticFieldId.StartsWith("projectile.", StringComparison.Ordinal) && f.SemanticFieldId.EndsWith("." + key, StringComparison.Ordinal))
                            .OrderBy(f => f.Target.Attack == "primary" ? 0 : 1).FirstOrDefault() is { } f)
                        list.Add(new(key, f.DisplayName, f.CurrentDefault, Mount: f));
                break;
            default:
                foreach (var key in RowScalarKeys)
                    if (Metadata.Stratagems?.FieldInstances.FirstOrDefault(f => f.Target.Stratagem == owner && f.SemanticFieldId == "projectile." + key) is { } f)
                        list.Add(new(key, f.DisplayName, f.CurrentDefault, Stratagem: f));
                break;
        }
        return list;
    }

    // ---- row writes (slots, mode label and icon) -----------------------------------------------------------------------------------------
    public IReadOnlyList<OutputRowChange> RowChanges(string output) => Project?.OutputRowChanges?.Where(c => c.Output == output).ToArray() ?? [];
    public OutputRowChange? RowChange(string output, string field) => Project?.OutputRowChanges?.FirstOrDefault(c => c.Output == output && c.Field == field);
    public string? RowIssue(OutputRowChange change)
    {
        try
        {
            OutputRowChangeService.Validate(Metadata!, Project, change);
            return OutputRowChangeService.TerminalConflict(Metadata!, Project!, change) is { } t
                ? CoreText.Format("Messages.Build.Row.TerminalConflict", change.OutputName, OutputRowChangeService.Label(change.Field), t.Weapon) : null;
        }
        catch (InvalidDataException e) { return e.Message; }
    }
    /// <summary>Writes one row field (null, or the row's reviewed value, removes the edit). Editing the label re-resolves an 'auto' icon.</summary>
    public Task SetRowAsync(string output, string field, string? value) => EditRowsAsync(project =>
    {
        var list = project.OutputRowChanges ?? [];
        var old = list.FirstOrDefault(c => c.Output == output && c.Field == field);
        list.RemoveAll(c => c.Output == output && c.Field == field);
        var row = OutputRowChangeService.Row(Metadata!, output);
        if (value != null && value != OutputRowChangeService.Baseline(row, field))
        {
            project.OutputRowChanges = list;
            var next = OutputRowChangeService.Create(Metadata!, project, output, field, value);
            if (old != null) next = next with { Id = old.Id, Enabled = old.Enabled, EnsureEnabled = old.EnsureEnabled, Group = old.Group, Notes = old.Notes };
            list.Add(next);
        }
        project.OutputRowChanges = list;
    }, output);
    public Task ToggleRowAsync(Guid id) => EditRowsAsync(project =>
    {
        var list = project.OutputRowChanges ?? throw new InvalidOperationException(CoreText.Get("Messages.Workspace.ProjectChanged"));
        var index = list.FindIndex(c => c.Id == id); list[index] = list[index] with { Enabled = !list[index].Enabled };
    }, Project?.OutputRowChanges?.FirstOrDefault(c => c.Id == id)?.Output);
    public Task ResetRowAsync(string output, string? field = null) => EditRowsAsync(project =>
        project.OutputRowChanges?.RemoveAll(c => c.Output == output && (field == null || c.Field == field)), output);
    /// <summary>Re-reviews a saved row write against the current SDK (its baseline, sharing, proof and opt-ins), keeping the chosen value.</summary>
    public Task AcceptRowAsync(Guid id) => EditRowsAsync(project =>
    {
        var list = project.OutputRowChanges!; var index = list.FindIndex(c => c.Id == id); var old = list[index];
        list[index] = OutputRowChangeService.Create(Metadata!, project, old.Output, old.Field, old.Value)
            with { Id = old.Id, Enabled = old.Enabled, EnsureEnabled = old.EnsureEnabled, Group = old.Group, Notes = old.Notes };
    }, Project?.OutputRowChanges?.FirstOrDefault(c => c.Id == id)?.Output);
    private async Task EditRowsAsync(Action<ModProject> edit, string? output)
    {
        var project = Project; await weaponEditGate.WaitAsync();
        try
        {
            if (project == null || !ReferenceEquals(project, Project)) throw new InvalidOperationException(CoreText.Get("Messages.Workspace.ProjectChanged"));
            var (previous, format) = (project.OutputRowChanges?.ToList(), project.FormatVersion);
            try
            {
                // An 'auto' icon that was current before this edit follows the label; one that needs review stays flagged.
                var icon = output == null ? null : project.OutputRowChanges?.FirstOrDefault(c => c.Output == output && c.Field == OutputRowChangeService.ModeIcon && c.Value == ModePresentation.Auto);
                var current = icon != null && Current(project, icon);
                edit(project);
                if (current) RefreshAutoIcon(project, output!);
                if (project.OutputRowChanges is { Count: 0 }) project.OutputRowChanges = null;
                await SaveChangesAsync();
            }
            catch { project.OutputRowChanges = previous; project.FormatVersion = format; throw; }
        }
        finally { weaponEditGate.Release(); }
    }
    private bool Current(ModProject project, OutputRowChange change)
    { try { OutputRowChangeService.Validate(Metadata!, project, change); return true; } catch (InvalidDataException) { return false; } }
    // An 'auto' icon resolves with the label written in the same operation: its opt-ins follow the row's label edit.
    private void RefreshAutoIcon(ModProject project, string output)
    {
        var list = project.OutputRowChanges; if (list == null) return;
        var index = list.FindIndex(c => c.Output == output && c.Field == OutputRowChangeService.ModeIcon && c.Value == ModePresentation.Auto);
        if (index < 0) return;
        var old = list[index];
        try { list[index] = OutputRowChangeService.Create(Metadata!, project, output, old.Field, old.Value) with { Id = old.Id, Enabled = old.Enabled, EnsureEnabled = old.EnsureEnabled, Group = old.Group, Notes = old.Notes, BaselineSdkVersion = old.BaselineSdkVersion }; }
        catch (InvalidDataException) { }
    }
}

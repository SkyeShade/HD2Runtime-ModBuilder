using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Services;

public sealed partial class BuilderWorkspace
{
    private readonly IStratagemChangeService stratagemChanges = new StratagemChangeService();
    public BuilderWorkspace(Projects.IProjectStore store, Projects.IProjectService projects, Metadata.ISdkCache cache, ISdkUpdateService updates,
        IChangeService changes, ILuaGenerator generator, IModExporter exporter, IFolderOpener folders, IProjectFilePicker picker,
        Storage.AppPaths paths, IWeaponChangeService weaponChanges, IProjectileChangeService projectileChanges,
        ICompositionChangeService compositionChanges, ISupportChangeService supportChanges, IStratagemChangeService stratagemChanges)
        : this(store, projects, cache, updates, changes, generator, exporter, folders, picker, paths, weaponChanges, projectileChanges, compositionChanges, supportChanges)
        => this.stratagemChanges = stratagemChanges;
    public string? StratagemIssue(StratagemChange c)
    { try { stratagemChanges.Validate(Project!, Metadata!, c); return null; } catch (InvalidDataException e) { return e.Message; } }
    private async Task EditStratagemAsync(Action<ModProject> edit)
    {
        var project = Project; await weaponEditGate.WaitAsync();
        try
        {
            if (project == null || !ReferenceEquals(project, Project)) throw new InvalidOperationException(CoreText.Get("Messages.Workspace.ProjectChanged"));
            var previous = project.StratagemChanges.ToList(); var approvals = new Dictionary<string, string>(project.StratagemApprovals); var format = project.FormatVersion;
            try
            {
                edit(project); project.StratagemChanges.RemoveAll(c => StratagemChangeService.NoOp(Metadata!, c) && !ModOptionsService.HasOption(project, ModOptionsService.StratagemKey(c.InstanceKey)));
                // Format 5 adds entity/weapon graph identities to stratagem changes.
                if (project.StratagemChanges.Count > 0) project.FormatVersion = Math.Max(project.FormatVersion, 5);
                await SaveChangesAsync();
            }
            catch { project.StratagemChanges = previous; project.StratagemApprovals = approvals; project.FormatVersion = format; throw; }
        }
        finally { weaponEditGate.Release(); }
    }
    public Task SetStratagemAsync(string instance, string value, bool acceptBaseline = false) => EditStratagemAsync(p => ApplyStratagem(p, instance, value, acceptBaseline));
    private void ApplyStratagem(ModProject p, string instance, string value, bool acceptBaseline)
    {
        var catalog = StratagemChangeService.Catalog(Metadata!); var f = catalog.Field(instance);
        var next = stratagemChanges.Create(Metadata!, instance, value);
        var old = StratagemChangeService.Saved(p, catalog, f);
        if (old != null)
        {
            // Keep the original scoped handle when editing the Eagle shared system through another consumer.
            var handle = catalog.Resolve(old)!;
            next = stratagemChanges.Create(Metadata!, handle.InstanceKey, value) with { Id = old.Id, Enabled = old.Enabled, EnsureEnabled = old.EnsureEnabled,
                Notes = old.Notes, Group = old.Group, EffectAcknowledgement = old.EffectAcknowledgement, ExpectedValue = acceptBaseline ? f.CurrentDefault : old.ExpectedValue,
                CapabilityEvidence = acceptBaseline ? StratagemChangeService.Evidence(handle) : old.CapabilityEvidence,
                BaselineSdkVersion = acceptBaseline ? Metadata!.Version : old.BaselineSdkVersion };
            if (!acceptBaseline && old.InstanceKey != handle.InstanceKey) next = next with { InstanceKey = old.InstanceKey };
            p.StratagemChanges.Remove(old);
        }
        // A value back to vanilla keeps the edit while the field has an in-game option (an option-only edit).
        if (!StratagemScalar.Equal(f, next.DesiredValue, f.CurrentDefault) || ModOptionsService.HasOption(p, ModOptionsService.StratagemKey(next.InstanceKey))) p.StratagemChanges.Add(next);
    }
    public Task ResetStratagemAsync(string? stratagem = null, string? instance = null) => EditStratagemAsync(p =>
    {
        var catalog = StratagemChangeService.Catalog(Metadata!);
        var saved = instance == null ? null : catalog.Find(instance) is { } f ? StratagemChangeService.Saved(p, catalog, f) : null;
        p.StratagemChanges.RemoveAll(c => (stratagem == null || c.Stratagem == stratagem) && (instance == null || c.InstanceKey == instance || c == saved));
    });
    // One acknowledgement per exact published shared scope; it records the reviewed consumer list so scope changes invalidate it.
    public Task SetStratagemApprovalAsync(string instance, bool approved) => EditStratagemAsync(p =>
    {
        var f = StratagemChangeService.Catalog(Metadata!).Field(instance);
        if (approved && f.AllowSharedRequired) p.StratagemApprovals[f.ScopeKey] = StratagemChangeService.ApprovalEvidence(f);
        else p.StratagemApprovals.Remove(f.ScopeKey);
    });
    // 0.26.0: Runtime's allow_unverified_effect opt-in for one saved change (mission uses). Kept when the value changes.
    public Task SetStratagemEffectAcknowledgedAsync(string instance, bool acknowledged) => EditStratagemAsync(p =>
    {
        var catalog = StratagemChangeService.Catalog(Metadata!); var f = catalog.Field(instance);
        var old = StratagemChangeService.Saved(p, catalog, f) ?? throw new InvalidDataException(CoreText.Get("Messages.Workspace.ChangeBeforeAcknowledging"));
        p.StratagemChanges[p.StratagemChanges.IndexOf(old)] = old with { EffectAcknowledgement = acknowledged ? StratagemChangeService.EffectEvidence(f) : null };
    });
    public Task ToggleStratagemAsync(string instance) => EditStratagemAsync(p =>
        p.StratagemChanges = p.StratagemChanges.Select(c => c.InstanceKey == instance ? c with { Enabled = !c.Enabled } : c).ToList());
}

using HD2RuntimeGUI.Core.Generation;
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
            if (project == null || !ReferenceEquals(project, Project)) throw new InvalidOperationException("Active project changed.");
            var previous = project.StratagemChanges.ToList(); var approvals = new Dictionary<string, string>(project.StratagemApprovals);
            try { edit(project); project.StratagemChanges.RemoveAll(c => StratagemChangeService.NoOp(Metadata!, c)); await SaveChangesAsync(); }
            catch { project.StratagemChanges = previous; project.StratagemApprovals = approvals; throw; }
        }
        finally { weaponEditGate.Release(); }
    }
    public Task SetStratagemAsync(string instance, string value, bool acceptBaseline = false) => EditStratagemAsync(p =>
    {
        var catalog = StratagemChangeService.Catalog(Metadata!); var f = catalog.Field(instance);
        var next = stratagemChanges.Create(Metadata!, instance, value);
        var old = StratagemChangeService.Saved(p, catalog, f);
        if (old != null)
        {
            // Keep the original scoped handle when editing the Eagle shared system through another consumer.
            next = stratagemChanges.Create(Metadata!, old.InstanceKey, value) with { Id = old.Id, Enabled = old.Enabled, EnsureEnabled = old.EnsureEnabled,
                Notes = old.Notes, Group = old.Group, ExpectedValue = acceptBaseline ? f.CurrentDefault : old.ExpectedValue,
                CapabilityEvidence = acceptBaseline ? StratagemChangeService.Evidence(catalog.Field(old.InstanceKey)) : old.CapabilityEvidence,
                BaselineSdkVersion = acceptBaseline ? Metadata!.Version : old.BaselineSdkVersion };
            p.StratagemChanges.Remove(old);
        }
        if (!StratagemScalar.Equal(f, next.DesiredValue, f.CurrentDefault)) p.StratagemChanges.Add(next);
    });
    public Task ResetStratagemAsync(string? stratagem = null, string? instance = null) => EditStratagemAsync(p =>
    {
        var catalog = StratagemChangeService.Catalog(Metadata!);
        var saved = instance == null ? null : catalog.FieldInstances.FirstOrDefault(f => f.InstanceKey == instance) is { } f ? StratagemChangeService.Saved(p, catalog, f) : null;
        p.StratagemChanges.RemoveAll(c => (stratagem == null || c.Stratagem == stratagem) && (instance == null || c.InstanceKey == instance || c == saved));
    });
    public Task SetStratagemApprovalAsync(string instance, bool approved) => EditStratagemAsync(p =>
    {
        var f = StratagemChangeService.Catalog(Metadata!).Field(instance);
        if (approved && f.AllowSharedRequired) p.StratagemApprovals[f.BackingObjectId] = StratagemChangeService.ApprovalEvidence(f);
        else p.StratagemApprovals.Remove(f.BackingObjectId);
    });
    public Task ToggleStratagemAsync(string instance) => EditStratagemAsync(p =>
        p.StratagemChanges = p.StratagemChanges.Select(c => c.InstanceKey == instance ? c with { Enabled = !c.Enabled } : c).ToList());
}

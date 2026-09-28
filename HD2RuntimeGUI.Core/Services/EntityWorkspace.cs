using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Services;

// Vehicle and backpack edits (SDK 0.23.0+). Same autosave/rollback pattern as the other semantic editors.
public sealed partial class BuilderWorkspace
{
    private readonly IEntityChangeService entityChanges = new EntityChangeService();
    public string? EntityIssue(EntityChange c)
    { try { entityChanges.Validate(Project!, Metadata!, c); return null; } catch (InvalidDataException e) { return e.Message; } }
    private async Task EditEntityAsync(Action<ModProject> edit)
    {
        var project = Project; await weaponEditGate.WaitAsync();
        try
        {
            if (project == null || !ReferenceEquals(project, Project)) throw new InvalidOperationException("Active project changed.");
            var previous = project.EntityChanges.ToList(); var approvals = new Dictionary<string, string>(project.EntityApprovals); var format = project.FormatVersion;
            try
            {
                edit(project); project.EntityChanges.RemoveAll(c => EntityChangeService.NoOp(Metadata!, c));
                // Format 6 adds vehicle/backpack changes.
                if (project.EntityChanges.Count > 0) project.FormatVersion = Math.Max(project.FormatVersion, 6);
                await SaveChangesAsync();
            }
            catch { project.EntityChanges = previous; project.EntityApprovals = approvals; project.FormatVersion = format; throw; }
        }
        finally { weaponEditGate.Release(); }
    }
    public Task SetEntityAsync(string instance, string value, bool acceptBaseline = false) => EditEntityAsync(p =>
    {
        var f = EntityChangeService.Catalog(Metadata!).Field(instance) ?? throw new InvalidDataException("Vehicle/backpack capability is missing.");
        var next = entityChanges.Create(Metadata!, instance, value);
        if (EntityChangeService.Saved(p, f) is { } old)
        {
            next = next with { Id = old.Id, Enabled = old.Enabled, EnsureEnabled = old.EnsureEnabled, Group = old.Group, Notes = old.Notes,
                ExpectedValue = acceptBaseline ? next.ExpectedValue : old.ExpectedValue, BaselineSdkVersion = acceptBaseline ? next.BaselineSdkVersion : old.BaselineSdkVersion,
                CapabilityEvidence = acceptBaseline ? next.CapabilityEvidence : old.CapabilityEvidence,
                // An acknowledgement covers one exact replacement; choosing a different weapon requires a new one.
                ReferenceAcknowledgement = old.ReferenceAcknowledgement == EntityChangeService.ReferenceEvidence(f, next.DesiredValue) ? old.ReferenceAcknowledgement : null };
            p.EntityChanges.Remove(old);
        }
        if (!EntityScalar.Equal(f, next.DesiredValue, f.CurrentDefault)) p.EntityChanges.Add(next);
    });
    public Task SetEntityReferenceAcknowledgedAsync(string instance, bool acknowledged) => EditEntityAsync(p =>
    {
        var f = EntityChangeService.Catalog(Metadata!).Field(instance) ?? throw new InvalidDataException("Vehicle/backpack capability is missing.");
        var old = EntityChangeService.Saved(p, f) ?? throw new InvalidDataException("Choose a replacement weapon before acknowledging.");
        if (!f.IsReference) throw new InvalidDataException("Only mount references need this acknowledgement.");
        p.EntityChanges[p.EntityChanges.IndexOf(old)] = old with { ReferenceAcknowledgement = acknowledged ? EntityChangeService.ReferenceEvidence(f, old.DesiredValue) : null };
    });
    public Task ResetEntityAsync(string? resource = null, string? entity = null, string? instance = null) => EditEntityAsync(p =>
        p.EntityChanges.RemoveAll(c => (resource == null || c.Resource == resource) && (entity == null || c.Entity == entity) && (instance == null || c.InstanceKey == instance)));
    public Task ToggleEntityAsync(string instance) => EditEntityAsync(p =>
        p.EntityChanges = p.EntityChanges.Select(c => c.InstanceKey == instance ? c with { Enabled = !c.Enabled } : c).ToList());
}

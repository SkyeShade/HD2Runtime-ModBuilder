using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
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
        // A magazine attachment or booster target is acknowledged as a whole: new edits inherit an existing acknowledgement of the same scope.
        else if (f.Acknowledgement == "allow_unverified_effect" && EntityChangeService.Approved(p, f))
            next = next with { ReferenceAcknowledgement = EntityChangeService.ReferenceEvidence(f, next.DesiredValue) };
        if (!EntityScalar.Equal(f, next.DesiredValue, f.CurrentDefault)) p.EntityChanges.Add(next);
    });
    public Task SetEntityReferenceAcknowledgedAsync(string instance, bool acknowledged) => EditEntityAsync(p =>
    {
        var f = EntityChangeService.Catalog(Metadata!).Field(instance) ?? throw new InvalidDataException("Vehicle/backpack capability is missing.");
        var old = EntityChangeService.Saved(p, f) ?? throw new InvalidDataException("Choose a replacement weapon before acknowledging.");
        if (!f.IsReference) throw new InvalidDataException("Only mount references need this acknowledgement.");
        p.EntityChanges[p.EntityChanges.IndexOf(old)] = old with { ReferenceAcknowledgement = acknowledged ? EntityChangeService.ReferenceEvidence(f, old.DesiredValue) : null };
    });
    // One acknowledgement per magazine attachment definition: records the shared-scope approval (allow_shared) and the
    // unverified-effect acknowledgement (allow_unverified_effect) for every edited field of that definition.
    public Task SetAttachmentAcknowledgedAsync(string attachment, bool acknowledged) => EditEntityAsync(p =>
    {
        var catalog = EntityChangeService.Catalog(Metadata!).Attachments ?? throw new InvalidDataException("Rebind to SDK 0.23.1 or newer for magazine attachments.");
        var fields = catalog.FieldInstances.Where(f => f.Target.Attachment == attachment).ToArray();
        if (fields.Length == 0) throw new InvalidDataException("Unknown magazine attachment.");
        if (acknowledged) p.EntityApprovals[fields[0].SharedScopeKey] = EntityChangeService.ApprovalEvidence(fields[0]); else p.EntityApprovals.Remove(fields[0].SharedScopeKey);
        p.EntityChanges = p.EntityChanges.Select(c => fields.FirstOrDefault(f => f.InstanceKey == c.InstanceKey) is { } f
            ? c with { ReferenceAcknowledgement = acknowledged ? EntityChangeService.ReferenceEvidence(f, c.DesiredValue) : null } : c).ToList();
    });
    public static bool AttachmentAcknowledged(ModProject? p, MagazineAttachmentCatalog catalog, string attachment)
    {
        var fields = catalog.FieldInstances.Where(f => f.Target.Attachment == attachment).ToArray();
        var saved = fields.Select(f => (Field: f, Change: EntityChangeService.Saved(p, f))).Where(x => x.Change != null).ToArray();
        return p != null && fields.Length > 0 && EntityChangeService.Approved(p, fields[0])
            && saved.All(x => x.Change!.ReferenceAcknowledgement == EntityChangeService.ReferenceEvidence(x.Field, x.Change.DesiredValue));
    }
    // One acknowledgement per booster (0.24.0+): allow_unverified_effect for every edited field, plus the shared-scope approval
    // (allow_shared) where Runtime requires it. Each booster target's scope is recorded, so later edits inherit the acknowledgement.
    public Task SetBoosterAcknowledgedAsync(string booster, bool acknowledged) => EditEntityAsync(p =>
    {
        var fields = BoosterFields(EntityChangeService.Catalog(Metadata!), booster);
        foreach (var scope in fields.GroupBy(f => f.SharedScopeKey))
            if (acknowledged) p.EntityApprovals[scope.Key] = EntityChangeService.ApprovalEvidence(scope.First()); else p.EntityApprovals.Remove(scope.Key);
        p.EntityChanges = p.EntityChanges.Select(c => fields.FirstOrDefault(f => f.InstanceKey == c.InstanceKey) is { } f
            ? c with { ReferenceAcknowledgement = acknowledged ? EntityChangeService.ReferenceEvidence(f, c.DesiredValue) : null } : c).ToList();
    });
    public static bool BoosterAcknowledged(ModProject? p, EntityAuthoring catalog, string booster)
    {
        var fields = catalog.Boosters?.FieldInstances.Where(f => f.Target.Booster == booster).ToArray() ?? [];
        return p != null && fields.Length > 0 && fields.GroupBy(f => f.SharedScopeKey).All(s => EntityChangeService.Approved(p, s.First()))
            && fields.All(f => EntityChangeService.Saved(p, f) is not { } c || c.ReferenceAcknowledgement == EntityChangeService.ReferenceEvidence(f, c.DesiredValue));
    }
    private static EntityField[] BoosterFields(EntityAuthoring catalog, string booster)
    {
        var fields = (catalog.Boosters ?? throw new InvalidDataException("Rebind to SDK 0.24.0 or newer for booster authoring.")).FieldInstances.Where(f => f.Target.Booster == booster).ToArray();
        return fields.Length > 0 ? fields : throw new InvalidDataException("This booster has no published writable fields.");
    }
    public Task ResetEntityAsync(string? resource = null, string? entity = null, string? instance = null) => EditEntityAsync(p =>
        p.EntityChanges.RemoveAll(c => (resource == null || c.Resource == resource) && (entity == null || c.Entity == entity) && (instance == null || c.InstanceKey == instance)));
    public Task ToggleEntityAsync(string instance) => EditEntityAsync(p =>
        p.EntityChanges = p.EntityChanges.Select(c => c.InstanceKey == instance ? c with { Enabled = !c.Enabled } : c).ToList());
}

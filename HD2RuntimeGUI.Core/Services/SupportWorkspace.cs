using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Services;

public sealed partial class BuilderWorkspace
{
    private readonly ISupportChangeService supportChanges = new SupportChangeService();
    public string? SupportIssue(SupportChange c)
    { try { supportChanges.Validate(Project!, Metadata!, c); return null; } catch (InvalidDataException e) { return e.Message; } }
    private async Task EditSupportAsync(Action<ModProject> edit)
    {
        var project = Project; await weaponEditGate.WaitAsync();
        try
        {
            if (project == null || !ReferenceEquals(project, Project)) throw new InvalidOperationException(CoreText.Get("Messages.Workspace.ProjectChanged"));
            var previous = project.SupportChanges.ToList(); var approvals = new Dictionary<string, string>(project.SupportApprovals);
            try { edit(project); project.SupportChanges.RemoveAll(c => SupportChangeService.NoOp(Metadata!, c) && !ModOptionsService.HasOption(project, ModOptionsService.SupportKey(c.InstanceKey))); await SaveChangesAsync(); }
            catch { project.SupportChanges = previous; project.SupportApprovals = approvals; throw; }
        }
        finally { weaponEditGate.Release(); }
    }
    public async Task SetSupportAsync(string instance, string value, bool acceptBaseline = false)
    {
        // 1.4.0: with rate modes edited, the older fire rate is their default (Y) slot and is written there.
        if (!acceptBaseline && SupportChangeService.Catalog(Metadata!).FieldInstances.FirstOrDefault(f => f.InstanceKey == instance) is { Target.Path: "weapon" } field
            && await FoldLegacyFireRateAsync(CompositionKind.Support, field.SupportWeapon, field.SemanticFieldId, value)) return;
        await SetSupportValueAsync(instance, value, acceptBaseline);
    }
    private Task SetSupportValueAsync(string instance, string value, bool acceptBaseline) => EditSupportAsync(p => ApplySupportValue(p, instance, value, acceptBaseline));
    private void ApplySupportValue(ModProject p, string instance, string value, bool acceptBaseline)
    {
        var next = supportChanges.Create(Metadata!, instance, value);
        var old = p.SupportChanges.SingleOrDefault(c => c.InstanceKey == instance);
        if (old != null) next = next with { Id = old.Id, Enabled = old.Enabled, EnsureEnabled = old.EnsureEnabled, Group = old.Group, Notes = old.Notes,
            EffectAcknowledgement = old.EffectAcknowledgement,
            ExpectedValue = acceptBaseline ? next.ExpectedValue : old.ExpectedValue,
            CapabilityEvidence = acceptBaseline ? next.CapabilityEvidence : old.CapabilityEvidence,
            BaselineSdkVersion = acceptBaseline ? next.BaselineSdkVersion : old.BaselineSdkVersion };
        p.SupportChanges.RemoveAll(c => c.InstanceKey == instance);
        var field = SupportChangeService.Catalog(Metadata!).Field(instance);
        // A deliberate edit back to today's displayed vanilla is a reset, even after rebind, unless the field has an in-game option
        // (an option-only edit).
        if (!SupportScalar.Equal(field, next.DesiredValue, field.Value.Baseline) || ModOptionsService.HasOption(p, ModOptionsService.SupportKey(instance))) p.SupportChanges.Add(next);
    }
    public Task ResetSupportAsync(string? weapon = null, string? instance = null)
    {
        // A rate-of-fire / programmable-ammunition field resets with its pair (Runtime writes them together).
        if (instance != null && Metadata?.SupportAuthoring?.FieldInstances.FirstOrDefault(f => f.InstanceKey == instance) is { Target.Path: "weapon" } field
            && WeaponSelectorRules.IsSelectorField(field.SemanticFieldId))
            return ResetSelectorFieldAsync(CompositionKind.Support, field.SupportWeapon, field.SemanticFieldId);
        return EditSupportAsync(p => p.SupportChanges.RemoveAll(c => (weapon == null || c.Weapon == weapon) && (instance == null || c.InstanceKey == instance)));
    }
    public Task SetSupportApprovalAsync(string instance, bool approved) => EditSupportAsync(p =>
    {
        var f = SupportChangeService.Catalog(Metadata!).Field(instance);
        if (approved && f.SharedScope.RequiresAcknowledgement) p.SupportApprovals[f.SharedScope.ScopeKey] = SupportChangeService.ApprovalEvidence(f);
        else p.SupportApprovals.Remove(f.SharedScope.ScopeKey);
    });
    // Runtime's allow_unverified_effect opt-in for one saved field (0.24.0+). Unlike shared approval it is per field, not per object.
    public Task SetSupportEffectAcknowledgedAsync(string instance, bool acknowledged) => EditSupportAsync(p =>
    {
        var f = SupportChangeService.Catalog(Metadata!).Field(instance);
        if (f.Operation.Acknowledgement != "allow_unverified_effect") throw new InvalidDataException(CoreText.Get("Messages.Workspace.EffectAcknowledgementNotRequired"));
        p.SupportChanges = p.SupportChanges.Select(c => c.InstanceKey == instance ? c with { EffectAcknowledgement = acknowledged ? SupportChangeService.EffectEvidence(f) : null } : c).ToList();
    });
    public async Task ToggleSupportAsync(string instance)
    {
        // A weapon's rate-of-fire / programmable-ammunition group is one transaction: it is enabled or disabled as a whole.
        if (Metadata?.SupportAuthoring?.FieldInstances.FirstOrDefault(f => f.InstanceKey == instance) is { Target.Path: "weapon" } field
            && await ToggleSelectorAsync(CompositionKind.Support, field.SupportWeapon, field.SemanticFieldId)) return;
        await EditSupportAsync(p => p.SupportChanges = p.SupportChanges.Select(c => c.InstanceKey == instance ? c with { Enabled = !c.Enabled } : c).ToList());
    }
}

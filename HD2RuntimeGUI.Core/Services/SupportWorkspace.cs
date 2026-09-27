using HD2RuntimeGUI.Core.Generation;
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
            if (project == null || !ReferenceEquals(project, Project)) throw new InvalidOperationException("Active project changed.");
            var previous = project.SupportChanges.ToList(); var approvals = new Dictionary<string, string>(project.SupportApprovals);
            try { edit(project); project.SupportChanges.RemoveAll(c => SupportChangeService.NoOp(Metadata!, c)); await SaveChangesAsync(); }
            catch { project.SupportChanges = previous; project.SupportApprovals = approvals; throw; }
        }
        finally { weaponEditGate.Release(); }
    }
    public Task SetSupportAsync(string instance, string value, bool acceptBaseline = false) => EditSupportAsync(p =>
    {
        var next = supportChanges.Create(Metadata!, instance, value);
        var old = p.SupportChanges.SingleOrDefault(c => c.InstanceKey == instance);
        if (old != null) next = next with { Id = old.Id, Enabled = old.Enabled, EnsureEnabled = old.EnsureEnabled, Group = old.Group, Notes = old.Notes,
            ExpectedValue = acceptBaseline ? next.ExpectedValue : old.ExpectedValue,
            CapabilityEvidence = acceptBaseline ? next.CapabilityEvidence : old.CapabilityEvidence,
            BaselineSdkVersion = acceptBaseline ? next.BaselineSdkVersion : old.BaselineSdkVersion };
        p.SupportChanges.RemoveAll(c => c.InstanceKey == instance);
        var field = SupportChangeService.Catalog(Metadata!).Field(instance);
        // A deliberate edit back to today's displayed vanilla is a reset, even after rebind.
        if (!SupportScalar.Equal(field, next.DesiredValue, field.Value.Baseline)) p.SupportChanges.Add(next);
    });
    public Task ResetSupportAsync(string? weapon = null, string? instance = null) => EditSupportAsync(p =>
        p.SupportChanges.RemoveAll(c => (weapon == null || c.Weapon == weapon) && (instance == null || c.InstanceKey == instance)));
    public Task SetSupportApprovalAsync(string instance, bool approved) => EditSupportAsync(p =>
    {
        var f = SupportChangeService.Catalog(Metadata!).Field(instance);
        if (approved && f.SharedScope.RequiresAcknowledgement) p.SupportApprovals[f.SharedScope.ScopeKey] = SupportChangeService.ApprovalEvidence(f);
        else p.SupportApprovals.Remove(f.SharedScope.ScopeKey);
    });
    public Task ToggleSupportAsync(string instance) => EditSupportAsync(p =>
        p.SupportChanges = p.SupportChanges.Select(c => c.InstanceKey == instance ? c with { Enabled = !c.Enabled } : c).ToList());
}

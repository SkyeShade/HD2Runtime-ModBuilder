using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Services;

// Projectile donors through the active projectile source (0.28.0 development SDKs). A same-class donor that the classic projectile graph
// also offers stays a classic reference swap (its projectile and explosion edits can follow it); every other donor, and every
// ammunition-fed host (INDIRECT weapons such as the AR-23 Liberator), becomes an attack output written through the source Runtime reports.
public sealed partial class BuilderWorkspace
{
    public AttackOutputChange? AttackOutput(string weapon, string role) => Project?.AttackOutputChanges?.FirstOrDefault(c => c.Weapon == weapon && c.AttackRole == role);
    public string? AttackOutputIssue(AttackOutputChange change) { try { AttackOutputChangeService.Validate(Metadata!, change); return null; } catch (InvalidDataException e) { return e.Message; } }
    public AttackOutputHost? OutputHost(string weapon, string role, out string reason)
    {
        reason = CoreText.Get("Messages.Output.NoSources");
        return Metadata?.AttackOutputs is { } catalog && Metadata.PlayerWeapons is { } weapons ? catalog.Host(weapons, weapon, role, out reason) : null;
    }
    // The classic reference swap that expresses the same donor, when there is one (same class, a player weapon the projectile graph offers).
    public ProjectileReference? ClassicSource(AttackOutputHost host, AttackOutput output)
    {
        if (host.Mechanism != HD2RuntimeGUI.Core.Metadata.AttackOutputHost.Component || output.Owner.Kind != "player_weapon" || output.CompatibilityClass != host.CompatibilityClass) return null;
        var matches = ProjectileSources(host.Weapon, host.Role).Where(s => s.Weapon == output.Owner.Name).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
    /// <summary>Makes the attack fire a catalogued output (null restores its own projectile). Classic same-class donors become reference
    /// swaps; everything else an attack output. Projectile and explosion edits cannot follow an attack output, so they must be discarded.</summary>
    public async Task SetAttackOutputAsync(string weapon, string role, string? output, bool discardObjectEdits = false)
    {
        var project = Project; await weaponEditGate.WaitAsync();
        try
        {
            if (project == null || !ReferenceEquals(project, Project)) throw new InvalidOperationException(CoreText.Get("Messages.Workspace.ProjectChanged"));
            var (outputs, projectiles, composition, format) = (project.AttackOutputChanges?.ToList(), project.ProjectileChanges.ToList(), project.CompositionChanges.ToList(), project.FormatVersion);
            try
            {
                var list = project.AttackOutputChanges ?? [];
                var old = list.FirstOrDefault(c => c.Weapon == weapon && c.AttackRole == role);
                list.RemoveAll(c => c.Weapon == weapon && c.AttackRole == role);
                if (output != null)
                {
                    var next = AttackOutputChangeService.Create(Metadata!, weapon, role, output);
                    if (old != null) next = next with { Id = old.Id, Enabled = old.Enabled, EnsureEnabled = old.EnsureEnabled, Group = old.Group, Notes = old.Notes };
                    var edits = project.CompositionChanges.Where(c => c.Weapon == weapon && c.AttackRole == role).ToArray();
                    if (edits.Length > 0 && !discardObjectEdits)
                        throw new InvalidDataException(CoreText.Plural("Messages.Output.EditsBlockSwitch", edits.Length, weapon));
                    project.CompositionChanges.RemoveAll(c => c.Weapon == weapon && c.AttackRole == role);
                    project.ProjectileChanges.RemoveAll(c => c.Weapon == weapon && c.AttackRole == role);
                    list.Add(next);
                }
                project.AttackOutputChanges = list.Count > 0 ? list : null;
                await SaveChangesAsync();
            }
            catch { project.AttackOutputChanges = outputs; project.ProjectileChanges = projectiles; project.CompositionChanges = composition; project.FormatVersion = format; throw; }
        }
        finally { weaponEditGate.Release(); }
    }
    public async Task ToggleAttackOutputAsync(Guid id)
    {
        var list = Project!.AttackOutputChanges ?? throw new InvalidOperationException("No attack outputs.");
        var index = list.FindIndex(c => c.Id == id); var old = list[index];
        list[index] = old with { Enabled = !old.Enabled };
        try { await SaveChangesAsync(); } catch { list[index] = old; throw; }
    }
}

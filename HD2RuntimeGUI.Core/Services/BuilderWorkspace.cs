using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Projects;
using HD2RuntimeGUI.Core.Storage;

namespace HD2RuntimeGUI.Core.Services;

public interface IFolderOpener
{
    Task OpenAsync(string path, bool selectFile = false);
    // Opens a file with the application the OS associates with it (for src/addon.lua: the user's Lua editor).
    Task OpenFileAsync(string path) => OpenAsync(path, true);
}
public interface IProjectFilePicker { Task<string?> PickAsync(); }

// Application use cases. Components delegate I/O and generation to this service.
public sealed partial class BuilderWorkspace(IProjectStore store, IProjectService projects, ISdkCache cache,
    ISdkUpdateService updates, IChangeService changes, ILuaGenerator generator, IModExporter exporter,
    IFolderOpener folders, IProjectFilePicker picker, AppPaths paths)
{
    private readonly IWeaponChangeService weaponChanges = new WeaponChangeService();
    private readonly SemaphoreSlim weaponEditGate = new(1);
    public BuilderWorkspace(IProjectStore store, IProjectService projects, ISdkCache cache, ISdkUpdateService updates,
        IChangeService changes, ILuaGenerator generator, IModExporter exporter, IFolderOpener folders,
        IProjectFilePicker picker, AppPaths paths, IWeaponChangeService weaponChanges)
        : this(store, projects, cache, updates, changes, generator, exporter, folders, picker, paths) => this.weaponChanges = weaponChanges;
    private readonly IProjectileChangeService projectileChanges = new ProjectileChangeService();
    public BuilderWorkspace(IProjectStore store, IProjectService projects, ISdkCache cache, ISdkUpdateService updates,
        IChangeService changes, ILuaGenerator generator, IModExporter exporter, IFolderOpener folders,
        IProjectFilePicker picker, AppPaths paths, IWeaponChangeService weaponChanges, IProjectileChangeService projectileChanges)
        : this(store, projects, cache, updates, changes, generator, exporter, folders, picker, paths, weaponChanges) => this.projectileChanges = projectileChanges;
    public IReadOnlyList<ProjectileReference> ProjectileSources(string weapon, string role) => projectileChanges.Sources(Metadata!, weapon, role);
    public string? ProjectileIssue(ProjectileChange change)
    { try { projectileChanges.Validate(Metadata!, change); return null; } catch (InvalidDataException e) { return e.Message; } }
    public async Task SetProjectileAsync(string weapon, string role, ProjectileReference? replacement, bool acceptBaseline = false)
    {
        var project = Project;
        await weaponEditGate.WaitAsync();
        try
        {
            if (project == null || !ReferenceEquals(project, Project)) throw new InvalidOperationException(CoreText.Get("Messages.Workspace.ProjectChanged"));
            var previous = project.ProjectileChanges.ToList();
            ApplyProjectile(project, weapon, role, replacement, acceptBaseline);
            try { await SaveChangesAsync(); } catch { project.ProjectileChanges = previous; throw; }
        }
        finally { weaponEditGate.Release(); }
    }
    private void ApplyProjectile(ModProject project, string weapon, string role, ProjectileReference? replacement, bool acceptBaseline)
    {
        var old = project.ProjectileChanges.SingleOrDefault(c => c.Weapon == weapon && c.AttackRole == role);
        ProjectileChange? next = null;
        if (replacement != null)
        {
            next = projectileChanges.Create(Metadata!, weapon, role, replacement);
            if (projectileChanges.IsBaseline(Metadata!, weapon, role, replacement)) next = null;
            else if (old != null)
            {
                next.Id = old.Id; next.Enabled = old.Enabled; next.EnsureEnabled = old.EnsureEnabled; next.Group = old.Group; next.Notes = old.Notes;
                if (!acceptBaseline)
                {
                    next.ExpectedEvidence = old.ExpectedEvidence; next.BaselineSdkVersion = old.BaselineSdkVersion; next.CompatibilityClass = old.CompatibilityClass;
                    if (old.ReplacementProjectile == replacement) next.ReplacementEvidence = old.ReplacementEvidence;
                }
            }
        }
        project.ProjectileChanges.RemoveAll(c => c.Weapon == weapon && c.AttackRole == role);
        if (next != null) project.ProjectileChanges.Add(next);
        project.FormatVersion = Math.Max(project.FormatVersion, 4);
    }

    // Projectile swaps with existing object edits. Object edits (projectile/explosion stats and terminal actions) belong to the projectile an
    // attack fires; swapping the projectile would leave them on an object the attack no longer uses. They are handled where the swap happens.
    /// <summary>Object edits of one attack made on the projectile it fires now.</summary>
    public IReadOnlyList<CompositionChange> ObjectEditsOnCurrentProjectile(string weapon, string role)
    { var target = EffectiveProjectile(weapon, role); return Project!.CompositionChanges.Where(c => c.Weapon == weapon && c.AttackRole == role && c.Target == target).ToList(); }
    /// <summary>Object edits of one attack left on a projectile it no longer fires (for example from an earlier swap). They block the build.</summary>
    public IReadOnlyList<CompositionChange> OrphanedObjectEdits(string weapon, string role)
    { var target = EffectiveProjectile(weapon, role); return Project!.CompositionChanges.Where(c => c.Weapon == weapon && c.AttackRole == role && c.Target != target).ToList(); }
    /// <summary>The first weapon with orphaned object edits, so the UI can send the user straight to it.</summary>
    public string? WeaponWithOrphanedObjectEdits => Project?.CompositionChanges.FirstOrDefault(c => c.Target != EffectiveProjectile(c.Weapon, c.AttackRole))?.Weapon;
    public string ObjectEditLabel(CompositionChange c)
    {
        if (c.Kind == "terminal") return CoreText.Format("Messages.Weapon.ObjectEdit.Terminal", ExplosionLabel(c.Phase!), c.DesiredExplosion!.Label);
        string name; try { name = CompositionChangeService.Capability(Metadata!, c).DisplayName; } catch (InvalidDataException) { name = c.Scalar!.SemanticFieldId; }
        var value = c.Scalar!.DesiredValue.GetRawText();
        return c.Kind == "explosion" ? CoreText.Format("Messages.Weapon.ObjectEdit.Explosion", ExplosionLabel(c.Phase!), name, value) : CoreText.Format("Messages.Weapon.ObjectEdit.Projectile", name, value);
    }
    // "Impact explosion" / "Expiry explosion"; another phase shows capitalized, as published.
    private static string ExplosionLabel(string phase) => phase switch
    {
        "impact" => CoreText.Get("Messages.Weapon.Explosion.Impact"),
        "expiry" => CoreText.Get("Messages.Weapon.Explosion.Expiry"),
        _ => CoreText.Format("Messages.Weapon.Explosion.Phase", char.ToUpperInvariant(phase[0]) + phase[1..]),
    };

    /// <summary>Swaps an attack's projectile and, in the same save, keeps the values of its object edits on the new projectile (keepValues) or
    /// discards them. Values that the new projectile cannot take are listed in the result instead of failing the swap.</summary>
    public async Task<ObjectEditCarryResult> SwapProjectileAsync(string weapon, string role, ProjectileReference? replacement, bool keepValues)
    {
        var project = Project;
        await weaponEditGate.WaitAsync();
        try
        {
            if (project == null || !ReferenceEquals(project, Project)) throw new InvalidOperationException(CoreText.Get("Messages.Workspace.ProjectChanged"));
            var (projectiles, composition) = (project.ProjectileChanges.ToList(), project.CompositionChanges.ToList());
            var edits = ObjectEditsOnCurrentProjectile(weapon, role);
            try
            {
                ApplyProjectile(project, weapon, role, replacement, acceptBaseline: false);
                var result = Retarget(project, weapon, role, edits, keepValues);
                await SaveChangesAsync(); return result;
            }
            catch { project.ProjectileChanges = projectiles; project.CompositionChanges = composition; throw; }
        }
        finally { weaponEditGate.Release(); }
    }
    /// <summary>Moves orphaned object edits of one attack to the projectile it fires now (keepValues) or discards them.</summary>
    public async Task<ObjectEditCarryResult> ResolveOrphanedObjectEditsAsync(string weapon, string role, bool keepValues)
    {
        var project = Project;
        await weaponEditGate.WaitAsync();
        try
        {
            if (project == null || !ReferenceEquals(project, Project)) throw new InvalidOperationException(CoreText.Get("Messages.Workspace.ProjectChanged"));
            var composition = project.CompositionChanges.ToList();
            try { var result = Retarget(project, weapon, role, OrphanedObjectEdits(weapon, role), keepValues); await SaveChangesAsync(); return result; }
            catch { project.CompositionChanges = composition; throw; }
        }
        finally { weaponEditGate.Release(); }
    }
    // Removes the edits and, when keeping values, re-creates each one on the attack's current projectile (terminal actions first, since they
    // decide which explosion the explosion stats belong to). The same desired value is applied against the new object's own baseline.
    private ObjectEditCarryResult Retarget(ModProject project, string weapon, string role, IReadOnlyList<CompositionChange> edits, bool keepValues)
    {
        project.CompositionChanges.RemoveAll(c => edits.Contains(c));
        if (!keepValues || edits.Count == 0) return new(0, 0, []);
        int kept = 0, atBase = 0; var dropped = new List<string>();
        foreach (var c in edits.OrderBy(c => c.Kind == "terminal" ? 0 : 1))
        {
            try
            {
                var next = c.Kind == "terminal"
                    ? compositionChanges.CreateTerminal(project, Metadata!, weapon, role, c.Phase!, c.DesiredExplosion!, acknowledge: true)
                    : compositionChanges.CreateScalar(project, Metadata!, weapon, role, c.Kind, c.Phase, c.Scalar!.SemanticFieldId, c.Scalar.DesiredValue.GetRawText(), acknowledge: true);
                next.Enabled = c.Enabled; next.EnsureEnabled = c.EnsureEnabled; next.Group = c.Group; next.Notes = c.Notes;
                project.CompositionChanges.RemoveAll(x => x.Weapon == next.Weapon && x.AttackRole == next.AttackRole && x.Kind == next.Kind && x.Phase == next.Phase && x.Scalar?.SemanticFieldId == next.Scalar?.SemanticFieldId);
                if (compositionChanges.IsNoOp(Metadata!, next)) { atBase++; continue; }
                project.CompositionChanges.Add(next); kept++;
            }
            catch (InvalidDataException e) { dropped.Add(CoreText.Format("Messages.Weapon.ObjectEdit.NotKept", ObjectEditLabel(c), CompositionChangeService.IsFieldNotOnTarget(e) ? CoreText.Get("Messages.Weapon.ObjectEdit.NoSuchValue") : e.Message)); }
        }
        project.FormatVersion = Math.Max(project.FormatVersion, 4);
        return new(kept, atBase, dropped);
    }
    public async Task ToggleProjectileAsync(Guid id)
    {
        var c = Project!.ProjectileChanges.Single(c => c.Id == id); c.Enabled = !c.Enabled;
        try { await SaveChangesAsync(); } catch { c.Enabled = !c.Enabled; throw; }
    }
    private readonly ICompositionChangeService compositionChanges = new CompositionChangeService();
    public BuilderWorkspace(IProjectStore store, IProjectService projects, ISdkCache cache, ISdkUpdateService updates,
        IChangeService changes, ILuaGenerator generator, IModExporter exporter, IFolderOpener folders,
        IProjectFilePicker picker, AppPaths paths, IWeaponChangeService weaponChanges, IProjectileChangeService projectileChanges, ICompositionChangeService compositionChanges)
        : this(store, projects, cache, updates, changes, generator, exporter, folders, picker, paths, weaponChanges, projectileChanges) => this.compositionChanges = compositionChanges;
    public BuilderWorkspace(IProjectStore store, IProjectService projects, ISdkCache cache, ISdkUpdateService updates,
        IChangeService changes, ILuaGenerator generator, IModExporter exporter, IFolderOpener folders,
        IProjectFilePicker picker, AppPaths paths, IWeaponChangeService weaponChanges, IProjectileChangeService projectileChanges, ICompositionChangeService compositionChanges, ISupportChangeService supportChanges)
        : this(store, projects, cache, updates, changes, generator, exporter, folders, picker, paths, weaponChanges, projectileChanges, compositionChanges) => this.supportChanges = supportChanges;
    public ProjectileReference EffectiveProjectile(string weapon, string role) => compositionChanges.EffectiveProjectile(Project!, weapon, role);
    public ExplosionReference EffectiveExplosion(string weapon, string role, string phase) => compositionChanges.EffectiveExplosion(Project!, Metadata!, weapon, role, phase);
    public IReadOnlyList<ExplosionReference> ExplosionSources => compositionChanges.ExplosionSources(Metadata!);
    public IReadOnlyList<WeaponCapability> ObjectFields(string weapon, string role, string kind, string? phase = null) => compositionChanges.Fields(Project!, Metadata!, weapon, role, kind, phase);
    public string? CompositionIssue(CompositionChange change) { try { compositionChanges.Validate(Project!, Metadata!, change); return null; } catch (InvalidDataException e) { return e.Message; } }
    public Task SetObjectScalarAsync(string weapon, string role, string kind, string? phase, string field, string value, bool acknowledge, bool acceptBaseline = false) => EditCompositionAsync(
        () => compositionChanges.CreateScalar(Project!, Metadata!, weapon, role, kind, phase, field, value, acknowledge), acceptBaseline);
    public Task SetTerminalAsync(string weapon, string role, string phase, ExplosionReference desired, bool acknowledge, bool acceptBaseline = false) => EditCompositionAsync(
        () => compositionChanges.CreateTerminal(Project!, Metadata!, weapon, role, phase, desired, acknowledge), acceptBaseline);
    private async Task EditCompositionAsync(Func<CompositionChange> create, bool acceptBaseline)
    {
        var project = Project; await weaponEditGate.WaitAsync();
        try
        {
            if (project == null || !ReferenceEquals(project, Project)) throw new InvalidOperationException(CoreText.Get("Messages.Workspace.ProjectChanged"));
            var next = create(); var previous = project.CompositionChanges.ToList();
            var old = previous.SingleOrDefault(c => c.Weapon == next.Weapon && c.AttackRole == next.AttackRole && c.Kind == next.Kind && c.Phase == next.Phase && c.Scalar?.SemanticFieldId == next.Scalar?.SemanticFieldId);
            var revokeApproval = old != null && CompositionChangeService.ApprovalCurrent(Metadata!, old) && !next.SharedAcknowledged;
            var field = CompositionChangeService.Capability(Metadata!, next);
            if (!revokeApproval && field.AffectsMultipleWeapons && CompositionChangeService.HasObjectApproval(project, Metadata!, next.Scalar?.Weapon ?? next.Target.Weapon, field))
                next = CompositionChangeService.WithApproval(Metadata!, next, true);
            if (old != null)
            {
                next.Id = old.Id; next.Enabled = old.Enabled; next.EnsureEnabled = old.EnsureEnabled; next.Group = old.Group; next.Notes = old.Notes;
                // An edit left on a projectile or explosion the attack no longer uses is superseded by editing the value on the current object;
                // the old object's baseline and evidence do not carry over.
                if (!acceptBaseline && old.Target == next.Target && old.ExplosionTarget == next.ExplosionTarget)
                {
                    next.TargetEvidence = old.TargetEvidence; next.ReferenceEvidence = old.ReferenceEvidence; next.ExpectedExplosion = old.ExpectedExplosion;
                    next.BaselineSdkVersion = old.BaselineSdkVersion;
                    if (next.Scalar != null) { next.Scalar.ExpectedValue = old.Scalar!.ExpectedValue; next.Scalar.BaselineSdkVersion = old.Scalar.BaselineSdkVersion; }
                    if (next.DesiredExplosion == old.DesiredExplosion) next.DesiredReferenceEvidence = old.DesiredReferenceEvidence;
                }
            }
            project.CompositionChanges = project.CompositionChanges.Select(c => (revokeApproval || next.SharedAcknowledged) && CompositionChangeService.SameApprovalScope(Metadata!, c, next)
                ? CompositionChangeService.WithApproval(Metadata!, c, !revokeApproval) : c).ToList();
            project.CompositionChanges.RemoveAll(c => c.Id == old?.Id);
            if (!compositionChanges.IsNoOp(Metadata!, next)) project.CompositionChanges.Add(next);
            project.FormatVersion = Math.Max(project.FormatVersion, 4);
            try { await SaveChangesAsync(); } catch { project.CompositionChanges = previous; throw; }
        }
        finally { weaponEditGate.Release(); }
    }
    public async Task RemoveCompositionAsync(Guid id)
    {
        var previous = Project!.CompositionChanges.ToList(); Project.CompositionChanges.RemoveAll(c => c.Id == id);
        try { await SaveChangesAsync(); } catch { Project.CompositionChanges = previous; throw; }
    }
    public async Task SetCompositionApprovalAsync(Guid id, bool approved)
    {
        await weaponEditGate.WaitAsync();
        try
        {
            var project = Project!; var selected = project.CompositionChanges.Single(c => c.Id == id);
            var previous = project.CompositionChanges;
            project.CompositionChanges = previous.Select(c => CompositionChangeService.SameApprovalScope(Metadata!, c, selected)
                ? CompositionChangeService.WithApproval(Metadata!, c, approved) : c).ToList();
            try { await SaveChangesAsync(); } catch { project.CompositionChanges = previous; throw; }
        }
        finally { weaponEditGate.Release(); }
    }
    public async Task ToggleCompositionAsync(Guid id)
    {
        var c = Project!.CompositionChanges.Single(c => c.Id == id); c.Enabled = !c.Enabled;
        try { await SaveChangesAsync(); } catch { c.Enabled = !c.Enabled; throw; }
    }
    public async Task MoveToCompositionAsync(Guid id)
    {
        var project = Project; await weaponEditGate.WaitAsync();
        try
        {
            if (project == null || !ReferenceEquals(project, Project)) throw new InvalidOperationException(CoreText.Get("Messages.Workspace.ProjectChanged"));
            var group = WeaponGroups.Single(g => g.Sources.Any(c => c.Id == id));
            if (group.Conflict != null) throw new InvalidDataException(group.Conflict);
            var old = group.Representative; var field = group.Field!;
            if (!CompositionChangeService.ProjectileOwned(field)) throw new InvalidDataException(CoreText.Get("Messages.Weapon.NotProjectileObjectField"));
            var role = Metadata!.Composition!.Projectiles.Weapons.Single(w => w.Weapon == old.Weapon).Attacks.Single(a => a.Role == field.Backing!.Branch || a.Role == "feed_" + field.Backing.Branch).Role;
            if (EffectiveProjectile(old.Weapon, role) != new ProjectileReference(old.Weapon, role)) throw new InvalidDataException(CoreText.Get("Messages.Weapon.OverrideTargetsOriginal"));
            if (project.CompositionChanges.Any(c => c.Weapon == old.Weapon && c.AttackRole == role && c.Scalar?.SemanticFieldId == field.SemanticFieldId)) throw new InvalidDataException(CoreText.Get("Messages.Weapon.CompositionOverrideExists"));
            var next = compositionChanges.CreateScalar(project, Metadata, old.Weapon, role, "projectile", null, field.SemanticFieldId, old.DesiredValue.GetRawText(), false);
            next.Scalar!.ExpectedValue = old.ExpectedValue; next.Scalar.BaselineSdkVersion = old.BaselineSdkVersion; next.BaselineSdkVersion = old.BaselineSdkVersion;
            next.Enabled = group.Enabled; next.EnsureEnabled = old.EnsureEnabled; next.Group = old.Group; next.Notes = old.Notes;
            var previous = project.WeaponChanges.ToList(); project.WeaponChanges.RemoveAll(c => group.Sources.Contains(c)); project.CompositionChanges.Add(next);
            try { await SaveChangesAsync(); } catch { project.WeaponChanges = previous; project.CompositionChanges.Remove(next); throw; }
        }
        finally { weaponEditGate.Release(); }
    }
    public string? BuildError { get; private set; }
    public IReadOnlyList<WeaponChangeGroup> WeaponGroups => Project == null || Metadata == null ? [] : WeaponAliasResolver.Group(Metadata, Project.WeaponChanges);
    // One weapon's groups: a group never mixes weapons, so this is WeaponGroups filtered to the weapon, without grouping every other
    // weapon's edits (field editors read it several times per render).
    public IReadOnlyList<WeaponChangeGroup> WeaponGroupsFor(string weapon) => Project == null || Metadata == null ? []
        : WeaponAliasResolver.Group(Metadata, Project.WeaponChanges.Where(c => c.Weapon == weapon));
    public IReadOnlyList<WeaponChangeIssue> WeaponIssues => Project == null || Metadata == null ? [] : weaponChanges.Review(Metadata, Project.WeaponChanges);
    public IReadOnlyList<ProjectSummary> Library { get; private set; } = [];
    public SdkStatus? SdkStatus { get; private set; }
    public ModProject? Project { get; private set; }
    public SdkMetadata? Metadata { get; private set; }
    public string LuaPreview { get; private set; } = "";
    public string? LastExport { get; private set; }
    public string StorageLocation => paths.Root;
    public async Task InitializeAsync() { Library = await store.ListAsync(); await CheckUpdatesAsync(); }
    /// <summary>Why the installed SDK could not be loaded; null once it loads.</summary>
    public string? SdkError { get; private set; }
    public async Task CheckUpdatesAsync()
    {
        try { SdkStatus = await updates.CheckAsync(); SdkError = null; }
        catch (Exception e) when (e is InvalidDataException or NotSupportedException or IOException or System.Text.Json.JsonException) { SdkError = e.Message; throw; }
    }
    public async Task<CreationTicket> BeginCreationAsync()
    { var ticket = await updates.BeginCreationAsync(); SdkStatus = ticket.Status; return ticket; }
    public async Task<SdkMetadata> ResolveCreationAsync(CreationTicket ticket, UpdateDecision decision)
    {
        var sdk = await updates.ResolveCreationAsync(ticket, decision);
        if (SdkStatus != null) SdkStatus = SdkStatus with { Installed = sdk };
        return sdk;
    }
    public async Task InstallUpdateAsync()
    {
        await updates.InstallLatestAsync(SdkStatus?.Latest ?? throw new InvalidOperationException(CoreText.Get("Messages.Sdk.CheckFirst")));
        await CheckUpdatesAsync();
    }
    public async Task CreateAsync(CreateProjectRequest request, SdkMetadata sdk)
    { await OpenCreatedAsync(await projects.CreateAsync(request, sdk)); }
    public async Task DuplicateAsync(Guid id, CreateProjectRequest request)
    { await OpenCreatedAsync(await projects.DuplicateAsync(await store.LoadAsync(id), request)); }
    public async Task OpenAsync(Guid id) => await OpenCreatedAsync(await store.LoadAsync(id));
    private async Task OpenCreatedAsync(ModProject project)
    {
        var sdk = await cache.GetVersionAsync(project.SdkVersion);
        // Clean redundant overrides written by older GUI versions before exposing
        // the project. Unknown/type-changed fields remain available for review.
        if (WeaponAliasResolver.RemoveNoOps(sdk, project.WeaponChanges) + RemoveProjectileNoOps(sdk, project) + project.SupportChanges.RemoveAll(c => SupportChangeService.NoOp(sdk, c)) + project.StratagemChanges.RemoveAll(c => StratagemChangeService.NoOp(sdk, c)) + project.EntityChanges.RemoveAll(c => EntityChangeService.NoOp(sdk, c)) > 0) await store.SaveAsync(project);
        Project = project; Metadata = sdk; savedState = SavedState(project); RefreshPreview(); LastExport = null;
        // Custom Lua gets its working copy back if it was removed (never over an existing file, which may hold outside edits).
        await EnsureCustomLuaFileAsync();
        Library = await store.ListAsync();
    }
    public async Task ImportAsync()
    { var file = await picker.PickAsync(); if (file != null) await OpenCreatedAsync(await store.ImportAsync(file)); }
    public async Task RenameAsync(Guid id, string name)
    { await projects.RenameAsync(await store.LoadAsync(id), name); Library = await store.ListAsync(); if (Project?.Id == id) await OpenAsync(id); }
    public async Task RemoveAsync(Guid id)
    { await store.RemoveFromLibraryAsync(id); Library = await store.ListAsync(); if (Project?.Id == id) CloseProject(); }
    public void CloseProject() { Project = null; Metadata = null; LastExport = null; LuaPreview = ""; savedState = null; }
    public async Task AddOrEditChangeAsync(string target, string field, string value, bool ensure, string group, Guid? editId)
    {
        var next = changes.Create(Metadata!, target, field, value, ensure, group);
        var previous = Project!.Changes.ToList();
        if (editId is Guid id)
        { var old = Project.Changes.Single(c => c.Id == id); next.Id = old.Id; next.Enabled = old.Enabled; Project.Changes.Remove(old); }
        Project.Changes.Add(next);
        try { await SaveChangesAsync(); } catch { Project.Changes = previous; throw; }
    }
    public async Task ToggleChangeAsync(Guid id)
    {
        var change = Project!.Changes.Single(c => c.Id == id); change.Enabled = !change.Enabled;
        try { await SaveChangesAsync(); } catch { change.Enabled = !change.Enabled; throw; }
    }
    public async Task RemoveChangeAsync(Guid id)
    {
        var previous = Project!.Changes.ToList(); Project.Changes.RemoveAll(c => c.Id == id);
        try { await SaveChangesAsync(); } catch { Project.Changes = previous; throw; }
    }
    private async Task SaveChangesAsync()
    {
        var previous = Project!.WeaponChanges.ToList();
        var previousReferences = Project.ProjectileChanges.ToList();
        WeaponAliasResolver.RemoveNoOps(Metadata!, Project.WeaponChanges);
        RemoveProjectileNoOps(Metadata!, Project);
        // Format 8 marks 0.26.0 edits; a project without them keeps its format, so older ModBuilders still open it.
        var format = Project.FormatVersion; Project.FormatVersion = Math.Max(Project.FormatVersion, Projects.ProjectIdentity.RequiredFormat(Project));
        // An edit that leaves the project exactly as saved (a value committed again) has nothing to save, regenerate or re-list. The saved
        // JSON is the whole project state generation reads.
        if (SavedState(Project) == savedState) return;
        try { await store.SaveAsync(Project); } catch { Project.WeaponChanges = previous; Project.ProjectileChanges = previousReferences; Project.FormatVersion = format; throw; }
        savedState = SavedState(Project);
        RefreshPreview(); LastExport = null;
        // The store has just written this project's library entry; an edit changes no other entry, so the library is not read back.
        Library = [.. Library.Where(s => s.Id != Project.Id).Append(new ProjectSummary(Project.Id, Project.DisplayName, Project.ResourceId, Project.SdkVersion, Project.ModifiedAt))
            .OrderByDescending(s => s.ModifiedAt)];
    }
    // The project as the store writes it (JsonStorage.Options), including the ModifiedAt of its last save.
    private string? savedState;
    private static string SavedState(ModProject project) => System.Text.Json.JsonSerializer.Serialize(project, JsonStorage.Options);
    private int RemoveProjectileNoOps(SdkMetadata sdk, ModProject project) => project.ProjectileChanges.RemoveAll(c =>
    {
        // Changed/missing SDK evidence must remain visible for explicit review.
        try { projectileChanges.Validate(sdk, c); return projectileChanges.IsBaseline(sdk, c.Weapon, c.AttackRole, c.ReplacementProjectile); }
        catch (InvalidDataException) { return false; }
    });
    /// <summary>After a UI language change: rebuilds the preview so the build error (UI text) is in the new language. The generated Lua is
    /// the same in every language.</summary>
    public void RefreshMessages() { if (Project != null && Metadata != null) RefreshPreview(); }
    private void RefreshPreview()
    {
        optionTargets = null;
        try { LuaPreview = generator.Generate(Project!, Metadata!); BuildError = null; }
        catch (InvalidDataException e) { LuaPreview = "-- Build blocked: review the Changes page.\n"; BuildError = e.Message; }
    }
    public async Task SetWeaponChangeAsync(string weapon, string field, string value, bool acknowledge, string group = "Gameplay", string? notes = null)
    {
        // 1.4.0: with rate modes edited, the older fire rate is their default (Y) slot and is written there.
        if (await FoldLegacyFireRateAsync(CompositionKind.Player, weapon, field, value)) return;
        var project = Project;
        await weaponEditGate.WaitAsync();
        try
        {
            if (project == null || !ReferenceEquals(project, Project)) throw new InvalidOperationException(CoreText.Get("Messages.Workspace.ProjectChangedBeforeEdit"));
            var next = weaponChanges.Create(Metadata!, weapon, field, value, acknowledge); var previous = Project!.WeaponChanges.ToList();
            var saved = WeaponGroups.SingleOrDefault(g => g.Weapon == weapon && g.FieldId == next.SemanticFieldId);
            if (saved?.Conflict != null) throw new InvalidDataException(saved.Conflict);
            var old = saved?.Representative;
            if (old != null) { next.Id = old.Id; next.ExpectedValue = old.ExpectedValue; next.BaselineSdkVersion = old.BaselineSdkVersion; next.Enabled = old.Enabled; next.EnsureEnabled = old.EnsureEnabled; next.EffectAcknowledgement = old.EffectAcknowledgement; }
            next.Group = string.IsNullOrWhiteSpace(group) ? "Gameplay" : group.Trim(); next.Notes = notes;
            Project.WeaponChanges.RemoveAll(c => saved?.Sources.Contains(c) == true);
            if (!WeaponScalar.IsNoOp(Metadata!, next)) Project.WeaponChanges.Add(next);
            try { await SaveChangesAsync(); } catch { Project.WeaponChanges = previous; throw; }
        }
        finally { weaponEditGate.Release(); }
    }
    public async Task ResetWeaponsAsync(string? weapon = null, string? field = null)
    {
        // A reset of an available field is precisely an edit back to its SDK value.
        var capability = weapon != null && field != null ? Metadata?.PlayerWeapons?.FindCanonicalField(weapon, field) : null;
        var saved = WeaponGroups.SingleOrDefault(g => g.Weapon == weapon && g.FieldId == capability?.SemanticFieldId);
        // A rate-of-fire / programmable-ammunition field resets with its pair (Runtime writes them together).
        if (capability is { InWeaponSelector: true } && saved?.Conflict == null) { await ResetSelectorFieldAsync(CompositionKind.Player, weapon!, capability.SemanticFieldId); return; }
        if (capability?.Editable == true && field != null && saved?.Conflict == null)
        { await SetWeaponChangeAsync(weapon!, field, capability.CurrentDefault.GetRawText(), false); return; }
        var project = Project;
        await weaponEditGate.WaitAsync();
        try
        {
            if (project == null || !ReferenceEquals(project, Project)) throw new InvalidOperationException(CoreText.Get("Messages.Workspace.ProjectChangedBeforeReset"));
            // Resetting a whole weapon also resets its sub-targets (its underbarrel).
            bool Owned(string name) => name == weapon || field == null && Metadata?.PlayerWeapons?.FindSubweapon(name)?.SubweaponOf == weapon;
            var old = Project!.WeaponChanges.ToList(); Project.WeaponChanges.RemoveAll(c => (weapon == null || Owned(c.Weapon)) && (field == null || c.SemanticFieldId == field || saved?.Sources.Contains(c) == true));
            var oldReferences = Project.ProjectileChanges.ToList();
            var oldObjects = Project.CompositionChanges.ToList();
            var oldOutputs = Project.AttackOutputChanges?.ToList();
            if (field == null) Project.CompositionChanges.RemoveAll(c => weapon == null || c.Weapon == weapon);
            if (field == null) Project.ProjectileChanges.RemoveAll(c => weapon == null || c.Weapon == weapon);
            if (field == null && Project.AttackOutputChanges != null) { Project.AttackOutputChanges.RemoveAll(c => c.IsPlayer && (weapon == null || c.Weapon == weapon)); if (Project.AttackOutputChanges.Count == 0) Project.AttackOutputChanges = null; }
            try { await SaveChangesAsync(); } catch { Project.WeaponChanges = old; Project.ProjectileChanges = oldReferences; Project.CompositionChanges = oldObjects; Project.AttackOutputChanges = oldOutputs; throw; }
        }
        finally { weaponEditGate.Release(); }
    }
    // 0.26.0: Runtime's allow_unverified_effect opt-in for one saved player-weapon field (reticle, fire modes). Kept when the value changes.
    public async Task SetWeaponEffectAcknowledgedAsync(string weapon, string field, bool acknowledged)
    {
        var project = Project;
        await weaponEditGate.WaitAsync();
        try
        {
            if (project == null || !ReferenceEquals(project, Project)) throw new InvalidOperationException(CoreText.Get("Messages.Workspace.ProjectChangedBeforeAcknowledgement"));
            var f = Metadata!.PlayerWeapons!.FindCanonicalField(weapon, field) ?? throw new InvalidDataException(CoreText.Format("Messages.Weapon.FieldMissing", field));
            if (f.Acknowledgement != "allow_unverified_effect") throw new InvalidDataException(CoreText.Get("Messages.Workspace.EffectAcknowledgementNotRequired"));
            var changes = project.WeaponChanges.Where(c => c.Weapon == weapon && c.SemanticFieldId == f.SemanticFieldId).ToList();
            if (changes.Count == 0) throw new InvalidDataException(CoreText.Get("Messages.Workspace.ChangeBeforeAcknowledging"));
            var previous = changes.Select(c => c.EffectAcknowledgement).ToList();
            foreach (var c in changes) c.EffectAcknowledgement = acknowledged ? WeaponChangeService.EffectEvidence(weapon, f) : null;
            try { await SaveChangesAsync(); } catch { for (var i = 0; i < changes.Count; i++) changes[i].EffectAcknowledgement = previous[i]; throw; }
        }
        finally { weaponEditGate.Release(); }
    }
    public async Task ToggleWeaponChangeAsync(Guid id)
    {
        var group = WeaponGroups.Single(g => g.Sources.Any(c => c.Id == id)); var enabled = !group.Enabled;
        if (await ToggleSelectorAsync(CompositionKind.Player, group.Weapon, group.FieldId)) return;
        var previous = group.Sources.Select(c => (Change: c, c.Enabled)).ToArray();
        foreach (var c in group.Sources) c.Enabled = enabled;
        try { await SaveChangesAsync(); } catch { foreach (var item in previous) item.Change.Enabled = item.Enabled; throw; }
    }
    public async Task ResolveWeaponAliasAsync(Guid keepId)
    {
        var project = Project;
        await weaponEditGate.WaitAsync();
        try
        {
            if (project == null || !ReferenceEquals(project, Project)) throw new InvalidOperationException(CoreText.Get("Messages.Workspace.ProjectChangedBeforeResolve"));
            var group = WeaponGroups.Single(g => g.Sources.Any(c => c.Id == keepId));
            var previous = Project!.WeaponChanges.ToList();
            Project.WeaponChanges.RemoveAll(c => group.Sources.Contains(c) && c.Id != keepId);
            try { await SaveChangesAsync(); } catch { Project.WeaponChanges = previous; throw; }
        }
        finally { weaponEditGate.Release(); }
    }
    public async Task AcceptWeaponBaselineAsync(Guid id)
    {
        var c = Project!.WeaponChanges.Single(c => c.Id == id); var f = WeaponChangeService.Catalog(Metadata!).FindCanonicalField(c.Weapon, c.SemanticFieldId)!;
        var old = (c.ExpectedValue, c.BaselineSdkVersion); c.ExpectedValue = f.CurrentDefault.Clone(); c.BaselineSdkVersion = Metadata!.Version;
        try { await SaveChangesAsync(); } catch { (c.ExpectedValue, c.BaselineSdkVersion) = old; throw; }
    }
    public async Task RebindToInstalledSdkAsync()
    {
        var sdk = await cache.GetCurrentAsync(); var old = (Project!.SdkVersion, Metadata);
        var stratagems = (Project.StratagemChanges.ToList(), new Dictionary<string, string>(Project.StratagemApprovals), Project.FormatVersion);
        var entities = Project.EntityChanges.ToList();
        var projectiles = Project.ProjectileChanges.Select(c => (c, c.ExpectedEvidence, c.ReplacementEvidence)).ToArray();
        var composition = Project.CompositionChanges.Select(c => (c, c.TargetEvidence, c.ReferenceEvidence, c.DesiredReferenceEvidence)).ToArray();
        Project.SdkVersion = sdk.Version; Metadata = sdk;
        if (sdk.Stratagems != null && StratagemChangeService.Rebind(Project, old.Metadata?.Stratagems, sdk.Stratagems) > 0
            && Project.StratagemChanges.Count > 0) Project.FormatVersion = 5;
        // Evidence that differs only in package-residency data (SDK 0.27.0 automatic asset loading) is carried over, never re-reviewed.
        if (sdk.Entities != null) EntityChangeService.Rebind(Project, old.Metadata?.Entities, sdk.Entities);
        if (old.Metadata != null) { ProjectileChangeService.Rebind(Project, old.Metadata, sdk); CompositionChangeService.Rebind(Project, old.Metadata, sdk); }
        try { await SaveChangesAsync(); }
        catch
        {
            (Project.SdkVersion, Metadata) = old; (Project.StratagemChanges, Project.StratagemApprovals, Project.FormatVersion) = stratagems; Project.EntityChanges = entities;
            foreach (var (c, expected, replacement) in projectiles) (c.ExpectedEvidence, c.ReplacementEvidence) = (expected, replacement);
            foreach (var (c, target, reference, desired) in composition) (c.TargetEvidence, c.ReferenceEvidence, c.DesiredReferenceEvidence) = (target, reference, desired);
            throw;
        }
    }
    public async Task SaveExportDirectoryAsync(string path)
    {
        var previous = Project!.ExportDirectory; Project.ExportDirectory = path.Trim();
        try { await store.SaveAsync(Project); } catch { Project.ExportDirectory = previous; throw; }
    }
    public async Task SaveDetailsAsync(string name, string author, string version, string description)
    {
        var p = Project!;
        var previous = (p.DisplayName, p.Author, p.Version, p.Description);
        (p.DisplayName, p.Author, p.Version, p.Description) = (name.Trim(), author.Trim(), version.Trim(), description.Trim());
        try { await SaveChangesAsync(); }
        catch { (p.DisplayName, p.Author, p.Version, p.Description) = previous; throw; }
    }
    public async Task ExportAsync()
    {
        // Custom Lua is exported as ModBuilder saved it; a working copy changed outside ModBuilder must be reloaded or overwritten first.
        if (CustomLuaSyncIssue() is { } issue) throw new InvalidDataException(issue);
        LastExport = await exporter.ExportAsync(Project!, Metadata!);
    }
    public Task OpenExportAsync() => folders.OpenAsync(LastExport ?? Project!.ExportDirectory, LastExport != null);
    public Task OpenProjectFolderAsync(Guid id) => folders.OpenAsync(paths.ProjectDirectory(id));
}

/// <summary>Outcome of moving object edits to another projectile: values kept, values that equal the new object's baseline (nothing to
/// write), and values the new object cannot take (with the reason).</summary>
public sealed record ObjectEditCarryResult(int Kept, int AtBaseline, IReadOnlyList<string> NotKept);

using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Projects;
using HD2RuntimeGUI.Core.Storage;

namespace HD2RuntimeGUI.Core.Services;

public interface IFolderOpener { Task OpenAsync(string path, bool selectFile = false); }
public interface IProjectFilePicker { Task<string?> PickAsync(); }

// Application use cases. Components delegate I/O and generation to this service.
public sealed class BuilderWorkspace(IProjectStore store, IProjectService projects, ISdkCache cache,
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
            if (project == null || !ReferenceEquals(project, Project)) throw new InvalidOperationException("Active project changed.");
            var previous = project.ProjectileChanges.ToList();
            var old = previous.SingleOrDefault(c => c.Weapon == weapon && c.AttackRole == role);
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
            project.FormatVersion = 4;
            try { await SaveChangesAsync(); } catch { project.ProjectileChanges = previous; throw; }
        }
        finally { weaponEditGate.Release(); }
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
            if (project == null || !ReferenceEquals(project, Project)) throw new InvalidOperationException("Active project changed.");
            var next = create(); var previous = project.CompositionChanges.ToList();
            var old = previous.SingleOrDefault(c => c.Weapon == next.Weapon && c.AttackRole == next.AttackRole && c.Kind == next.Kind && c.Phase == next.Phase && c.Scalar?.SemanticFieldId == next.Scalar?.SemanticFieldId);
            if (old != null)
            {
                next.Id = old.Id; next.Enabled = old.Enabled; next.EnsureEnabled = old.EnsureEnabled; next.Group = old.Group; next.Notes = old.Notes;
                if (!acceptBaseline)
                {
                    if (old.Target != next.Target || old.ExplosionTarget != next.ExplosionTarget) throw new InvalidDataException("Composition target changed. Reset the old object edit before editing the new object.");
                    next.TargetEvidence = old.TargetEvidence; next.ReferenceEvidence = old.ReferenceEvidence; next.ExpectedExplosion = old.ExpectedExplosion;
                    next.BaselineSdkVersion = old.BaselineSdkVersion;
                    if (next.Scalar != null) { next.Scalar.ExpectedValue = old.Scalar!.ExpectedValue; next.Scalar.BaselineSdkVersion = old.Scalar.BaselineSdkVersion; }
                    if (next.DesiredExplosion == old.DesiredExplosion) next.DesiredReferenceEvidence = old.DesiredReferenceEvidence;
                }
            }
            project.CompositionChanges.RemoveAll(c => c.Id == old?.Id);
            if (!compositionChanges.IsNoOp(Metadata!, next)) project.CompositionChanges.Add(next);
            project.FormatVersion = 4;
            try { await SaveChangesAsync(); } catch { project.CompositionChanges = previous; throw; }
        }
        finally { weaponEditGate.Release(); }
    }
    public async Task RemoveCompositionAsync(Guid id)
    {
        var previous = Project!.CompositionChanges.ToList(); Project.CompositionChanges.RemoveAll(c => c.Id == id);
        try { await SaveChangesAsync(); } catch { Project.CompositionChanges = previous; throw; }
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
            if (project == null || !ReferenceEquals(project, Project)) throw new InvalidOperationException("Active project changed.");
            var group = WeaponGroups.Single(g => g.Sources.Any(c => c.Id == id));
            if (group.Conflict != null) throw new InvalidDataException(group.Conflict);
            var old = group.Representative; var field = group.Field!;
            if (!CompositionChangeService.ProjectileOwned(field)) throw new InvalidDataException("This is not a projectile-object field.");
            var role = Metadata!.Composition!.Projectiles.Weapons.Single(w => w.Weapon == old.Weapon).Attacks.Single(a => a.Role == field.Backing!.Branch || a.Role == "feed_" + field.Backing.Branch).Role;
            if (EffectiveProjectile(old.Weapon, role) != new ProjectileReference(old.Weapon, role)) throw new InvalidDataException("The saved override targets the original projectile. Reset the replacement first, or remove the old override and edit the replacement object.");
            if (project.CompositionChanges.Any(c => c.Weapon == old.Weapon && c.AttackRole == role && c.Scalar?.SemanticFieldId == field.SemanticFieldId)) throw new InvalidDataException("A Composition override already exists for this field.");
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
    public IReadOnlyList<WeaponChangeIssue> WeaponIssues => Project == null || Metadata == null ? [] : weaponChanges.Review(Metadata, Project.WeaponChanges);
    public IReadOnlyList<ProjectSummary> Library { get; private set; } = [];
    public SdkStatus? SdkStatus { get; private set; }
    public ModProject? Project { get; private set; }
    public SdkMetadata? Metadata { get; private set; }
    public string LuaPreview { get; private set; } = "";
    public string? LastExport { get; private set; }
    public string StorageLocation => paths.Root;
    public async Task InitializeAsync() { Library = await store.ListAsync(); SdkStatus = await updates.CheckAsync(); }
    public async Task CheckUpdatesAsync() => SdkStatus = await updates.CheckAsync();
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
        await updates.InstallLatestAsync(SdkStatus?.Latest ?? throw new InvalidOperationException("Check for SDK updates first."));
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
        if (WeaponAliasResolver.RemoveNoOps(sdk, project.WeaponChanges) + RemoveProjectileNoOps(sdk, project) > 0) await store.SaveAsync(project);
        Project = project; Metadata = sdk; RefreshPreview(); LastExport = null;
        Library = await store.ListAsync();
    }
    public async Task ImportAsync()
    { var file = await picker.PickAsync(); if (file != null) await OpenCreatedAsync(await store.ImportAsync(file)); }
    public async Task RenameAsync(Guid id, string name)
    { await projects.RenameAsync(await store.LoadAsync(id), name); Library = await store.ListAsync(); if (Project?.Id == id) await OpenAsync(id); }
    public async Task RemoveAsync(Guid id)
    { await store.RemoveFromLibraryAsync(id); Library = await store.ListAsync(); if (Project?.Id == id) CloseProject(); }
    public void CloseProject() { Project = null; Metadata = null; LastExport = null; LuaPreview = ""; }
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
        try { await store.SaveAsync(Project); } catch { Project.WeaponChanges = previous; Project.ProjectileChanges = previousReferences; throw; }
        RefreshPreview(); LastExport = null; Library = await store.ListAsync();
    }
    private int RemoveProjectileNoOps(SdkMetadata sdk, ModProject project) => project.ProjectileChanges.RemoveAll(c =>
    {
        // Changed/missing SDK evidence must remain visible for explicit review.
        try { projectileChanges.Validate(sdk, c); return projectileChanges.IsBaseline(sdk, c.Weapon, c.AttackRole, c.ReplacementProjectile); }
        catch (InvalidDataException) { return false; }
    });
    private void RefreshPreview()
    {
        try { LuaPreview = generator.Generate(Project!, Metadata!); BuildError = null; }
        catch (InvalidDataException e) { LuaPreview = "-- Build blocked: review the Changes page.\n"; BuildError = e.Message; }
    }
    public async Task SetWeaponChangeAsync(string weapon, string field, string value, bool acknowledge, string group = "Gameplay", string? notes = null)
    {
        var project = Project;
        await weaponEditGate.WaitAsync();
        try
        {
            if (project == null || !ReferenceEquals(project, Project)) throw new InvalidOperationException("The active project changed before the edit could be saved.");
            var next = weaponChanges.Create(Metadata!, weapon, field, value, acknowledge); var previous = Project!.WeaponChanges.ToList();
            var saved = WeaponGroups.SingleOrDefault(g => g.Weapon == weapon && g.FieldId == next.SemanticFieldId);
            if (saved?.Conflict != null) throw new InvalidDataException(saved.Conflict);
            var old = saved?.Representative;
            if (old != null) { next.Id = old.Id; next.ExpectedValue = old.ExpectedValue; next.BaselineSdkVersion = old.BaselineSdkVersion; next.Enabled = old.Enabled; next.EnsureEnabled = old.EnsureEnabled; }
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
        if (capability?.Editable == true && field != null && saved?.Conflict == null)
        { await SetWeaponChangeAsync(weapon!, field, capability.CurrentDefault.GetRawText(), false); return; }
        var project = Project;
        await weaponEditGate.WaitAsync();
        try
        {
            if (project == null || !ReferenceEquals(project, Project)) throw new InvalidOperationException("The active project changed before the reset could be saved.");
            var old = Project!.WeaponChanges.ToList(); Project.WeaponChanges.RemoveAll(c => (weapon == null || c.Weapon == weapon) && (field == null || c.SemanticFieldId == field || saved?.Sources.Contains(c) == true));
            var oldReferences = Project.ProjectileChanges.ToList();
            var oldObjects = Project.CompositionChanges.ToList();
            if (field == null) Project.CompositionChanges.RemoveAll(c => weapon == null || c.Weapon == weapon);
            if (field == null) Project.ProjectileChanges.RemoveAll(c => weapon == null || c.Weapon == weapon);
            try { await SaveChangesAsync(); } catch { Project.WeaponChanges = old; Project.ProjectileChanges = oldReferences; Project.CompositionChanges = oldObjects; throw; }
        }
        finally { weaponEditGate.Release(); }
    }
    public async Task ToggleWeaponChangeAsync(Guid id)
    {
        var group = WeaponGroups.Single(g => g.Sources.Any(c => c.Id == id)); var enabled = !group.Enabled;
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
            if (project == null || !ReferenceEquals(project, Project)) throw new InvalidOperationException("The active project changed before the conflict could be resolved.");
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
        Project.SdkVersion = sdk.Version; Metadata = sdk;
        try { await SaveChangesAsync(); } catch { (Project.SdkVersion, Metadata) = old; throw; }
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
    public async Task ExportAsync() => LastExport = await exporter.ExportAsync(Project!, Metadata!);
    public Task OpenExportAsync() => folders.OpenAsync(LastExport ?? Project!.ExportDirectory, LastExport != null);
    public Task OpenProjectFolderAsync(Guid id) => folders.OpenAsync(paths.ProjectDirectory(id));
}

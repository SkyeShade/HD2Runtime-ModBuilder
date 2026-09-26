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
        var preview = generator.Generate(project, sdk);
        Project = project; Metadata = sdk; LuaPreview = preview; LastExport = null;
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
    { var preview = generator.Generate(Project!, Metadata!); await store.SaveAsync(Project!); LuaPreview = preview; LastExport = null; Library = await store.ListAsync(); }
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

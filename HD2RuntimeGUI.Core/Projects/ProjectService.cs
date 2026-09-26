using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Storage;

namespace HD2RuntimeGUI.Core.Projects;

public static class ProjectIdentity
{
    public const string RuntimeModule = "mods/skyeshade/hd2runtime";
    public static void ValidateResource(string resource)
    {
        if (string.IsNullOrEmpty(resource) || !Regex.IsMatch(resource, @"\Amods/[A-Za-z0-9_]+(?:/[A-Za-z0-9_]+)+\z") ||
            resource.Equals(RuntimeModule, StringComparison.OrdinalIgnoreCase) || resource.StartsWith(RuntimeModule + "/", StringComparison.OrdinalIgnoreCase) ||
            resource.Equals("mods/codex/loader", StringComparison.OrdinalIgnoreCase) || Encoding.UTF8.GetByteCount($"-- HD2-Addon: {resource}\n") > 256)
            throw new InvalidDataException("Resource ID must be mods/author/mod_id using ASCII letters, digits and underscores, within 256 discovery bytes. Runtime/loader IDs are reserved.");
    }
    public static Guid ManagerGuid(string resource)
    {
        ValidateResource(resource);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("hd2runtime-mod:" + resource))[..16];
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50); bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }
    public static void Validate(ModProject p)
    {
        if (p.FormatVersion != 1 || p.Id == Guid.Empty) throw new InvalidDataException("Unsupported project format or identity.");
        if (string.IsNullOrWhiteSpace(p.DisplayName) || p.DisplayName.Length > 120 || string.IsNullOrWhiteSpace(p.Author) || p.Author.Length > 120)
            throw new InvalidDataException("Mod name and author are required (maximum 120 characters).");
        ValidateResource(p.ResourceId);
        if (p.ManagerGuid != ManagerGuid(p.ResourceId)) throw new InvalidDataException("Project manager GUID does not match its resource identity.");
        if (!Regex.IsMatch(p.Version, @"\A(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)\z")) throw new InvalidDataException("Mod version must be major.minor.patch.");
        SemVersion.Parse(p.Version); SemVersion.Parse(p.SdkVersion);
        if (p.RuntimeApi != 1 || p.Changes == null || p.Changes.Count > 1000 || p.Description.Length > 8000) throw new InvalidDataException("Invalid project data.");
        if (p.Changes.Select(c => c.Id).Distinct().Count() != p.Changes.Count) throw new InvalidDataException("Duplicate change IDs.");
        if (!Path.IsPathFullyQualified(p.ExportDirectory)) throw new InvalidDataException("Choose an absolute export directory.");
    }
}

public interface IProjectStore
{
    Task<IReadOnlyList<ProjectSummary>> ListAsync();
    Task<ModProject> LoadAsync(Guid id);
    Task SaveAsync(ModProject project);
    Task RemoveFromLibraryAsync(Guid id);
    Task<ModProject> ImportAsync(string file);
}

public sealed class JsonProjectStore(AppPaths paths) : IProjectStore
{
    private readonly SemaphoreSlim gate = new(1);
    private sealed class Library { public int FormatVersion { get; set; } = 1; public List<ProjectSummary> Projects { get; set; } = []; }
    private async Task<Library> ReadLibraryAsync()
    {
        var library = File.Exists(paths.Library) ? await JsonStorage.ReadAsync<Library>(paths.Library) : new Library();
        if (library.FormatVersion != 1 || library.Projects == null) throw new InvalidDataException("Unsupported project library.");
        return library;
    }
    public async Task<IReadOnlyList<ProjectSummary>> ListAsync() => (await ReadLibraryAsync()).Projects.OrderByDescending(p => p.ModifiedAt).ToArray();
    public async Task<ModProject> LoadAsync(Guid id)
    {
        var project = await JsonStorage.ReadAsync<ModProject>(paths.ProjectFile(id));
        ProjectIdentity.Validate(project);
        if (project.Id != id) throw new InvalidDataException("Project file identity mismatch.");
        return project;
    }
    public async Task SaveAsync(ModProject project)
    {
        ProjectIdentity.Validate(project);
        await gate.WaitAsync();
        try
        {
            var library = await ReadLibraryAsync();
            if (library.Projects.Any(p => p.Id != project.Id && p.ResourceId.Equals(project.ResourceId, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("A project with this resource ID already exists in the library.");
            project.ModifiedAt = DateTimeOffset.UtcNow;
            await JsonStorage.WriteAtomicAsync(paths.ProjectFile(project.Id), project);
            library.Projects.RemoveAll(p => p.Id == project.Id);
            library.Projects.Add(new(project.Id, project.DisplayName, project.ResourceId, project.SdkVersion, project.ModifiedAt));
            await JsonStorage.WriteAtomicAsync(paths.Library, library);
        }
        finally { gate.Release(); }
    }
    public async Task RemoveFromLibraryAsync(Guid id)
    {
        await gate.WaitAsync();
        try { var library = await ReadLibraryAsync(); library.Projects.RemoveAll(p => p.Id == id); await JsonStorage.WriteAtomicAsync(paths.Library, library); }
        finally { gate.Release(); }
    }
    public async Task<ModProject> ImportAsync(string file)
    {
        var project = await JsonStorage.ReadAsync<ModProject>(file);
        ProjectIdentity.Validate(project);
        // Keep one managed project root. Opening a removed project re-registers its file.
        if (File.Exists(paths.ProjectFile(project.Id)) && !Path.GetFullPath(file).Equals(paths.ProjectFile(project.Id), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("This project is already stored locally. Open it from the library to avoid overwriting changes.");
        await SaveAsync(project); return project;
    }
}

public interface IProjectService
{
    Task<ModProject> CreateAsync(CreateProjectRequest request, SdkMetadata sdk);
    Task<ModProject> DuplicateAsync(ModProject source, CreateProjectRequest request);
    Task RenameAsync(ModProject project, string name);
}
public sealed class ProjectService(IProjectStore store, AppPaths paths) : IProjectService
{
    public async Task<ModProject> CreateAsync(CreateProjectRequest request, SdkMetadata sdk)
    {
        var project = New(request, sdk.Version, sdk.ApiVersion);
        await store.SaveAsync(project); return project;
    }
    public async Task<ModProject> DuplicateAsync(ModProject source, CreateProjectRequest request)
    {
        var project = New(request, source.SdkVersion, source.RuntimeApi);
        project.Changes = System.Text.Json.JsonSerializer.Deserialize<List<ModChange>>(System.Text.Json.JsonSerializer.Serialize(source.Changes))!;
        foreach (var change in project.Changes) change.Id = Guid.NewGuid();
        await store.SaveAsync(project); return project;
    }
    public async Task RenameAsync(ModProject project, string name)
    {
        var old = project.DisplayName; project.DisplayName = name;
        try { await store.SaveAsync(project); } catch { project.DisplayName = old; throw; }
    }
    private ModProject New(CreateProjectRequest r, string sdkVersion, int api) => new()
    {
        DisplayName = r.DisplayName.Trim(), Author = r.Author.Trim(), ResourceId = r.ResourceId.Trim(),
        ManagerGuid = ProjectIdentity.ManagerGuid(r.ResourceId.Trim()), Version = r.Version.Trim(), Description = r.Description.Trim(),
        SdkVersion = sdkVersion, RuntimeApi = api, ExportDirectory = paths.Exports
    };
}

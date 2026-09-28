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
    // Suggested "mods/<author>/<mod>" from free-text names; null until both parts yield a valid identifier.
    // Taken IDs get a numeric suffix so the suggestion never collides with a library project.
    public static string? SuggestResourceId(string? author, string? modName, IEnumerable<string>? taken = null)
    {
        var (a, m) = (Slug(author, 48), Slug(modName, 64));
        if (a.Length == 0 || m.Length == 0) return null;
        var used = new HashSet<string>(taken ?? [], StringComparer.OrdinalIgnoreCase);
        for (var n = 1; n < 1000; n++)
        {
            var candidate = $"mods/{a}/{m}{(n == 1 ? "" : "_" + n)}";
            if (used.Contains(candidate)) continue;
            try { ValidateResource(candidate); return candidate; } catch (InvalidDataException) { return null; }
        }
        return null;
    }
    private static string Slug(string? text, int max)
    {
        var builder = new StringBuilder();
        foreach (var c in (text ?? "").Normalize(NormalizationForm.FormD))
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            if (char.IsAsciiLetterOrDigit(c)) builder.Append(char.ToLowerInvariant(c));
            else if (builder.Length > 0 && builder[^1] != '_') builder.Append('_');
        }
        var slug = builder.ToString().Trim('_');
        return slug.Length <= max ? slug : slug[..max].TrimEnd('_');
    }
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
    // Semantic graph identity only: target kind must agree with the graph path, and entity/weapon/attack are short published identifiers.
    private static bool ValidStratagemTarget(StratagemChange c)
    {
        static bool Id(string? s) => s != null && Regex.IsMatch(s, @"\A[a-z][a-z0-9_]{0,63}\z");
        return (c.TargetKind, c.Path) switch
        {
            _ when c.Zone != null && (c.TargetKind, c.Path) != ("deployed_entity", "damage_zone") => false,
            ("stratagem", "stratagem" or "eagle_rearm") => c.Entity == null && c.Weapon == null && c.Attack == null,
            ("stratagem", "attack") => c.Entity == null && c.Weapon == null && Id(c.Attack),
            ("deployed_entity", "deployed_entity" or "shield") => Id(c.Entity) && c.Weapon == null && c.Attack == null,
            ("deployed_entity", "damage_zone") => Id(c.Entity) && Id(c.Zone) && c.Weapon == null && c.Attack == null,
            ("mounted_weapon", "weapon") => Id(c.Entity) && Id(c.Weapon) && c.Attack == null,
            ("mounted_weapon", "attack") => Id(c.Entity) && Id(c.Weapon) && Id(c.Attack),
            _ => false,
        };
    }
    public static void Validate(ModProject p)
    {
        if (p.FormatVersion is not (1 or 2 or 3 or 4 or 5 or 6) || p.Id == Guid.Empty) throw new InvalidDataException("Unsupported project format or identity.");
        if (string.IsNullOrWhiteSpace(p.DisplayName) || p.DisplayName.Length > 120 || string.IsNullOrWhiteSpace(p.Author) || p.Author.Length > 120)
            throw new InvalidDataException("Mod name and author are required (maximum 120 characters).");
        ValidateResource(p.ResourceId);
        if (p.ManagerGuid != ManagerGuid(p.ResourceId)) throw new InvalidDataException("Project manager GUID does not match its resource identity.");
        if (!Regex.IsMatch(p.Version, @"\A(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)\z")) throw new InvalidDataException("Mod version must be major.minor.patch.");
        SemVersion.Parse(p.Version); SemVersion.Parse(p.SdkVersion);
        if (p.RuntimeApi != 1 || p.Changes == null || p.Changes.Count > 1000 || p.Description.Length > 8000) throw new InvalidDataException("Invalid project data.");
        if (p.Changes.Select(c => c.Id).Distinct().Count() != p.Changes.Count) throw new InvalidDataException("Duplicate change IDs.");
        if (p.WeaponChanges == null || p.WeaponChanges.Count > 1000 || p.WeaponChanges.Any(c => c.Id == Guid.Empty || string.IsNullOrWhiteSpace(c.Weapon) || c.Weapon.Length > 256 || string.IsNullOrWhiteSpace(c.SemanticFieldId) || c.SemanticFieldId.Length > 128 || c.Group.Length > 120 || c.Notes?.Length > 4000 || c.AcknowledgedAffectedWeapons == null || c.ExpectedValue.ValueKind is System.Text.Json.JsonValueKind.Undefined or System.Text.Json.JsonValueKind.Object or System.Text.Json.JsonValueKind.Array || c.DesiredValue.ValueKind is System.Text.Json.JsonValueKind.Undefined or System.Text.Json.JsonValueKind.Object or System.Text.Json.JsonValueKind.Array)) throw new InvalidDataException("Invalid weapon overrides.");
        if (p.WeaponChanges.Select(c => c.Id).Distinct().Count() != p.WeaponChanges.Count) throw new InvalidDataException("Duplicate weapon change IDs.");
        if (p.ProjectileChanges == null || p.ProjectileChanges.Count > 1000 || p.ProjectileChanges.Any(c => c.Id == Guid.Empty || string.IsNullOrWhiteSpace(c.Weapon) || c.Weapon.Length > 256
            || !Regex.IsMatch(c.AttackRole, "\\A[a-z][a-z_0-9]{0,63}\\z") || c.SemanticFieldId != "attack.projectile" || c.ExpectedProjectile != new ProjectileReference(c.Weapon, c.AttackRole)
            || c.ReplacementProjectile == null || string.IsNullOrWhiteSpace(c.ReplacementProjectile.Weapon) || c.ReplacementProjectile.Weapon.Length > 256
            || !Regex.IsMatch(c.ReplacementProjectile.AttackRole, "\\A[a-z][a-z_0-9]{0,63}\\z") || c.Group.Length > 120 || c.Notes?.Length > 4000
            || !Regex.IsMatch(c.ExpectedEvidence, "\\A[a-f0-9]{64}\\z") || !Regex.IsMatch(c.ReplacementEvidence, "\\A[a-f0-9]{64}\\z"))) throw new InvalidDataException("Invalid semantic projectile overrides.");
        if (p.ProjectileChanges.Select(c => c.Id).Distinct().Count() != p.ProjectileChanges.Count) throw new InvalidDataException("Duplicate projectile change IDs.");
        foreach (var c in p.ProjectileChanges) SemVersion.Parse(c.BaselineSdkVersion);
        if (p.SupportChanges == null || p.SupportChanges.Count > 2000 || p.SupportApprovals == null || p.SupportApprovals.Count > 2000
            || p.SupportChanges.Select(c => c.InstanceKey).Distinct().Count() != p.SupportChanges.Count || p.SupportChanges.Select(c => c.Id).Distinct().Count() != p.SupportChanges.Count
            || p.SupportChanges.Any(c => c.Id == Guid.Empty || c.InstanceKey.Length > 512 || !c.InstanceKey.StartsWith("support-field/v1/", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(c.Weapon) || c.Weapon.Length > 256 || c.SemanticFieldId.Length > 128 || c.Group.Length > 120 || c.Notes?.Length > 4000
                || !Regex.IsMatch(c.CapabilityEvidence, @"\A[a-f0-9]{64}\z") || c.EffectAcknowledgement != null && !Regex.IsMatch(c.EffectAcknowledgement, @"\A[a-f0-9]{64}\z")
                || c.FieldType is not ("number" or "integer" or "boolean")
                || c.ExpectedValue.ValueKind is not (System.Text.Json.JsonValueKind.Number or System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)
                || c.DesiredValue.ValueKind is not (System.Text.Json.JsonValueKind.Number or System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False))) throw new InvalidDataException("Invalid support overrides.");
        foreach (var c in p.SupportChanges) SemVersion.Parse(c.BaselineSdkVersion);
        foreach (var a in p.SupportApprovals) if (a.Key.Length > 256 || !a.Key.StartsWith("support-scope/v1/", StringComparison.Ordinal) || !Regex.IsMatch(a.Value, @"\A[a-f0-9]{64}\z")) throw new InvalidDataException("Invalid support approval.");
        if (p.StratagemChanges == null || p.StratagemApprovals == null || p.StratagemChanges.Count > 2000 || p.StratagemApprovals.Count > 2000
            || p.StratagemChanges.Select(c => c.InstanceKey).Distinct().Count() != p.StratagemChanges.Count
            || p.StratagemChanges.Select(c => c.Id).Distinct().Count() != p.StratagemChanges.Count
            || p.StratagemChanges.Any(c => c.Id == Guid.Empty || !ValidStratagemTarget(c) || string.IsNullOrWhiteSpace(c.Stratagem)
                || c.Stratagem.Length > 256 || !c.InstanceKey.StartsWith("stratagem:", StringComparison.Ordinal) || c.InstanceKey.Length > 512
                || c.SemanticFieldId.Length > 128
                || c.Group.Length > 120 || c.Notes?.Length > 4000 || c.FieldType is not ("number" or "integer" or "boolean")
                || !Regex.IsMatch(c.CapabilityEvidence, @"\A[a-f0-9]{64}\z")
                || c.ExpectedValue.ValueKind is not (System.Text.Json.JsonValueKind.Number or System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)
                || c.DesiredValue.ValueKind is not (System.Text.Json.JsonValueKind.Number or System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)))
            throw new InvalidDataException("Invalid semantic stratagem overrides.");
        foreach (var c in p.StratagemChanges) SemVersion.Parse(c.BaselineSdkVersion);
        foreach (var a in p.StratagemApprovals) if (a.Key.Length > 256 || !Regex.IsMatch(a.Value, @"\A[a-f0-9]{64}\z")) throw new InvalidDataException("Invalid stratagem approval.");
        // Format 6: vehicle/backpack (0.23.0), magazine attachment (0.23.1) and booster (0.24.0) changes. Targets are published names and zone_N / slot_N identities; mount values are published semantic IDs.
        static bool Slot(string? s, string prefix) => s != null && Regex.IsMatch(s, @"\A" + prefix + @"_[0-9]{1,3}\z");
        if (p.EntityChanges == null || p.EntityApprovals == null || p.EntityChanges.Count > 4000 || p.EntityApprovals.Count > 2000
            || p.EntityChanges.Select(c => c.Id).Distinct().Count() != p.EntityChanges.Count
            || p.EntityChanges.Select(c => c.InstanceKey).Distinct().Count() != p.EntityChanges.Count
            || p.EntityChanges.Any(c => c.Id == Guid.Empty || string.IsNullOrWhiteSpace(c.Entity) || c.Entity.Length > 256 || c.SemanticFieldId.Length > 128
                || !c.InstanceKey.StartsWith((c.Resource == "weapon_attachment" ? "attachment" : c.Resource) + ":", StringComparison.Ordinal) || c.InstanceKey.Length > 512
                || !((c.Resource, c.Path) switch
                {
                    ("vehicle", "entity") => c.Zone == null && c.Mount == null,
                    ("vehicle", "damage_zone") => Slot(c.Zone, "zone") && c.Mount == null,
                    ("vehicle", "mount") => Slot(c.Mount, "slot") && c.Zone == null && c.FieldType == EntityField.ReferenceType,
                    ("backpack", "backpack") => c.Zone == null && c.Mount == null,
                    // 0.23.1 magazine attachment definitions: Entity is the published attachment semantic ID.
                    ("weapon_attachment", "magazine") => c.Zone == null && c.Mount == null && c.FieldType == "integer"
                        && Regex.IsMatch(c.Entity, @"\Aweapon-attachment/v1/magazine/[a-z0-9-]{1,96}/[0-9a-f]{16}\z"),
                    // 0.24.0 boosters: Entity is the published booster name; only its reviewed sub-targets carry fields.
                    ("booster", "deployed_entity" or "status_effect") => c.Zone == null && c.Mount == null && BoosterAuthoringReader.ValidName(c.Entity),
                    _ => false,
                })
                || (c.FieldType == EntityField.ReferenceType
                    ? c.ExpectedValue.ValueKind != System.Text.Json.JsonValueKind.String || c.DesiredValue.ValueKind != System.Text.Json.JsonValueKind.String
                        || !Regex.IsMatch(c.DesiredValue.GetString()!, @"\Amounted-weapon/v1/[a-z0-9-]{1,96}/[0-9a-f]{16}\z")
                    : c.FieldType is not ("integer" or "number") || c.ExpectedValue.ValueKind != System.Text.Json.JsonValueKind.Number || c.DesiredValue.ValueKind != System.Text.Json.JsonValueKind.Number)
                || c.Group.Length > 120 || c.Notes?.Length > 4000 || !Regex.IsMatch(c.CapabilityEvidence, @"\A[a-f0-9]{64}\z")
                || c.ReferenceAcknowledgement != null && !Regex.IsMatch(c.ReferenceAcknowledgement, @"\A[a-f0-9]{64}\z")))
            throw new InvalidDataException("Invalid semantic vehicle/backpack overrides.");
        foreach (var c in p.EntityChanges) SemVersion.Parse(c.BaselineSdkVersion);
        foreach (var a in p.EntityApprovals) if (a.Key.Length > 256 || !Regex.IsMatch(a.Value, @"\A[a-f0-9]{64}\z")) throw new InvalidDataException("Invalid vehicle/backpack approval.");
        if (p.CompositionChanges == null || p.CompositionChanges.Count > 1000 || p.CompositionChanges.Select(c => c.Id).Distinct().Count() != p.CompositionChanges.Count) throw new InvalidDataException("Invalid composition changes.");
        foreach (var c in p.CompositionChanges)
        {
            bool Reference(ProjectileReference? r) => r != null && !string.IsNullOrWhiteSpace(r.Weapon) && r.Weapon.Length <= 256 && Regex.IsMatch(r.AttackRole, @"\A[a-z][a-z_0-9]{0,63}\z");
            bool Explosion(ExplosionReference? r) => r != null && (r.IsNone ? r.Phase == null : Reference(r.Projectile) && r.Phase is "impact" or "expiry");
            if (c.Id == Guid.Empty || !Reference(new(c.Weapon, c.AttackRole)) || !Reference(c.Target) || c.Kind is not ("projectile" or "explosion" or "terminal")
                || (c.Kind == "projectile" ? c.Phase != null : c.Phase is not ("impact" or "expiry")) || c.Group.Length > 120 || c.Notes?.Length > 4000
                || (c.Kind == "terminal" ? c.Scalar != null || !Explosion(c.ExpectedExplosion) || !Explosion(c.DesiredExplosion) : c.Scalar == null)
                || c.Kind == "explosion" && !Explosion(c.ExplosionTarget)) throw new InvalidDataException("Invalid semantic composition override.");
            SemVersion.Parse(c.BaselineSdkVersion);
        }
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
        project.WeaponChanges = System.Text.Json.JsonSerializer.Deserialize<List<WeaponChange>>(System.Text.Json.JsonSerializer.Serialize(source.WeaponChanges))!;
        foreach (var change in project.WeaponChanges) change.Id = Guid.NewGuid();
        project.ProjectileChanges = System.Text.Json.JsonSerializer.Deserialize<List<ProjectileChange>>(System.Text.Json.JsonSerializer.Serialize(source.ProjectileChanges))!;
        foreach (var change in project.ProjectileChanges) change.Id = Guid.NewGuid();
        project.CompositionChanges = System.Text.Json.JsonSerializer.Deserialize<List<CompositionChange>>(System.Text.Json.JsonSerializer.Serialize(source.CompositionChanges))!;
        foreach (var change in project.CompositionChanges) change.Id = Guid.NewGuid();
        project.SupportChanges = source.SupportChanges.Select(c => c with { Id = Guid.NewGuid() }).ToList();
        project.SupportApprovals = new(source.SupportApprovals);
        project.StratagemChanges = source.StratagemChanges.Select(c => c with { Id = Guid.NewGuid() }).ToList();
        project.StratagemApprovals = new(source.StratagemApprovals);
        project.EntityChanges = source.EntityChanges.Select(c => c with { Id = Guid.NewGuid() }).ToList();
        project.EntityApprovals = new(source.EntityApprovals);
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

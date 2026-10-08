using System.Text.Json;
using HD2RuntimeGUI.Core.CustomContent;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Storage;

namespace HD2RuntimeGUI.Core.Services;

/// <summary>Assigning a custom projectile would replace what the attack fires now (a vanilla projectile swap, and/or projectile and explosion
/// edits made on its own projectile), or would take the custom projectile away from other attacks because the target binds only so many at
/// once (Displaced). Asked where the assignment is made; nothing is saved until the user chooses.</summary>
public sealed class CustomAssignmentConflictException(string weapon, int swaps, int edits, IReadOnlyList<CustomProjectileAssignment>? displaced = null)
    : InvalidOperationException(Describe(weapon, swaps, edits, displaced ?? []))
{
    public int Swaps { get; } = swaps;
    public int Edits { get; } = edits;
    public IReadOnlyList<CustomProjectileAssignment> Displaced { get; } = displaced ?? [];
    private static string Describe(string weapon, int swaps, int edits, IReadOnlyList<CustomProjectileAssignment> displaced) => string.Join(" ", new[]
    {
        swaps + edits > 0 ? CoreText.Format("CustomContent.Assignment.Pending", weapon, swaps, edits) : null,
        displaced.Count > 0 ? CoreText.Format("CustomContent.Assignment.Displaces", string.Join(", ", displaced.Select(a => CustomProjectileHost.Of(a).Label + " (" + a.Projectile.Id + ")")), weapon) : null,
    }.OfType<string>());
}
/// <summary>A custom projectile cannot be deleted while something uses it: the uses, to resolve where the deletion is asked.</summary>
public sealed class CustomReferenceException(string id, IReadOnlyList<CustomEdge> uses)
    : InvalidOperationException(CoreText.Format("CustomContent.Delete.Referenced", id, uses.Count))
{
    public IReadOnlyList<CustomEdge> Uses { get; } = uses;
}

// Experimental custom content (format 14): every edit goes through EditCustomContentAsync, which saves the whole section or nothing. Edits
// that leave a problem are saved too (the problem is shown where the object is edited and refuses the build), as for additional files.
public sealed partial class BuilderWorkspace
{
    private ICustomContentTarget? customTarget;
    /// <summary>The target adapter custom content compiles through (the HD2Runtime 0.30.0 custom stratagem API in 1.7).</summary>
    public ICustomContentTarget CustomTarget { get => customTarget ??= CustomContentTargets.Current; set => customTarget = value; }
    public CustomContentSection? CustomContent => Project?.CustomContent;
    public IReadOnlyList<CustomStratagem> CustomStratagems => Project?.CustomContent?.Stratagems ?? [];
    public IReadOnlyList<CustomProjectile> CustomProjectiles => Project?.CustomContent?.Projectiles ?? [];
    public CustomStratagem? FindCustomStratagem(string id) => CustomStratagems.FirstOrDefault(s => s.Id == id);
    public CustomProjectile? FindCustomProjectile(string id) => CustomProjectiles.FirstOrDefault(p => p.Id == id);

    // ---- diagnostics and graph ------------------------------------------------------------------------------------------------------------

    private (string? State, object? Sdk, IReadOnlyList<CustomDiagnostic> List) customDiagnostics;
    /// <summary>Every check of the project's custom content, computed once per saved state.</summary>
    public IReadOnlyList<CustomDiagnostic> CustomDiagnostics()
    {
        if (Project?.CustomContent == null) return [];
        if (customDiagnostics.List != null && ReferenceEquals(customDiagnostics.State, savedState) && ReferenceEquals(customDiagnostics.Sdk, Metadata)) return customDiagnostics.List;
        var list = CustomContentValidator.Validate(Project, CustomTarget, Metadata);
        customDiagnostics = (savedState, Metadata, list);
        return list;
    }
    public IReadOnlyList<CustomDiagnostic> CustomDiagnosticsFor(string owner) => CustomDiagnostics().Where(d => d.Owner == owner).ToArray();
    /// <summary>The worst severity among an object's errors and warnings (Pass when it has neither).</summary>
    public CustomSeverity CustomState(string owner) => CustomDiagnosticsFor(owner).Select(d => d.Severity).Where(s => s is CustomSeverity.Error or CustomSeverity.Warning)
        .DefaultIfEmpty(CustomSeverity.Pass).Max();
    /// <summary>The first error that refuses the build (the UI opens its owner), or null.</summary>
    public CustomDiagnostic? CustomBuildIssue => CustomDiagnostics().FirstOrDefault(d => d.Severity == CustomSeverity.Error);
    public CustomContentGraph CustomGraph() => CustomContentGraph.Build(Project?.CustomContent);
    public string CustomProjectilePreview(string id) => FindCustomProjectile(id) is { } p ? CustomTarget.ProjectilePreview(p, Project!) : "";
    public string CustomRegistrationPreview(string id) => FindCustomStratagem(id) is { } s ? CustomTarget.RegistrationPreview(s, Project!.CustomContent!) : "";

    // ---- stratagems -------------------------------------------------------------------------------------------------------------------------

    /// <summary>Creates a custom stratagem with an id suggested from its name and the chosen family's defaults. Returns its id.</summary>
    public async Task<string> CreateCustomStratagemAsync(string name, string family = DeliveryFamily.Orbital)
    {
        string id = "";
        await EditCustomContentAsync(c =>
        {
            id = CustomIds.Suggest(name, c.Stratagems.Select(s => s.Id), "custom") ?? CustomIds.Unique("custom_stratagem", c.Stratagems.Select(s => s.Id));
            // It starts complete (a description the author replaces), so a new custom stratagem is ready to build from its first save.
            var s = new CustomStratagem { Id = id, Name = name.Trim().ToUpperInvariant(), NameCased = name.Trim(), Description = name.Trim() + ".",
                Code = UniqueCode(["up", "down", "right", "left"], c), Delivery = CustomFields.NewDelivery(family) };
            CustomTarget.ApplyFamilyDefaults(s);
            // Every custom stratagem needs an icon: a generated placeholder until one is imported.
            s.Icon = NewImage(c, s.Id, CustomIconCompiler.Placeholder(s.Id), null).Id;
            c.Stratagems.Add(s);
        });
        return id;
    }
    public async Task<string> CreateCustomStratagemFromTemplateAsync(string key)
    {
        var template = CustomTemplates.Stratagems.First(t => t.Key == key);
        string id = "";
        await EditCustomContentAsync(c =>
        {
            var s = template.Create();
            s.Id = id = CustomIds.Unique(s.Id, c.Stratagems.Select(x => x.Id));
            // The icon keeps the Runtime example's own image id (it may differ from the stratagem's), so a template registers exactly as
            // the example does; a placeholder until a PNG is imported.
            var image = NewImage(c, s.Icon is { } own && CustomIds.ValidImage(own) ? own : s.Id, CustomIconCompiler.Placeholder(s.Id), null);
            s.Icon = image.Id;
            // A second copy of a template must not collide with another call-in code.
            s.Code = UniqueCode(s.Code, c);
            PlaceholderPresentationIcons(c, s);
            c.Stratagems.Add(s);
        });
        return id;
    }
    public async Task<string> DuplicateCustomStratagemAsync(string id)
    {
        string copy = "";
        await EditCustomContentAsync(c =>
        {
            var source = c.Stratagems.Single(s => s.Id == id);
            var s = Clone(source);
            s.Id = copy = CustomIds.Unique(source.Id + "_copy", c.Stratagems.Select(x => x.Id));
            s.Name = Truncate(source.Name + " COPY", 64); s.NameCased = source.NameCased == null ? null : Truncate(source.NameCased + " Copy", 64);
            if (source.Icon != null && c.Images.FirstOrDefault(i => i.Id == source.Icon) is { } image)
                s.Icon = NewImage(c, s.Id, Convert.FromBase64String(image.Png), image.SourceName, image.Preparation).Id;
            // The copy gets its own call-in code: the same code (or one that starts it) would collide.
            s.Code = UniqueCode(source.Code, c);
            // A delivered weapon's own icon is copied too, so editing one never changes the other.
            foreach (var presentation in new[] { s.Delivery.Expendable?.Presentation, s.Delivery.Variant?.Presentation })
                if (presentation is { IconMode: DeliveredPresentation.Custom, Icon: { } icon } && c.Images.FirstOrDefault(i => i.Id == icon) is { } weaponImage)
                    presentation.Icon = NewImage(c, s.Id + "_weapon", Convert.FromBase64String(weaponImage.Png), weaponImage.SourceName, weaponImage.Preparation).Id;
            // So is its model.
            if (s.Delivery.Variant is { Model: { } modelId } && c.Models.FirstOrDefault(m => m.Id == modelId) is { } model)
            {
                var copyModel = Clone(model);
                copyModel.Id = CustomIds.Unique(s.Id + "_model", c.Models.Select(m => m.Id));
                c.Models.Add(copyModel);
                s.Delivery.Variant.Model = copyModel.Id;
            }
            c.Stratagems.Insert(c.Stratagems.IndexOf(source) + 1, s);
        });
        return copy;
    }
    /// <summary>Renames the stratagem's display names only; its id (its identity) never follows a name.</summary>
    public Task RenameCustomStratagemAsync(string id, string name) => EditCustomStratagemAsync(id, s =>
    {
        s.Name = name.Trim().ToUpperInvariant();
        s.NameCased = name.Trim();
    });
    public Task EditCustomStratagemAsync(string id, Action<CustomStratagem> edit) => EditCustomContentAsync(c => edit(c.Stratagems.Single(s => s.Id == id)));
    /// <summary>Switches the delivery family: the new family starts from its defaults (only one family's settings are kept), and the beacon
    /// moves to one the family can throw.</summary>
    public Task SetCustomDeliveryFamilyAsync(string id, string family) => EditCustomStratagemAsync(id, s =>
    {
        if (s.Delivery.Family == family) return;
        s.Delivery = CustomFields.NewDelivery(family);
        CustomTarget.ApplyFamilyDefaults(s);
    });
    /// <summary>Switches an orbital between the Runtime barrage and the vanilla barrage. Only the pattern and the impact carry over (the fields a
    /// vanilla barrage takes); a carrier group that cannot carry the new mode moves to its default group.</summary>
    public Task SetOrbitalModeAsync(string id, string mode) => EditCustomStratagemAsync(id, s =>
    {
        var o = s.Delivery.Orbital ??= new();
        if (o.Mode == mode) return;
        s.Delivery.Orbital = new() { Mode = mode, Pattern = o.Pattern, ImpactPayload = o.ImpactPayload, Shell = mode == OrbitalDelivery.RuntimeBarrage ? o.Shell : null };
        if (s.Carrier.Group != null && !CustomTarget.CarrierGroups(s).Any(g => g.Name == s.Carrier.Group)) s.Carrier.Group = CustomTarget.DefaultCarrierGroup(s);
    });
    // A code that no other custom stratagem's code equals, starts or is started by, and that is no native stratagem's code (nor the start of,
    // or started by, a selectable one's): extended with directions until it is free.
    private List<string> UniqueCode(IReadOnlyList<string> code, CustomContentSection c)
    {
        var natives = CustomTarget.Schema.Catalogs.NativeCodes;
        bool Taken(IReadOnlyList<string> x) => natives.Any(n => Related(x, n.Code) is { } equal && (equal || n.Catalogued));
        return CustomIds.UniqueCode(code, c.Stratagems.Select(o => o.Code), Taken) ?? [.. code];
    }
    // null: unrelated; true: equal; false: one starts the other.
    private static bool? Related(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var n = Math.Min(a.Count, b.Count);
        for (var i = 0; i < n; i++) if (a[i] != b[i]) return null;
        return a.Count == b.Count;
    }
    // A template's delivered weapon icon names an image of the Runtime's example project: it starts as a placeholder of its own.
    private static void PlaceholderPresentationIcons(CustomContentSection c, CustomStratagem s)
    {
        foreach (var presentation in new[] { s.Delivery.Expendable?.Presentation, s.Delivery.Variant?.Presentation })
            if (presentation is { IconMode: DeliveredPresentation.Custom })
                presentation.Icon = NewImage(c, s.Id + "_weapon", CustomIconCompiler.Placeholder(s.Id + "_weapon"), null).Id;
    }
    /// <summary>Changes a custom stratagem's id (its identity: other players' Runtimes and installed builds know it by it).</summary>
    public Task ChangeCustomStratagemIdAsync(string id, string newId) => EditCustomContentAsync(c =>
    {
        newId = newId.Trim();
        if (!CustomIds.Valid(newId)) throw new InvalidDataException(CoreText.Format("CustomContent.Check.IdInvalid", CustomIds.MaxLength));
        if (newId != id && c.Stratagems.Any(s => s.Id == newId)) throw new InvalidDataException(CoreText.Format("CustomContent.Check.IdDuplicate", newId));
        c.Stratagems.Single(s => s.Id == id).Id = newId;
    });
    public Task DeleteCustomStratagemAsync(string id) => EditCustomContentAsync(c => c.Stratagems.RemoveAll(s => s.Id == id));
    /// <summary>Moves a custom stratagem up or down the list (the order the build registers them in).</summary>
    public Task MoveCustomStratagemAsync(string id, int offset) => EditCustomContentAsync(c =>
    {
        var index = c.Stratagems.FindIndex(s => s.Id == id);
        var to = Math.Clamp(index + offset, 0, c.Stratagems.Count - 1);
        if (index < 0 || to == index) return;
        var s = c.Stratagems[index];
        c.Stratagems.RemoveAt(index);
        c.Stratagems.Insert(to, s);
    });

    // ---- HD2Runtime project files ---------------------------------------------------------------------------------------------------------

    /// <summary>The project's custom stratagems as HD2Runtime's own project file (custom_stratagems.json), as the Runtime SDK's tools read it.
    /// Scripted ones and the advanced section's Lua have no place in that format and are left out.</summary>
    public string CustomRuntimeProjectJson() => RuntimeProjectForm.Project(CustomStratagems.Where(s => s.Delivery.Family != DeliveryFamily.Scripted), CustomTarget.Schema.Catalogs.PodItems,
        sharedHost: w => CustomTarget.Schema.VariantDonor(w) is { IsShared: true })
        .ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n";
    public sealed record RuntimeImport(IReadOnlyList<string> Ids, IReadOnlyList<string> Notes);
    /// <summary>Imports the custom stratagems of an HD2Runtime project file (custom_stratagems.json, or a project folder holding one). Each gets an
    /// id and a call-in code of its own in this project; its icon (and a delivered weapon's icon) is read from the project's images/&lt;id&gt;.png when
    /// it is a valid icon, else a placeholder. Fields ModBuilder does not know are named in the notes and left out; a model is not imported (its
    /// colours are set again in ModBuilder).</summary>
    public async Task<RuntimeImport> ImportRuntimeProjectAsync(string path)
    {
        var full = Path.GetFullPath(path.Trim());
        var file = Directory.Exists(full) ? Path.Combine(full, "custom_stratagems.json") : full;
        if (!File.Exists(file)) throw new InvalidDataException(CoreText.Format("PackagedFiles.Source.Missing", file));
        if (new FileInfo(file).Length > CustomSchemaReader.MaxBytes) throw new InvalidDataException(CoreText.Get("CustomContent.Import.TooLarge"));
        System.Text.Json.Nodes.JsonObject project;
        try { project = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(file)) as System.Text.Json.Nodes.JsonObject ?? throw new InvalidDataException(CoreText.Get("CustomContent.Import.NotProject")); }
        catch (JsonException e) { throw new InvalidDataException(CoreText.Format("CustomContent.Import.Malformed", e.Message)); }
        if (project["format"] is not System.Text.Json.Nodes.JsonValue format || !format.TryGetValue<string>(out var f) || f != RuntimeProjectForm.Format
            || project["stratagems"] is not System.Text.Json.Nodes.JsonArray list)
            throw new InvalidDataException(CoreText.Format("CustomContent.Import.Format", RuntimeProjectForm.Format));
        var folder = Path.GetDirectoryName(file)!;
        var ids = new List<string>(); var notes = new List<string>();
        byte[]? Png(string? image)
        {
            if (image == null || !CustomIds.ValidImage(image)) return null;
            var png = Path.Combine(folder, "images", image + ".png");
            try
            {
                if (!File.Exists(png) || new FileInfo(png).Length > CustomContentShape.MaxPngBytes) return null;
                var bytes = File.ReadAllBytes(png);
                return CustomIconCompiler.IconIssue(bytes) == null ? bytes : null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
        }
        await EditCustomContentAsync(c =>
        {
            foreach (var node in list.OfType<System.Text.Json.Nodes.JsonObject>())
            {
                var ignored = new List<string>();
                var s = RuntimeProjectForm.Read(node, ignored);
                var original = s.Id;
                s.Id = CustomIds.Unique(CustomIds.Valid(s.Id) ? s.Id : "custom_stratagem", c.Stratagems.Select(x => x.Id));
                var code = UniqueCode(s.Code, c);
                if (!code.SequenceEqual(s.Code)) notes.Add(CoreText.Format("CustomContent.Import.CodeChanged", s.Id, string.Join(" ", s.Code), string.Join(" ", code)));
                s.Code = code;
                var icon = Png(s.Icon);
                if (icon == null) notes.Add(CoreText.Format("CustomContent.Import.IconPlaceholder", s.Id));
                s.Icon = NewImage(c, s.Id, icon ?? CustomIconCompiler.Placeholder(s.Id), icon == null ? null : s.Icon + ".png").Id;
                foreach (var presentation in new[] { s.Delivery.Expendable?.Presentation, s.Delivery.Variant?.Presentation })
                    if (presentation is { IconMode: DeliveredPresentation.Custom })
                    {
                        var weaponPng = Png(presentation.Icon);
                        presentation.Icon = NewImage(c, s.Id + "_weapon", weaponPng ?? CustomIconCompiler.Placeholder(s.Id + "_weapon"), weaponPng == null ? null : presentation.Icon + ".png").Id;
                    }
                if (s.Delivery.Variant is { Model: { } model })
                {
                    notes.Add(CoreText.Format("CustomContent.Import.ModelNotImported", s.Id, model));
                    s.Delivery.Variant.Model = null; s.Delivery.Variant.ModelUse = null;
                }
                foreach (var key in ignored) notes.Add(CoreText.Format("CustomContent.Import.Ignored", s.Id, key));
                if (original != s.Id) notes.Add(CoreText.Format("CustomContent.Import.IdChanged", original, s.Id));
                c.Stratagems.Add(s);
                ids.Add(s.Id);
            }
        });
        return new(ids, notes);
    }

    // ---- icons ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>Uses a PNG on the user's computer as the stratagem's icon: read now and kept in the project (refused, nothing saved, when it is
    /// not a 256 x 256 non-interlaced PNG).</summary>
    public async Task ImportCustomIconAsync(string id, string path)
    {
        var info = new FileInfo(Path.GetFullPath(path.Trim()));
        if (!info.Exists) throw new InvalidDataException(CoreText.Format("PackagedFiles.Source.Missing", info.FullName));
        if (info.Length > CustomContentShape.MaxPngBytes) throw new InvalidDataException(CoreText.Format("CustomContent.Icon.TooLarge", CustomContentShape.MaxPngBytes / 1024));
        await SetCustomIconAsync(id, await File.ReadAllBytesAsync(info.FullName), info.Name);
    }
    public Task SetCustomIconAsync(string id, byte[] png, string? sourceName) => EditCustomContentAsync(c =>
    {
        if (png.Length > CustomContentShape.MaxPngBytes) throw new InvalidDataException(CoreText.Format("CustomContent.Icon.TooLarge", CustomContentShape.MaxPngBytes / 1024));
        if (CustomIconCompiler.IconIssue(png) is { } issue) throw new InvalidDataException(issue);
        var s = c.Stratagems.Single(x => x.Id == id);
        var preparation = c.Images.FirstOrDefault(i => i.Id == s.Icon)?.Preparation ?? Models.CustomImage.Auto;
        // The stratagem's own image is replaced in place; an image another stratagem also shows is left to it.
        if (s.Icon != null && c.Images.FirstOrDefault(i => i.Id == s.Icon) is { } own && c.Stratagems.Count(x => x.Icon == own.Id) == 1)
        { own.Png = Convert.ToBase64String(png); own.Sha256 = CustomContentValidator.Sha256(png); own.SourceName = sourceName; return; }
        s.Icon = NewImage(c, s.Id, png, sourceName, preparation).Id;
    });
    public Task UsePlaceholderIconAsync(string id) => SetCustomIconAsync(id, CustomIconCompiler.Placeholder(id), null);
    public Task RemoveCustomIconAsync(string id) => EditCustomStratagemAsync(id, s => s.Icon = null);
    /// <summary>Gives a delivered weapon (an expendable clone, a weapon variant) its own icon for its marker and prompt: an image of its own
    /// (&lt;id&gt;_weapon), replaced in place on a later import.</summary>
    public Task SetCustomWeaponIconAsync(string id, byte[] png, string? sourceName) => EditCustomContentAsync(c =>
    {
        if (png.Length > CustomContentShape.MaxPngBytes) throw new InvalidDataException(CoreText.Format("CustomContent.Icon.TooLarge", CustomContentShape.MaxPngBytes / 1024));
        if (CustomIconCompiler.IconIssue(png) is { } issue) throw new InvalidDataException(issue);
        var s = c.Stratagems.Single(x => x.Id == id);
        var presentation = s.Delivery.Family == DeliveryFamily.Variant ? (s.Delivery.Variant ??= new()).Presentation ??= new() : (s.Delivery.Expendable ??= new()).Presentation ??= new();
        presentation.IconMode = DeliveredPresentation.Custom;
        if (presentation.Icon != null && c.Images.FirstOrDefault(i => i.Id == presentation.Icon) is { } own
            && Runtime030Target.UsedImageReferences(c).Count(x => x == own.Id) == 1)
        { own.Png = Convert.ToBase64String(png); own.Sha256 = CustomContentValidator.Sha256(png); own.SourceName = sourceName; return; }
        presentation.Icon = NewImage(c, s.Id + "_weapon", png, sourceName).Id;
    });
    public async Task ImportCustomWeaponIconAsync(string id, string path)
    {
        var info = new FileInfo(Path.GetFullPath(path.Trim()));
        if (!info.Exists) throw new InvalidDataException(CoreText.Format("PackagedFiles.Source.Missing", info.FullName));
        if (info.Length > CustomContentShape.MaxPngBytes) throw new InvalidDataException(CoreText.Format("CustomContent.Icon.TooLarge", CustomContentShape.MaxPngBytes / 1024));
        await SetCustomWeaponIconAsync(id, await File.ReadAllBytesAsync(info.FullName), info.Name);
    }

    // ---- models ----------------------------------------------------------------------------------------------------------------------------

    public CustomModel? FindCustomModel(string? id) => id == null ? null : Project?.CustomContent?.Models.FirstOrDefault(m => m.Id == id);
    /// <summary>Gives a weapon variant a model of its own: the variant weapon's unit with the debug palette (one colour per zone, to learn which
    /// zone is which part), ready to recolour. Returns the model id.</summary>
    public async Task<string> CreateCustomModelAsync(string stratagemId)
    {
        string id = "";
        await EditCustomContentAsync(c =>
        {
            var s = c.Stratagems.Single(x => x.Id == stratagemId);
            var variant = s.Delivery.Variant ??= new();
            if (string.IsNullOrEmpty(variant.Weapon)) throw new InvalidDataException(CoreText.Get("CustomContent.Model.NeedsWeapon"));
            id = CustomIds.Unique(stratagemId + "_model", c.Models.Select(m => m.Id));
            c.Models.Add(new CustomModel { Id = id, Base = variant.Weapon, Debug = true });
            variant.Model = id;
        });
        return id;
    }
    public Task EditCustomModelAsync(string id, Action<CustomModel> edit) => EditCustomContentAsync(c => edit(c.Models.Single(m => m.Id == id)));
    public Task RemoveCustomModelAsync(string stratagemId) => EditCustomStratagemAsync(stratagemId, s => { if (s.Delivery.Variant != null) { s.Delivery.Variant.Model = null; s.Delivery.Variant.ModelUse = null; } });
    public Task SetCustomIconPreparationAsync(string id, string preparation) => EditCustomContentAsync(c =>
    {
        if (preparation is not (Models.CustomImage.Auto or Models.CustomImage.Raw)) throw new InvalidDataException(CoreText.Format("CustomContent.Icon.Preparation", preparation));
        var s = c.Stratagems.Single(x => x.Id == id);
        if (c.Images.FirstOrDefault(i => i.Id == s.Icon) is { } image) image.Preparation = preparation;
    });
    public CustomImage? CustomImageOf(string? imageId) => imageId == null ? null : Project?.CustomContent?.Images.FirstOrDefault(i => i.Id == imageId);
    public string? CustomIconUri(string? imageId) => CustomImageOf(imageId) is { } image ? "data:image/png;base64," + image.Png : null;
    private readonly Dictionary<string, string> previews = [];
    /// <summary>The icon as the game draws it (its masks in the category colour and white), or null when it cannot be prepared.</summary>
    public string? CustomIconPreviewUri(CustomStratagem s)
    {
        if (CustomImageOf(s.Icon) is not { } image) return null;
        var colour = CategoryColour(s);
        var key = image.Sha256 + image.Preparation + colour;
        if (previews.TryGetValue(key, out var uri)) return uri;
        try { uri = "data:image/png;base64," + Convert.ToBase64String(CustomIconCompiler.Preview(Convert.FromBase64String(image.Png), image.Preparation, colour)); }
        catch (Exception e) when (e is InvalidDataException or FormatException) { return null; }
        if (previews.Count > 64) previews.Clear();
        return previews[key] = uri;
    }
    // The stratagem category colours ModBuilder's UI uses (app.css --cat-*): support blue, defensive green, offensive red.
    private static (byte, byte, byte) CategoryColour(CustomStratagem s) => s.Delivery.Family switch
    {
        DeliveryFamily.Support or DeliveryFamily.Pod or DeliveryFamily.Expendable or DeliveryFamily.Variant or DeliveryFamily.Silo => (121, 174, 252),
        DeliveryFamily.Sentry => (98, 213, 150),
        DeliveryFamily.Scripted when s.Carrier.Beacon == CustomCarrier.Support || s.Delivery.Scripted?.VanillaPod != null => (121, 174, 252),
        _ => (255, 128, 120),
    };
    private static Models.CustomImage NewImage(CustomContentSection c, string id, byte[] png, string? sourceName, string preparation = Models.CustomImage.Auto)
    {
        var image = new Models.CustomImage { Id = CustomIds.Unique(id, c.Images.Select(i => i.Id)), Png = Convert.ToBase64String(png), Sha256 = CustomContentValidator.Sha256(png),
            Preparation = preparation, SourceName = sourceName };
        c.Images.Add(image);
        return image;
    }

    // ---- projectiles ------------------------------------------------------------------------------------------------------------------------

    public async Task<string> CreateCustomProjectileAsync(string name, string? baseOutput = null)
    {
        string id = "";
        await EditCustomContentAsync(c =>
        {
            id = CustomIds.Suggest(name, c.Projectiles.Select(p => p.Id), "projectile") ?? CustomIds.Unique("custom_projectile", c.Projectiles.Select(p => p.Id));
            c.Projectiles.Add(new CustomProjectile { Id = id, Name = name.Trim(), Base = baseOutput ?? "" });
        });
        return id;
    }
    /// <summary>Every vanilla projectile the bound SDK maps, as a starting point for a custom projectile that is an exact copy of it, with the
    /// target's reason where Runtime cannot use it as a base (null: it can). Player, support and mounted weapons first, then the rest, each
    /// by name. Spare twins (borrowed rows) are not vanilla projectiles and are not listed. Neither are 0.30's projectile donors (an Eagle
    /// payload, an orbital shell, a sentry round): Runtime's base resolver (custom_projectiles.lua projectile_output) needs the row's
    /// dependencyKey, which no donor row has, so it would refuse each as "the package … is not known".</summary>
    public IReadOnlyList<(AttackOutput Output, string? Issue)> CustomProjectileCloneSources() => Metadata?.AttackOutputs is { } catalog
        ? [.. catalog.Outputs.Where(o => o.Family == "projectile" && o.SpareTwin == null && o.Owner.Kind != "spare_row" && !o.IsDonor)
            .OrderBy(o => o.Owner.Kind switch { AttackProjectileSource.PlayerKind => 0, AttackProjectileSource.SupportKind => 1, AttackProjectileSource.VehicleKind => 2, _ => 3 })
            .ThenBy(o => o.Label, StringComparer.OrdinalIgnoreCase).ThenBy(o => o.SemanticId, StringComparer.Ordinal)
            .Select(o => (o, CustomTarget.ProjectileOutputIssue(o.SemanticId, true, Metadata)))]
        : [];
    /// <summary>Creates a custom projectile that starts as an exact copy of a vanilla projectile: that projectile is its base and no component
    /// is taken from another one (Runtime defines it as a clone of the base row). Refused with the target's reason where Runtime cannot use
    /// the projectile as a base.</summary>
    public Task<string> CloneVanillaProjectileAsync(string output, string name) =>
        CustomTarget.ProjectileOutputIssue(output, true, Metadata) is { } issue ? throw new InvalidDataException(issue) : CreateCustomProjectileAsync(name, output);
    public async Task<string> CreateCustomProjectileFromTemplateAsync(string key)
    {
        var template = CustomTemplates.Projectiles.Single(t => t.Key == key);
        string id = "";
        await EditCustomContentAsync(c =>
        {
            var p = template.Create();
            p.Id = id = CustomIds.Unique(p.Id, c.Projectiles.Select(x => x.Id));
            c.Projectiles.Add(p);
        });
        return id;
    }
    public async Task<string> DuplicateCustomProjectileAsync(string id)
    {
        string copy = "";
        await EditCustomContentAsync(c =>
        {
            var source = c.Projectiles.Single(p => p.Id == id);
            var p = Clone(source);
            p.Id = copy = CustomIds.Unique(source.Id + "_copy", c.Projectiles.Select(x => x.Id));
            p.Name = Truncate(source.Name + " Copy", 64);
            c.Projectiles.Insert(c.Projectiles.IndexOf(source) + 1, p);
        });
        return copy;
    }
    public Task EditCustomProjectileAsync(string id, Action<CustomProjectile> edit) => EditCustomContentAsync(c => edit(c.Projectiles.Single(p => p.Id == id)));
    /// <summary>Renames a custom projectile and returns its id. The id stays (Change ID moves every reference), except for the draft of a
    /// weapon's projectile selector: while nothing refers to it and its id was not set explicitly, its id follows its name.</summary>
    public async Task<string> RenameCustomProjectileAsync(string id, string name)
    {
        var newId = id;
        var draft = CustomDraft;
        await EditCustomContentAsync(c =>
        {
            var p = c.Projectiles.Single(x => x.Id == id);
            p.Name = name.Trim();
            if (draft is { IdFollowsName: true } && draft.ProjectileId == id && CustomContentGraph.Build(c).UsedBy(CustomNodeKind.Projectile, id).Count == 0
                && CustomIds.Suggest(p.Name, c.Projectiles.Where(x => x != p).Select(x => x.Id), "projectile") is { } suggested)
                p.Id = newId = suggested;
        });
        if (newId != id && customDraft is { } d) customDraft = d with { Draft = d.Draft with { ProjectileId = newId } };
        return newId;
    }
    /// <summary>Changes a custom projectile's id and every reference to it in the same save (weapon assignments, delivered weapon rounds).</summary>
    public async Task ChangeCustomProjectileIdAsync(string id, string newId)
    {
        newId = newId.Trim();
        await EditCustomContentAsync(c =>
        {
            if (!CustomIds.Valid(newId)) throw new InvalidDataException(CoreText.Format("CustomContent.Check.IdInvalid", CustomIds.MaxLength));
            if (newId == id) return;
            if (c.Projectiles.Any(p => p.Id == newId)) throw new InvalidDataException(CoreText.Format("CustomContent.Check.ProjectileIdDuplicate", newId));
            c.Projectiles.Single(p => p.Id == id).Id = newId;
            CustomContentGraph.Replace(c, id, ContentRef.Custom(newId));
        });
        // A draft's id set explicitly no longer follows its name.
        if (customDraft is { } d && d.Draft.ProjectileId == id) customDraft = d with { Draft = d.Draft with { ProjectileId = newId, IdFollowsName = false } };
    }
    /// <summary>Deletes a custom projectile. While something uses it, the deletion is refused (CustomReferenceException with the uses) unless
    /// the uses are moved to a replacement (another custom projectile, or for a delivered weapon a vanilla round) or removed with it.
    /// Deleting the draft of a weapon's projectile selector ends that flow (the weapon keeps what it fires).</summary>
    public async Task DeleteCustomProjectileAsync(string id, ContentRef? replacement = null, bool removeReferences = false)
    {
        await EditCustomContentAsync(c =>
        {
            var uses = CustomContentGraph.Build(c).UsedBy(CustomNodeKind.Projectile, id);
            if (uses.Count > 0 && replacement == null && !removeReferences) throw new CustomReferenceException(id, uses);
            if (replacement is { IsCustom: true } && (replacement.Id == id || c.Projectiles.All(p => p.Id != replacement.Id)))
                throw new InvalidDataException(CoreText.Format("CustomContent.Check.ProjectileMissing", replacement.Id));
            if (uses.Count > 0) CustomContentGraph.Replace(c, id, replacement);
            c.Projectiles.RemoveAll(p => p.Id == id);
        });
        if (customDraft?.Draft.ProjectileId == id) customDraft = null;
    }

    // ---- creating a custom projectile from a weapon's projectile selector --------------------------------------------------------------------

    private (ModProject Project, CustomProjectileDraft Draft)? customDraft;
    /// <summary>The custom projectile being created from a weapon's projectile selector, if any (null once the project changes or the
    /// projectile is gone).</summary>
    public CustomProjectileDraft? CustomDraft => customDraft is { } d && ReferenceEquals(d.Project, Project) && FindCustomProjectile(d.Draft.ProjectileId) != null ? d.Draft : null;
    /// <summary>Starts creating a custom projectile for a weapon attack: a new one starting from what the attack fires now (when that can be
    /// a custom projectile's base), or a copy of copyOf. It is saved in the project at once; the attack keeps what it fires until
    /// CompleteCustomProjectileDraftAsync assigns it. A host that cannot fire custom projectiles is refused with the target's reason.</summary>
    public async Task<string> BeginCustomProjectileDraftAsync(CustomProjectileHost origin, string name, string? copyOf = null)
    {
        if (CustomProjectileHostSupport(origin) is { Supported: false } support) throw new InvalidDataException(support.Reason);
        var fired = FiredRow(origin.Kind, origin.Weapon, origin.Role)?.SemanticId;
        var start = fired != null && CustomTarget.ProjectileOutputIssue(fired, true, Metadata) == null ? fired : "";
        string id = "";
        await EditCustomContentAsync(c =>
        {
            var taken = c.Projectiles.Select(p => p.Id);
            if (copyOf != null)
            {
                var source = c.Projectiles.SingleOrDefault(p => p.Id == copyOf) ?? throw new InvalidDataException(CoreText.Format("CustomContent.Check.ProjectileMissing", copyOf));
                var copy = Clone(source);
                copy.Id = id = CustomIds.Unique(source.Id + "_copy", taken);
                copy.Name = Truncate(name.Trim(), 64);
                c.Projectiles.Insert(c.Projectiles.IndexOf(source) + 1, copy);
            }
            else
            {
                id = CustomIds.Suggest(name, taken, "projectile") ?? CustomIds.Unique("custom_projectile", taken);
                c.Projectiles.Add(new CustomProjectile { Id = id, Name = Truncate(name.Trim(), 64), Base = start });
            }
        });
        customDraft = (Project!, new CustomProjectileDraft(id, origin, copyOf));
        return id;
    }
    /// <summary>Completes the draft: once the custom projectile has no error, it is assigned to the attack it was created for (conflicts are
    /// asked as for any assignment: CustomAssignmentConflictException, then discardConflicts). Returns where it was created.</summary>
    public async Task<CustomProjectileHost> CompleteCustomProjectileDraftAsync(bool discardConflicts = false)
    {
        var draft = CustomDraft ?? throw new InvalidOperationException(CoreText.Get("CustomProjectiles.Draft.None"));
        if (CustomDiagnosticsFor(CustomDiagnostic.ProjectileOwner(draft.ProjectileId)).FirstOrDefault(d => d.Severity == CustomSeverity.Error) is { } error)
            throw new InvalidDataException(CoreText.Format("CustomProjectiles.Draft.NotReady", error.Message));
        await AssignCustomProjectileAsync(draft.Origin, draft.ProjectileId, discardConflicts);
        customDraft = null;
        return draft.Origin;
    }
    /// <summary>Cancels the draft: the attack keeps what it fires. The draft projectile is deleted unless something uses it by now (assigned
    /// elsewhere from its editor), in which case it stays as an ordinary custom projectile. Returns where it was created (null: no draft).</summary>
    public async Task<CustomProjectileHost?> CancelCustomProjectileDraftAsync()
    {
        if (CustomDraft is not { } draft) { customDraft = null; return null; }
        if (CustomGraph().UsedBy(CustomNodeKind.Projectile, draft.ProjectileId).Count == 0) await DeleteCustomProjectileAsync(draft.ProjectileId);
        customDraft = null;
        return draft.Origin;
    }

    // ---- weapon assignments -----------------------------------------------------------------------------------------------------------------

    /// <summary>Whether a projectile host can fire a custom projectile with the current target (and why not).</summary>
    public CustomFieldInfo CustomProjectileHostSupport(CustomProjectileHost host) => Project is { } project
        ? CustomTarget.DescribeProjectileHost(host, project, Metadata) : new("projectileHost", CustomFieldStatus.Unavailable, CoreText.Get("Messages.Workspace.ProjectChanged"));
    public CustomProjectileAssignment? CustomAssignment(CustomProjectileHost host) => Project?.CustomContent?.Assignments.FirstOrDefault(host.Matches);
    public CustomProjectileAssignment? CustomAssignment(string weapon, string role) => CustomAssignment(CustomProjectileHost.Player(weapon, role));
    public Task AssignCustomProjectileAsync(string weapon, string role, string? projectileId, bool discardConflicts = false) =>
        AssignCustomProjectileAsync(CustomProjectileHost.Player(weapon, role), projectileId, discardConflicts);
    /// <summary>Makes a weapon attack fire a project custom projectile (null removes it). A host the target cannot bind is refused with its
    /// reason. A vanilla projectile swap and projectile or explosion edits on the attack's own projectile cannot apply together with it, and
    /// the target binds only so many attacks at once: unless discardConflicts, a CustomAssignmentConflictException asks first; with it, the
    /// swap and edits, and the custom projectile of the attacks over the limit, are removed in the same save.</summary>
    public async Task AssignCustomProjectileAsync(CustomProjectileHost host, string? projectileId, bool discardConflicts = false)
    {
        if (projectileId != null && CustomProjectileHostSupport(host) is { Supported: false } support) throw new InvalidDataException(support.Reason);
        var (weapon, role) = (host.Weapon, host.Role);
        var project = Project; await weaponEditGate.WaitAsync();
        try
        {
            if (project == null || !ReferenceEquals(project, Project)) throw new InvalidOperationException(CoreText.Get("Messages.Workspace.ProjectChanged"));
            var previous = (project.AttackOutputChanges?.ToList(), project.ProjectileChanges.ToList(), project.CompositionChanges.ToList(), project.CustomContent, project.FormatVersion);
            var section = project.CustomContent == null ? new CustomContentSection() : Clone(project.CustomContent);
            try
            {
                section.Assignments.RemoveAll(host.Matches);
                if (projectileId != null)
                {
                    if (section.Projectiles.All(p => p.Id != projectileId)) throw new InvalidDataException(CoreText.Format("CustomContent.Check.ProjectileMissing", projectileId));
                    var swaps = (project.AttackOutputChanges ?? []).Count(c => c.IsPlayer && c.Weapon == weapon && c.AttackRole == role) + project.ProjectileChanges.Count(c => c.Weapon == weapon && c.AttackRole == role);
                    var edits = project.CompositionChanges.Count(c => c.Weapon == weapon && c.AttackRole == role);
                    var active = section.Assignments.Where(a => a.Enabled).ToList();
                    var displaced = CustomTarget.ProjectileBindingLimit is { } limit && active.Count >= limit ? active.Take(active.Count - limit + 1).ToList() : [];
                    if (swaps + edits + displaced.Count > 0 && !discardConflicts) throw new CustomAssignmentConflictException(host.Label, swaps, edits, displaced);
                    project.AttackOutputChanges?.RemoveAll(c => c.IsPlayer && c.Weapon == weapon && c.AttackRole == role);
                    if (project.AttackOutputChanges is { Count: 0 }) project.AttackOutputChanges = null;
                    project.ProjectileChanges.RemoveAll(c => c.Weapon == weapon && c.AttackRole == role);
                    project.CompositionChanges.RemoveAll(c => c.Weapon == weapon && c.AttackRole == role);
                    section.Assignments.RemoveAll(displaced.Contains);
                    section.Assignments.Add(new() { HostKind = host.Kind, Weapon = weapon, AttackRole = role, Projectile = ContentRef.Custom(projectileId) });
                }
                project.CustomContent = Normalized(section, project);
                await SaveChangesAsync();
            }
            catch { (project.AttackOutputChanges, project.ProjectileChanges, project.CompositionChanges, project.CustomContent, project.FormatVersion) = previous; throw; }
        }
        finally { weaponEditGate.Release(); }
    }
    public Task ToggleCustomAssignmentAsync(Guid id) => EditCustomContentAsync(c => { var a = c.Assignments.Single(x => x.Id == id); a.Enabled = !a.Enabled; });
    public Task RemoveCustomAssignmentAsync(Guid id) => EditCustomContentAsync(c => c.Assignments.RemoveAll(a => a.Id == id));
    // A vanilla projectile chosen for an attack replaces its custom projectile (the choice is made where the custom one is shown).
    private static void ReplaceCustomAssignment(ModProject project, string weapon, string role)
    {
        if (project.CustomContent is not { } custom || !custom.Assignments.Any(a => a.HostKind == AttackOutputChange.PlayerHost && a.Weapon == weapon && a.AttackRole == role)) return;
        // A copy, so the caller's rollback (which restores the previous section object) restores the assignment too.
        var section = Clone(custom);
        section.Assignments.RemoveAll(a => a.HostKind == AttackOutputChange.PlayerHost && a.Weapon == weapon && a.AttackRole == role);
        project.CustomContent = Normalized(section, project);
    }

    // ---- editing --------------------------------------------------------------------------------------------------------------------------

    /// <summary>Applies an edit to a copy of the custom content section and saves it, or leaves the project exactly as it was.</summary>
    public async Task EditCustomContentAsync(Action<CustomContentSection> edit)
    {
        var project = Project; await weaponEditGate.WaitAsync();
        try
        {
            if (project == null || !ReferenceEquals(project, Project)) throw new InvalidOperationException(CoreText.Get("Messages.Workspace.ProjectChanged"));
            var previous = project.CustomContent; var format = project.FormatVersion;
            var section = previous == null ? new CustomContentSection() : Clone(previous);
            try
            {
                edit(section);
                project.CustomContent = Normalized(section, project);
                await SaveChangesAsync();
            }
            catch { project.CustomContent = previous; project.FormatVersion = format; throw; }
        }
        finally { weaponEditGate.Release(); }
    }
    // Images and models no custom stratagem shows are dropped (an image a stratagem icon edit shows is kept); an empty section is omitted
    // again (the format stays 14 once custom content existed).
    private static CustomContentSection? Normalized(CustomContentSection section, ModProject? project)
    {
        var used = Runtime030Target.UsedImageReferences(section).Concat(Generation.StratagemPresentation.ImageReferences(project)).ToHashSet(StringComparer.Ordinal);
        section.Images.RemoveAll(i => !used.Contains(i.Id));
        var models = section.Stratagems.Select(s => s.Delivery.Variant?.Model).OfType<string>().ToHashSet(StringComparer.Ordinal);
        section.Models.RemoveAll(m => !models.Contains(m.Id));
        foreach (var s in section.Stratagems)
        {
            if (s.Callbacks is { IsEmpty: true }) s.Callbacks = null;
            if (s.NameCased is { Length: 0 }) s.NameCased = null;
        }
        return section.IsEmpty && section.Images.Count == 0 ? null : section;
    }
    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JsonStorage.Options), JsonStorage.Options)!;
    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];
}

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Projects;
using HD2RuntimeGUI.Core.Storage;

namespace HD2RuntimeGUI.Core.Generation;

public interface IModExporter { Task<string> ExportAsync(ModProject project, SdkMetadata sdk); }
public sealed class ModExporter(ILuaGenerator generator) : IModExporter
{
    public async Task<string> ExportAsync(ModProject project, SdkMetadata sdk)
    {
        var lua = generator.Generate(project, sdk);
        // Experimental custom content (format 14): compiled again with its icons; null for every project without custom content, which
        // exports exactly as before.
        var custom = CustomContent.CustomContentBuild.Compile(project, sdk, images: true);
        // 0.30.0: images stratagem icon edits write (presentation_icon = hd2.resources.image), compiled as custom content's are.
        var stratagemImages = StratagemPresentation.Compile(project, custom?.Images);
        if (custom == null && project.PlayerArmor == null && LuaGenerator.CustomSource(project) == null && !(project.AttackOutputChanges ?? []).Any(c => c.Enabled) && !(project.OutputRowChanges ?? []).Any(c => c.Enabled) && !project.EntityChanges.Any(c => c.Enabled) && !project.StratagemChanges.Any(c => c.Enabled) && !project.SupportChanges.Any(c => c.Enabled) && !project.Changes.Any(c => c.Enabled) && !project.WeaponChanges.Any(c => c.Enabled) && !project.ProjectileChanges.Any(c => c.Enabled) && !project.CompositionChanges.Any(c => c.Enabled)) throw new InvalidDataException(CoreText.Get("Messages.Build.Export.NothingEnabled"));
        // The HD2Runtime the mod needs: the bound SDK's, the custom content target's (0.30.0) when the project has custom content, or the
        // Runtime that first carries a field the project writes (RuntimeRequirement), whichever is newest.
        var minimum = RuntimeRequirement.Minimum(project, sdk);
        var requires = new { bingus = new { min_release = 15, api = 1 }, hd2runtime = new { module = ProjectIdentity.RuntimeModule, min_version = minimum, api = sdk.ApiVersion } };
        var description = $"Requires Bingus Shared Loader v15+ / API 1 and HD2Runtime {minimum}+ / API {sdk.ApiVersion}; install dependencies separately.";
        // In-game options: Mod Options Menu is optional (never in requires), declared only when at least one active edit is bound.
        var usesOptions = ModOptionsService.ActiveRows(project, sdk).Count > 0;
        if (usesOptions) description += " " + ModOptionsService.DependencyNote(sdk, project.ModOptions!);
        var optional = new { mod_options_menu = ModOptionsService.OptionalDependency };
        byte[] Json(object value) => JsonSerializer.SerializeToUtf8Bytes(value, new JsonSerializerOptions { WriteIndented = true });
        byte[] Text(string value) => Encoding.UTF8.GetBytes(value);
        // HD2Arsenal presentation: the manifest description starts with the user's text; IconPath names the packaged icon.
        var manifestDescription = Arsenal.ManifestDescription(project, description);
        var options = new[] { new { Name = project.DisplayName, Description = description, Include = new[] { "mod" } } };
        var entries = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["manifest.json"] = Arsenal.IconEntry(project) is { } icon
                ? Json(new { Version = 1, Guid = project.ManagerGuid, Name = project.DisplayName + " " + project.Version, Description = manifestDescription, IconPath = icon, Options = options })
                : Json(new { Version = 1, Guid = project.ManagerGuid, Name = project.DisplayName + " " + project.Version, Description = manifestDescription, Options = options }),
            ["hd2runtime.json"] = usesOptions
                ? Json(new { format = 1, name = project.DisplayName, author = project.Author, version = project.Version, resource = project.ResourceId, guid = project.ManagerGuid, requires, optional })
                : Json(new { format = 1, name = project.DisplayName, author = project.Author, version = project.Version, resource = project.ResourceId, guid = project.ManagerGuid, requires }),
            ["build-report.json"] = usesOptions
                ? Json(new { resource = project.ResourceId, sdk_version = sdk.Version, runtime_bundled = false, sdk_stubs_bundled = false, requires, optional, builder = $"{BuildInfo.ProductName} {BuildInfo.Version} / .NET 10", deployed = false, game_launched = false })
                : Json(new { resource = project.ResourceId, sdk_version = sdk.Version, runtime_bundled = false, sdk_stubs_bundled = false, requires, builder = $"{BuildInfo.ProductName} {BuildInfo.Version} / .NET 10", deployed = false, game_launched = false }),
            ["README.md"] = Text($"# {project.DisplayName}\n\n{project.Description}\n\nBy {project.Author}.\n\n{description}\n\n"
                + (custom == null ? "" : $"This mod uses HD2Runtime's experimental custom content (custom stratagems and projectiles), which needs HD2Runtime {minimum} or newer.\n\n")
                + "Install this gameplay ZIP through your mod manager after installing both dependencies.\n"),
            ["src/addon.lua"] = Text(lua),
            [$"mod/{GameplayArchive.ArchiveName}"] = custom == null && stratagemImages.Count == 0 ? GameplayArchive.Build(project.ResourceId, Text(WrapFor(project, lua, minimum))) : [],
            [$"mod/{GameplayArchive.ArchiveName}.stream"] = [],
            [$"mod/{GameplayArchive.ArchiveName}.gpu_resources"] = []
        };
        if (custom != null) AddCustomContent(project, custom with { Images = [.. custom.Images, .. stratagemImages] }, minimum, Text(Wrap(project, lua, minimum)), entries, GameData);
        else if (stratagemImages.Count > 0) AddStratagemImages(project, stratagemImages, Text(WrapFor(project, lua, minimum)), entries);
        // The Arsenal icon, then the user's additional files, exactly as they are on disk; refused rather than skipped or overwriting.
        Arsenal.AddTo(project, entries);
        PackagedFiles.AddTo(project, entries);
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            foreach (var (name, bytes) in entries)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
                entry.ExternalAttributes = 0;
                using var stream = entry.Open(); stream.Write(bytes);
            }
        var safeName = Regex.Replace(project.DisplayName, "[^A-Za-z0-9._-]+", "-").Trim('-', '.');
        if (safeName.Length == 0) safeName = "HD2Mod";
        // The version suffix also avoids bare Windows DOS device basenames.
        var path = Path.Combine(project.ExportDirectory, safeName + "-" + project.Version + ".zip");
        var package = output.ToArray();
        ModPackageValidator.Validate(package, entries);
        await JsonStorage.WriteAtomicBytesAsync(path, package); return path;
    }
    /// <summary>The installed game's data folder custom models are derived from (HD2_GAME_DATA, as the SDK build reads it, else the Steam
    /// install ModBuilder finds); null when there is none. Only exports with a custom model read it.</summary>
    public Func<string?> GameData { get; init; } = () => Environment.GetEnvironmentVariable("HD2_GAME_DATA") is { Length: > 0 } data && Directory.Exists(data)
        ? data : GameAssets.GameIconStore.DefaultGameDataPath();

    // Custom content: the archive (the addon, and with icons each icon's texture and GUI material plus the image record, in the layout of the
    // game's own archives), each icon's editable source PNG beside the manifest (outside mod/: the game never reads it), a weapon variant's
    // model (derived from the user's installed game into a patch of its base weapon's own package archive, with its build record in the mod's
    // archive, as the SDK build does: docs/custom-models.md), and the build report's custom_content record. No vanilla resource is ever
    // replaced: every resource is named under the mod's own resource id.
    private static void AddCustomContent(ModProject project, CustomContent.CompiledCustomContent custom, string minimum, byte[] addon, IDictionary<string, byte[]> entries,
        Func<string?> gameData)
    {
        var models = custom.Models is { Count: > 0 } used ? DeriveModels(project, used, gameData, entries) : new SortedDictionary<string, CustomContent.ModelRecord>(StringComparer.Ordinal);
        if (custom.Images.Count == 0 && models.Count == 0) entries[$"mod/{GameplayArchive.ArchiveName}"] = GameplayArchive.Build(project.ResourceId, addon);
        else PackArchive(project, addon, custom.Images, models, entries);
        var report = System.Text.Json.Nodes.JsonNode.Parse(entries["build-report.json"])!.AsObject();
        report["custom_content"] = new System.Text.Json.Nodes.JsonObject
        {
            ["experimental"] = true, ["target"] = custom.TargetId, ["requires_hd2runtime"] = minimum, ["schema_sha256"] = custom.SchemaSha256,
            ["model_version"] = project.CustomContent!.Version,
            ["stratagems"] = new System.Text.Json.Nodes.JsonArray([.. custom.StratagemIds.Select(i => (System.Text.Json.Nodes.JsonNode?)i)]),
            ["projectiles"] = new System.Text.Json.Nodes.JsonArray([.. custom.ProjectileIds.Select(i => (System.Text.Json.Nodes.JsonNode?)i)]),
            ["image_sources"] = ImageSources(custom.Images),
        };
        if (models.Count > 0)
            report["custom_content"]!["models"] = new System.Text.Json.Nodes.JsonArray([.. models.Select(m => (System.Text.Json.Nodes.JsonNode?)new System.Text.Json.Nodes.JsonObject
            {
                ["model"] = m.Key, ["base"] = m.Value.Base, ["archive"] = m.Value.Archive, ["game_build"] = m.Value.Build, ["unit"] = m.Value.Unit,
                ["sha256"] = new System.Text.Json.Nodes.JsonObject { ["unit"] = m.Value.Sha256.Unit, ["material"] = m.Value.Sha256.Material, ["lut"] = m.Value.Sha256.Lut },
            })]);
        entries["build-report.json"] = JsonSerializer.SerializeToUtf8Bytes(report, new JsonSerializerOptions { WriteIndented = true });
    }
    // 0.30.0 stratagem icon edits (presentation_icon = hd2.resources.image) in a project without custom content: their images packed exactly as
    // custom content's are, and listed in the build report as the SDK build lists them.
    private static void AddStratagemImages(ModProject project, IReadOnlyList<CustomContent.CompiledImage> images, byte[] addon, IDictionary<string, byte[]> entries)
    {
        PackArchive(project, addon, images, new SortedDictionary<string, CustomContent.ModelRecord>(StringComparer.Ordinal), entries);
        var report = System.Text.Json.Nodes.JsonNode.Parse(entries["build-report.json"])!.AsObject();
        report["image_sources"] = ImageSources(images);
        entries["build-report.json"] = JsonSerializer.SerializeToUtf8Bytes(report, new JsonSerializerOptions { WriteIndented = true });
    }
    // The mod's archive with resources beside the addon (each icon's texture and GUI material plus the image record, a model's build record),
    // in the layout of the game's own archives, and each icon's editable source PNG beside the manifest (outside mod/: the game never reads it).
    private static void PackArchive(ModProject project, byte[] addon, IReadOnlyList<CustomContent.CompiledImage> images, SortedDictionary<string, CustomContent.ModelRecord> models,
        IDictionary<string, byte[]> entries)
    {
        var resources = new Dictionary<(ulong Type, ulong Name), (byte[] Main, byte[] Gpu)>
        {
            [(ResourceArchive.LuaType, GameplayArchive.ResourceHash(project.ResourceId))] = (ResourceArchive.LuaResource(addon), []),
        };
        if (images.Count > 0)
            resources[(ResourceArchive.LuaType, GameplayArchive.ResourceHash(project.ResourceId + "/" + ImageRecord))] = (ResourceArchive.LuaResource(Encoding.UTF8.GetBytes(ImageRecordLua(images))), []);
        if (models.Count > 0)
            resources[(ResourceArchive.LuaType, GameplayArchive.ResourceHash(project.ResourceId + "/" + CustomContent.CustomModelCompiler.Record))] =
                (ResourceArchive.LuaResource(Encoding.UTF8.GetBytes(CustomContent.CustomModelCompiler.RecordLua(models))), []);
        foreach (var image in images)
        {
            if (!resources.TryAdd((CustomContent.CustomIconCompiler.TextureType, image.NameHash), (image.TextureMain, image.TextureGpu)))
                throw new InvalidDataException(CoreText.Format("CustomContent.Build.ImageHash", image.Id));
            resources[(CustomContent.CustomIconCompiler.MaterialType, image.NameHash)] = (image.Material, []);
        }
        var (archive, gpu) = ResourceArchive.Build(resources);
        entries[$"mod/{GameplayArchive.ArchiveName}"] = archive;
        entries[$"mod/{GameplayArchive.ArchiveName}.gpu_resources"] = gpu;
        foreach (var image in images) entries.Add("images/" + image.Id + ".png", image.Png);
    }
    private static System.Text.Json.Nodes.JsonArray ImageSources(IEnumerable<CustomContent.CompiledImage> images) =>
        new([.. images.Select(i => (System.Text.Json.Nodes.JsonNode?)new System.Text.Json.Nodes.JsonObject
        {
            ["image"] = i.Id, ["file"] = "images/" + i.Id + ".png", ["source_sha256"] = i.Sha256, ["prepared"] = i.Prepared, ["resource"] = i.ResourceName,
        })]);
    // Each model's three resources, derived from the installed game's base parts (which must be the reviewed bytes), packed per base package
    // into mod/<base archive>.patch_0 (+ .stream, .gpu_resources) as the SDK build packs them; their build records for the mod's archive.
    private static SortedDictionary<string, CustomContent.ModelRecord> DeriveModels(ModProject project, IReadOnlyList<CustomModel> models, Func<string?> gameData,
        IDictionary<string, byte[]> entries)
    {
        var bases = CustomContent.ModelBases.Read(CustomContent.CustomModelCompiler.BundledBases());
        var data = gameData() ?? throw new InvalidDataException(CoreText.Get("CustomContent.Build.ModelNoGame"));
        var records = new SortedDictionary<string, CustomContent.ModelRecord>(StringComparer.Ordinal);
        var patches = new SortedDictionary<string, Dictionary<(ulong Type, ulong Name), (byte[] Main, byte[] Gpu)>>(StringComparer.Ordinal);
        var parts = new Dictionary<string, IReadOnlyDictionary<(ulong, ulong), (byte[], byte[])>>(StringComparer.Ordinal);
        foreach (var model in models)
        {
            if (!bases.TryGetValue(model.Base, out var facts)) throw new InvalidDataException(CoreText.Format("CustomContent.Build.ModelBase", model.Id, model.Base));
            if (!parts.TryGetValue(model.Base, out var baseParts))
            {
                try { baseParts = parts[model.Base] = CustomContent.CustomModelCompiler.GameParts(data, CustomContent.CustomModelCompiler.BaseKeys(facts)); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new InvalidDataException(CoreText.Format("CustomContent.Build.ModelGame", model.Id, data, e.Message), e); }
            }
            var derived = CustomContent.CustomModelCompiler.Derive(project.ResourceId, model.Id, model.Base, Palette(model), facts, baseParts);
            var archive = CustomContent.CustomModelCompiler.PatchArchiveName(facts);
            if (!patches.TryGetValue(archive, out var patch)) patches[archive] = patch = [];
            foreach (var (key, value) in derived.Resources) patch[key] = value;
            records[model.Id] = derived.Record;
        }
        foreach (var (archive, resources) in patches)
        {
            var (bytes, gpu) = ResourceArchive.Build(resources);
            entries["mod/" + archive] = bytes; entries["mod/" + archive + ".stream"] = []; entries["mod/" + archive + ".gpu_resources"] = gpu;
        }
        return records;
    }
    /// <summary>A model's palette as the SDK's model files hold it: "debug", or the colour for every zone and per-zone colours.</summary>
    public static CustomContent.ModelPalette Palette(CustomModel model) => model.Debug ? CustomContent.ModelPalette.DebugPalette
        : new(false, model.All, model.Zones.ToDictionary(z => z.Key, z => z.Value, StringComparer.Ordinal));

    // The Lua resource <mod resource>/hd2runtime_images, as the SDK build writes it: each icon's source PNG, its SHA-256, how it was prepared
    // and built, and its resource name. The Runtime logs it once per icon when the mod asks for it (runtime/image_resources).
    public const string ImageRecord = "hd2runtime_images";
    public static string ImageRecordLua(IEnumerable<CustomContent.CompiledImage> images) =>
        "-- Generated by " + BuildInfo.ProductName + " (as the HD2Runtime SDK build does, docs/custom-images.md): the icons this mod compiled from its PNGs.\n"
        + "return {format=1,images={\n"
        + string.Concat(images.Select(i => "[" + CustomContent.LuaTable.Quote(i.Id) + "]={source=" + CustomContent.LuaTable.Quote("images/" + i.Id + ".png") + ",sha256="
            + CustomContent.LuaTable.Quote(i.Sha256) + ",prepared=" + CustomContent.LuaTable.Quote(i.Prepared) + ",build=\"compiled\",resource=" + CustomContent.LuaTable.Quote(i.ResourceName) + "},\n"))
        + "}}\n";
    // A project with custom content can need a newer HD2Runtime than its SDK (0.30.0): its check compares SemVer like the SDK's own wrapper
    // (a prerelease is older than its release) and, when the installed Runtime is too old, tells it (hd2.compatibility, 0.28.0+) before it
    // fails closed. The SDK the mod was built with is still the bound one: Runtime reads that declaration from the wrapper's locals to apply
    // its legacy acknowledgements (core/sdk_compatibility.lua: 'minimum' with 'at_least' is the SDK build's declaration, x,y,z from 'version'
    // ModBuilder's), so the requirement is named required_runtime and the bound SDK is declared as x,y,z. Projects without custom content keep
    // the wrapper below unchanged.
    public static string Wrap(ModProject p, string source, string minimum) => $$"""
        -- HD2-Addon: {{p.ResourceId}}
        local loader=rawget(_G,'CowboyBingusModLoader')
        assert(loader and loader.api==1 and type(loader.version)=='number' and loader.version>=16,
            'Requires Bingus Shared Loader v15+ / API 1')
        local runtime=require('mods/skyeshade/hd2runtime')
        local function semver(v)
            local core,pre=tostring(v):match('^([^%-+]+)%-?([^+]*)')
            local a,b,c=(core or''):match('^(%d+)%.(%d+)%.(%d+)$')
            assert(a,'Invalid HD2Runtime version: '..tostring(v))
            local ids={}
            for id in(pre~=''and pre..'.'or''):gmatch('([^%.]*)%.')do ids[#ids+1]=id end
            return {tonumber(a),tonumber(b),tonumber(c),ids}
        end
        local function order(p,q)
            local x,y=tonumber(p:match('^%d+$')),tonumber(q:match('^%d+$'))
            if x and y then return x<y and -1 or x>y and 1 or 0 end
            if x then return -1 end
            if y then return 1 end
            return p<q and -1 or p>q and 1 or 0
        end
        local function at_least(have,need)
            local x,y=semver(have),semver(need)
            for i=1,3 do if x[i]~=y[i]then return x[i]>y[i]end end
            local p,q=x[4],y[4]
            if#p==0 then return true end
            if#q==0 then return false end
            for i=1,math.max(#p,#q)do
                if p[i]==nil then return false end
                if q[i]==nil then return true end
                local o=order(p[i],q[i])
                if o~=0 then return o>0 end
            end
            return true
        end
        local function version(v)
            local s=semver(v);return s[1],s[2],s[3]
        end
        local x,y,z=version({{LuaGenerator.Quote(p.SdkVersion)}})
        local required_runtime={{LuaGenerator.Quote(minimum)}}
        local satisfied=runtime.api_version=={{p.RuntimeApi}} and at_least(runtime.version,required_runtime)
        if not satisfied and runtime.api_version=={{p.RuntimeApi}} then
            local compatibility=type(runtime.compatibility)=='table' and runtime.compatibility.require_runtime
            if type(compatibility)=='function' then pcall(compatibility,{{LuaGenerator.Quote(p.ResourceId)}},required_runtime,{{LuaGenerator.Quote(p.DisplayName)}}) end
        end
        assert(satisfied,'HD2Runtime dependency version mismatch')
        local key='HD2RuntimeMod:{{p.ResourceId}}'
        local existing=rawget(_G,key)
        if existing then return existing end
        local function start()
        {{source}}
        end
        local state=start() or true
        rawset(_G,key,state)
        return state
        """ + "\n";
    // The wrapper an export without custom content uses. Projects bound to an SDK before 0.30 keep the plain wrapper below byte for byte. From
    // the 0.30 SDKs on (the 0.30.0-dev test SDK is a SemVer prerelease, which the plain wrapper's pattern refuses: "Invalid HD2Runtime
    // version") and whenever the required Runtime is newer than the bound SDK, the SemVer-aware wrapper above, which still declares the bound
    // SDK as local x,y,z.
    public static readonly SemVersion SemVerWrapperFrom = SemVersion.Parse(RuntimeRequirement.First030);
    public static string WrapFor(ModProject p, string source, string minimum) =>
        SemVersion.Parse(p.SdkVersion).CompareTo(SemVerWrapperFrom) >= 0 || minimum != p.SdkVersion ? Wrap(p, source, minimum) : Wrap(p, source);
    public static string Wrap(ModProject p, string source) => $$"""
        -- HD2-Addon: {{p.ResourceId}}
        local loader=rawget(_G,'CowboyBingusModLoader')
        assert(loader and loader.api==1 and type(loader.version)=='number' and loader.version>=16,
            'Requires Bingus Shared Loader v15+ / API 1')
        local runtime=require('mods/skyeshade/hd2runtime')
        local function version(v)
            local a,b,c=tostring(v):match('^(%d+)%.(%d+)%.(%d+)$')
            assert(a,'Invalid HD2Runtime version');return tonumber(a),tonumber(b),tonumber(c)
        end
        local a,b,c=version(runtime.version)
        local x,y,z=version('{{p.SdkVersion}}')
        assert(runtime.api_version=={{p.RuntimeApi}} and (a>x or a==x and (b>y or b==y and c>=z)),
            'HD2Runtime dependency version mismatch')
        local key='HD2RuntimeMod:{{p.ResourceId}}'
        local existing=rawget(_G,key)
        if existing then return existing end
        local function start()
        {{source}}
        end
        local state=start() or true
        rawset(_G,key,state)
        return state
        """ + "\n";
}

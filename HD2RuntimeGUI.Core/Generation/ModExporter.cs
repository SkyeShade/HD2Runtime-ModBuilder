using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
        if (!project.Changes.Any(c => c.Enabled) && !project.WeaponChanges.Any(c => c.Enabled)) throw new InvalidDataException("Add at least one enabled modification before exporting.");
        var requires = new { bingus = new { min_release = 15, api = 1 }, hd2runtime = new { module = ProjectIdentity.RuntimeModule, min_version = sdk.Version, api = sdk.ApiVersion } };
        var description = $"Requires Bingus Shared Loader v15+ / API 1 and HD2Runtime {sdk.Version}+ / API {sdk.ApiVersion}; install dependencies separately.";
        byte[] Json(object value) => JsonSerializer.SerializeToUtf8Bytes(value, new JsonSerializerOptions { WriteIndented = true });
        byte[] Text(string value) => Encoding.UTF8.GetBytes(value);
        var entries = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["manifest.json"] = Json(new { Version = 1, Guid = project.ManagerGuid, Name = project.DisplayName + " " + project.Version, Description = description,
                Options = new[] { new { Name = project.DisplayName, Description = description, Include = new[] { "mod" } } } }),
            ["hd2runtime.json"] = Json(new { format = 1, name = project.DisplayName, author = project.Author, version = project.Version, resource = project.ResourceId, guid = project.ManagerGuid, requires }),
            ["build-report.json"] = Json(new { resource = project.ResourceId, sdk_version = sdk.Version, runtime_bundled = false, sdk_stubs_bundled = false, requires, builder = "HD2RuntimeGUI 0.2.0 / .NET 10", deployed = false, game_launched = false }),
            ["README.md"] = Text($"# {project.DisplayName}\n\n{project.Description}\n\nBy {project.Author}.\n\n{description}\n\nInstall this gameplay ZIP through your mod manager after installing both dependencies.\n"),
            ["src/addon.lua"] = Text(lua),
            [$"mod/{GameplayArchive.ArchiveName}"] = GameplayArchive.Build(project.ResourceId, Text(Wrap(project, lua))),
            [$"mod/{GameplayArchive.ArchiveName}.stream"] = [],
            [$"mod/{GameplayArchive.ArchiveName}.gpu_resources"] = []
        };
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

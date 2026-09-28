using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Storage;

namespace HD2RuntimeGUI.Core.GameAssets;

// Game-derived UI icons. They are extracted on the user's machine from their own installed game into the local data folder
// (never shipped in this repository) and are only attached where a published Runtime identity names the icon:
// boosters through BoosterAuthoringCapabilities identity.uiIcon, stratagems through StratagemAuthoringCapabilities uiIcon (after 0.24.0).
public sealed record IconSource(string Resource, string Sha256, int Icons);
public sealed record IconManifest(int FormatVersion, string GameDataPath, DateTimeOffset ImportedAt, IconSource[] Sources,
    Dictionary<string, string> StratagemTypeIcons, Dictionary<string, string> BoosterTypeIcons);

public static class XamlIcons
{
    public const string StratagemLibrary = "content/ui/shared/resources/generated_icons/stratagem_icons";
    public const string BoosterLibrary = "content/ui/shared/resources/generated_icons/booster_icons";
    private static readonly Regex Key = new(@"\A[A-Za-z][A-Za-z0-9]{0,63}\z", RegexOptions.CultureInvariant);
    private static readonly Regex PathData = new(@"\A[MmLlHhVvCcSsQqTtAaZz0-9eE+\-.,\s]{1,200000}\z", RegexOptions.CultureInvariant);
    private static readonly Regex Colour = new(@"\A#([0-9A-Fa-f]{2})?([0-9A-Fa-f]{6})\z", RegexOptions.CultureInvariant);
    private const string Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation", X = "http://schemas.microsoft.com/winfx/2006/xaml";

    public static bool ValidKey(string key) => Key.IsMatch(key);
    // Stingray XAML resource: 16-byte header (u32 text size + 12 bytes), then the XAML text.
    public static string Text(byte[] resource)
    {
        if (resource.Length < 16) throw new InvalidDataException("XAML resource too small.");
        var size = BitConverter.ToUInt32(resource, 0);
        if (size > resource.Length - 16) throw new InvalidDataException("XAML resource size mismatch.");
        return Encoding.UTF8.GetString(resource, 16, (int)size);
    }
    private static XDocument Parse(string xaml) => XDocument.Load(XmlReader.Create(new StringReader(xaml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }));

    // Vector DataTemplates (Viewbox/Canvas/Path) -> standalone SVG. Unknown content is skipped rather than approximated.
    public static IReadOnlyDictionary<string, string> ToSvgs(string xaml)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var template in Parse(xaml).Root!.Elements(XName.Get("DataTemplate", Presentation)))
        {
            var key = (string?)template.Attribute(XName.Get("Key", X));
            if (key == null || !Key.IsMatch(key)) continue;
            var canvas = template.Element(XName.Get("Viewbox", Presentation))?.Element(XName.Get("Canvas", Presentation)) ?? template.Element(XName.Get("Canvas", Presentation));
            var paths = (canvas ?? template).Elements(XName.Get("Path", Presentation)).ToArray();
            if (paths.Length == 0) continue;
            var width = Number(canvas?.Attribute("Width")) ?? paths.Max(p => (Number(p.Attribute("Width")) ?? 0) + (Number(p.Attribute(XName.Get("Canvas.Left"))) ?? 0));
            var height = Number(canvas?.Attribute("Height")) ?? paths.Max(p => (Number(p.Attribute("Height")) ?? 0) + (Number(p.Attribute(XName.Get("Canvas.Top"))) ?? 0));
            if (width is <= 0 or > 4096 || height is <= 0 or > 4096) { width = 256; height = 256; }
            var svg = new StringBuilder($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {F(width)} {F(height)}\">");
            var ok = true;
            foreach (var p in paths)
            {
                var data = ((string?)p.Attribute("Data") ?? "").Trim();
                var rule = "evenodd";
                if (data.StartsWith("F0", StringComparison.Ordinal)) data = data[2..];
                else if (data.StartsWith("F1", StringComparison.Ordinal)) { rule = "nonzero"; data = data[2..]; }
                if (!PathData.IsMatch(data)) { ok = false; break; }
                svg.Append("<path d=\"").Append(data).Append("\" fill-rule=\"").Append(rule).Append('"');
                var fill = (string?)p.Attribute("Fill") ?? (string?)p.Element(XName.Get("Path.Fill", Presentation))?.Element(XName.Get("SolidColorBrush", Presentation))?.Attribute("Color");
                svg.Append(Paint("fill", fill));
                if ((string?)p.Attribute("Stroke") is { } stroke) svg.Append(Paint("stroke", stroke));
                if (Number(p.Attribute("StrokeThickness")) is { } thickness) svg.Append(" stroke-width=\"").Append(F(thickness)).Append('"');
                var left = Number(p.Attribute(XName.Get("Canvas.Left"))) ?? 0; var top = Number(p.Attribute(XName.Get("Canvas.Top"))) ?? 0;
                var matrix = (string?)p.Element(XName.Get("Path.RenderTransform", Presentation))?.Element(XName.Get("MatrixTransform", Presentation))?.Attribute("Matrix");
                if (matrix != null)
                {
                    var m = matrix.Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
                    if (m.Length != 6 || m.Any(v => !double.IsFinite(v))) { ok = false; break; }
                    svg.Append($" transform=\"matrix({F(m[0])} {F(m[1])} {F(m[2])} {F(m[3])} {F(left)} {F(top)})\"");
                }
                else if (left != 0 || top != 0) svg.Append($" transform=\"translate({F(left)} {F(top)})\"");
                svg.Append("/>");
            }
            if (ok) result[key] = svg.Append("</svg>").ToString();
        }
        return result;
    }
    // The game's own native-name -> icon bindings (DataTriggers of the type DataTemplate), kept for reference.
    public static Dictionary<string, string> TypeBindings(string xaml, string templateKey)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var template = Parse(xaml).Root!.Elements(XName.Get("DataTemplate", Presentation)).FirstOrDefault(t => (string?)t.Attribute(XName.Get("Key", X)) == templateKey);
        foreach (var trigger in template?.Descendants(XName.Get("DataTrigger", Presentation)) ?? [])
        {
            var value = (string?)trigger.Attribute("Value");
            var icon = trigger.Elements(XName.Get("Setter", Presentation)).Select(s => (string?)s.Attribute("Value")).FirstOrDefault(v => v?.StartsWith("{DynamicResource ", StringComparison.Ordinal) == true);
            if (value == null || icon == null) continue;
            var key = icon["{DynamicResource ".Length..^1].Trim();
            if (Regex.IsMatch(value, @"\A[A-Za-z0-9_]{1,64}\z") && Key.IsMatch(key)) result[value] = key;
        }
        return new(result, StringComparer.Ordinal);
    }
    private static string Paint(string attribute, string? colour)
    {
        if (colour == null) return "";
        var m = Colour.Match(colour.Trim());
        if (!m.Success) return $" {attribute}=\"none\"";
        var alpha = m.Groups[1].Success ? Convert.ToInt32(m.Groups[1].Value, 16) / 255.0 : 1;
        return $" {attribute}=\"#{m.Groups[2].Value.ToUpperInvariant()}\"" + (alpha < 1 ? $" {attribute}-opacity=\"{F(alpha)}\"" : "");
    }
    private static double? Number(XAttribute? a) => a != null && double.TryParse(a.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : null;
    private static string F(double v) => Math.Round(v, 4).ToString(CultureInfo.InvariantCulture);
}

public sealed class GameIconStore(AppPaths paths)
{
    public const string Stratagem = "stratagem", Booster = "booster";
    public string Folder => Path.Combine(paths.Root, "Icons");
    private IconManifest? manifest; private readonly Dictionary<string, string?> cache = new(StringComparer.Ordinal);
    public IconManifest? Manifest => manifest ??= Load();
    public static string? DefaultGameDataPath()
    {
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) })
        {
            var path = Path.Combine(root, "Steam", "steamapps", "common", "Helldivers 2", "data");
            if (root.Length > 0 && IsGameData(path)) return path;
        }
        return null;
    }
    // A Helldivers 2 data folder: slim edition (bundles.nxa) or fat edition (boot archive).
    public static bool IsGameData(string path) => File.Exists(Path.Combine(path, "bundles.nxa")) || File.Exists(Path.Combine(path, "9ba626afa44a3aa3"));
    // Raised after icons are imported or removed, so open pages refresh their icons.
    public event Action? Changed;
    private IconManifest? Load()
    {
        var file = Path.Combine(Folder, "manifest.json");
        try { return File.Exists(file) ? JsonSerializer.Deserialize<IconManifest>(File.ReadAllText(file), JsonStorage.Options) : null; }
        catch (JsonException) { return null; }
    }
    // Extracts both icon libraries from the installed game (read-only) and replaces the local icon folder atomically.
    public async Task<IconManifest> ImportAsync(string gameDataPath, CancellationToken ct = default)
    {
        var type = GameDataReader.Hash("xaml");
        var ids = new[] { XamlIcons.StratagemLibrary, XamlIcons.BoosterLibrary }.ToDictionary(n => n, n => new GameDataReader.ResourceId(GameDataReader.Hash(n), type));
        var data = await Task.Run(() => GameDataReader.Open(gameDataPath).ReadMain(ids.Values.ToArray(), ct), ct);
        foreach (var (name, id) in ids) if (!data.ContainsKey(id)) throw new InvalidDataException("Icon library not found in the game data: " + name);
        var stage = Folder + ".staging-" + Guid.NewGuid().ToString("N");
        try
        {
            var sources = new List<IconSource>(); Dictionary<string, string> stratagemTypes = [], boosterTypes = [];
            foreach (var (name, kind) in new[] { (XamlIcons.StratagemLibrary, Stratagem), (XamlIcons.BoosterLibrary, Booster) })
            {
                var bytes = data[ids[name]]; var xaml = XamlIcons.Text(bytes); var svgs = XamlIcons.ToSvgs(xaml);
                Directory.CreateDirectory(Path.Combine(stage, kind));
                foreach (var (key, svg) in svgs) await File.WriteAllTextAsync(Path.Combine(stage, kind, key + ".svg"), svg, ct);
                sources.Add(new(name, Convert.ToHexString(SHA256.HashData(bytes)), svgs.Count));
                if (kind == Stratagem) stratagemTypes = XamlIcons.TypeBindings(xaml, "StratagemTypeDataTemplate"); else boosterTypes = XamlIcons.TypeBindings(xaml, "BoosterDataTemplate");
            }
            var result = new IconManifest(1, gameDataPath, DateTimeOffset.UtcNow, sources.ToArray(), stratagemTypes, boosterTypes);
            await File.WriteAllTextAsync(Path.Combine(stage, "manifest.json"), JsonSerializer.Serialize(result, JsonStorage.Options), ct);
            if (Directory.Exists(Folder)) Directory.Delete(Folder, true);
            Directory.Move(stage, Folder);
            manifest = result; cache.Clear(); EnableAutoImport(); Changed?.Invoke();
            return result;
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
    public void Clear() { if (Directory.Exists(Folder)) Directory.Delete(Folder, true); manifest = null; cache.Clear(); Changed?.Invoke(); }
    public int Count(string kind) => Directory.Exists(Path.Combine(Folder, kind)) ? Directory.GetFiles(Path.Combine(Folder, kind), "*.svg").Length : 0;
    // SVG text of an imported icon, or null when absent.
    public string? Svg(string kind, string? key)
    {
        if (key == null || !XamlIcons.ValidKey(key) || kind is not (Stratagem or Booster)) return null;
        var id = kind + "/" + key;
        if (!cache.TryGetValue(id, out var svg)) cache[id] = svg = File.Exists(Path.Combine(Folder, kind, key + ".svg")) ? File.ReadAllText(Path.Combine(Folder, kind, key + ".svg")) : null;
        return svg;
    }
    public string? DataUri(string kind, string? key) => Svg(kind, key) is { } svg ? "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg)) : null;

    // Published identity only: Runtime's uiIcon for the booster's native member, and the imported game template must bind that
    // same native member to that same icon. Enum-value ambiguity (CANDIDATES) does not affect which icon the member uses.
    public static string? BoosterIconKey(Booster b, IconManifest? manifest) =>
        b.Identity is { UiIcon: { } icon, NativeName: { } native } && manifest?.BoosterTypeIcons.GetValueOrDefault(native) == icon ? icon : null;
    public string? BoosterIcon(Booster b) => BoosterIconKey(b, Manifest);
    // Published identity only: Runtime's uiIcon, resolved structurally from the stratagem's native type to the game's own
    // icon binding. SDK 0.24.0 and older publish none, so their stratagems keep category glyphs.
    public static string? StratagemIconKey(StratagemDefinition s) => s.UiIcon is { State: "resolved", IconKey: { } key } ? key : null;

    // Automatic import: once, when a Helldivers 2 install is detected and no icons are cached. Removing icons in Settings
    // turns it off until the next manual import.
    private string AutoImportOff => Path.Combine(paths.Root, "icons-auto-import-off");
    public bool AutoImportEnabled => !File.Exists(AutoImportOff);
    public string? AutoImportPath() => Manifest == null && AutoImportEnabled ? DefaultGameDataPath() : null;
    public void DisableAutoImport() { Directory.CreateDirectory(paths.Root); File.WriteAllText(AutoImportOff, "Game icon auto-import turned off in Settings.\n"); }
    public void EnableAutoImport() { if (File.Exists(AutoImportOff)) File.Delete(AutoImportOff); }
}

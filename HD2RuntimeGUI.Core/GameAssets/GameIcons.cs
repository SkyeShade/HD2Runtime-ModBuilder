using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Storage;

namespace HD2RuntimeGUI.Core.GameAssets;

// Game-derived UI icons. They are extracted on the user's machine from their own installed game into the local data folder
// (never shipped in this repository) and are only attached where a published Runtime identity names the icon: first the game's HUD
// sprite that HD2Runtime's HudIconSprites.json names for the stratagem or booster (by its published display name), else the vector
// loadout icon its published uiIcon names (BoosterAuthoringCapabilities identity.uiIcon, StratagemAuthoringCapabilities uiIcon).
public sealed record IconSource(string Resource, string Sha256, int Icons);
public sealed record IconManifest(int FormatVersion, string GameDataPath, DateTimeOffset ImportedAt, IconSource[] Sources,
    Dictionary<string, string> StratagemTypeIcons, Dictionary<string, string> BoosterTypeIcons,
    // Format 2: signature of the game data files the libraries were read from, so a game update refreshes the cache.
    string? GameSignature = null,
    // Format 3: the HUD sprite icons (display name -> PNG file stem, the sprite name in hex), the build of the sprite list they were
    // read with, and the installed game's build (SHA-256 prefix of bin/helldivers2.exe) when it could be read.
    Dictionary<string, string>? StratagemSprites = null, Dictionary<string, string>? BoosterSprites = null, string? SpriteBuild = null, string? GameBuild = null);

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
    // The accent of each vector template: its most used saturated fill, stroke or brush colour (HD2R Editor tools/game_icons.py accents);
    // the colour a stratagem's HUD sprite is painted in.
    public static IReadOnlyDictionary<string, (byte R, byte G, byte B)> Accents(string xaml)
    {
        var result = new Dictionary<string, (byte, byte, byte)>(StringComparer.Ordinal);
        foreach (var template in Parse(xaml).Root!.Elements(XName.Get("DataTemplate", Presentation)))
        {
            if ((string?)template.Attribute(XName.Get("Key", X)) is not { } key || !Key.IsMatch(key)) continue;
            var counts = new List<((byte, byte, byte) Rgb, int Count)>();
            foreach (var element in template.DescendantsAndSelf())
                foreach (var name in new[] { "Fill", "Stroke", "Color" })
                {
                    if (Colour.Match(((string?)element.Attribute(name) ?? "").Trim()) is not { Success: true } m) continue;
                    var hex = m.Groups[2].Value; var rgb = (Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..], 16));
                    if (Math.Max(rgb.Item1, Math.Max(rgb.Item2, rgb.Item3)) - Math.Min(rgb.Item1, Math.Min(rgb.Item2, rgb.Item3)) <= 56) continue;
                    var at = counts.FindIndex(c => c.Rgb == rgb);
                    if (at < 0) counts.Add((rgb, 1)); else counts[at] = (rgb, counts[at].Count + 1);
                }
            // The first colour reaching the highest count wins, as Python's max() over insertion order.
            if (counts.Count > 0) result[key] = counts.Aggregate((best, c) => c.Count > best.Count ? c : best).Rgb;
        }
        return result;
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
    public const int ManifestFormat = 3;
    // Helldivers 2 data folders in the default Steam install and every Steam library (libraryfolders.vdf), first valid one wins.
    public static string? DefaultGameDataPath() => GameDataCandidates().FirstOrDefault(IsGameData);
    public static IEnumerable<string> GameDataCandidates()
    {
        var steamRoots = new List<string>();
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) })
            if (root.Length > 0) steamRoots.Add(Path.Combine(root, "Steam"));
        if (OperatingSystem.IsWindows())
            try { if (Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string steam && steam.Length > 0) steamRoots.Add(Path.GetFullPath(steam)); }
            catch (Exception e) when (e is System.Security.SecurityException or IOException or ArgumentException or UnauthorizedAccessException) { }
        var libraries = new List<string>();
        foreach (var steam in steamRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            libraries.Add(steam);
            var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            try { if (File.Exists(vdf)) libraries.AddRange(SteamLibraries(File.ReadAllText(vdf))); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return libraries.Distinct(StringComparer.OrdinalIgnoreCase).Select(l => Path.Combine(l, "steamapps", "common", "Helldivers 2", "data"));
    }
    // "path" entries of Steam's libraryfolders.vdf (backslashes are escaped in the file).
    public static IEnumerable<string> SteamLibraries(string vdf) =>
        Regex.Matches(vdf, "\"path\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\"").Select(m => Regex.Unescape(m.Groups[1].Value)).Where(p => p.Length > 0);
    // Cheap change detection: the archive index files change whenever the game is updated.
    public static string? GameSignature(string dataPath)
    {
        var parts = new[] { "bundles.nxa", "9ba626afa44a3aa3", "9ba626afa44a3aa3.stream" }.Select(n => new FileInfo(Path.Combine(dataPath, n))).Where(f => f.Exists)
            .Select(f => $"{f.Name}:{f.Length}:{f.LastWriteTimeUtc.Ticks}").ToArray();
        return parts.Length == 0 ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", parts))))[..32];
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
    // Extracts both icon libraries and the HUD sprites from the installed game (read-only) and replaces the local icon folder atomically.
    // A sprite that cannot be read or decoded is left out: that item keeps its vector icon, or the category glyph.
    public async Task<IconManifest> ImportAsync(string gameDataPath, CancellationToken ct = default)
    {
        var type = GameDataReader.Hash("xaml");
        var ids = new[] { XamlIcons.StratagemLibrary, XamlIcons.BoosterLibrary }.ToDictionary(n => n, n => new GameDataReader.ResourceId(GameDataReader.Hash(n), type));
        var reader = await Task.Run(() => GameDataReader.Open(gameDataPath), ct);
        var data = await Task.Run(() => reader.ReadMain(ids.Values.ToArray(), ct), ct);
        foreach (var (name, id) in ids) if (!data.ContainsKey(id)) throw new InvalidDataException(CoreText.Format("Messages.Icons.LibraryMissing", name));
        var list = HudIconSprites.Bundled.Value;
        var stage = Folder + ".staging-" + Guid.NewGuid().ToString("N");
        try
        {
            var sources = new List<IconSource>(); Dictionary<string, string> stratagemTypes = [], boosterTypes = [];
            var accents = XamlIcons.Accents(XamlIcons.Text(data[ids[XamlIcons.StratagemLibrary]]));
            foreach (var (name, kind) in new[] { (XamlIcons.StratagemLibrary, Stratagem), (XamlIcons.BoosterLibrary, Booster) })
            {
                var bytes = data[ids[name]]; var xaml = XamlIcons.Text(bytes); var svgs = XamlIcons.ToSvgs(xaml);
                Directory.CreateDirectory(Path.Combine(stage, kind));
                foreach (var (key, svg) in svgs) await File.WriteAllTextAsync(Path.Combine(stage, kind, key + ".svg"), svg, ct);
                sources.Add(new(name, Convert.ToHexString(SHA256.HashData(bytes)), svgs.Count));
                if (kind == Stratagem) stratagemTypes = XamlIcons.TypeBindings(xaml, "StratagemTypeDataTemplate"); else boosterTypes = XamlIcons.TypeBindings(xaml, "BoosterDataTemplate");
            }
            var sprites = await Task.Run(() => ReadSprites(reader, list, StratagemAccents(accents, BundledStratagems.Value), ct), ct);
            foreach (var (kind, source) in new[] { (Stratagem, HudIconSprites.StratagemSource), (Booster, HudIconSprites.BoosterSource) })
            {
                Directory.CreateDirectory(Path.Combine(stage, kind, Hud));
                foreach (var (stem, png) in sprites[kind].Files) await File.WriteAllBytesAsync(Path.Combine(stage, kind, Hud, stem + ".png"), png, ct);
                sources.Add(new(source, list.Sha256, sprites[kind].Files.Count));
            }
            var build = await Task.Run(() => GameBuild(gameDataPath), ct);
            var result = new IconManifest(ManifestFormat, gameDataPath, DateTimeOffset.UtcNow, sources.ToArray(), stratagemTypes, boosterTypes, GameSignature(gameDataPath),
                sprites[Stratagem].Names, sprites[Booster].Names, list.Build, build);
            await File.WriteAllTextAsync(Path.Combine(stage, "manifest.json"), JsonSerializer.Serialize(result, JsonStorage.Options), ct);
            if (Directory.Exists(Folder)) Directory.Delete(Folder, true);
            Directory.Move(stage, Folder);
            manifest = result; cache.Clear(); EnableAutoImport(); Changed?.Invoke();
            return result;
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
    private const string Hud = "hud";
    private static readonly Regex SpriteStem = new(@"\A[0-9a-f]{16}\z", RegexOptions.CultureInvariant);
    // The HUD sprite of every listed item as a PNG: stratagems recoloured with their accent, boosters as the game draws them.
    private static Dictionary<string, (Dictionary<string, string> Names, Dictionary<string, byte[]> Files)> ReadSprites(GameDataReader reader, HudIconSpriteList list,
        IReadOnlyDictionary<string, (byte R, byte G, byte B)> accents, CancellationToken ct)
    {
        var result = new[] { Stratagem, Booster }.ToDictionary(k => k, _ => (Names: new Dictionary<string, string>(StringComparer.Ordinal), Files: new Dictionary<string, byte[]>(StringComparer.Ordinal)));
        HudIconReader hud;
        // No readable atlas at all (another game layout): every item keeps its vector icon.
        try { hud = new HudIconReader(reader, ct); }
        catch (Exception e) when (e is InvalidDataException or IOException or ArgumentException or IndexOutOfRangeException) { return result; }
        foreach (var kind in new[] { Stratagem, Booster })
            foreach (var (name, sprite) in list.Of(kind))
            {
                ct.ThrowIfCancellationRequested();
                var stem = sprite.ToString("x16");
                try
                {
                    if (!result[kind].Files.ContainsKey(stem))
                    {
                        if (hud.Sprite(sprite) is not { } got) continue;
                        var rgba = kind == Stratagem ? TextureAtlas.Recolour(got.Rgba, accents.GetValueOrDefault(name, SupportBlue)) : got.Rgba;
                        result[kind].Files[stem] = CustomContent.CustomIconCompiler.WritePng(got.Width, got.Height, rgba);
                    }
                    result[kind].Names[name] = stem;
                }
                catch (Exception e) when (e is InvalidDataException or IOException or ArgumentException or IndexOutOfRangeException) { }
            }
        return result;
    }
    // ModBuilder's stratagem category colours (app.css --cat-*), the last fallback for an accent.
    private static readonly (byte, byte, byte) SupportBlue = (121, 174, 252);
    private static (byte, byte, byte) CategoryColour(string family) => family switch
    {
        "orbital" or "eagle" => (255, 128, 120), "sentry" or "emplacement" or "mine" => (98, 213, 150), _ => SupportBlue,
    };
    // Reviewed by hand in the HD2R Editor: the library icon whose colour an unbound stratagem takes (its colour only, never its artwork).
    private static readonly Dictionary<string, string> UnboundAccent = new(StringComparer.Ordinal) { ["AC-8 Autocannon"] = "StratagemWeaponAutocannon" };
    // The colour each stratagem's red mask is painted in (HD2R Editor tools/game_icons.py): the accent of its own loadout library icon (uiIcon
    // resolved), else the most common accent of its family's library icons, else of the support family's, else ModBuilder's category colour.
    public static IReadOnlyDictionary<string, (byte R, byte G, byte B)> StratagemAccents(IReadOnlyDictionary<string, (byte R, byte G, byte B)> library,
        IEnumerable<(string Name, string Family, string? IconKey)> stratagems)
    {
        var all = stratagems.ToArray(); var own = new Dictionary<string, (byte, byte, byte)>(StringComparer.Ordinal);
        var byFamily = new Dictionary<string, List<((byte, byte, byte) Rgb, int Count)>>(StringComparer.Ordinal);
        foreach (var (name, family, key) in all)
        {
            if ((key ?? UnboundAccent.GetValueOrDefault(name)) is not { } k || !library.TryGetValue(k, out var rgb)) continue;
            own[name] = rgb;
            var counts = byFamily.TryGetValue(family, out var c) ? c : byFamily[family] = [];
            var at = counts.FindIndex(x => x.Rgb == rgb);
            if (at < 0) counts.Add((rgb, 1)); else counts[at] = (rgb, counts[at].Count + 1);
        }
        (byte, byte, byte)? Common(string family) => byFamily.TryGetValue(family, out var c) ? c.Aggregate((best, x) => x.Count > best.Count ? x : best).Rgb : null;
        return all.ToDictionary(s => s.Name, s => own.TryGetValue(s.Name, out var rgb) ? rgb : Common(s.Family) ?? Common("support") ?? CategoryColour(s.Family), StringComparer.Ordinal);
    }
    // Name, family and resolved icon key of every stratagem of the bundled SDK (only the accent colours use it).
    private static readonly Lazy<(string Name, string Family, string? IconKey)[]> BundledStratagems = new(() =>
    {
        using var stream = typeof(GameIconStore).Assembly.GetManifestResourceStream("HD2RuntimeGUI.Core.Metadata.Bundled.StratagemAuthoringCapabilities.json")!;
        using var doc = JsonDocument.Parse(stream);
        return doc.RootElement.GetProperty("stratagems").EnumerateArray().Select(s => (s.GetProperty("name").GetString()!, s.GetProperty("family").GetString() ?? "",
            s.TryGetProperty("uiIcon", out var icon) && icon.ValueKind == JsonValueKind.Object && icon.TryGetProperty("state", out var state) && state.GetString() == "resolved"
                && icon.TryGetProperty("iconKey", out var key) ? key.GetString() : null)).ToArray();
    });
    // The installed game's build as HD2Runtime names it (the first 12 hex digits of bin/helldivers2.exe's SHA-256), or null when unreadable.
    public static string? GameBuild(string gameDataPath)
    {
        try
        {
            var exe = Path.Combine(gameDataPath, "..", "bin", "helldivers2.exe");
            if (!File.Exists(exe)) return null;
            using var stream = File.OpenRead(exe); return Convert.ToHexString(SHA256.HashData(stream))[..12];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }
    public void Clear() { if (Directory.Exists(Folder)) Directory.Delete(Folder, true); manifest = null; cache.Clear(); Changed?.Invoke(); }
    // Imported icon files of a kind: vector icons and HUD sprites.
    public int Count(string kind) => Files(Path.Combine(Folder, kind), "*.svg") + Files(Path.Combine(Folder, kind, Hud), "*.png");
    private static int Files(string folder, string pattern) => Directory.Exists(folder) ? Directory.GetFiles(folder, pattern).Length : 0;
    // SVG text of an imported icon, or null when absent.
    public string? Svg(string kind, string? key)
    {
        if (key == null || !XamlIcons.ValidKey(key) || kind is not (Stratagem or Booster)) return null;
        var id = kind + "/" + key;
        if (!cache.TryGetValue(id, out var svg)) cache[id] = svg = File.Exists(Path.Combine(Folder, kind, key + ".svg")) ? File.ReadAllText(Path.Combine(Folder, kind, key + ".svg")) : null;
        return svg;
    }
    public string? DataUri(string kind, string? key) => Svg(kind, key) is { } svg ? "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg)) : null;
    // PNG data URI of an item's imported HUD sprite, by its published display name, or null when absent.
    public string? SpriteUri(string kind, string? name)
    {
        var names = kind switch { Stratagem => Manifest?.StratagemSprites, Booster => Manifest?.BoosterSprites, _ => null };
        if (name == null || names?.GetValueOrDefault(name) is not { } stem || !SpriteStem.IsMatch(stem)) return null;
        var id = kind + "/" + Hud + "/" + stem;
        if (!cache.TryGetValue(id, out var uri))
        {
            var file = Path.Combine(Folder, kind, Hud, stem + ".png");
            cache[id] = uri = File.Exists(file) ? "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(file)) : null;
        }
        return uri;
    }
    // The icon an item shows: its HUD sprite (as on the game's HUD and in the HD2R Editor), else its vector loadout icon.
    public string? IconUri(string kind, string? name, string? key) => SpriteUri(kind, name) ?? DataUri(kind, key);
    public string? StratagemIcon(StratagemDefinition s) => IconUri(Stratagem, s.Name, StratagemIconKey(s));
    public string? BoosterIconUri(Booster b) => IconUri(Booster, b.Name, BoosterIcon(b));
    // Why an item has no game icon at all, as HudIconSprites.json publishes it (SG-88, CQC-72: no call-in stratagem), or null.
    public static string? NoIconReason(string name) => HudIconSprites.Bundled.Value.Missing.GetValueOrDefault(name);
    // The sprite list build when it differs from the installed game's build (read at import) or, failing that, from the build the SDK's
    // catalogs were researched on: some sprites may be missing or moved until ModBuilder ships a newer list. A warning only.
    public (string Sprites, string Other, bool Game)? SpriteBuildMismatch(string? sdkBuild)
    {
        if (Manifest is not { SpriteBuild: { } sprites } m) return null;
        if (m.GameBuild is { } game) return game.Equals(sprites, StringComparison.OrdinalIgnoreCase) ? null : (sprites, game, true);
        return sdkBuild != null && !sdkBuild.Equals(sprites, StringComparison.OrdinalIgnoreCase) ? (sprites, sdkBuild, false) : null;
    }

    // Published identity only: Runtime's uiIcon for the booster's native member, and the imported game template must bind that
    // same native member to that same icon. Enum-value ambiguity (CANDIDATES) does not affect which icon the member uses.
    public static string? BoosterIconKey(Booster b, IconManifest? manifest) =>
        b.Identity is { UiIcon: { } icon, NativeName: { } native } && manifest?.BoosterTypeIcons.GetValueOrDefault(native) == icon ? icon : null;
    public string? BoosterIcon(Booster b) => BoosterIconKey(b, Manifest);
    // Published identity only: Runtime's uiIcon, resolved structurally from the stratagem's native type to the game's own
    // icon binding. SDK 0.24.0 and older publish none, so their stratagems keep category glyphs.
    public static string? StratagemIconKey(StratagemDefinition s) => s.UiIcon is { State: "resolved", IconKey: { } key } ? key : null;

    // Automatic import, in the background at startup: when a Helldivers 2 install is found and the local cache is missing or stale
    // (older cache format, game files changed, or icon files missing). A valid cache is never re-extracted. Removing icons, or turning
    // automatic import off in Settings, is respected until the user imports again or turns it back on.
    private string AutoImportOff => Path.Combine(paths.Root, "icons-auto-import-off");
    public bool AutoImportEnabled => !File.Exists(AutoImportOff);
    // Install detection (replaceable in tests so they never read a real game install).
    public Func<string?> Detect { get; init; } = DefaultGameDataPath;
    public void DisableAutoImport() { Directory.CreateDirectory(paths.Root); File.WriteAllText(AutoImportOff, "Game icon auto-import turned off in Settings.\n"); Changed?.Invoke(); }
    public void EnableAutoImport() { if (File.Exists(AutoImportOff)) File.Delete(AutoImportOff); }
    // Why the cache for this install needs (re)importing, or null when it is current.
    public string? RefreshReason(string gameDataPath)
    {
        if (Manifest is not { } m) return CoreText.Get("Messages.Icons.Reason.NotImported");
        if (m.GameSignature == null) return CoreText.Get("Messages.Icons.Reason.CacheOutdated");
        // An older cache format, or a ModBuilder that ships another sprite list: the icons are read again.
        if (m.FormatVersion < ManifestFormat || m.Sources.Any(s => s.Resource is HudIconSprites.StratagemSource or HudIconSprites.BoosterSource && s.Sha256 != HudIconSprites.Bundled.Value.Sha256))
            return CoreText.Get("Messages.Icons.Reason.ReaderUpdated");
        if (GameSignature(gameDataPath) != m.GameSignature) return CoreText.Get("Messages.Icons.Reason.GameChanged");
        foreach (var source in m.Sources)
            if (source.Resource switch
                {
                    XamlIcons.BoosterLibrary => Files(Path.Combine(Folder, Booster), "*.svg"), HudIconSprites.StratagemSource => Files(Path.Combine(Folder, Stratagem, Hud), "*.png"),
                    HudIconSprites.BoosterSource => Files(Path.Combine(Folder, Booster, Hud), "*.png"), _ => Files(Path.Combine(Folder, Stratagem), "*.svg"),
                } < source.Icons) return CoreText.Get("Messages.Icons.Reason.FilesMissing");
        return null;
    }
    public (string Path, string Reason)? AutoImportPlan()
    {
        if (!AutoImportEnabled) return null;
        var path = Manifest?.GameDataPath is { } previous && IsGameData(previous) ? previous : Detect();
        return path != null && RefreshReason(path) is { } reason ? (path, reason) : null;
    }
    public bool Importing { get; private set; }
    public string? LastError { get; private set; }
    public string? LastReason { get; private set; }
    private int autoImportRunning;
    // Runs the plan once (concurrent calls are ignored). Failures keep the previous cache and category glyphs, and are reported
    // through LastError for Settings; authoring is never affected.
    public async Task<bool> AutoImportAsync(CancellationToken ct = default)
    {
        if (AutoImportPlan() is not { } plan || Interlocked.Exchange(ref autoImportRunning, 1) == 1) return false;
        try
        {
            Importing = true; LastReason = plan.Reason; Changed?.Invoke();
            await ImportAsync(plan.Path, ct); LastError = null; return true;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or XmlException or JsonException)
        { LastError = e.Message; return false; }
        finally { Importing = false; Interlocked.Exchange(ref autoImportRunning, 0); Changed?.Invoke(); }
    }
    // SHA-256 of the imported stratagem icon library, compared with the SDK's uiIconContract.librarySha256.
    public string? StratagemLibrarySha => Manifest?.Sources.FirstOrDefault(s => s.Resource == XamlIcons.StratagemLibrary)?.Sha256;
}

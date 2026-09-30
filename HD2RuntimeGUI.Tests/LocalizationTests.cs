using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Services;
using HD2RuntimeGUI.Core.Storage;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// UI localization: resource integrity (keys, translations, placeholders, markup), key usage by the source, fallback, plural rules,
// the language preference, and that nothing ModBuilder generates depends on the UI language. Language changes in these tests are
// scoped to the test's async flow (UiCulture.Use), so parallel tests keep English.
public sealed class LocalizationTests
{
    private static readonly UiLanguage Chinese = UiLanguages.Find(UiLanguages.SimplifiedChinese)!;
    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "HD2RuntimeGUI.Core", "Resources", "Strings"))) return dir.FullName;
        throw new DirectoryNotFoundException("Repository root not found.");
    }
    private static string Folder => Path.Combine(Root(), "HD2RuntimeGUI.Core", "Resources", "Strings");
    private sealed record Entry(string Value, string? Comment);
    private static (Dictionary<string, Entry> Entries, List<string> Duplicates) Read(string? culture)
    {
        var doc = XDocument.Load(Path.Combine(Folder, culture == null ? "Strings.resx" : $"Strings.{culture}.resx"));
        var entries = new Dictionary<string, Entry>(StringComparer.Ordinal); var duplicates = new List<string>();
        foreach (var data in doc.Root!.Elements("data"))
        {
            var name = (string)data.Attribute("name")!;
            if (!entries.TryAdd(name, new((string?)data.Element("value") ?? "", (string?)data.Element("comment")))) duplicates.Add(name);
        }
        return (entries, duplicates);
    }
    private static int[] Placeholders(string s) => Regex.Matches(s.Replace("{{", "").Replace("}}", ""), @"\{(\d+)(?:[,:][^}]*)?\}")
        .Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).Distinct().Order().ToArray();
    private static readonly Regex AllowedTag = new(@"^</?(code|strong|em|br)\s*/?>$");
    private static IEnumerable<string> Translations => UiLanguages.Supported.Where(l => l != UiLanguages.Neutral).Select(l => l.Id);

    // ---- resources ------------------------------------------------------------------------------------------------------------------

    [Fact] public void The_neutral_resources_have_unique_semantic_keys_and_no_empty_values()
    {
        var (entries, duplicates) = Read(null);
        Assert.Empty(duplicates);
        Assert.True(entries.Count > 500, $"only {entries.Count} neutral keys");
        Assert.All(entries, e => Assert.Matches(@"^[A-Z][A-Za-z0-9]*(\.[A-Za-z0-9]+)+$", e.Key));
        Assert.Empty(entries.Where(e => string.IsNullOrWhiteSpace(e.Value.Value)).Select(e => e.Key));
        // Counted text always has the Other form, and every plural form of a key has the same base.
        var plural = new Regex(@"\.(Zero|One|Two|Few|Many|Other)$");
        Assert.DoesNotContain(entries.Keys, k => plural.IsMatch(k) && !entries.ContainsKey(plural.Replace(k, ".Other")));
    }

    [Fact] public void Markup_appears_only_in_html_keys_and_only_as_inline_formatting()
    {
        foreach (var culture in Translations.Prepend(null))
            foreach (var (key, entry) in Read(culture).Entries)
            {
                var tags = Regex.Matches(entry.Value, "<[^>]*>").Select(m => m.Value).ToArray();
                if (key.EndsWith(".Html", StringComparison.Ordinal)) Assert.True(tags.All(t => AllowedTag.IsMatch(t)), $"{culture ?? "neutral"} {key}: {string.Join(" ", tags)}");
                else Assert.True(tags.Length == 0, $"{culture ?? "neutral"} {key} has markup but is not a .Html key");
            }
    }

    [Fact] public void Every_supported_language_has_every_key_with_the_same_placeholders()
    {
        var neutral = Read(null).Entries;
        foreach (var culture in Translations)
        {
            var (entries, duplicates) = Read(culture);
            Assert.Empty(duplicates);
            var missing = neutral.Keys.Where(k => !entries.ContainsKey(k)).ToArray();
            Assert.True(missing.Length == 0, $"{culture} lacks {missing.Length} keys, e.g. {string.Join(", ", missing.Take(10))}. Add them to Strings.{culture}.resx.");
            Assert.DoesNotContain(entries.Keys, k => !neutral.ContainsKey(k));
            Assert.Empty(entries.Where(e => string.IsNullOrWhiteSpace(e.Value.Value)).Select(e => e.Key));
            var wrong = neutral.Where(n => !Placeholders(entries[n.Key].Value).SequenceEqual(Placeholders(n.Value.Value))).Select(n => n.Key).ToArray();
            Assert.True(wrong.Length == 0, $"{culture}: placeholders differ from English in {string.Join(", ", wrong)}");
        }
    }

    [Fact] public void Date_format_resources_are_valid_patterns_in_every_language()
    {
        var date = new DateTime(2026, 9, 30, 6, 39, 0);
        foreach (var language in UiLanguages.Supported)
            foreach (var key in Read(null).Entries.Keys.Where(k => k.EndsWith(".DateTime.Format", StringComparison.Ordinal)))
            {
                var text = date.ToString(TextResources.Get(key, language.Culture), language.Culture);
                Assert.Contains("2026", text); Assert.Contains("39", text);
            }
        using (UiCulture.Use(Chinese)) Assert.Equal("2026年9月30日 06:39", date.ToString(new UiText()["Common.DateTime.Format"], Chinese.Culture));
        Assert.Equal("30 Sep 2026 06:39", date.ToString(new UiText()["Common.DateTime.Format"], UiLanguages.Neutral.Culture));
    }

    [Fact] public void The_language_registry_and_the_resource_files_agree()
    {
        var files = Directory.GetFiles(Folder, "Strings.*.resx").Select(f => Path.GetFileName(f)["Strings.".Length..^".resx".Length]).Order().ToArray();
        Assert.Equal(Translations.Order(), files);
        Assert.Equal(UiLanguages.English, UiLanguages.Neutral.Id);
        Assert.All(UiLanguages.Supported, l => Assert.Equal(l.Id, l.Culture.Name));
    }

    // ---- source ↔ resources ---------------------------------------------------------------------------------------------------------

    private static IEnumerable<string> SourceFiles() =>
        new[] { Path.Combine(Root(), "HD2RuntimeGUI", "Components"), Path.Combine(Root(), "HD2RuntimeGUI", "Services"), Path.Combine(Root(), "HD2RuntimeGUI.Core") }
            .SelectMany(d => Directory.EnumerateFiles(d, "*.*", SearchOption.AllDirectories))
            .Where(f => (f.EndsWith(".razor", StringComparison.Ordinal) || f.EndsWith(".cs", StringComparison.Ordinal))
                && !Regex.IsMatch(f, @"[\\/](bin|obj|Resources)[\\/]"));
    // Explicit lookups plus any other literal shaped like a key of a known area (conditional keys: T[x ? "A.B" : "A.C"]).
    private static Dictionary<string, HashSet<string>> UsedKeys(ISet<string> areas)
    {
        var used = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        void Add(string key, string file) { if (!used.TryGetValue(key, out var files)) used[key] = files = []; files.Add(Path.GetFileName(file)); }
        foreach (var file in SourceFiles())
        {
            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"(?:\bT|\bt|\bText|CoreText)\s*(?:\[\s*|\.(?:Format|Plural|Or|Markup|Html|Get)\(\s*)""([^""]+)""")) Add(m.Groups[1].Value, file);
            foreach (Match m in Regex.Matches(text, @"""([A-Z][A-Za-z0-9]*(?:\.[A-Za-z0-9]+)+)""")) if (areas.Contains(m.Groups[1].Value.Split('.')[0])) Add(m.Groups[1].Value, file);
        }
        return used;
    }

    [Fact] public void Every_key_the_source_uses_exists_and_every_resource_is_used()
    {
        var neutral = Read(null).Entries;
        var used = UsedKeys(neutral.Keys.Select(k => k.Split('.')[0]).ToHashSet());
        var missing = used.Where(u => !neutral.ContainsKey(u.Key) && !neutral.ContainsKey(u.Key + ".Other")).Select(u => $"{u.Key} ({string.Join(", ", u.Value)})").ToArray();
        Assert.True(missing.Length == 0, "Keys used by the source but missing from Strings.resx: " + string.Join("; ", missing));
        static string Base(string key) => Regex.Replace(key, @"\.(Zero|One|Two|Few|Many|Other)$", "");
        var unused = neutral.Keys.Where(k => !used.ContainsKey(k) && !used.ContainsKey(Base(k))).ToArray();
        Assert.True(unused.Length == 0, "Resources no source uses (remove them): " + string.Join(", ", unused));
    }

    [Fact] public void Markup_is_only_rendered_from_html_keys()
    {
        foreach (var file in SourceFiles())
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"\.(?:Markup|Html)\(\s*""([^""]+)"""))
                Assert.True(m.Groups[1].Value.EndsWith(".Html", StringComparison.Ordinal), $"{Path.GetFileName(file)}: {m.Groups[1].Value}");
    }

    // ---- lookup behaviour -----------------------------------------------------------------------------------------------------------

    [Fact] public void Text_follows_the_ui_language_and_falls_back_to_English_then_to_the_key()
    {
        var text = new UiText();
        Assert.Equal("Language", text["Settings.Language.Title"]);
        using (UiCulture.Use(Chinese))
        {
            Assert.Equal("语言", text["Settings.Language.Title"]); Assert.Equal("语言", CoreText.Get("Settings.Language.Title"));
            Assert.Equal("zh-Hans", text.Culture.Name);
        }
        Assert.Equal("Language", text["Settings.Language.Title"]);
        // A culture without resources shows English; a key no resource has shows as itself, never blank.
        Assert.Equal("Language", TextResources.Get("Settings.Language.Title", CultureInfo.GetCultureInfo("fr-FR")));
        Assert.Equal("Nope.NotAKey", text["Nope.NotAKey"]);
        Assert.Equal("fallback", text.Or("Nope.NotAKey", "fallback"));
        Assert.Equal("Language", text.Or("Settings.Language.Title", "fallback"));
        // A translated template whose placeholders do not fit shows the English sentence.
        Assert.Equal("English 7", TextResources.SafeFormat(CultureInfo.InvariantCulture, "译文 {1}", "English {0}", [7]));
        Assert.Equal("broken {", TextResources.SafeFormat(CultureInfo.InvariantCulture, "译文 {1}", "broken {", [7]));
        // Html keys encode their arguments.
        Assert.Equal("Written with <code>&lt;b&gt;</code>.", text.Html("OptIn.WrittenWith.Html", "<b>"));
    }

    [Fact] public void Counted_text_uses_the_plural_rules_of_the_language()
    {
        var english = UiLanguages.Neutral;
        Assert.Equal(PluralCategory.One, english.PluralOf(1)); Assert.Equal(PluralCategory.Other, english.PluralOf(0)); Assert.Equal(PluralCategory.Other, english.PluralOf(2));
        Assert.All(new long[] { 0, 1, 2, 5 }, n => Assert.Equal(PluralCategory.Other, Chinese.PluralOf(n)));
        var counted = Read(null).Entries.Keys.Where(k => k.EndsWith(".One", StringComparison.Ordinal)).Select(k => k[..^".One".Length]).First();
        var text = new UiText();
        Assert.Equal(TextResources.Format(counted + ".One", CultureInfo.InvariantCulture, [1L]), text.Plural(counted, 1));
        Assert.Equal(TextResources.Format(counted + ".Other", CultureInfo.InvariantCulture, [3L]), text.Plural(counted, 3));
        using (UiCulture.Use(Chinese)) Assert.Equal(TextResources.Format(counted + ".Other", Chinese.Culture, [1L]), text.Plural(counted, 1));
    }

    // ---- the language preference ----------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("en-US", "en")] [InlineData("en-GB", "en")] [InlineData("zh-CN", "zh-Hans")] [InlineData("zh-SG", "zh-Hans")] [InlineData("zh-Hans", "zh-Hans")]
    [InlineData("zh-TW", "en")] [InlineData("de-DE", "en")] [InlineData("ar-SA", "en")] [InlineData("", "en")]
    public void System_default_follows_the_OS_language_when_supported(string os, string expected)
    {
        Assert.Equal(expected, UiLanguages.Resolve(UiLanguages.System, CultureInfo.GetCultureInfo(os)).Id);
        Assert.Equal("zh-Hans", UiLanguages.Resolve("zh-Hans", CultureInfo.GetCultureInfo(os)).Id);
        Assert.Equal("en", UiLanguages.Resolve("en", CultureInfo.GetCultureInfo(os)).Id);
        Assert.Equal(expected, UiLanguages.Resolve("tlh", CultureInfo.GetCultureInfo(os)).Id); // an unknown preference counts as "system"
    }

    [Fact] public async Task The_language_preference_is_saved_applied_and_restored()
    {
        using var e = new TestEnvironment();
        var applied = new List<string>();
        var service = new LanguageService(e.Paths, CultureInfo.GetCultureInfo("en-US"), l => applied.Add(l.Id));
        Assert.Equal(UiLanguages.System, service.Preference); Assert.Equal("en", service.Language.Id);
        Assert.False(File.Exists(LanguageService.SettingsPath(e.Paths)));
        service.Apply(); Assert.Equal(["en"], applied);
        var changed = 0; service.Changed += () => changed++;
        await service.SetPreferenceAsync("zh-Hans");
        Assert.Equal("zh-Hans", service.Language.Id); Assert.Equal(["en", "zh-Hans"], applied); Assert.Equal(1, changed);
        Assert.Contains("\"language\": \"zh-Hans\"", await File.ReadAllTextAsync(LanguageService.SettingsPath(e.Paths)));
        // Next start: the saved choice wins over the OS language.
        Assert.Equal("zh-Hans", new LanguageService(e.Paths, CultureInfo.GetCultureInfo("en-US"), _ => { }).Language.Id);
        await service.SetPreferenceAsync(UiLanguages.System);
        Assert.Equal("en", new LanguageService(e.Paths, CultureInfo.GetCultureInfo("de-DE"), _ => { }).Language.Id);
        Assert.Equal("zh-Hans", new LanguageService(e.Paths, CultureInfo.GetCultureInfo("zh-CN"), _ => { }).Language.Id);
    }

    [Theory]
    [InlineData("{ not json")] [InlineData("[]")] [InlineData("{\"language\": 7}")] [InlineData("{\"language\": \"tlh\"}")] [InlineData("")]
    public void An_unreadable_or_unsupported_preference_starts_with_the_system_language(string content)
    {
        using var e = new TestEnvironment();
        Directory.CreateDirectory(e.Paths.Root); File.WriteAllText(LanguageService.SettingsPath(e.Paths), content);
        Assert.Equal(UiLanguages.System, LanguageService.Load(e.Paths).Language);
        Assert.Equal("zh-Hans", new LanguageService(e.Paths, CultureInfo.GetCultureInfo("zh-CN"), _ => { }).Language.Id);
    }

    // ---- generated output is language-independent ------------------------------------------------------------------------------------

    private static async Task<(string Lua, string Json, byte[] Zip)> Build(BuilderWorkspace w, TestEnvironment e)
    {
        await w.OpenAsync(w.Project!.Id);
        Assert.Null(w.BuildError);
        await w.ExportAsync();
        return (w.LuaPreview, await File.ReadAllTextAsync(e.Paths.ProjectFile(w.Project!.Id)), await File.ReadAllBytesAsync(w.LastExport!));
    }

    [Fact] public async Task A_project_generates_the_same_lua_json_and_zip_in_every_ui_language()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        await w.SetEntityAsync("backpack:sh-20-ballistic-shield-backpack:sh-20-ballistic-shield-backpack-damage-zone-zone-0:zone.armor", "6");
        await w.SetAttackOutputAsync("AR-23 Liberator", "primary", "output/v1/projectile/eat-700-expendable-napalm");
        await w.SetObjectScalarAsync("SG-20 Halt", "feed_primary", "projectile", null, "damage.primary.standard_damage", "40", true);
        await w.SetObjectScalarAsync("SG-20 Halt", "feed_alternate", "projectile", null, "projectile.alternate.velocity", "312.75", true);
        // A classic projectile swap (its references have display labels that must never reach the project file), a stratagem and an enemy edit.
        var host = w.OutputHost("SMG-32 Reprimand", "primary", out _)!;
        var talon = sdk.AttackOutputs!.Donors(host).Single(d => d.Output.SemanticId == "output/v1/projectile/las-58-talon");
        await w.SwapProjectileAsync("SMG-32 Reprimand", "primary", w.ClassicSource(host, talon.Output), keepValues: false);
        await w.SetStratagemAsync(sdk.Stratagems!.FieldInstances.Single(f => f.Target.Stratagem == "Resupply" && f.SemanticFieldId == "stratagem.cooldown").InstanceKey, "120");
        await w.SetEntityAsync(sdk.Entities!.Enemies!.FieldInstances.First(f => f.Editable && f.SemanticFieldId == "entity.health").InstanceKey, "1500");
        await w.AddCustomLuaAsync(); await w.SaveCustomLuaAsync(w.Project!.CustomLua!.Source + "hd2.mod():log('ünïcode · 中文 stays as written')\n");
        await w.SetModOptionsEnabledAsync(true);
        var english = await Build(w, e);
        var culture = CultureInfo.CurrentCulture;
        try
        {
            // Chinese UI, and a formatting culture with a decimal comma: nothing generated may change.
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            using (UiCulture.Use(Chinese))
            {
                var chinese = await Build(w, e);
                Assert.Equal(english.Lua, chinese.Lua); Assert.Equal(english.Json, chinese.Json); Assert.Equal(english.Zip, chinese.Zip);
                // The UI really was Chinese while building (build messages would be), yet no CJK text reached the project file.
                Assert.Equal("语言", new UiText()["Settings.Language.Title"]);
                Assert.DoesNotMatch(@"[一-鿿]", chinese.Json.Replace("中文 stays as written", ""));
            }
        }
        finally { CultureInfo.CurrentCulture = culture; }
        Assert.Contains("ünïcode · 中文 stays as written", english.Lua); Assert.Contains("value=312.75", english.Lua);
    }

    [Fact] public async Task An_older_project_opens_the_same_in_either_language()
    {
        using var e = new TestEnvironment(); var sdk = await SdkFixtures.Install(e, "0.27.0"); var w = e.Workspace();
        await w.CreateAsync(new("Old", "Tests", "mods/tests/old_language", "0.1.0"), sdk);
        await w.SetObjectScalarAsync("AR-23 Liberator", "primary", "projectile", null, "damage.standard_damage", "100", true);
        var english = await Build(w, e);
        using (UiCulture.Use(Chinese))
        {
            var chinese = await Build(w, e);
            Assert.Equal(english.Lua, chinese.Lua); Assert.Equal(english.Json, chinese.Json); Assert.Equal(english.Zip, chinese.Zip);
        }
        using var zip = new ZipArchive(new MemoryStream(english.Zip));
        Assert.NotNull(zip.GetEntry("src/addon.lua"));
    }
}

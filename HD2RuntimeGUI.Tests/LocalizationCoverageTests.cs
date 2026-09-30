using System.Text.RegularExpressions;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// Guards against English creeping back into the UI: Razor markup may not contain prose as literal text or in title / placeholder /
// aria-label / alt attributes, and C# expressions in markup may not return sentence-like literals. Technical tokens (product names,
// file names, Lua, symbols) are allowed; everything a user reads goes through the resources (docs/localization.md).
public sealed class LocalizationCoverageTests
{
    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "HD2RuntimeGUI", "Components"))) return dir.FullName;
        throw new DirectoryNotFoundException("Repository root not found.");
    }
    // Words that are names or technical tokens in every language.
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "HD2Runtime", "ModBuilder", "HD2", "Runtime", "Bingus", "SDK", "API", "Lua", "ZIP", "JSON", "ID", "IDs", "RPM", "UI", "GUI", "HD2SNAP",
        "Ctrl", "Space", "Tab", "Enter", "addon", "lua", "hd2", "src", "px", "ms", "s", "x", "n", "nil", "true", "false", "vs",
        "SHA", "SHA-256", "dll", "NET", "MAUI", "Blazor", "Hybrid",
    };
    // Literal values that are not UI text: example input in placeholders (SDK names, paths and ids the user would type, which only
    // match untranslated) and logic keys compared against SDK or section values (their display text comes from resources).
    private static readonly HashSet<string> Exempt = new(StringComparer.Ordinal)
    {
        "EAT-700, Talon, arc…", "head, leg, zone_3…", "Hellbomb, Eruptor, fire…", "mods/your_name/my_mod", @"…\steamapps\common\Helldivers 2\data",
        "Gameplay proven", "Live ownership proven", "Eagle Shared System", "Ammo / Magazine", "Heat / Heatsink", "Status Effects", "Advanced / Read-only",
    };
    // Identifiers and paths (hd2.actions.heal, allow_shared=true, zone_3, game.dll, mods/a/b) are never prose.
    private static readonly Regex Identifier = new(@"\S*\w[._/\\=]\w\S*");
    private static readonly Regex Word = new(@"[A-Za-z][A-Za-z0-9'’\-]*[A-Za-z0-9]|[A-Za-z]");
    private static bool Prose(string text)
    {
        if (Exempt.Contains(text.Trim())) return false;
        var decoded = Identifier.Replace(System.Net.WebUtility.HtmlDecode(text), " ");
        return Word.Matches(decoded).Any(m => m.Value.Length > 1 && !Allowed.Contains(m.Value) && !Allowed.Contains(m.Value.Trim('\'', '’')));
    }
    private static string Strip(string razor)
    {
        razor = Regex.Replace(razor, @"@\*.*?\*@", m => new string('\n', m.Value.Count(c => c == '\n')), RegexOptions.Singleline);
        var code = Regex.Match(razor, @"^@code\s*\{", RegexOptions.Multiline);
        if (code.Success) razor = razor[..code.Index];
        razor = Regex.Replace(razor, @"<code>.*?</code>", m => new string('\n', m.Value.Count(c => c == '\n')), RegexOptions.Singleline);
        return razor;
    }
    private static int Line(string text, int index) => text[..index].Count(c => c == '\n') + 1;

    [Fact] public void Razor_markup_has_no_hard_coded_English()
    {
        var findings = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Root(), "HD2RuntimeGUI", "Components"), "*.razor", SearchOption.AllDirectories))
        {
            var name = Path.GetRelativePath(Path.Combine(Root(), "HD2RuntimeGUI", "Components"), file);
            var text = Strip(File.ReadAllText(file));
            // Text nodes: after the end of an HTML tag (not a C# comparison or lambda), up to the next tag / Razor expression.
            // (A candidate holding a raw quote or starting with ';' is C# inside an attribute expression or after a component tag.)
            foreach (Match m in Regex.Matches(text, @"(?<=<[A-Za-z][^<>]*)(?<![=\-])>([^<>@{}]+)(?=<|@|\{|\})"))
                if (!m.Groups[1].Value.Contains('"') && !m.Groups[1].Value.TrimStart().StartsWith(';') && Prose(m.Groups[1].Value)) findings.Add($"{name}:{Line(text, m.Index)} text \"{m.Groups[1].Value.Trim()}\"");
            // Prose attributes with literal (non-@) values.
            foreach (Match m in Regex.Matches(text, @"\b(title|placeholder|aria-label|alt)=""([^""@]*)"""))
                if (Prose(m.Groups[2].Value)) findings.Add($"{name}:{Line(text, m.Index)} {m.Groups[1].Value}=\"{m.Groups[2].Value}\"");
            // Sentence-like C# literals inside markup expressions: a capitalised word followed by more words.
            foreach (Match m in Regex.Matches(text, @"""([A-Z][a-z]+(?:[ ,'’\-][A-Za-z0-9…·→%()/:]+)+[.!?…]?)"""))
                if (Prose(m.Groups[1].Value) && !Regex.IsMatch(m.Groups[1].Value, @"^[A-Z][A-Za-z0-9]*(\.[A-Za-z0-9]+)+$"))
                    findings.Add($"{name}:{Line(text, m.Index)} literal \"{m.Groups[1].Value}\"");
        }
        Assert.True(findings.Count == 0, $"{findings.Count} hard-coded UI strings (use T[...] resources, see docs/localization.md):\n" + string.Join("\n", findings));
    }

    // @code blocks: sentences (three or more words, capitalised) are UI text too, e.g. messages a component stores and shows later.
    [Fact] public void Component_code_has_no_hard_coded_English_sentences()
    {
        var findings = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Root(), "HD2RuntimeGUI", "Components"), "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".razor", StringComparison.Ordinal) || f.EndsWith(".cs", StringComparison.Ordinal)))
        {
            var text = File.ReadAllText(file);
            var start = file.EndsWith(".razor", StringComparison.Ordinal) ? Regex.Match(text, @"^@code\s*\{", RegexOptions.Multiline) : null;
            if (start is { Success: false }) continue;
            var code = start == null ? text : text[start.Index..];
            code = Regex.Replace(code, @"//[^\n]*", ""); code = Regex.Replace(code, @"/\*.*?\*/", "", RegexOptions.Singleline);
            foreach (Match m in Regex.Matches(code, @"(?<![\w@])""([A-Z][a-z]+(?: [A-Za-z0-9'’,:()/…·→\-]+){2,}[.!?…:]?)"""))
                if (Prose(m.Groups[1].Value)) findings.Add($"{Path.GetFileName(file)}: \"{m.Groups[1].Value}\"");
        }
        Assert.True(findings.Count == 0, $"{findings.Count} hard-coded sentences in component code:\n" + string.Join("\n", findings));
    }
}

using System.Text.RegularExpressions;

namespace HD2RuntimeGUI.Core.Scripting;

// Autocomplete data read from the SDK's generated LuaLS stub (stubs/mods/skyeshade/hd2runtime.lua): the hd2 table, every annotated
// class with its fields and functions (inherited members included: an event payload class also has the common HD2Event fields), the
// sub-tables the stub declares on hd2 (hd2.diagnostics typed by ---@type, hd2.compatibility as a plain table), and every string alias
// (event, explosion, status, weapon, attack output names ...). Only annotations are read; the stub is never executed.
// Param is the declared type of a function's first parameter (an alias name or inline string literals), which the editor completes.
public sealed record LuaMember(string Name, string Kind, string? Signature, string? Type, string? Doc, string? Param = null);
public sealed class LuaApiIndex
{
    public const string StubPath = "stubs/mods/skyeshade/hd2runtime.lua", CacheName = "hd2runtime-stubs.lua";
    public const int MaxBytes = 4 * 1024 * 1024;
    public required IReadOnlyDictionary<string, LuaMember[]> Classes { get; init; }
    public required IReadOnlyDictionary<string, string[]> Aliases { get; init; }
    public required string RootClass { get; init; }
    // The doc comment above each ---@class line, where the stub has one.
    public IReadOnlyDictionary<string, string> ClassDocs { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<LuaMember> Members(string type) => Classes.GetValueOrDefault(type) ?? [];
    public IReadOnlyList<string> Alias(string name) => Aliases.GetValueOrDefault(name) ?? [];
    public string? ClassDoc(string type) => ClassDocs.GetValueOrDefault(type);
    // The first non-nil class of a declared type ("HD2Explosion|nil" → HD2Explosion).
    public static string? FirstType(string? type) => type?.Split('|').Select(t => t.Trim().TrimEnd('?')).FirstOrDefault(t => t.Length > 0 && t != "nil");
    /// <summary>A member by its call path: "hd2.diagnostics.write_conflicts" walks the hd2 table's fields and sub-tables;
    /// "HD2Subscription:describe" or "HD2Events.status" starts at a class. Null when the stub does not publish it.</summary>
    public LuaMember? Resolve(string path)
    {
        var parts = path.Split('.', ':');
        if (parts.Length < 2) return null;
        var type = parts[0] == "hd2" ? RootClass : parts[0];
        LuaMember? member = null;
        for (var i = 1; i < parts.Length; i++)
        {
            if (type == null || !Classes.ContainsKey(type)) return null;
            member = Members(type).FirstOrDefault(m => m.Name == parts[i]);
            if (member == null) return null;
            type = FirstType(member.Type);
        }
        return member;
    }
}

public static class LuaStubReader
{
    private static readonly Regex ClassLine = new(@"^---@class\s+([A-Za-z_][\w.]*)(?:\s*:\s*([A-Za-z_][\w.]*(?:\s*,\s*[A-Za-z_][\w.]*)*))?", RegexOptions.CultureInvariant);
    private static readonly Regex FieldLine = new(@"^---@field\s+([A-Za-z_]\w*)\??\s+(.+)$", RegexOptions.CultureInvariant);
    private static readonly Regex ParamLine = new(@"^---@param\s+([A-Za-z_]\w*|\.\.\.)\??\s+(.+?)\s*$", RegexOptions.CultureInvariant);
    private static readonly Regex ReturnLine = new(@"^---@return\s+(.+)$", RegexOptions.CultureInvariant);
    private static readonly Regex TypeLine = new(@"^---@type\s+(\S+)", RegexOptions.CultureInvariant);
    private static readonly Regex AliasLine = new(@"^---@alias\s+([A-Za-z_]\w*)\s+(.*)$", RegexOptions.CultureInvariant);
    private static readonly Regex Literal = new("\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.CultureInvariant);
    private static readonly Regex FunctionLine = new(@"^function\s+([A-Za-z_][\w.]*)([.:])([A-Za-z_]\w*)\s*\(([^)]*)\)", RegexOptions.CultureInvariant);
    private static readonly Regex RootLine = new(@"^local\s+hd2\s*=\s*\{\s*\}", RegexOptions.CultureInvariant);
    private static readonly Regex TableLine = new(@"^(hd2(?:\.[A-Za-z_]\w*)+)\s*=\s*\{\s*\}", RegexOptions.CultureInvariant);

    public static LuaApiIndex Read(string text)
    {
        var classes = new Dictionary<string, List<LuaMember>>(StringComparer.Ordinal);
        var parents = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var classDocs = new Dictionary<string, string>(StringComparer.Ordinal);
        var aliases = new Dictionary<string, string[]>(StringComparer.Ordinal);
        // hd2 sub-tables ("hd2.diagnostics") and the class each one is.
        var tables = new Dictionary<string, string>(StringComparer.Ordinal);
        string? current = null, root = null, declared = null;
        var doc = new List<string>(); var parameters = new List<(string Name, string Type)>(); var returns = new List<string>();
        List<LuaMember> Class(string name) => classes.TryGetValue(name, out var list) ? list : classes[name] = [];
        string? Doc() => doc.Count > 0 ? string.Join(" ", doc) : null;
        void Reset() { doc.Clear(); parameters.Clear(); returns.Clear(); declared = null; }
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();
            if (line.StartsWith("---@", StringComparison.Ordinal))
            {
                Match m;
                if ((m = ClassLine.Match(line)).Success)
                {
                    current = m.Groups[1].Value; Class(current);
                    if (m.Groups[2].Success) parents[current] = m.Groups[2].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    // The comment above a class describes the class, not its first field.
                    if (Doc() is { } classDoc) classDocs[current] = classDoc;
                    doc.Clear(); continue;
                }
                if ((m = FieldLine.Match(line)).Success && current != null)
                {
                    var (type, rest) = SplitType(m.Groups[2].Value);
                    Class(current).Add(new(m.Groups[1].Value, "field", null, type, rest.Length > 0 ? rest : Doc()));
                    doc.Clear(); continue;
                }
                if ((m = ParamLine.Match(line)).Success) { parameters.Add((m.Groups[1].Value, SplitType(m.Groups[2].Value).Type)); continue; }
                if ((m = ReturnLine.Match(line)).Success) { returns.AddRange(ReturnTypes(m.Groups[1].Value)); continue; }
                if ((m = TypeLine.Match(line)).Success) { declared = m.Groups[1].Value; continue; }
                if ((m = AliasLine.Match(line)).Success)
                {
                    var values = Literal.Matches(m.Groups[2].Value).Select(x => x.Groups[1].Value).Distinct(StringComparer.Ordinal).ToArray();
                    if (values.Length > 0) aliases[m.Groups[1].Value] = values;
                    continue;
                }
                continue; // @overload, @meta, ...
            }
            if (line.StartsWith("---", StringComparison.Ordinal)) { var d = line[3..].Trim(); if (d.Length > 0) doc.Add(d); continue; }
            if (RootLine.IsMatch(line)) { root = current; Reset(); continue; }
            var t = TableLine.Match(line);
            if (t.Success)
            {
                // hd2.x = {}: a field of the hd2 table (or of a sub-table), typed by the ---@type above it or a class of its own.
                var path = t.Groups[1].Value; var cut = path.LastIndexOf('.');
                var owner = path[..cut] == "hd2" ? root ?? "hd2" : tables.GetValueOrDefault(path[..cut]) ?? path[..cut];
                var type = declared ?? path; Class(type);
                if (declared == null && Doc() is { } tableDoc) classDocs[type] = tableDoc;
                tables[path] = type;
                Class(owner).RemoveAll(x => x.Name == path[(cut + 1)..]);
                Class(owner).Add(new(path[(cut + 1)..], "field", null, type, Doc()));
                Reset(); continue;
            }
            var f = FunctionLine.Match(line);
            if (f.Success)
            {
                var target = f.Groups[1].Value;
                var owner = target == "hd2" ? root ?? "hd2" : tables.GetValueOrDefault(target) ?? target;
                var names = f.Groups[4].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var args = string.Join(", ", names.Select(a => parameters.FirstOrDefault(p => p.Name == a) is { Name: not null } p ? a + ": " + p.Type : a));
                var first = names.Length > 0 ? parameters.FirstOrDefault(p => p.Name == names[0]).Type : null;
                var name = f.Groups[3].Value;
                Class(owner).RemoveAll(x => x.Name == name && x.Kind != "field");
                Class(owner).Add(new(name, f.Groups[2].Value == ":" ? "method" : "function", name + "(" + args + ")" + (returns.Count > 0 ? " → " + string.Join(", ", returns) : ""),
                    returns.FirstOrDefault(), Doc(), first));
                Reset(); continue;
            }
            if (line.Length > 0 && !line.StartsWith("--", StringComparison.Ordinal)) Reset();
        }
        if (root == null) throw new InvalidDataException("The SDK Lua stub does not declare the hd2 table.");
        // Inherited members (---@class Child : Parent), the child's own declaration winning.
        IEnumerable<LuaMember> All(string name, HashSet<string> seen)
        {
            if (!seen.Add(name) || !classes.TryGetValue(name, out var own)) return [];
            return own.Concat(parents.GetValueOrDefault(name, []).SelectMany(p => All(p, seen)));
        }
        return new()
        {
            Classes = classes.Keys.ToDictionary(name => name, name => All(name, new(StringComparer.Ordinal)).GroupBy(m => m.Name).Select(g => g.First())
                .OrderBy(m => m.Name, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal),
            Aliases = aliases, RootClass = root, ClassDocs = classDocs,
        };
    }
    // A LuaLS type expression and the text after it: "fun(binding: table)|nil Called on press" → ("fun(binding: table)|nil", "Called on press").
    // Brackets, braces, parentheses and quoted literals may contain spaces (so may a function type's ": result"); the type ends at the
    // first other space outside them.
    private static (string Type, string After) SplitType(string text)
    {
        var depth = 0; var quote = '\0';
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quote != '\0') { if (c == '\\') i++; else if (c == quote) quote = '\0'; continue; }
            if (c is '"' or '\'') quote = c;
            else if (c is '(' or '{' or '[' or '<') depth++;
            else if (c is ')' or '}' or ']' or '>') depth = Math.Max(0, depth - 1);
            else if (char.IsWhiteSpace(c) && depth == 0 && !(i > 0 && text[i - 1] is ':' or '|')) return (text[..i], text[(i + 1)..].Trim());
        }
        return (text.Trim(), "");
    }
    // "integer, boolean" / "HD2Explosion|nil, string|nil" / "string? reason" → the returned types, without their names or comments.
    private static IEnumerable<string> ReturnTypes(string text)
    {
        var rest = text.Trim();
        while (rest.Length > 0)
        {
            var (type, after) = SplitType(rest);
            var more = type.EndsWith(',');
            yield return type.TrimEnd(',');
            if (!more && !after.StartsWith(',')) yield break;
            rest = after.TrimStart(',').Trim();
        }
    }
}

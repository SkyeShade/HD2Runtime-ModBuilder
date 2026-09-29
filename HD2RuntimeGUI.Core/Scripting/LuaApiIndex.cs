using System.Text.RegularExpressions;

namespace HD2RuntimeGUI.Core.Scripting;

// Autocomplete data read from the SDK's generated LuaLS stub (stubs/mods/skyeshade/hd2runtime.lua): the hd2 table, every annotated
// class with its fields and functions, and every string alias (event, explosion, status, weapon names ...). Only annotations are read;
// the stub is never executed.
public sealed record LuaMember(string Name, string Kind, string? Signature, string? Type, string? Doc);
public sealed class LuaApiIndex
{
    public const string StubPath = "stubs/mods/skyeshade/hd2runtime.lua", CacheName = "hd2runtime-stubs.lua";
    public const int MaxBytes = 4 * 1024 * 1024;
    public required IReadOnlyDictionary<string, LuaMember[]> Classes { get; init; }
    public required IReadOnlyDictionary<string, string[]> Aliases { get; init; }
    public required string RootClass { get; init; }
    public IReadOnlyList<LuaMember> Members(string type) => Classes.GetValueOrDefault(type) ?? [];
    public IReadOnlyList<string> Alias(string name) => Aliases.GetValueOrDefault(name) ?? [];
}

public static class LuaStubReader
{
    private static readonly Regex ClassLine = new(@"^---@class\s+([A-Za-z_][\w.]*)", RegexOptions.CultureInvariant);
    private static readonly Regex FieldLine = new(@"^---@field\s+([A-Za-z_]\w*)\??\s+(\S+)\s*(.*)$", RegexOptions.CultureInvariant);
    private static readonly Regex ParamLine = new(@"^---@param\s+([A-Za-z_]\w*|\.\.\.)\??\s+(.+?)\s*$", RegexOptions.CultureInvariant);
    private static readonly Regex ReturnLine = new(@"^---@return\s+(\S+)", RegexOptions.CultureInvariant);
    private static readonly Regex AliasLine = new(@"^---@alias\s+([A-Za-z_]\w*)\s+(.*)$", RegexOptions.CultureInvariant);
    private static readonly Regex Literal = new("\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.CultureInvariant);
    private static readonly Regex FunctionLine = new(@"^function\s+([A-Za-z_]\w*)([.:])([A-Za-z_]\w*)\s*\(([^)]*)\)", RegexOptions.CultureInvariant);
    private static readonly Regex RootLine = new(@"^local\s+hd2\s*=\s*\{\s*\}", RegexOptions.CultureInvariant);

    public static LuaApiIndex Read(string text)
    {
        var classes = new Dictionary<string, List<LuaMember>>(StringComparer.Ordinal);
        var aliases = new Dictionary<string, string[]>(StringComparer.Ordinal);
        string? current = null, root = null, returns = null;
        var doc = new List<string>(); var parameters = new List<(string Name, string Type)>();
        List<LuaMember> Class(string name) => classes.TryGetValue(name, out var list) ? list : classes[name] = [];
        void Reset() { doc.Clear(); parameters.Clear(); returns = null; }
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();
            if (line.StartsWith("---@", StringComparison.Ordinal))
            {
                Match m;
                if ((m = ClassLine.Match(line)).Success) { current = m.Groups[1].Value; Class(current); continue; }
                if ((m = FieldLine.Match(line)).Success && current != null)
                {
                    var docText = m.Groups[3].Value.Trim();
                    Class(current).Add(new(m.Groups[1].Value, "field", null, m.Groups[2].Value, docText.Length > 0 ? docText : doc.Count > 0 ? string.Join(" ", doc) : null));
                    doc.Clear(); continue;
                }
                if ((m = ParamLine.Match(line)).Success) { parameters.Add((m.Groups[1].Value, m.Groups[2].Value)); continue; }
                if ((m = ReturnLine.Match(line)).Success) { returns ??= m.Groups[1].Value; continue; }
                if ((m = AliasLine.Match(line)).Success)
                {
                    var values = Literal.Matches(m.Groups[2].Value).Select(x => x.Groups[1].Value).Distinct(StringComparer.Ordinal).ToArray();
                    if (values.Length > 0) aliases[m.Groups[1].Value] = values;
                    continue;
                }
                continue; // @overload, @meta, @type, ...
            }
            if (line.StartsWith("---", StringComparison.Ordinal)) { var d = line[3..].Trim(); if (d.Length > 0) doc.Add(d); continue; }
            if (RootLine.IsMatch(line)) { root = current; Reset(); continue; }
            var f = FunctionLine.Match(line);
            if (f.Success)
            {
                var owner = f.Groups[1].Value == "hd2" ? root ?? "hd2" : f.Groups[1].Value;
                var args = string.Join(", ", f.Groups[4].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(a => parameters.FirstOrDefault(p => p.Name == a) is { Name: not null } p ? a + ": " + p.Type : a));
                var name = f.Groups[3].Value;
                Class(owner).RemoveAll(x => x.Name == name && x.Kind != "field");
                Class(owner).Add(new(name, f.Groups[2].Value == ":" ? "method" : "function", name + "(" + args + ")" + (returns != null ? " → " + returns : ""), returns,
                    doc.Count > 0 ? string.Join(" ", doc) : null));
                Reset(); continue;
            }
            if (line.Length > 0 && !line.StartsWith("--", StringComparison.Ordinal)) Reset();
        }
        if (root == null) throw new InvalidDataException("The SDK Lua stub does not declare the hd2 table.");
        return new()
        {
            Classes = classes.ToDictionary(p => p.Key, p => p.Value.GroupBy(m => m.Name).Select(g => g.Last()).OrderBy(m => m.Name, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal),
            Aliases = aliases, RootClass = root,
        };
    }
}

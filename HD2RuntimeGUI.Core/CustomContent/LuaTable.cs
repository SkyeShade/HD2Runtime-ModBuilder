using System.Globalization;
using System.Text;

namespace HD2RuntimeGUI.Core.CustomContent;

/// <summary>Readable generated Lua for custom content: one key per line, nested tables indented four spaces, short lists of plain values
/// on one line, double-quoted strings, invariant numbers. Keys keep the order they were added in, so the output is deterministic.</summary>
public sealed class LuaTable
{
    private readonly List<(string Key, object Value)> entries = [];
    public bool IsEmpty => entries.Count == 0;
    /// <summary>Adds key = value; a null value is skipped. Values: string, bool, int, long, double, LuaTable, LuaList, LuaCode.</summary>
    public LuaTable Add(string key, object? value)
    {
        if (value != null && !(value is LuaTable { IsEmpty: true }) && !(value is LuaList { IsEmpty: true })) entries.Add((key, value));
        return this;
    }
    public string Write(int indent = 0)
    {
        var builder = new StringBuilder("{\n");
        foreach (var (key, value) in entries)
            builder.Append(' ', (indent + 1) * 4).Append(Key(key)).Append(" = ").Append(Value(value, indent + 1)).Append(",\n");
        return builder.Append(' ', indent * 4).Append('}').ToString();
    }
    /// <summary>The same table as JSON (Lua code as its text), for comparing what a registration says with other descriptions of it.</summary>
    public System.Text.Json.Nodes.JsonObject ToJson()
    {
        var json = new System.Text.Json.Nodes.JsonObject();
        foreach (var (key, value) in entries) json[key] = Json(value);
        return json;
    }
    internal static System.Text.Json.Nodes.JsonNode? Json(object value) => value switch
    {
        string s => s,
        bool b => b,
        int i => i,
        long l => l,
        double d => d,
        LuaTable t => t.ToJson(),
        LuaList l => l.ToJson(),
        LuaCode c => c.Write(0),
        _ => throw new ArgumentException("Unsupported Lua value: " + value.GetType().Name),
    };
    internal static string Value(object value, int indent) => value switch
    {
        string s => Quote(s),
        bool b => b ? "true" : "false",
        int i => i.ToString(CultureInfo.InvariantCulture),
        long l => l.ToString(CultureInfo.InvariantCulture),
        double d => Number(d),
        LuaTable t => t.Write(indent),
        LuaList l => l.Write(indent),
        LuaCode c => c.Write(indent),
        _ => throw new ArgumentException("Unsupported Lua value: " + value.GetType().Name),
    };
    public static string Number(double value)
    {
        if (!double.IsFinite(value)) throw new ArgumentException("Lua numbers must be finite.");
        if (value == Math.Floor(value) && Math.Abs(value) < 1e15) return ((long)value).ToString(CultureInfo.InvariantCulture);
        return value.ToString("R", CultureInfo.InvariantCulture);
    }
    private static string Key(string key) => System.Text.RegularExpressions.Regex.IsMatch(key, @"\A[A-Za-z_][A-Za-z0-9_]*\z") ? key : "[" + Quote(key) + "]";
    /// <summary>A double-quoted Lua string literal (control characters as decimal escapes).</summary>
    public static string Quote(string value)
    {
        var builder = new StringBuilder("\"");
        foreach (var c in value)
            builder.Append(c switch
            {
                '\\' => "\\\\", '"' => "\\\"", '\n' => "\\n", '\r' => "\\r", '\t' => "\\t",
                _ when char.IsControl(c) => "\\" + ((int)c).ToString("D3", CultureInfo.InvariantCulture),
                _ => c.ToString(),
            });
        return builder.Append('"').ToString();
    }
}

/// <summary>A Lua list. Up to eight plain values stay on one line; tables go one per line.</summary>
public sealed class LuaList(IEnumerable<object> items)
{
    private readonly List<object> items = items.ToList();
    public bool IsEmpty => items.Count == 0;
    public System.Text.Json.Nodes.JsonArray ToJson() => new([.. items.Select(LuaTable.Json)]);
    public string Write(int indent)
    {
        if (items.All(i => i is string or int or long or double or bool) && items.Count <= 8)
            return "{ " + string.Join(", ", items.Select(i => LuaTable.Value(i, indent))) + " }";
        var builder = new StringBuilder("{\n");
        foreach (var item in items) builder.Append(' ', (indent + 1) * 4).Append(LuaTable.Value(item, indent + 1)).Append(",\n");
        return builder.Append(' ', indent * 4).Append('}').ToString();
    }
}

/// <summary>Lua code written as is: an expression (hd2.resources.image("x")), or a callback function around a hand-written body. The body's
/// lines keep their own text and relative indentation and are indented with the surrounding code (so a multi-line long string inside a
/// callback gains that indentation; callbacks are short code, never data).</summary>
public sealed class LuaCode(string code, bool function = false)
{
    public string Write(int indent)
    {
        if (!function) return code;
        var pad = new string(' ', (indent + 1) * 4);
        var body = code.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n').Split('\n').Select(l => l.Length == 0 ? "" : pad + l);
        return "function(ctx)\n" + string.Join("\n", body) + "\n" + new string(' ', indent * 4) + "end";
    }
}

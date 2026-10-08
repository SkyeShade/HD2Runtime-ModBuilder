using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace HD2RuntimeGUI.Core.CustomContent;

/// <summary>Internal ids of custom objects. A custom object's id is its identity (Runtime registries, multiplayer agreement, asset
/// names): it is suggested once from the first name, can be changed explicitly, and is never derived from a display name again.</summary>
public static class CustomIds
{
    public const int MaxLength = 48;
    public const int MaxImageId = 64;
    private static readonly Regex Pattern = new(@"\A[a-z][a-z0-9_]*\z", RegexOptions.CultureInvariant);
    private static readonly Regex ImagePattern = new(@"\A[a-z0-9_]{1,64}\z", RegexOptions.CultureInvariant);

    /// <summary>a-z, 0-9 and _, starting with a letter, at most 48 characters (Runtime's custom stratagem id rule).</summary>
    public static bool Valid(string? id) => id != null && id.Length <= MaxLength && Pattern.IsMatch(id);
    public static bool ValidImage(string? id) => id != null && ImagePattern.IsMatch(id);

    /// <summary>An id suggested from a name: lowercase letters and digits of each word (a hyphenated designation stays one word, so
    /// "EAT-17G Gas Expendable Anti-Tank" starts "eat17g_gas"), words joined by '_' up to 24 characters, made unique with a suffix.
    /// Null when the name has no letter or digit.</summary>
    public static string? Suggest(string? name, IEnumerable<string> taken, string fallback = "custom")
    {
        var words = new List<string>();
        foreach (var word in (name ?? "").Normalize(NormalizationForm.FormD).Split([' ', '\t', '/', '.', ',', '(', ')', '[', ']', ':', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            var builder = new StringBuilder();
            foreach (var c in word)
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsAsciiLetterOrDigit(c)) builder.Append(char.ToLowerInvariant(c));
            if (builder.Length > 0) words.Add(builder.ToString());
        }
        if (words.Count == 0) return null;
        var id = words[0];
        foreach (var word in words.Skip(1))
        {
            if (id.Length + 1 + word.Length > 24) break;
            id += "_" + word;
        }
        if (id.Length > 32) id = id[..32];
        if (!char.IsAsciiLetter(id[0])) id = fallback + "_" + id;
        return Unique(id, taken);
    }
    /// <summary>The id itself when free, else id_2, id_3, … (within MaxLength).</summary>
    public static string Unique(string id, IEnumerable<string> taken)
    {
        var used = new HashSet<string>(taken, StringComparer.Ordinal);
        if (!used.Contains(id)) return id;
        for (var n = 2; ; n++)
        {
            var suffix = "_" + n.ToString(CultureInfo.InvariantCulture);
            var candidate = (id.Length + suffix.Length > MaxLength ? id[..(MaxLength - suffix.Length)] : id) + suffix;
            if (!used.Contains(candidate)) return candidate;
        }
    }

    /// <summary>A call-in code like the given one that neither equals, starts nor is started by any of the others (Runtime's collision rule),
    /// nor is refused by refused (the native codes): the code itself when it is free, else the same length with another last input, else the
    /// first free code of that length and longer (up to max inputs). Null when every code up to max is taken.</summary>
    public static List<string>? UniqueCode(IReadOnlyList<string> code, IEnumerable<IReadOnlyList<string>> others, Func<IReadOnlyList<string>, bool>? refused = null, int max = 8)
    {
        string[] directions = ["up", "down", "left", "right"];
        var taken = others.Where(o => o.Count > 0).Select(o => o.ToArray()).ToArray();
        bool Free(IReadOnlyList<string> c) => c.Count > 0 && taken.All(o => !o.Take(c.Count).SequenceEqual(c.Take(o.Length))) && refused?.Invoke(c) != true;
        if (Free(code)) return [.. code];
        if (code.Count > 0)
            foreach (var d in directions) { var c = code.Take(code.Count - 1).Append(d).ToList(); if (Free(c)) return c; }
        for (var length = Math.Max(code.Count, 1); length <= max; length++)
        {
            var c = new int[length];
            while (true)
            {
                var candidate = c.Select(i => directions[i]).ToList();
                if (Free(candidate)) return candidate;
                var k = length - 1;
                while (k >= 0 && ++c[k] == directions.Length) c[k--] = 0;
                if (k < 0) break;
            }
        }
        return null;
    }

    /// <summary>The id a custom projectile has inside the Runtime: Runtime projectile definitions are one registry per game session, so
    /// the id is qualified by the mod's resource ("author/mod/id"). Deterministic; a resource too long for Runtime's 96 characters is
    /// replaced by a digest of it.</summary>
    public static string RuntimeProjectileId(string resource, string id)
    {
        var scope = resource.StartsWith("mods/", StringComparison.Ordinal) ? resource["mods/".Length..] : resource;
        var full = scope + "/" + id;
        if (full.Length <= 96 && Regex.IsMatch(full, @"\A[A-Za-z0-9_\-]+/[A-Za-z0-9_\-/]+\z")) return full;
        return "mb_" + Generation.SupportChangeService.Hash(resource)[..16] + "/" + id;
    }
}

using System.Text.RegularExpressions;

namespace HD2RuntimeGUI.Core.Models;

public sealed record SemVersion(int Major, int Minor, int Patch, string? Prerelease = null) : IComparable<SemVersion>
{
    public static SemVersion Parse(string value)
    {
        if (value.Length > 128) throw new FormatException("Version is too long.");
        var m = Regex.Match(value, @"\A(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\z", RegexOptions.CultureInvariant);
        if (!m.Success) throw new FormatException("Use a semantic version such as 0.1.0.");
        string? pre = m.Groups[4].Success ? m.Groups[4].Value : null;
        if (pre?.Split('.').Any(p => p.All(char.IsAsciiDigit) && p.Length > 1 && p[0] == '0') == true)
            throw new FormatException("Numeric prerelease identifiers cannot have leading zeroes.");
        return new(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), pre);
    }

    public int CompareTo(SemVersion? other)
    {
        if (other is null) return 1;
        foreach (var comparison in new[] { Major.CompareTo(other.Major), Minor.CompareTo(other.Minor), Patch.CompareTo(other.Patch) })
            if (comparison != 0) return comparison;
        if (Prerelease == other.Prerelease) return 0;
        if (Prerelease is null) return 1;
        if (other.Prerelease is null) return -1;
        var left = Prerelease.Split('.'); var right = other.Prerelease.Split('.');
        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            bool ln = left[i].All(char.IsAsciiDigit), rn = right[i].All(char.IsAsciiDigit);
            int c = ln && rn ? (left[i].Length == right[i].Length ? string.CompareOrdinal(left[i], right[i]) : left[i].Length.CompareTo(right[i].Length))
                : ln != rn ? (ln ? -1 : 1) : string.CompareOrdinal(left[i], right[i]);
            if (c != 0) return c;
        }
        return left.Length.CompareTo(right.Length);
    }
    public override string ToString() => $"{Major}.{Minor}.{Patch}" + (Prerelease is null ? "" : "-" + Prerelease);
}

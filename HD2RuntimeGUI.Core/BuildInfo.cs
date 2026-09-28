using System.Reflection;

namespace HD2RuntimeGUI.Core;

/// <summary>Application identity shared by networking, exports and the UI.</summary>
public static class BuildInfo
{
    public static string Version =>
        typeof(BuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?.Split('+', 2)[0] ?? "0.4.0";

    /// <summary>Git commit the build was compiled from, with ".dirty" when the working tree had uncommitted changes; null if unknown.</summary>
    public static string? Commit =>
        typeof(BuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?.Split('+', 2) is [_, var revision] && revision.Length > 0 ? revision : null;

    /// <summary>Version plus short commit, so builds that share a version number can be told apart.</summary>
    public static string Identity => Commit is { } c ? $"{Version} ({(c.Length >= 40 ? c[..12] + c[40..] : c)})" : Version;

    /// <summary>Public product name. Technical identifiers (assembly, AppData folder, user agent) keep "HD2RuntimeGUI".</summary>
    public const string ProductName = "HD2Runtime ModBuilder";

    public static string UserAgent => $"HD2RuntimeGUI/{Version}";
}

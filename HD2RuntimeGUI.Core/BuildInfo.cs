using System.Reflection;

namespace HD2RuntimeGUI.Core;

/// <summary>Application identity shared by networking, exports and the UI.</summary>
public static class BuildInfo
{
    public static string Version =>
        typeof(BuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?.Split('+', 2)[0] ?? "1.4.3";

    /// <summary>Git commit the build was compiled from, with ".dirty" when the working tree had uncommitted changes; null if unknown.</summary>
    public static string? Commit =>
        typeof(BuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?.Split('+', 2) is [_, var revision] && revision.Length > 0 ? revision : null;

    /// <summary>Version plus short commit, so builds that share a version number can be told apart.</summary>
    public static string Identity => Commit is { } c ? $"{Version} ({(c.Length >= 40 ? c[..12] + c[40..] : c)})" : Version;

    /// <summary>Public product name. Namespaces, ApplicationId and the AppData folder keep "HD2RuntimeGUI"; the executable is HD2RuntimeModBuilder.exe since 1.0.1.</summary>
    public const string ProductName = "HD2Runtime ModBuilder";

    /// <summary>The application's own GitHub repository (source, releases and ModBuilder updates).</summary>
    public const string Repository = "SkyeShade/HD2Runtime-ModBuilder";
    public const string RepositoryUrl = "https://github.com/" + Repository;

    public static string UserAgent => $"HD2Runtime-ModBuilder/{Version}";
}

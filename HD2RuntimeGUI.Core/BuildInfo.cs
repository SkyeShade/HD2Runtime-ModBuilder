using System.Reflection;

namespace HD2RuntimeGUI.Core;

/// <summary>Application identity shared by networking, exports and the UI.</summary>
public static class BuildInfo
{
    public static string Version =>
        typeof(BuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?.Split('+', 2)[0] ?? "0.4.0";

    public static string UserAgent => $"HD2RuntimeGUI/{Version}";
}

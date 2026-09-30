using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Metadata;

/// <summary>
/// The newest HD2Runtime SDK this ModBuilder release was built and validated against. Newer SDK releases are never downloaded,
/// offered or installed by this version: their capability metadata may change meaning without changing shape, so they need a
/// newer ModBuilder. Older SDKs remain fully supported (projects stay pinned to the SDK they were created with).
/// </summary>
public static class SdkCompatibility
{
    public const string NewestSupportedVersion = "0.28.0";
    public static SemVersion NewestSupported { get; } = SemVersion.Parse(NewestSupportedVersion);
    public static bool IsSupported(string version) => SemVersion.Parse(version).CompareTo(NewestSupported) <= 0;
}

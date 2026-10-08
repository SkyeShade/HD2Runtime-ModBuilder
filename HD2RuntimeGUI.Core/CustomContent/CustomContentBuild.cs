using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.CustomContent;

/// <summary>How the build uses custom content: whether a project has any, compiling it through the current target (refused while it has
/// errors, with the first error and its owner; the editor shows them all), and the HD2Runtime version an export then requires (the
/// target's, when it is newer than the bound SDK's).</summary>
public static class CustomContentBuild
{
    public static bool Has(ModProject project) => project.CustomContent is { IsEmpty: false };
    public static CompiledCustomContent? Compile(ModProject project, SdkMetadata sdk, bool images, ICustomContentTarget? target = null)
    {
        if (!Has(project)) return null;
        target ??= CustomContentTargets.Current;
        var errors = CustomContentValidator.Errors(CustomContentValidator.Validate(project, target, sdk)).ToArray();
        if (errors.Length > 0) throw new InvalidDataException(Blocked(errors));
        return target.Compile(project.CustomContent!, project, sdk, images);
    }
    public static string Blocked(IReadOnlyList<CustomDiagnostic> errors) => errors.Count == 1
        ? CoreText.Format("CustomContent.Build.BlockedSingle", errors[0].Message, OwnerId(errors[0].Owner))
        : CoreText.Format("CustomContent.Build.BlockedSeveral", errors.Count, errors[0].Message, OwnerId(errors[0].Owner));
    public static string OwnerId(string owner) => owner.Contains(':') ? owner[(owner.IndexOf(':') + 1)..] : owner;
    /// <summary>The minimum HD2Runtime an export of this project declares: the bound SDK's, or the custom content target's when the project
    /// has custom content and that is newer (0.30.0 for the 0.30.0 custom stratagem API).</summary>
    public static string RequiredRuntime(ModProject project, SdkMetadata sdk, ICustomContentTarget? target = null)
    {
        if (!Has(project)) return sdk.Version;
        var minimum = (target ?? CustomContentTargets.Current).MinimumRuntime;
        return SemVersion.Parse(minimum).CompareTo(SemVersion.Parse(sdk.Version)) > 0 ? minimum : sdk.Version;
    }
}

using System.Text.Json.Nodes;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.CustomContent;

/// <summary>Versioned migrations of a project's custom content section (customContent.version). They run on the project's JSON when it is
/// opened or imported, before it is read, so a section saved by an earlier ModBuilder is brought up to the current model step by step and
/// never thrown away. A section from a newer ModBuilder is refused (an older one would drop what it does not know). Runtime API changes
/// normally need no step at all, only a target adapter update: a step is for a change of the semantic model itself.</summary>
public static class CustomContentMigrations
{
    public sealed record Step(int From, string Description, Action<JsonObject> Apply);
    // Version 1 (ModBuilder 1.7.0) is the first custom content model, so there is no step yet. A step moves a section from From to From + 1.
    public static readonly IReadOnlyList<Step> Steps = [];

    /// <summary>Brings project.customContent up to CustomContentSection.CurrentVersion. Returns how many steps ran (0: already current or no
    /// custom content). Throws InvalidDataException for a missing, unknown or newer version.</summary>
    public static int Migrate(JsonObject project) => Migrate(project, Steps, CustomContentSection.CurrentVersion);
    public static int Migrate(JsonObject project, IReadOnlyList<Step> steps, int current)
    {
        if (project["customContent"] is not JsonObject section) return 0;
        int version;
        try { version = section["version"]?.GetValue<int>() ?? throw new InvalidDataException(CoreText.Get("CustomContent.Migration.NoVersion")); }
        catch (Exception e) when (e is FormatException or InvalidOperationException) { throw new InvalidDataException(CoreText.Get("CustomContent.Migration.NoVersion")); }
        if (version > current) throw new InvalidDataException(CoreText.Format("CustomContent.Migration.Newer", version, current));
        if (version < 1) throw new InvalidDataException(CoreText.Format("CustomContent.Migration.Unknown", version));
        var ran = 0;
        while (version < current)
        {
            var step = steps.SingleOrDefault(s => s.From == version) ?? throw new InvalidDataException(CoreText.Format("CustomContent.Migration.Unknown", version));
            step.Apply(section);
            section["version"] = ++version;
            ran++;
        }
        return ran;
    }
}

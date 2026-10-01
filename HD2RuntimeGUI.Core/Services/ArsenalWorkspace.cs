using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Services;

/// <summary>The project's Arsenal presentation as the Export page shows it: the description and icon as saved, the icon's path in the
/// ZIP, and the first reason each cannot be written (null when it can).</summary>
public sealed record ArsenalState(string Description, string? Icon, string? IconEntry, string? DescriptionIssue, string? IconIssue)
{
    public bool Configured => Description.Length > 0 || Icon != null;
}

// HD2Arsenal presentation (format 12): written into manifest.json (Description, IconPath) and the ZIP (the icon) on every export.
// Edits are saved like additional files; a problem is shown in the section and refuses the export until it is fixed.
public sealed partial class BuilderWorkspace
{
    public ArsenalState ArsenalState()
    {
        var arsenal = Project?.Arsenal;
        return new(arsenal?.Description ?? "", arsenal?.Icon, Project is { } p ? Arsenal.IconEntry(p) : null,
            Arsenal.DescriptionIssue(arsenal?.Description), arsenal?.Icon is { } icon ? Arsenal.IconIssue(icon) : null);
    }
    /// <summary>Saves the description (line breaks kept, surrounding blank space removed); an empty text removes it.</summary>
    public Task SetArsenalDescriptionAsync(string text) => EditArsenalAsync(a =>
    {
        var description = Arsenal.NormalizeDescription(text);
        a.Description = description.Length > 0 ? description : null;
    });
    /// <summary>Uses an image as the Arsenal icon, refused (nothing saved) when Arsenal could not show it; null removes the icon.</summary>
    public Task SetArsenalIconAsync(string? path) => EditArsenalAsync(a =>
    {
        if (path == null) { a.Icon = null; return; }
        var icon = Path.GetFullPath(path.Trim());
        if (Arsenal.IconIssue(icon) is { } issue) throw new InvalidDataException(issue);
        a.Icon = icon;
    });
    private async Task EditArsenalAsync(Action<ArsenalPresentation> edit)
    {
        var project = Project; await weaponEditGate.WaitAsync();
        try
        {
            if (project == null || !ReferenceEquals(project, Project)) throw new InvalidOperationException("Active project changed.");
            var previous = project.Arsenal; var format = project.FormatVersion;
            var arsenal = new ArsenalPresentation { Description = previous?.Description, Icon = previous?.Icon };
            try
            {
                edit(arsenal);
                // The section is omitted again once neither is set.
                project.Arsenal = arsenal.Description != null || arsenal.Icon != null ? arsenal : null;
                await SaveChangesAsync();
            }
            catch { project.Arsenal = previous; project.FormatVersion = format; throw; }
        }
        finally { weaponEditGate.Release(); }
    }
}

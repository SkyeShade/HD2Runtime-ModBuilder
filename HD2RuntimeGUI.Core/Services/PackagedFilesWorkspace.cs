using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Services;

/// <summary>Picks a file on the user's computer for exported mod ZIPs (any file, or an image Arsenal accepts for its icon); returns its
/// full path, or null when cancelled.</summary>
public interface IPackagedFilePicker { Task<string?> PickAsync(); Task<string?> PickImageAsync(); }

/// <summary>One additional file as the Export page lists it, with the first reason it cannot be packaged (null when it can) and whether
/// that reason is its ZIP path (otherwise its source file).</summary>
public sealed record PackagedFileState(int Index, string Name, string Source, string Destination, string? Issue, bool DestinationIssue);

// Additional packaged files (format 12): the project keeps each source path and ZIP path; the file itself is read at every export.
// Every edit is saved, also when it leaves a problem; the problem is shown on the file's row and the export refuses until it is fixed.
public sealed partial class BuilderWorkspace
{
    public IReadOnlyList<PackagedFileState> PackagedFileStates()
    {
        var files = Project?.PackagedFiles ?? [];
        var icon = Project is { } project ? Arsenal.IconEntry(project) : null;
        var issues = PackagedFiles.Issues(files, icon == null ? PackagedFiles.Generated : [.. PackagedFiles.Generated, icon], arsenalIcon: icon);
        return [.. files.Select((f, i) => new PackagedFileState(i, PackagedFiles.Name(f), f.Source, f.Destination, issues[i],
            issues[i] != null && PackagedFiles.SourceIssue(f.Source) == null))];
    }
    /// <summary>Adds a file at the ZIP root under its own name; a name already used shows as a clash on its row to be renamed.</summary>
    public Task AddPackagedFileAsync(string source) => EditPackagedFilesAsync(files =>
    {
        source = Path.GetFullPath(source.Trim());
        if (files.Count >= PackagedFiles.MaxFiles) throw new InvalidDataException(CoreText.Format("PackagedFiles.TooMany", PackagedFiles.MaxFiles));
        if (PackagedFiles.SourceIssue(source) is { } issue) throw new InvalidDataException(issue);
        files.Add(new() { Source = source, Destination = PackagedFiles.DefaultDestination(source) });
    });
    // Row edits name the file as it was listed; a row that has since moved or changed (a second click on Remove) is left alone.
    public Task SetPackagedFileDestinationAsync(PackagedFileState file, string destination) =>
        EditPackagedFilesAsync(files => { if (Listed(files, file)) files[file.Index].Destination = PackagedFiles.Normalize(destination); });
    public Task RemovePackagedFileAsync(PackagedFileState file) => EditPackagedFilesAsync(files => { if (Listed(files, file)) files.RemoveAt(file.Index); });
    private static bool Listed(List<PackagedFile> files, PackagedFileState file) =>
        file.Index < files.Count && files[file.Index].Source == file.Source && files[file.Index].Destination == file.Destination;
    private async Task EditPackagedFilesAsync(Action<List<PackagedFile>> edit)
    {
        var project = Project; await weaponEditGate.WaitAsync();
        try
        {
            if (project == null || !ReferenceEquals(project, Project)) throw new InvalidOperationException("Active project changed.");
            var previous = project.PackagedFiles; var format = project.FormatVersion;
            var files = previous?.Select(f => new PackagedFile { Source = f.Source, Destination = f.Destination }).ToList() ?? [];
            try
            {
                edit(files);
                // The section is omitted again once the last file is removed.
                project.PackagedFiles = files.Count > 0 ? files : null;
                await SaveChangesAsync();
            }
            catch { project.PackagedFiles = previous; project.FormatVersion = format; throw; }
        }
        finally { weaponEditGate.Release(); }
    }
}

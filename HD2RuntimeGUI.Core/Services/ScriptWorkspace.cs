using System.Globalization;
using System.Text;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Scripting;

namespace HD2RuntimeGUI.Core.Services;

/// <summary>ModBuilder saved a different text than the working copy now holds: src/addon.lua was changed outside ModBuilder.</summary>
public sealed class CustomLuaConflictException(string disk) : IOException(CoreText.Format("CustomLua.Conflict.Error", CustomLuaSettings.RelativePath))
{
    public string DiskText { get; } = disk;
}
/// <summary>The working copy compared with what the project saved: missing, in sync, or changed outside ModBuilder.</summary>
public sealed record CustomLuaDiskState(string Path, bool Exists, string? Text, bool InSync);

// Hand-written Runtime Lua (format 11). The project JSON keeps the saved text exactly; src/addon.lua in the project folder is the working
// copy an external editor edits. ModBuilder writes the working copy only when the user saves, reloads it only when asked, and never
// overwrites outside changes without an explicit choice.
public sealed partial class BuilderWorkspace
{
    public static readonly string StarterLua = """
        -- Custom HD2Runtime Lua for this mod. ModBuilder's generated modifications are registered before this runs.
        -- `hd2` is already required; the event API reference and snippets are in ModBuilder's Custom Lua page.
        local mod = hd2.mod()

        """;
    private static readonly UTF8Encoding Utf8 = new(false);
    // Unsaved editor text of the open project, kept while the user visits other pages (never written until Save).
    private (Guid Project, string Text)? customLuaDraft;
    public string? CustomLuaDraft
    {
        get => Project != null && customLuaDraft?.Project == Project.Id ? customLuaDraft.Value.Text : null;
        set => customLuaDraft = value == null || Project == null ? null : (Project.Id, value);
    }
    public string? CustomLuaPath => Project == null ? null : Path.Combine(paths.ProjectDirectory(Project.Id), "src", "addon.lua");
    public CustomLuaDiskState? CustomLuaDisk()
    {
        if (Project?.CustomLua is not { } lua || CustomLuaPath is not { } path) return null;
        if (!File.Exists(path)) return new(path, false, null, false);
        var text = Utf8.GetString(File.ReadAllBytes(path));
        return new(path, true, text, text == lua.Source);
    }
    // Why the working copy blocks an export (changed outside ModBuilder and not reloaded or overwritten), or null.
    public string? CustomLuaSyncIssue() => CustomLuaDisk() is { Exists: true, InSync: false }
        ? CoreText.Format("CustomLua.SyncIssue", CustomLuaSettings.RelativePath) : null;
    public IReadOnlyList<LuaDiagnostic> CustomLuaDiagnostics(string? text = null) =>
        (text ?? Project?.CustomLua?.Source) is { } source ? LuaScriptAnalyzer.Analyze(source, Metadata?.Events, Metadata?.Entities?.Enemies) : [];

    public async Task AddCustomLuaAsync()
    {
        if (Project!.CustomLua != null) return;
        await EditCustomLuaAsync(p => p.CustomLua = new() { Source = StarterLua }, write: true);
    }
    public Task SetCustomLuaEnabledAsync(bool enabled) => EditCustomLuaAsync(p => p.CustomLua = p.CustomLua! with { Enabled = enabled }, write: false);
    /// <summary>Saves the editor text to the project and its working copy. Refuses (CustomLuaConflictException) when the working copy
    /// changed outside ModBuilder, unless the user chose to overwrite it.</summary>
    public async Task SaveCustomLuaAsync(string text, bool overwriteExternal = false)
    {
        if (text.Length > CustomLuaSettings.MaxLength) throw new InvalidDataException(CoreText.Format("CustomLua.TooLong", CustomLuaSettings.MaxLength.ToString("N0", CultureInfo.InvariantCulture)));
        if (CustomLuaDisk() is { Exists: true, InSync: false, Text: { } disk } && disk != text && !overwriteExternal) throw new CustomLuaConflictException(disk);
        await EditCustomLuaAsync(p => p.CustomLua = (p.CustomLua ?? new()) with { Source = text }, write: true);
    }
    /// <summary>Takes the working copy's text (changed in an external editor) into the project.</summary>
    public async Task ReloadCustomLuaAsync()
    {
        var disk = CustomLuaDisk() ?? throw new InvalidOperationException(CoreText.Get("CustomLua.Missing"));
        if (!disk.Exists) { await EditCustomLuaAsync(_ => { }, write: true); return; }
        if (disk.Text!.Length > CustomLuaSettings.MaxLength || disk.Text.Contains('\0')) throw new InvalidDataException(CoreText.Format("CustomLua.NotText", CustomLuaSettings.RelativePath));
        await EditCustomLuaAsync(p => p.CustomLua = p.CustomLua! with { Source = disk.Text }, write: false);
    }
    public async Task OpenCustomLuaExternallyAsync()
    {
        await EnsureCustomLuaFileAsync();
        await folders.OpenFileAsync(CustomLuaPath!);
    }
    public async Task OpenCustomLuaFolderAsync()
    {
        await EnsureCustomLuaFileAsync();
        await folders.OpenAsync(CustomLuaPath!, selectFile: true);
    }
    // The working copy exists whenever the project has custom Lua (created from the saved text, never over an existing file).
    private async Task EnsureCustomLuaFileAsync()
    {
        if (Project?.CustomLua is not { } lua || CustomLuaPath is not { } path || File.Exists(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await Storage.JsonStorage.WriteAtomicBytesAsync(path, Utf8.GetBytes(lua.Source));
    }
    private async Task EditCustomLuaAsync(Action<ModProject> edit, bool write)
    {
        var project = Project; await weaponEditGate.WaitAsync();
        try
        {
            if (project == null || !ReferenceEquals(project, Project)) throw new InvalidOperationException("Active project changed.");
            var previous = project.CustomLua; var format = project.FormatVersion;
            try
            {
                edit(project);
                if (write && project.CustomLua is { } lua)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(CustomLuaPath!)!);
                    await Storage.JsonStorage.WriteAtomicBytesAsync(CustomLuaPath!, Utf8.GetBytes(lua.Source));
                }
                await SaveChangesAsync();
            }
            catch { project.CustomLua = previous; project.FormatVersion = format; throw; }
        }
        finally { weaponEditGate.Release(); }
    }
}

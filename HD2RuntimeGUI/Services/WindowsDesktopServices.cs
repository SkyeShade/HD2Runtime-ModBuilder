using System.Diagnostics;
using HD2RuntimeGUI.Core.Services;

namespace HD2RuntimeGUI.Services;

public sealed class WindowsFolderOpener : IFolderOpener
{
    public Task OpenAsync(string path, bool selectFile = false)
    {
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath) && !File.Exists(fullPath)) throw new DirectoryNotFoundException("The folder does not exist yet. Export the project first.");
        var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        start.Arguments = selectFile ? $"/select,\"{fullPath}\"" : $"\"{fullPath}\"";
        Process.Start(start);
        return Task.CompletedTask;
    }
    // The OS file association (ShellExecute): the user's default editor for .lua, or Windows' "open with" choice when there is none.
    public Task OpenFileAsync(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("The file does not exist.", fullPath);
        Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true })?.Dispose();
        return Task.CompletedTask;
    }
}
public sealed class ProjectFilePicker : IProjectFilePicker
{
    public async Task<string?> PickAsync()
    {
        var result = await FilePicker.Default.PickAsync(new PickOptions
        { PickerTitle = "Open HD2Runtime ModBuilder project", FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>> { [DevicePlatform.WinUI] = [".json"] }) });
        return result?.FullPath;
    }
}

public sealed class ResearchFilePicker : IResearchFilePicker
{
    public async Task<string?> PickAsync(bool report)
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = report ? "Link Runtime snapshot weapon-map report" : "Load or relink HD2SNAP", FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>> { [DevicePlatform.WinUI] = [report ? ".json" : ".hd2snap"] }) });
        return file?.FullPath;
    }
}

/// <summary>Starts the staged updater, quits the app for it, and opens official release pages in the browser.</summary>
public sealed class WindowsAppUpdateHost : HD2RuntimeGUI.Core.Updates.IAppUpdateHost
{
    private static readonly long StartTicks = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
    public string InstallDirectory { get; } = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
    public int ProcessId => Environment.ProcessId;
    public long ProcessStartTicks => StartTicks;
    public void Launch(ProcessStartInfo command) => Process.Start(command)?.Dispose();
    public void Exit() => MainThread.BeginInvokeOnMainThread(() => Application.Current?.Quit());
    public async Task OpenUrlAsync(Uri url) => await Launcher.Default.OpenAsync(url);
}

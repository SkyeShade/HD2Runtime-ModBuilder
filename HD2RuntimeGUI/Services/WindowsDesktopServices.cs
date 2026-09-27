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
}
public sealed class ProjectFilePicker : IProjectFilePicker
{
    public async Task<string?> PickAsync()
    {
        var result = await FilePicker.Default.PickAsync(new PickOptions
        { PickerTitle = "Open HD2RuntimeGUI project", FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>> { [DevicePlatform.WinUI] = [".json"] }) });
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

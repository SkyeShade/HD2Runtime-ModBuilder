using System.Diagnostics;

namespace HD2RuntimeModBuilder.Updater;

/// <summary>
/// Replaces an HD2Runtime ModBuilder installation with a verified, staged release after the running app has exited.
/// Only files listed in the old or new package inventory are touched; anything else in the folder (and all user data,
/// which lives in %LOCALAPPDATA%\HD2RuntimeGUI) is left alone. Old files are moved into a backup folder first and moved
/// back if anything fails, so a failed update leaves the working installation in place.
/// Updating a 1.0.0 installation (HD2RuntimeGUI.exe) removes its HD2RuntimeGUI.* files, which its inventory lists, and
/// its WebView2 cache folder; the package's HD2RuntimeGUI.exe compatibility copy is not installed.
/// </summary>
public sealed class UpdateInstaller(Action<ProcessStartInfo> start, Func<int, long, TimeSpan, bool> waitForExit)
{
    public TimeSpan ExitTimeout { get; init; } = TimeSpan.FromMinutes(2);
    public int Attempts { get; init; } = 6;
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromMilliseconds(500);
    /// <summary>Test hook: called before each new file is copied into the installation.</summary>
    public Action<string>? BeforeCopy { get; init; }

    /// <summary>Starts the installed app: HD2RuntimeModBuilder.exe, or HD2RuntimeGUI.exe when a 1.0.0 installation was restored.</summary>
    public static ProcessStartInfo RestartCommand(string installDirectory)
    {
        var entry = Path.Combine(installDirectory, UpdateContract.EntryPoint);
        var legacy = Path.Combine(installDirectory, UpdateContract.LegacyEntryPoint);
        return new(!File.Exists(entry) && File.Exists(legacy) ? legacy : entry) { WorkingDirectory = installDirectory, UseShellExecute = false };
    }

    public UpdateResult Run(UpdaterOptions options)
    {
        var install = options.InstallDirectory; var staged = options.StagedDirectory;
        if (!waitForExit(options.ProcessId, options.ProcessStartTicks, ExitTimeout))
            return Finish(options, false, "HD2Runtime ModBuilder did not close, so the update was not installed.", relaunch: false);

        Inventory next; HashSet<string> previous;
        try
        {
            next = UpdateJson.ReadInventory(Path.Combine(staged, UpdateContract.InventoryFile));
            UpdateContract.Validate(next, options.Version);
            foreach (var file in next.Files)
                if (new FileInfo(UpdateContract.Resolve(staged, file.Path)) is not { Exists: true } info || info.Length != file.Size) throw new InvalidDataException($"Staged file is missing or incomplete: {file.Path}");
            if (!File.Exists(Path.Combine(install, UpdateContract.EntryPoint)) && !File.Exists(Path.Combine(install, UpdateContract.LegacyEntryPoint)))
                throw new InvalidDataException($"The installation folder does not contain {UpdateContract.EntryPoint}.");
            previous = PreviousFiles(install);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        { return Finish(options, false, "The staged update could not be verified: " + e.Message, relaunch: true); }

        var backup = Path.Combine(install, UpdateContract.BackupDirectory);
        var moved = new List<string>(); var copied = new List<string>();
        try
        {
            // A backup left by an interrupted update is kept aside, never deleted.
            if (Directory.Exists(backup)) Retry(() => Directory.Move(backup, backup + "-" + DateTime.UtcNow.Ticks));
            Directory.CreateDirectory(backup);
            var replace = new HashSet<string>(previous, StringComparer.OrdinalIgnoreCase) { UpdateContract.InventoryFile };
            replace.UnionWith(next.Files.Select(f => f.Path));
            foreach (var path in replace.Order(StringComparer.OrdinalIgnoreCase))
            {
                var source = UpdateContract.Resolve(install, path);
                if (!File.Exists(source)) continue;
                var target = UpdateContract.Resolve(backup, path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                Retry(() => File.Move(source, target));
                moved.Add(path);
            }
            var installed = next.Files.Where(f => !f.Path.Equals(UpdateContract.LegacyEntryPoint, StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach (var file in installed.Select(f => f.Path))
            {
                BeforeCopy?.Invoke(file);
                var target = UpdateContract.Resolve(install, file);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                copied.Add(file);
                Retry(() => File.Copy(UpdateContract.Resolve(staged, file), target, overwrite: false));
            }
            // The installed inventory, written last, lists exactly the files now installed.
            BeforeCopy?.Invoke(UpdateContract.InventoryFile);
            copied.Add(UpdateContract.InventoryFile);
            UpdateJson.WriteInventory(UpdateContract.Resolve(install, UpdateContract.InventoryFile), next with { Files = installed });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            var restored = Rollback(install, backup, moved, copied);
            return Finish(options, false, restored
                ? $"The update to {options.Version} could not replace the application files ({e.Message}). The previous version was restored."
                : $"The update to {options.Version} failed ({e.Message}) and some files could not be restored. The previous files are in {backup}.", relaunch: restored);
        }

        RemoveEmptyDirectories(install, previous);
        TryDelete(backup);
        if (moved.Contains(UpdateContract.LegacyEntryPoint, StringComparer.OrdinalIgnoreCase)) TryDelete(Path.Combine(install, UpdateContract.LegacyWebView2Folder));
        return Finish(options, true, $"HD2Runtime ModBuilder was updated to {options.Version}.", relaunch: true);
    }

    private static HashSet<string> PreviousFiles(string install)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var path = Path.Combine(install, UpdateContract.InventoryFile);
        if (!File.Exists(path)) return set;
        // Only well-formed entries of the installed inventory are considered; the installed version is not re-validated.
        foreach (var file in UpdateJson.ReadInventory(path).Files)
            if (UpdateContract.SafeRelativePath(file.Path) == file.Path) set.Add(file.Path);
        return set;
    }

    private bool Rollback(string install, string backup, List<string> moved, List<string> copied)
    {
        var ok = true;
        foreach (var path in Enumerable.Reverse(copied))
            // Every new file's path was moved to the backup first, so anything at that path now is (part of) the new copy.
            try { var target = UpdateContract.Resolve(install, path); if (File.Exists(target)) Retry(() => File.Delete(target)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ok = false; }
        foreach (var path in Enumerable.Reverse(moved))
            try { var target = UpdateContract.Resolve(install, path); Directory.CreateDirectory(Path.GetDirectoryName(target)!); Retry(() => File.Move(UpdateContract.Resolve(backup, path), target, overwrite: true)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ok = false; }
        if (ok) TryDelete(backup);
        return ok;
    }

    private void Retry(Action action)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { action(); return; }
            catch (IOException) when (attempt < Attempts) { Thread.Sleep(RetryDelay); }
            catch (UnauthorizedAccessException) when (attempt < Attempts) { Thread.Sleep(RetryDelay); }
        }
    }

    private static void RemoveEmptyDirectories(string install, IEnumerable<string> previous)
    {
        foreach (var directory in previous.Select(p => Path.GetDirectoryName(UpdateContract.Resolve(install, p))!).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(d => d.Length))
            try { for (var d = directory; d.Length > install.Length && Directory.Exists(d) && !Directory.EnumerateFileSystemEntries(d).Any(); d = Path.GetDirectoryName(d)!) Directory.Delete(d); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static void TryDelete(string directory)
    {
        try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private UpdateResult Finish(UpdaterOptions options, bool success, string message, bool relaunch)
    {
        var result = new UpdateResult(options.Version, success, message, DateTimeOffset.UtcNow);
        try { UpdateJson.WriteResult(options.ResultPath, result); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        if (relaunch && RestartCommand(options.InstallDirectory) is { } restart && File.Exists(restart.FileName))
            try { start(restart); } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        return result;
    }
}

using System.Diagnostics;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Storage;
using HD2RuntimeModBuilder.Updater;

namespace HD2RuntimeGUI.Core.Updates;

/// <summary>Platform side of self-updating: where the app runs from, and how to start the updater, quit and open links.</summary>
public interface IAppUpdateHost
{
    string InstallDirectory { get; }
    int ProcessId { get; }
    long ProcessStartTicks { get; }
    void Launch(ProcessStartInfo command);
    void Exit();
    Task OpenUrlAsync(Uri url);
}

public sealed record AppUpdateCache(DateTimeOffset? LastChecked, AppRelease? Latest, string? DismissedVersion);

/// <summary>
/// HD2Runtime ModBuilder's own updates from GitHub Releases (separate from HD2Runtime SDK updates). Checks when the app starts
/// and then every 10 minutes while it runs, never blocks the UI, and stays silent when up to date or offline. Installing
/// downloads and verifies the release, stages it in %TEMP%, starts the staged updater and exits; the updater replaces the
/// application files and restarts the app. User data in the data root is never part of an update.
/// </summary>
public sealed class AppUpdateService(IAppReleaseClient client, AppPaths paths, IAppUpdateHost host, string? currentVersion = null, string? tempRoot = null, TimeProvider? time = null) : IDisposable
{
    /// <summary>Background check interval while the app runs (6 requests an hour, well inside GitHub's unauthenticated limit).</summary>
    public static readonly TimeSpan PeriodicCheckInterval = TimeSpan.FromMinutes(10);
    private ITimer? timer;
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private readonly SemaphoreSlim gate = new(1, 1);
    private AppUpdateCache cache = new(null, null, null);
    private bool initialized;

    public string CurrentVersion { get; } = currentVersion ?? BuildInfo.Version;
    public string CachePath => Path.Combine(paths.Root, "app-update.json");
    public string ResultPath => Path.Combine(paths.Root, UpdateContract.ResultFile);
    public string TempRoot { get; } = tempRoot ?? Path.Combine(Path.GetTempPath(), "HD2Runtime-ModBuilder-Update");

    public AppUpdateStatus Status { get; private set; } = AppUpdateStatus.NotChecked;
    public AppRelease? Latest => cache.Latest;
    public DateTimeOffset? LastChecked => cache.LastChecked;
    public string? CheckMessage { get; private set; }
    public bool Installing { get; private set; }
    public string? InstallStage { get; private set; }
    public string? InstallError { get; private set; }
    /// <summary>The install could not write to the application folder; the release page is the fallback.</summary>
    public bool InstallNeedsManualDownload { get; private set; }
    /// <summary>Outcome of an update that the updater finished before this start, shown once.</summary>
    public UpdateResult? PreviousResult { get; private set; }
    public bool UpdateAvailable => Status == AppUpdateStatus.UpdateAvailable && Latest != null;
    public bool Dismissed => Latest != null && cache.DismissedVersion == Latest.Version;
    public bool ShowBanner => UpdateAvailable && (!Dismissed || Installing || InstallError != null);
    /// <summary>Automatic install is offered only for a release package (it carries the file inventory the updater relies on).</summary>
    public bool IsReleaseInstall => File.Exists(Path.Combine(host.InstallDirectory, UpdateContract.InventoryFile));
    public string ReleaseNotesUrl => Latest?.ReleaseUrl ?? AppReleaseClient.ReleasesPage;
    public event Action? Changed;

    public async Task InitializeAsync()
    {
        if (initialized) return;
        initialized = true;
        try { if (File.Exists(CachePath)) cache = await JsonStorage.ReadAsync<AppUpdateCache>(CachePath); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException) { cache = new(null, null, null); }
        try
        {
            if (File.Exists(ResultPath))
            {
                PreviousResult = UpdateJson.ReadResult(ResultPath);
                File.Delete(ResultPath);
                if (PreviousResult.Success) CleanUp();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException) { }
        if (cache.Latest != null) Status = Compare(cache.Latest);
        Changed?.Invoke();
    }

    /// <summary>Checks now (at startup) and then every 10 minutes until disposed. Idempotent; never throws.</summary>
    public async Task StartAutomaticChecksAsync()
    {
        timer ??= clock.CreateTimer(_ => _ = CheckInBackgroundAsync(), null, PeriodicCheckInterval, PeriodicCheckInterval);
        await CheckAsync(background: true);
    }
    // A periodic check is skipped while an update installs; it runs quietly (see CheckAsync).
    private Task CheckInBackgroundAsync() => Installing ? Task.CompletedTask : CheckAsync(background: true);

    /// <summary>Queries GitHub now. A background check keeps the current state visible while it runs, and a failed background
    /// check (offline, rate-limited) keeps the last known result, so an available update's banner never flickers away.</summary>
    public async Task CheckAsync(bool background = false)
    {
        if (!await gate.WaitAsync(0)) return;
        try
        {
            await InitializeAsync();
            var known = Status != AppUpdateStatus.NotChecked;
            if (!background || !known) { Status = AppUpdateStatus.Checking; CheckMessage = null; Changed?.Invoke(); }
            var previous = Status;
            try
            {
                var latest = await client.GetLatestAsync();
                cache = cache with { LastChecked = clock.GetUtcNow(), Latest = latest };
                Status = latest == null ? AppUpdateStatus.UpToDate : Compare(latest);
                CheckMessage = latest == null ? "No HD2Runtime ModBuilder release is published yet." : null;
                await SaveAsync();
            }
            catch (AppUpdateCheckException e) { if (background && known) CheckMessage = e.Message; else { Status = e.Status; CheckMessage = e.Message; } }
            catch (Exception e) when (e is not OperationCanceledException) { if (background && known) CheckMessage = e.Message; else { Status = AppUpdateStatus.CheckFailed; CheckMessage = e.Message; } }
            if (background && known && Status == AppUpdateStatus.Checking) Status = previous;
        }
        finally { gate.Release(); Changed?.Invoke(); }
    }

    public void Dispose() { timer?.Dispose(); timer = null; }

    public async Task DismissAsync()
    {
        if (Latest == null) return;
        cache = cache with { DismissedVersion = Latest.Version }; InstallError = null;
        await SaveAsync(); Changed?.Invoke();
    }

    public void ClearPreviousResult() { PreviousResult = null; Changed?.Invoke(); }

    public Task OpenReleaseNotesAsync() => host.OpenUrlAsync(ValidatedReleaseUrl(ReleaseNotesUrl));

    /// <summary>Only pages of the official repository are opened.</summary>
    public static Uri ValidatedReleaseUrl(string url) =>
        url.StartsWith(BuildInfo.RepositoryUrl + "/releases", StringComparison.Ordinal) && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https"
            ? uri : throw new InvalidDataException("Only official HD2Runtime ModBuilder release pages can be opened.");

    /// <summary>The command that starts the staged updater; it runs from outside the installation folder.</summary>
    public static ProcessStartInfo UpdaterCommand(UpdaterOptions options)
    {
        var command = new ProcessStartInfo(Path.Combine(options.StagedDirectory, UpdateContract.UpdaterExe)) { WorkingDirectory = options.StagedDirectory, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in UpdateContract.Arguments(options)) command.ArgumentList.Add(argument);
        return command;
    }

    public static bool CanWrite(string directory)
    {
        var probe = Path.Combine(directory, ".modbuilder-write-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            var inventory = Path.Combine(directory, UpdateContract.InventoryFile);
            if (File.Exists(inventory)) using (new FileStream(inventory, FileMode.Open, FileAccess.ReadWrite, FileShare.Read)) { }
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    public async Task InstallAsync(CancellationToken ct = default)
    {
        if (Latest is not { } release || !UpdateAvailable || Installing) return;
        Installing = true; InstallError = null; InstallNeedsManualDownload = false;
        var work = Path.Combine(TempRoot, release.Version);
        try
        {
            if (!IsReleaseInstall) { InstallNeedsManualDownload = true; throw new InvalidOperationException("This copy of HD2Runtime ModBuilder was not installed from a release ZIP, so it cannot update itself. Download the new version from the release page."); }
            if (!CanWrite(host.InstallDirectory)) { InstallNeedsManualDownload = true; throw new UnauthorizedAccessException($"Automatic updating cannot write to {host.InstallDirectory}. Download the new version from the release page, or move HD2Runtime ModBuilder to a folder you can write to."); }
            Stage("Checking the update…");
            var manifest = AppUpdateManifest.Parse(await client.DownloadManifestAsync(release, ct), release);
            if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
            Directory.CreateDirectory(work);
            var zip = Path.Combine(work, release.Package.Name);
            Stage("Downloading…");
            var lastPercent = -1;
            await client.DownloadPackageAsync(release, zip, new Progress<long>(bytes =>
            {
                var percent = (int)(bytes * 100 / Math.Max(1, release.Package.Size));
                if (percent != lastPercent) { lastPercent = percent; Stage($"Downloading… {percent}%"); }
            }), ct);
            Stage("Verifying…");
            var staged = Path.Combine(work, "staged");
            await AppUpdatePackage.StageAsync(zip, manifest, staged, ct);
            File.Delete(zip);
            var options = new UpdaterOptions(host.ProcessId, host.ProcessStartTicks, Path.TrimEndingDirectorySeparator(Path.GetFullPath(host.InstallDirectory)), staged, release.Version, ResultPath);
            Stage("Restarting HD2Runtime ModBuilder…");
            host.Launch(UpdaterCommand(options));
            host.Exit();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or HttpRequestException or System.ComponentModel.Win32Exception)
        {
            InstallError = e is HttpRequestException ? "The update could not be downloaded: " + e.Message : e.Message;
            Installing = false; InstallStage = null;
            TryDelete(work);
            Changed?.Invoke();
        }
    }

    private void Stage(string message) { InstallStage = message; Changed?.Invoke(); }

    private AppUpdateStatus Compare(AppRelease latest) =>
        SemVersion.Parse(latest.Version).CompareTo(SemVersion.Parse(CurrentVersion.Split('+')[0])) > 0 ? AppUpdateStatus.UpdateAvailable : AppUpdateStatus.UpToDate;

    private async Task SaveAsync()
    {
        try { await JsonStorage.WriteAtomicAsync(CachePath, cache); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>After a successful update: remove the staged files and any backup the updater could not delete.</summary>
    private void CleanUp()
    {
        TryDelete(TempRoot);
        TryDelete(Path.Combine(host.InstallDirectory, UpdateContract.BackupDirectory));
    }

    private static void TryDelete(string directory)
    {
        try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

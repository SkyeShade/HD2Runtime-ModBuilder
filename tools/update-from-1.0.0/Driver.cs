#:project ../../artifacts/wt-1.0.0/HD2RuntimeGUI.Core/HD2RuntimeGUI.Core.csproj
#:property PublishAot=false
// Runs HD2Runtime ModBuilder 1.0.0's own in-app update code (HD2RuntimeGUI.Core built from the v1.0.0 tag) against a new
// release on disk, exactly as the 1.0.0 app would after "Install update": check, manifest, download, SHA-256, staging,
// starting the staged updater, then closing the running 1.0.0 app. Used by tools/update-from-1.0.0.ps1.
//   Driver.cs <releaseDir> <version> <installDir> <dataRoot> <tempRoot> <appPid>
using System.Diagnostics;
using HD2RuntimeGUI.Core;
using HD2RuntimeGUI.Core.Storage;
using HD2RuntimeGUI.Core.Updates;

var (releaseDir, version, install, dataRoot, tempRoot, pid) = (args[0], args[1], args[2], args[3], args[4], int.Parse(args[5]));
if (BuildInfo.Version != "1.0.0") throw new InvalidOperationException($"Expected the 1.0.0 update code, got {BuildInfo.Version}.");
var zip = Path.Combine(releaseDir, AppReleaseClient.PackageName(version));
var manifest = Path.Combine(releaseDir, AppReleaseClient.ManifestName); // 1.0.0 reads modbuilder-update.json
var tag = "v" + version; var sha = await AppUpdatePackage.Sha256Async(zip);
var release = new AppRelease(version, tag, AppReleaseClient.ReleasePage(tag),
    new(Path.GetFileName(zip), $"{BuildInfo.RepositoryUrl}/releases/download/{tag}/{Path.GetFileName(zip)}", new FileInfo(zip).Length, "sha256:" + sha),
    new(AppReleaseClient.ManifestName, $"{BuildInfo.RepositoryUrl}/releases/download/{tag}/{AppReleaseClient.ManifestName}", new FileInfo(manifest).Length, null));

var app = Process.GetProcessById(pid);
var host = new Host(install, app);
var service = new AppUpdateService(new LocalRelease(release, zip, manifest), new AppPaths(dataRoot), host, tempRoot: tempRoot);
await service.CheckAsync();
Console.WriteLine($"1.0.0 check: {service.Status}, latest {service.Latest?.Version}");
if (!service.UpdateAvailable) return 1;
await service.InstallAsync();
if (service.InstallError != null) { Console.WriteLine("1.0.0 install failed: " + service.InstallError); return 1; }
Console.WriteLine($"updater pid {host.UpdaterPid}");
return 0;

sealed class LocalRelease(AppRelease release, string zip, string manifest) : IAppReleaseClient
{
    public Task<AppRelease?> GetLatestAsync(CancellationToken ct = default) => Task.FromResult<AppRelease?>(release);
    public Task<byte[]> DownloadManifestAsync(AppRelease r, CancellationToken ct = default) => File.ReadAllBytesAsync(manifest, ct);
    public Task DownloadPackageAsync(AppRelease r, string destination, IProgress<long>? progress = null, CancellationToken ct = default)
    { File.Copy(zip, destination); progress?.Report(r.Package.Size); return Task.CompletedTask; }
}

sealed class Host(string install, Process app) : IAppUpdateHost
{
    public int UpdaterPid;
    public string InstallDirectory => install;
    public int ProcessId => app.Id;
    public long ProcessStartTicks => app.StartTime.ToUniversalTime().Ticks;
    public void Launch(ProcessStartInfo command) { var p = Process.Start(command)!; UpdaterPid = p.Id; Console.WriteLine($"started {command.FileName} {string.Join(' ', command.ArgumentList)}"); }
    // The 1.0.0 app quits itself after starting the updater; here the running 1.0.0 app is asked to close.
    public void Exit() { app.CloseMainWindow(); if (!app.WaitForExit(15000)) app.Kill(); }
    public Task OpenUrlAsync(Uri url) => Task.CompletedTask;
}

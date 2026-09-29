using HD2RuntimeGUI.Core.GitHub;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Storage;

namespace HD2RuntimeGUI.Core.Services;

// LocalSource: a developer-only local SDK (not a published release) is active for this run.
// NewerUnsupported: the newest published SDK is newer than this ModBuilder supports (SdkCompatibility); it is reported, never offered.
public sealed record SdkStatus(SdkMetadata Installed, SdkRelease? Latest, bool VerifiedOnline, string Message, string? LocalSource = null, string? NewerUnsupported = null)
{
    public bool UpdateAvailable => Latest != null && SemVersion.Parse(Latest.Version).CompareTo(SemVersion.Parse(Installed.Version)) > 0;
}
public sealed record CreationTicket(Guid Id, SdkStatus Status);
public enum UpdateDecision { UseInstalled, IgnoreThisTime, InstallUpdate }
public interface ISdkUpdateService
{
    Task<SdkStatus> CheckAsync(CancellationToken ct = default);
    Task<CreationTicket> BeginCreationAsync(CancellationToken ct = default);
    Task<SdkMetadata> ResolveCreationAsync(CreationTicket ticket, UpdateDecision decision, CancellationToken ct = default);
    Task<SdkMetadata> InstallLatestAsync(SdkRelease release, CancellationToken ct = default);
}

public sealed class SdkUpdateService(ISdkCache cache, IGitHubReleaseClient github, AppPaths paths) : ISdkUpdateService
{
    private readonly HashSet<Guid> tickets = [];
    public async Task<SdkStatus> CheckAsync(CancellationToken ct = default)
    {
        if (cache.LocalSdkPath is { } local)
        {
            // Local development SDK: fully validated on load, no GitHub download or release verification for this run.
            var sdk = await cache.GetCurrentAsync(ct);
            return new(sdk, null, false, $"Local SDK {sdk.Version} from {Path.GetFullPath(local)}. GitHub release checks are skipped for this run; the SDK cache is not changed.", Path.GetFullPath(local));
        }
        SdkMetadata installed; string? repaired = null;
        try { installed = await cache.GetCurrentAsync(ct); }
        catch (IncompleteSdkCacheException e)
        {
            // Reinstall the same published release; the cache only gains the missing files.
            SdkRelease? release = null;
            try { release = (await github.GetReleasesAsync(ct)).FirstOrDefault(r => r.Version == e.Version); }
            catch (Exception x) when (!ct.IsCancellationRequested && x is HttpRequestException or IOException or System.Text.Json.JsonException or TaskCanceledException or FormatException) { }
            if (release == null) throw;
            installed = await cache.InstallAsync(release, ct); repaired = $" Completed the cached SDK {e.Version} with {e.File}.";
        }
        var releaseFile = paths.CachePath("last-release.json");
        try
        {
            SdkRelease? latest = null; string? newer = null;
            int unsupported = 0;
            foreach (var candidate in (await github.GetReleasesAsync(ct)).OrderByDescending(r => SemVersion.Parse(r.Version)).Take(20))
            {
                GitHubReleaseClient.Validate(candidate);
                // Explicit compatibility pin: SDKs newer than this ModBuilder was validated against are never downloaded or offered.
                if (!SdkCompatibility.IsSupported(candidate.Version)) { newer ??= candidate.Version; continue; }
                try
                {
                    // The release API has no schema manifest; inspect metadata without activating the SDK.
                    if (candidate.Version != installed.Version) await cache.InspectAsync(candidate, ct);
                    latest = candidate; break;
                }
                // A release whose metadata this version cannot read is skipped, never fatal to the check.
                catch (Exception e) when (e is UnsupportedSdkException or InvalidDataException) { unsupported++; }
            }
            var newerNote = newer == null ? "" : $" HD2Runtime SDK {newer} is available but needs a newer HD2Runtime ModBuilder (this version supports SDKs up to {SdkCompatibility.NewestSupportedVersion}).";
            if (latest == null) return new(installed, null, true, "No compatible SDK release found. The installed SDK remains available; a newer HD2Runtime ModBuilder may be required." + newerNote, NewerUnsupported: newer);
            await JsonStorage.WriteAtomicAsync(releaseFile, latest, ct);
            return new(installed, latest, true, "GitHub release and schema/API compatibility checked." + (unsupported > 0 ? $" Skipped {unsupported} incompatible release(s)." : "") + newerNote + repaired, NewerUnsupported: newer);
        }
        catch (Exception e) when (e is HttpRequestException or IOException or System.Text.Json.JsonException or TaskCanceledException or FormatException or InvalidOperationException or KeyNotFoundException or OverflowException or InvalidDataException)
        {
            if (ct.IsCancellationRequested) throw;
            SdkRelease? cached = null;
            try { if (File.Exists(releaseFile)) { cached = await JsonStorage.ReadAsync<SdkRelease>(releaseFile, ct); GitHubReleaseClient.Validate(cached); if (!SdkCompatibility.IsSupported(cached.Version)) cached = null; } }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or FormatException) { cached = null; }
            return new(installed, cached, false, "Offline / not verified — using cached SDK. " + e.Message);
        }
    }
    public async Task<CreationTicket> BeginCreationAsync(CancellationToken ct = default)
    {
        var ticket = new CreationTicket(Guid.NewGuid(), await CheckAsync(ct));
        lock (tickets) tickets.Add(ticket.Id);
        return ticket;
    }
    public async Task<SdkMetadata> ResolveCreationAsync(CreationTicket ticket, UpdateDecision decision, CancellationToken ct = default)
    {
        lock (tickets) if (!tickets.Contains(ticket.Id)) throw new InvalidOperationException("This create-project action has already completed.");
        if (ticket.Status.UpdateAvailable && decision == UpdateDecision.UseInstalled) throw new InvalidOperationException("Choose Install Update or Ignore This Time.");
        var sdk = decision == UpdateDecision.InstallUpdate
            ? await cache.InstallAsync(ticket.Status.Latest ?? throw new InvalidOperationException("No SDK release available."), ct)
            : ticket.Status.Installed;
        lock (tickets) if (!tickets.Remove(ticket.Id)) throw new InvalidOperationException("Creation action already consumed.");
        return sdk;
    }
    public Task<SdkMetadata> InstallLatestAsync(SdkRelease release, CancellationToken ct = default) => cache.InstallAsync(release, ct);
}

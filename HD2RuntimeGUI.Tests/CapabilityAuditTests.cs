using System.IO.Compression;
using HD2RuntimeGUI.Core.Audit;
using HD2RuntimeGUI.Core.Metadata;
using Xunit;
using Xunit.Abstractions;

namespace HD2RuntimeGUI.Tests;

// The release gate for SDK coverage: every capability the pinned HD2Runtime SDK publishes is authored, shown read-only with Runtime's
// reason, or exempted with a justification. The report (capability-audit.json, COVERAGE.md) is written next to the test binaries and,
// when HD2_AUDIT_OUT is set, into that folder (the release-candidate build copies it).
public sealed class CapabilityAuditTests(ITestOutputHelper output)
{
    public static IReadOnlyDictionary<string, byte[]> Files(string version)
    {
        using var zip = ZipFile.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sdk-" + version + ".zip"));
        return zip.Entries.Where(e => !e.FullName.EndsWith('/')).ToDictionary(e => e.FullName, e =>
        {
            using var s = e.Open(); using var m = new MemoryStream(); s.CopyTo(m); return m.ToArray();
        });
    }
    public static async Task<CapabilityAuditReport> Report(TestEnvironment e)
    {
        var sdk = await SdkFixtures.Install(e, SdkPin.Version);
        return CapabilityAudit.Run(sdk, Files(SdkPin.Version));
    }

    [Fact] public async Task The_pinned_sdk_has_no_unexpected_missing_capability()
    {
        using var e = new TestEnvironment(); var report = await Report(e);
        var json = CapabilityAudit.Json(report); var markdown = CapabilityAudit.Markdown(report, "ModBuilder " + Core.BuildInfo.Version + " capability coverage (HD2Runtime " + report.SdkVersion + ")");
        foreach (var dir in new[] { Path.Combine(AppContext.BaseDirectory, "audit"), Environment.GetEnvironmentVariable("HD2_AUDIT_OUT") }.OfType<string>())
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "capability-audit.json"), json); File.WriteAllText(Path.Combine(dir, "COVERAGE.md"), markdown);
        }
        foreach (var m in report.UnexpectedMissing) output.WriteLine($"MISSING {m.Area} {m.Capability} {m.Type} {m.Class} n={m.Instances}: {m.Reason}");
        Assert.True(report.PinnedBuild);
        Assert.Empty(report.Files.Where(f => CapabilityAuditReport.Missing.Contains(f.Class)));
        Assert.Empty(report.UnexpectedMissing);
    }

    [Fact] public async Task The_audit_is_driven_by_the_sdk_data()
    {
        using var e = new TestEnvironment(); var report = await Report(e);
        // Every published field instance of every catalog is counted exactly once.
        var sdk = await e.Cache.GetVersionAsync(SdkPin.Version);
        int Count(params string[] areas) => report.Entries.Where(x => areas.Contains(x.Area)).Sum(x => x.Instances);
        Assert.Equal(sdk.PlayerWeapons!.Weapons.Sum(w => w.Fields.Count), Count("player_weapon"));
        Assert.Equal(sdk.SupportAuthoring!.FieldInstances.Length, Count("support_weapon"));
        Assert.Equal(sdk.Stratagems!.FieldInstances.Length, Count("stratagem"));
        Assert.Equal(sdk.Entities!.AllFields.Count(), report.Entries.Where(x => sdk.Entities.AllFields.Select(f => f.Target.Resource).Distinct().Contains(x.Area)).Sum(x => x.Instances));
        Assert.Equal(sdk.AttackOutputs!.ProjectileSources.Length, report.Entries.Where(x => x.Capability.StartsWith("projectile host", StringComparison.Ordinal)).Sum(x => x.Instances));
        Assert.Equal(sdk.Events!.Events.Length, Count("event"));
        // Read-only capabilities carry a reason; authored ones name their editor.
        Assert.All(report.Entries.Where(x => x.Class == CapabilityClass.RUNTIME_READ_ONLY), x => Assert.False(string.IsNullOrWhiteSpace(x.Reason)));
        Assert.All(report.Entries.Where(x => x.Class is CapabilityClass.SUPPORTED_UI or CapabilityClass.SUPPORTED_CUSTOM_LUA_ONLY), x => Assert.False(string.IsNullOrWhiteSpace(x.Surface)));
        // Every file in the SDK archive is classified.
        Assert.Equal(Files(SdkPin.Version).Count, report.Files.Count);
    }
}

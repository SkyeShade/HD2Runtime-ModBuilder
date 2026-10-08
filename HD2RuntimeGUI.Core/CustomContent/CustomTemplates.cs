using System.Reflection;
using System.Text.Json.Nodes;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.CustomContent;

/// <summary>A ready-made custom stratagem, read through the Runtime's project form. Two are HD2Runtime's own examples (sdk/fixtures/custom_stratagems,
/// bundled byte for byte; <c>RuntimeExample</c>), exactly as the Runtime has played them; the others are ModBuilder's own simple examples for the
/// main delivery families. Every template is checked by the Runtime SDK's validator and compiles to the same registration as the SDK's compiler writes
/// for it (the oracle tests hold each one to that). Its texts are the example's own (game text written into the mod, never translated); its icon
/// starts as a placeholder.</summary>
public sealed record CustomStratagemTemplate(string Key, string Name, string Description, string Family, Func<CustomStratagem> Create, string Source, bool RuntimeExample);
public sealed record CustomProjectileTemplate(string Key, string Label, string Description, Func<CustomProjectile> Create);

public static class CustomTemplates
{
    private const string Prefix = "HD2RuntimeGUI.Core.CustomContent.Templates.";

    /// <summary>The HD2Runtime examples among the templates (file names without .json), byte for byte the SDK fixtures of the same name.</summary>
    public static readonly IReadOnlySet<string> RuntimeExampleNames = new HashSet<string>(StringComparer.Ordinal) { "EAT17GExample", "GasBarrageExample" };

    /// <summary>The bundled example projects: file name (without .json) and its JSON.</summary>
    public static IReadOnlyList<(string Name, JsonObject Project)> Examples { get; } = LoadExamples();

    public static readonly IReadOnlyList<CustomStratagemTemplate> Stratagems = [.. Examples.SelectMany(e =>
        ((JsonArray)e.Project["stratagems"]!).OfType<JsonObject>().Select(s =>
        {
            var json = s.ToJsonString();
            var sample = RuntimeProjectForm.Read((JsonObject)JsonNode.Parse(json)!);
            return new CustomStratagemTemplate(e.Name, sample.NameCased ?? sample.Name, sample.Description, sample.Delivery.Family,
                () => RuntimeProjectForm.Read((JsonObject)JsonNode.Parse(json)!), e.Name, RuntimeExampleNames.Contains(e.Name));
        }))
        // Families first in the order the builder offers them, then by name.
        .OrderBy(t => DeliveryFamily.All.ToList().IndexOf(t.Family)).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)];

    private static IReadOnlyList<(string, JsonObject)> LoadExamples()
    {
        var assembly = Assembly.GetExecutingAssembly();
        return [.. assembly.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).Select(n =>
            {
                using var stream = assembly.GetManifestResourceStream(n)!;
                return (n[Prefix.Length..^".json".Length], (JsonObject)JsonNode.Parse(stream)!);
            })];
    }

    // Runtime's live-verified development proof (runtime/custom_projectiles.lua DEVELOPMENT_PROOF): a LAS-58 Talon bolt with the PLAS-1
    // Scorcher's in-flight look and the RS-422 Railgun's direct damage.
    public static readonly IReadOnlyList<CustomProjectileTemplate> Projectiles =
    [
        new("talon_hybrid", "CustomTemplates.TalonHybrid", "CustomTemplates.TalonHybrid.Help", () => new()
        {
            Id = "talon_hybrid_bolt", Name = "Talon Hybrid Bolt", Base = "output/v1/projectile/las-58-talon",
            Visual = "output/v1/projectile/plas-1-scorcher", Damage = "output/v1/projectile/rs-422-railgun",
        }),
    ];
}

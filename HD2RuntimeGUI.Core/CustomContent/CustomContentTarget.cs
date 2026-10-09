using System.Reflection;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.CustomContent;

// Pass: a check that holds (shown as ✓). Info: worth knowing. Experimental: works through Runtime machinery it calls experimental or internal.
// Warning: may not behave as hoped, builds anyway. Error: refuses the build.
public enum CustomSeverity { Pass, Info, Experimental, Warning, Error }

/// <summary>One validation result of custom content. Owner names the custom object ("stratagem:&lt;id&gt;", "projectile:&lt;id&gt;",
/// "assignment:&lt;guid&gt;", or "content"); Field is the semantic field path it is about, when it is about one.</summary>
public sealed record CustomDiagnostic(CustomSeverity Severity, string Area, string Owner, string? Field, string Message, bool Runtime = false)
{
    /// <summary>Whether the diagnostic is about a field or anything inside it (payload.gun for payload.gun.rpm, payload.items for payload.items[1].count).</summary>
    public bool About(string field) => Field is { } f && (f == field || f.StartsWith(field + ".", StringComparison.Ordinal) || f.StartsWith(field + "[", StringComparison.Ordinal)
        || field.Contains("[]", StringComparison.Ordinal) && System.Text.RegularExpressions.Regex.Replace(f, @"\[\d+\]", "[]") is var g && (g == field || g.StartsWith(field + ".", StringComparison.Ordinal)));
    public const string Identity = "identity", CallIn = "callin", Carrier = "carrier", Delivery = "delivery", Assets = "assets", Advanced = "advanced",
        Multiplayer = "multiplayer", Projectile = "projectile", Assignment = "assignment", Build = "build";
    public static string StratagemOwner(string id) => "stratagem:" + id;
    public static string ProjectileOwner(string id) => "projectile:" + id;
    public static string AssignmentOwner(Guid id) => "assignment:" + id.ToString("N");
    public const string ContentOwner = "content";
}

/// <summary>A compiled source image: the PNG it came from and the game resources the build packs (texture and GUI material).</summary>
public sealed record CompiledImage(string Id, string ResourceName, ulong NameHash, string Sha256, string Prepared, byte[] Png, byte[] TextureMain, byte[] TextureGpu, byte[] Material);

/// <summary>What a target makes of a project's custom content. Operations are guarded writes in the same form as every other generated
/// operation (they join the generated list); Section is the readable registration block that runs after them. Images are filled only
/// when the build asked for them.</summary>
public sealed record CompiledCustomContent(string TargetId, string RequiredRuntime, IReadOnlyList<string> Operations, string Section,
    IReadOnlyList<CompiledImage> Images, IReadOnlyList<string> StratagemIds, IReadOnlyList<string> ProjectileIds, string SchemaSha256,
    IReadOnlyList<CustomModel>? Models = null);

/// <summary>The boundary between ModBuilder's semantic custom content model and one Runtime API. Everything that knows Runtime field
/// names, families, donors, limits, refusals or the generated Lua syntax lives behind it; editors and the project model never do. A
/// Runtime API change means a new or updated target (and, only when the meaning changes, a CustomContentMigrations step).</summary>
public interface ICustomContentTarget
{
    string Id { get; }
    string RuntimeLabel { get; }
    string MinimumRuntime { get; }
    CustomSchema Schema { get; }
    /// <summary>The vanilla projectile a weapon fires so that a custom projectile can replace each of its shots.</summary>
    string ProjectileCarrier { get; }
    /// <summary>What Runtime says about a field (by its project form path: cooldown, carrier.group, payload.gun.rpm, payload.items[].modify.rpm,
    /// or a refusal's own name) for one custom stratagem.</summary>
    CustomFieldInfo Describe(string path, CustomStratagem? stratagem = null);
    /// <summary>How a dropdown shows one of a field's choices (a pod item's name, an attack output's label); the value itself otherwise.</summary>
    string ChoiceLabel(string path, string value);
    /// <summary>The current name of a saved choice the Runtime renamed (a sound name), when the field offers it; null otherwise.</summary>
    string? RenamedChoice(string path, string value) => null;
    /// <summary>The custom stratagem in the Runtime's own project form (custom_stratagems.json).</summary>
    System.Text.Json.Nodes.JsonObject ProjectForm(CustomStratagem stratagem);
    /// <summary>The carrier groups that can carry the stratagem's payload, and the one Runtime uses when none is chosen.</summary>
    IReadOnlyList<CarrierGroup> CarrierGroups(CustomStratagem stratagem);
    string? DefaultCarrierGroup(CustomStratagem stratagem);
    /// <summary>What Runtime has shown for a delivery family in play (live, partial, offline; multiplayer), or null when it says nothing.</summary>
    FamilyEvidence? Evidence(string family);
    /// <summary>Gives a stratagem the carrier its delivery family needs (its default group, the families it is limited to); called when a
    /// stratagem is created or switches family.</summary>
    void ApplyFamilyDefaults(CustomStratagem stratagem);
    /// <summary>The Runtime's own rules: ranges, donors, family rules, refusals, what this Runtime can build at all.</summary>
    IReadOnlyList<CustomDiagnostic> Validate(CustomContentSection content, ModProject project, SdkMetadata? sdk);
    /// <summary>The generated Lua for valid content (validate first); images are compiled only when asked (exports).</summary>
    CompiledCustomContent Compile(CustomContentSection content, ModProject project, SdkMetadata sdk, bool images);
    /// <summary>One custom stratagem's registration as the build writes it (read-only preview).</summary>
    string RegistrationPreview(CustomStratagem stratagem, CustomContentSection content);
    /// <summary>One custom projectile's definition as the build writes it (read-only preview).</summary>
    string ProjectilePreview(CustomProjectile projectile, ModProject project);
    /// <summary>The custom projectile components this Runtime composes, in order (visual, damage, impactExplosion, ballistics), and what has
    /// been shown in play for each ("verified", "observed" or null), as the Runtime's research records it.</summary>
    IReadOnlyList<(string Component, string? Evidence)> ProjectileComponents { get; }
    /// <summary>Whether a projectile host can fire a custom projectile with this Runtime (Supported), or why not (Unavailable, with the reason).
    /// Every projectile selector asks it before it offers custom projectiles.</summary>
    CustomFieldInfo DescribeProjectileHost(CustomProjectileHost host, ModProject project, SdkMetadata? sdk);
    /// <summary>How many weapon attacks can fire a custom projectile at the same time (null: no limit).</summary>
    int? ProjectileBindingLimit { get; }
    /// <summary>Why a catalogued projectile output cannot be a custom projectile's base or component (null when it can). Package: the
    /// component's assets must load (every component but ballistics).</summary>
    string? ProjectileOutputIssue(string outputId, bool package, SdkMetadata? sdk);
}

/// <summary>Which target compiles custom content. 1.7 has one: the HD2Runtime 0.30.0 custom stratagem API, read from the schema snapshot ModBuilder
/// bundles. HD2RUNTIME_CUSTOM_SCHEMA can point at a newer CustomStratagemSchema.json (a Runtime developer testing schema changes); a schema
/// whose format the target does not understand is refused with the reason and the bundled snapshot is used.</summary>
public static class CustomContentTargets
{
    public const string SchemaEnvironment = "HD2RUNTIME_CUSTOM_SCHEMA";
    public const string BundledSchemaResource = "HD2RuntimeGUI.Core.CustomContent.Targets.CustomStratagemSchema-0.30.3.json";
    private static readonly Lazy<ICustomContentTarget> current = new(Load);
    public static ICustomContentTarget Current => current.Value;
    /// <summary>Why the schema named by HD2RUNTIME_CUSTOM_SCHEMA was not used (null when it was, or when none is named).</summary>
    public static string? OverrideIssue { get; private set; }
    public static byte[] BundledSchema()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(BundledSchemaResource)!;
        using var buffer = new MemoryStream(); stream.CopyTo(buffer); return buffer.ToArray();
    }
    public static ICustomContentTarget FromSchema(byte[] json, string source) => new Runtime030Target(CustomSchemaReader.Read(json, source), json);
    private static ICustomContentTarget Load()
    {
        if (Environment.GetEnvironmentVariable(SchemaEnvironment) is { Length: > 0 } path)
        {
            try
            {
                var target = FromSchema(File.ReadAllBytes(path), Path.GetFullPath(path));
                if (target.Schema.Format == Runtime030Target.Format) return target;
                OverrideIssue = path + ": " + target.Schema.Format;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException) { OverrideIssue = path + ": " + e.Message; }
        }
        return FromSchema(BundledSchema(), "bundled");
    }
}

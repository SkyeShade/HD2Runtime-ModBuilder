using System.Runtime.CompilerServices;
using System.Text.Json;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Generation;

/// <summary>0.30.0 call-in codes (hd2.fields.stratagem.calldown_code, type calldown_code; HD2Runtime docs/stratagem-calldown-code.md): a
/// plain list of 1 to 9 of the published directions. A code equal to another stratagem's native code needs allow_unverified_effect (which
/// stratagem the game calls for two equal codes is not proven); Runtime checks every native code of the game, not only the catalogued ones,
/// so the uncatalogued natives (mission objectives, Reinforce, ...) come from the custom stratagem schema's nativeCodes, the same table.</summary>
public static class StratagemCode
{
    public static readonly IReadOnlyList<string> Directions = ["up", "right", "down", "left"];
    public static JsonElement Normalize(StratagemField f, JsonElement value)
    {
        var directions = f.Directions ?? [.. Directions]; int min = f.MinLength ?? 1, max = f.MaxLength ?? 9;
        if (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() is var n && n >= min && n <= max
            && value.EnumerateArray().All(x => x.ValueKind == JsonValueKind.String && directions.Contains(x.GetString())))
            return JsonSerializer.SerializeToElement(value.EnumerateArray().Select(x => x.GetString()!).ToArray());
        throw new InvalidDataException(CoreText.Format("Messages.Build.Stratagem.Code.Invalid", min, max, string.Join(", ", directions)));
    }
    public static IReadOnlyList<string> Of(JsonElement value) => value.ValueKind == JsonValueKind.Array
        ? value.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString()! : "").ToArray() : [];
    public static JsonElement Value(IEnumerable<string> code) => JsonSerializer.SerializeToElement(code.ToArray());
    public static bool Same(IReadOnlyList<string> a, IReadOnlyList<string> b) => a.SequenceEqual(b, StringComparer.Ordinal);
    /// <summary>Whether one code starts the other (and is shorter): the game's matcher then calls the shorter one first.</summary>
    public static bool Starts(IReadOnlyList<string> shorter, IReadOnlyList<string> longer) => shorter.Count < longer.Count && longer.Take(shorter.Count).SequenceEqual(shorter, StringComparer.Ordinal);
    public static string Lua(JsonElement value) => "{" + string.Join(",", Of(value).Select(LuaGenerator.Quote)) + "}";
    public static string Arrow(string direction) => direction switch { "up" => "↑", "down" => "↓", "left" => "←", "right" => "→", _ => "?" };
    public static string Arrows(JsonElement value) => string.Join(" ", Of(value).Select(Arrow));

    /// <summary>A native code of the game: a catalogued stratagem's (its published currentDefault) or, with Stratagem null, a stratagem the
    /// catalogue does not list (Runtime still refuses an equal code without the opt-in).</summary>
    public sealed record Native(string? Stratagem, IReadOnlyList<string> Code);
    private static readonly ConditionalWeakTable<StratagemCatalog, IReadOnlyList<Native>> natives = new();
    public static IReadOnlyList<Native> NativeCodes(StratagemCatalog catalog) => natives.GetValue(catalog, c =>
    {
        var list = c.FieldInstances.Where(f => f.Type == StratagemField.CalldownCode).Select(f => new Native(f.Target.Stratagem, Of(f.CurrentDefault))).ToList();
        try
        {
            foreach (var n in CustomContent.CustomContentTargets.Current.Schema.Catalogs.NativeCodes)
                if (!(n.Name != null && c.Root(n.Name) != null) && !list.Any(x => x.Stratagem == null && Same(x.Code, n.Code))) list.Add(new(null, n.Code));
        }
        catch (Exception e) when (e is InvalidDataException or IOException) { }
        return list;
    });
    /// <summary>The native codes (other than this stratagem's own) the value equals: what makes Runtime require allow_unverified_effect.</summary>
    public static IReadOnlyList<Native> EqualNative(StratagemCatalog catalog, StratagemField f, JsonElement value)
    {
        var code = Of(value);
        return NativeCodes(catalog).Where(n => n.Stratagem != f.Target.Stratagem && Same(n.Code, code)).ToArray();
    }
    /// <summary>Native codes the value starts or is started by (no opt-in; the shorter one is called first while both are equipped). Stratagems
    /// a mission grants are outside the catalogue (Stratagem null) and count too: Runtime's notes ask to check them.</summary>
    public static IReadOnlyList<Native> Overlapping(StratagemCatalog catalog, StratagemField f, JsonElement value)
    {
        var code = Of(value);
        return NativeCodes(catalog).Where(n => n.Stratagem != f.Target.Stratagem && (Starts(code, n.Code) || Starts(n.Code, code))).ToArray();
    }
    public static bool EffectRequired(StratagemCatalog? catalog, StratagemField f, JsonElement desired) =>
        !Same(Of(desired), Of(f.CurrentDefault)) && (catalog == null || EqualNative(catalog, f, desired).Count > 0);

    /// <summary>Codes this project gives two stratagems at once: another enabled code edit of this project with the same value, or a custom
    /// stratagem of the project with that code. (A value equal to an unedited stratagem's native code is the opt-in case, EqualNative.)</summary>
    public sealed record Clash(string Other, bool IsCustom);
    public static IReadOnlyList<Clash> ProjectClashes(ModProject? project, StratagemCatalog catalog, StratagemField f, JsonElement value)
    {
        if (project == null) return [];
        var code = Of(value);
        var edits = project.StratagemChanges.Where(c => c.Enabled && c.FieldType == StratagemField.CalldownCode && c.Stratagem != f.Target.Stratagem && Same(Of(c.DesiredValue), code))
            .Select(c => new Clash(c.Stratagem, false));
        var custom = (project.CustomContent?.Stratagems ?? []).Where(s => Same(s.Code, code)).Select(s => new Clash(s.Name.Length > 0 ? s.Name : s.Id, true));
        return edits.Concat(custom).DistinctBy(c => c.Other).ToArray();
    }
}

/// <summary>0.30.0 presentation (type stratagem_presentation; HD2Runtime docs/stratagem-presentation.md): a value names a catalogued stratagem
/// (presentationSources) whose own name, cased name, description or icon the field takes; the field's own stratagem is its native value. The
/// icon also takes the mod's own image, hd2.resources.image(id) (docs/custom-images.md), saved as {"image": id}: the PNG lives in the project's
/// image store (CustomContentSection.Images, shared with custom content) and is compiled into the mod's archive as the SDK build does.</summary>
public static class StratagemPresentation
{
    public const string ImageProperty = "image", CustomImages = "image";
    public static bool TakesImage(StratagemField f) => f.Type == StratagemField.Presentation && f.CustomValues?.Split(',').Contains(CustomImages) == true;
    public static JsonElement Normalize(StratagemField f, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 and <= 256 } name && !name.Any(char.IsControl)) return value.Clone();
        if (TakesImage(f) && Image(value) is { } id) return ImageValue(id);
        throw new InvalidDataException(CoreText.Format(TakesImage(f) ? "Messages.Build.Stratagem.Presentation.ValueOrImage" : "Messages.Build.Stratagem.Presentation.Value", f.DisplayName));
    }
    /// <summary>The project image a value names, or null for a stratagem name.</summary>
    public static string? Image(JsonElement value) => value.ValueKind == JsonValueKind.Object && value.EnumerateObject().Count() == 1
        && value.TryGetProperty(ImageProperty, out var id) && id.ValueKind == JsonValueKind.String && CustomContent.CustomIds.ValidImage(id.GetString()) ? id.GetString() : null;
    public static JsonElement ImageValue(string id) => JsonSerializer.SerializeToElement(new Dictionary<string, string> { [ImageProperty] = id });
    /// <summary>A stratagem name must be one Runtime offers for this field (presentationSources, less its exclusions) or the field's own.</summary>
    public static void CheckSource(StratagemCatalog catalog, StratagemField f, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String) return;
        var name = value.GetString()!;
        if (name != f.CurrentDefault.GetString() && catalog.PresentationSources?.Offers(f.SemanticFieldId, name) != true)
            throw new InvalidDataException(CoreText.Format("Messages.Build.Stratagem.Presentation.Source", name, f.DisplayName));
    }
    public static IEnumerable<string> Sources(StratagemCatalog catalog, StratagemField f) =>
        (catalog.PresentationSources?.Stratagems ?? []).Where(s => s == f.CurrentDefault.GetString() || catalog.PresentationSources!.Offers(f.SemanticFieldId, s));
    public static string Lua(JsonElement value) => Image(value) is { } id ? "hd2.resources.image(" + LuaGenerator.Quote(id) + ")" : LuaGenerator.Quote(value.GetString()!);
    public static string Display(JsonElement value) => Image(value) is { } id ? CoreText.Format("StratagemPresentation.CustomImage", id) : value.GetString() ?? "";

    /// <summary>Images the project's stratagem icon edits name (enabled or not: a disabled edit keeps its image).</summary>
    public static IEnumerable<string> ImageReferences(ModProject? project) => (project?.StratagemChanges ?? []).Where(c => c.FieldType == StratagemField.Presentation)
        .Select(c => Image(c.DesiredValue)).OfType<string>();
    /// <summary>The images enabled stratagem icon edits write, compiled as the SDK build compiles images/&lt;id&gt;.png, less those already compiled.</summary>
    public static IReadOnlyList<CustomContent.CompiledImage> Compile(ModProject project, IEnumerable<CustomContent.CompiledImage>? compiled = null)
    {
        var done = (compiled ?? []).Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        var used = project.StratagemChanges.Where(c => c.Enabled && c.FieldType == StratagemField.Presentation).Select(c => Image(c.DesiredValue)).OfType<string>()
            .Where(id => !done.Contains(id)).ToHashSet(StringComparer.Ordinal);
        return used.Order(StringComparer.Ordinal).Select(id => project.CustomContent?.Images.FirstOrDefault(i => i.Id == id)
                ?? throw new InvalidDataException(CoreText.Format("Messages.Build.Stratagem.Presentation.ImageMissing", id)))
            .Select(i => CustomContent.Runtime030Target.CompileImage(i, project)).ToArray();
    }
}

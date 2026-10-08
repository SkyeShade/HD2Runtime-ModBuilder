using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.CustomContent;

/// <summary>Every check of a project's custom content: ModBuilder's own (ids, names, source images, references between custom objects),
/// then the target Runtime's (ranges, donors, family rules, what that Runtime can build). Errors refuse the build; the rest is shown where
/// the object is edited.</summary>
public static class CustomContentValidator
{
    public static IReadOnlyList<CustomDiagnostic> Validate(ModProject project, ICustomContentTarget target, SdkMetadata? sdk)
    {
        if (project.CustomContent is not { } content) return [];
        var list = new List<CustomDiagnostic>();
        foreach (var s in content.Stratagems)
        {
            var owner = CustomDiagnostic.StratagemOwner(s.Id);
            if (!CustomIds.Valid(s.Id)) list.Add(new(CustomSeverity.Error, CustomDiagnostic.Identity, owner, "id", CoreText.Format("CustomContent.Check.IdInvalid", CustomIds.MaxLength)));
            else if (content.Stratagems.Count(o => o.Id == s.Id) > 1) list.Add(new(CustomSeverity.Error, CustomDiagnostic.Identity, owner, "id", CoreText.Format("CustomContent.Check.IdDuplicate", s.Id)));
            else list.Add(new(CustomSeverity.Pass, CustomDiagnostic.Identity, owner, "id", CoreText.Format("CustomContent.Check.IdUnique", s.Id)));
            if (s.Icon == null) list.Add(new(CustomSeverity.Error, CustomDiagnostic.Identity, owner, "icon", CoreText.Get("CustomContent.Check.IconMissing")));
            else if (content.Images.FirstOrDefault(i => i.Id == s.Icon) is not { } image) list.Add(new(CustomSeverity.Error, CustomDiagnostic.Identity, owner, "icon", CoreText.Get("CustomContent.Check.IconMissing")));
            else if (ImageIssue(image) is { } issue) list.Add(new(CustomSeverity.Error, CustomDiagnostic.Identity, owner, "icon", CoreText.Format("CustomContent.Check.IconInvalid", issue)));
            else list.Add(new(CustomSeverity.Pass, CustomDiagnostic.Assets, owner, "icon", CoreText.Format("CustomContent.Check.IconReady", image.Id)));
            // References from a stratagem to the project's own custom projectiles (a delivered weapon's round).
            foreach (var reference in CustomContentGraph.ReferencesOf(s).Where(r => r.IsCustom && content.Projectiles.All(p => p.Id != r.Id)))
                list.Add(new(CustomSeverity.Error, CustomDiagnostic.Delivery, owner, null, CoreText.Format("CustomContent.Check.ProjectileMissing", reference.Id)));
        }
        foreach (var p in content.Projectiles)
        {
            var owner = CustomDiagnostic.ProjectileOwner(p.Id);
            if (!CustomIds.Valid(p.Id)) list.Add(new(CustomSeverity.Error, CustomDiagnostic.Identity, owner, "id", CoreText.Format("CustomContent.Check.IdInvalid", CustomIds.MaxLength)));
            else if (content.Projectiles.Count(o => o.Id == p.Id) > 1) list.Add(new(CustomSeverity.Error, CustomDiagnostic.Identity, owner, "id", CoreText.Format("CustomContent.Check.ProjectileIdDuplicate", p.Id)));
            else list.Add(new(CustomSeverity.Pass, CustomDiagnostic.Identity, owner, "id", CoreText.Format("CustomContent.Check.IdUnique", p.Id)));
            if (string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 64 || p.Name.Any(char.IsControl))
                list.Add(new(CustomSeverity.Error, CustomDiagnostic.Identity, owner, "name", CoreText.Format("CustomContent.Check.NameLength", 64)));
        }
        list.AddRange(target.Validate(content, project, sdk));
        return list;
    }
    public static string? ImageIssue(CustomImage image)
    {
        byte[] png;
        try { png = Convert.FromBase64String(image.Png); } catch (FormatException) { return CoreText.Get("CustomContent.Icon.NotPng"); }
        if (!string.Equals(Sha256(png), image.Sha256, StringComparison.OrdinalIgnoreCase)) return CoreText.Get("CustomContent.Icon.Checksum");
        return CustomIconCompiler.IconIssue(png);
    }
    public static string Sha256(byte[] data) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)).ToLowerInvariant();
    public static IEnumerable<CustomDiagnostic> Errors(IEnumerable<CustomDiagnostic> list) => list.Where(d => d.Severity == CustomSeverity.Error);
}

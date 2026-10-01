using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Generation;

/// <summary>HD2Arsenal presentation (ModProject.Arsenal). What HD2Arsenal 0.36.2 reads from a mod's manifest.json when it imports the
/// ZIP (modsHandler extractFromManifest / iconHandler getModPackIcon): Name (the library label), Description (shown as plain text: it
/// removes anything between '&lt;' and '&gt;' and trims), IconPath (an image resolved relative to the ZIP root, ignored when the file is
/// missing). Its own Manifest Builder packages the icon at the ZIP root under its own file name and accepts .jpg, .png, .jpeg, .gif and
/// .webp. ModBuilder writes the same: the user's description before the dependency line every manifest description states (ModTemplate
/// convention), and the icon at the ZIP root with IconPath naming it. Arsenal is never needed to export.</summary>
public static class Arsenal
{
    public const int MaxDescription = 4000;
    public static readonly IReadOnlyList<string> ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".webp"];
    private static readonly Regex Tag = new("<[^>]*>");

    public static string NormalizeDescription(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
    /// <summary>The manifest description: the user's text, then the dependency statement ModBuilder always writes.</summary>
    public static string ManifestDescription(ModProject project, string dependencies) =>
        project.Arsenal?.Description is { Length: > 0 } text ? text + "\n\n" + dependencies : dependencies;
    /// <summary>The icon's path in the ZIP (its file name at the root, as Arsenal's Manifest Builder packages it), or null without one.</summary>
    public static string? IconEntry(ModProject project) => project.Arsenal?.Icon is { Length: > 0 } icon ? Path.GetFileName(icon) : null;

    /// <summary>Why Arsenal would not show the description as written, or null.</summary>
    public static string? DescriptionIssue(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        if (text.Length > MaxDescription) return CoreText.Format("Arsenal.Description.TooLong", MaxDescription);
        if (text.Any(c => char.IsControl(c) && c is not '\n' and not '\t')) return CoreText.Get("Arsenal.Description.Control");
        if (Tag.Match(text) is { Success: true } tag) return CoreText.Format("Arsenal.Description.Html", tag.Value);
        return null;
    }

    /// <summary>Why the icon cannot be packaged, from its path and its first bytes (null when it can): a full path to an existing,
    /// readable image of a type Arsenal accepts whose content is that type, with a file name that is a safe ZIP path.</summary>
    public static string? IconIssue(string icon)
    {
        if (PackagedFiles.SourceIssue(icon) is { } source) return source;
        var name = Path.GetFileName(icon);
        if (!ImageExtensions.Contains(Path.GetExtension(name).ToLowerInvariant())) return CoreText.Format("Arsenal.Icon.Type", name);
        if (PackagedFiles.DestinationIssue(name) is { } path) return path;
        try
        {
            using var stream = new FileStream(icon, FileMode.Open, FileAccess.Read, FileShare.Read);
            var head = new byte[16]; var read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
            return ImageContent(head.AsSpan(0, read), name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return CoreText.Format("PackagedFiles.Source.Unreadable", icon, ex.Message);
        }
    }
    // The file's signature must match its extension, so Arsenal does not show a broken image.
    private static string? ImageContent(ReadOnlySpan<byte> head, string name)
    {
        var extension = Path.GetExtension(name).ToLowerInvariant();
        var matches = extension switch
        {
            ".png" => head.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
            ".jpg" or ".jpeg" => head.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]),
            ".gif" => head.StartsWith("GIF87a"u8) || head.StartsWith("GIF89a"u8),
            ".webp" => head.Length >= 12 && head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8),
            _ => false,
        };
        return matches ? null : CoreText.Format("Arsenal.Icon.Content", name, extension.TrimStart('.').ToUpperInvariant());
    }

    /// <summary>Checks the description and adds the icon to the export's entries (exact bytes, at IconEntry), refusing the export with
    /// the reason when either cannot be written as configured. Without Arsenal presentation nothing changes.</summary>
    public static void AddTo(ModProject project, IDictionary<string, byte[]> entries)
    {
        if (DescriptionIssue(project.Arsenal?.Description) is { } description)
            throw new InvalidDataException(CoreText.Get("Arsenal.Description.Refused") + "\n" + description);
        if (project.Arsenal?.Icon is not { Length: > 0 } icon) return;
        var name = Path.GetFileName(icon);
        InvalidDataException Refused(string reason) => new(CoreText.Format("Arsenal.Icon.Refused", name) + "\n" + reason);
        if (IconIssue(icon) is { } issue) throw Refused(issue);
        if (entries.Keys.Any(k => k.Equals(name, StringComparison.OrdinalIgnoreCase) || k.StartsWith(name + "/", StringComparison.OrdinalIgnoreCase)))
            throw Refused(CoreText.Format("PackagedFiles.Destination.Generated", name));
        var bytes = PackagedFiles.ReadSource(icon, Refused);
        if (ImageContent(bytes, name) is { } content) throw Refused(content);
        entries.Add(name, bytes);
    }
}

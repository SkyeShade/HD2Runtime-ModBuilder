using HD2RuntimeGUI.Core.CustomContent;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Models;
using StratagemField = HD2RuntimeGUI.Core.Metadata.StratagemField;

namespace HD2RuntimeGUI.Core.Services;

public sealed partial class BuilderWorkspace
{
    private readonly IStratagemChangeService stratagemChanges = new StratagemChangeService();
    public BuilderWorkspace(Projects.IProjectStore store, Projects.IProjectService projects, Metadata.ISdkCache cache, ISdkUpdateService updates,
        IChangeService changes, ILuaGenerator generator, IModExporter exporter, IFolderOpener folders, IProjectFilePicker picker,
        Storage.AppPaths paths, IWeaponChangeService weaponChanges, IProjectileChangeService projectileChanges,
        ICompositionChangeService compositionChanges, ISupportChangeService supportChanges, IStratagemChangeService stratagemChanges)
        : this(store, projects, cache, updates, changes, generator, exporter, folders, picker, paths, weaponChanges, projectileChanges, compositionChanges, supportChanges)
        => this.stratagemChanges = stratagemChanges;
    public string? StratagemIssue(StratagemChange c)
    { try { stratagemChanges.Validate(Project!, Metadata!, c); return null; } catch (InvalidDataException e) { return e.Message; } }
    private async Task EditStratagemAsync(Action<ModProject> edit)
    {
        var project = Project; await weaponEditGate.WaitAsync();
        try
        {
            if (project == null || !ReferenceEquals(project, Project)) throw new InvalidOperationException(CoreText.Get("Messages.Workspace.ProjectChanged"));
            var previous = project.StratagemChanges.ToList(); var approvals = new Dictionary<string, string>(project.StratagemApprovals); var format = project.FormatVersion;
            var content = project.CustomContent; project.CustomContent = content == null ? null : Clone(content);
            try
            {
                edit(project); project.StratagemChanges.RemoveAll(c => StratagemChangeService.NoOp(Metadata!, c) && !ModOptionsService.HasOption(project, ModOptionsService.StratagemKey(c.InstanceKey)));
                // Format 5 adds entity/weapon graph identities to stratagem changes.
                if (project.StratagemChanges.Count > 0) project.FormatVersion = Math.Max(project.FormatVersion, 5);
                PruneStratagemImages(project);
                await SaveChangesAsync();
            }
            catch { project.StratagemChanges = previous; project.StratagemApprovals = approvals; project.FormatVersion = format; project.CustomContent = content; throw; }
        }
        finally { weaponEditGate.Release(); }
    }
    public Task SetStratagemAsync(string instance, string value, bool acceptBaseline = false) => EditStratagemAsync(p => ApplyStratagem(p, instance, value, acceptBaseline));
    private void ApplyStratagem(ModProject p, string instance, string value, bool acceptBaseline)
    {
        var catalog = StratagemChangeService.Catalog(Metadata!); var f = catalog.Field(instance);
        var next = stratagemChanges.Create(Metadata!, instance, value);
        var old = StratagemChangeService.Saved(p, catalog, f);
        if (old != null)
        {
            // Keep the original scoped handle when editing the Eagle shared system through another consumer.
            var handle = catalog.Resolve(old)!;
            next = stratagemChanges.Create(Metadata!, handle.InstanceKey, value) with { Id = old.Id, Enabled = old.Enabled, EnsureEnabled = old.EnsureEnabled,
                Notes = old.Notes, Group = old.Group, EffectAcknowledgement = old.EffectAcknowledgement, ExpectedValue = acceptBaseline ? f.CurrentDefault : old.ExpectedValue,
                CapabilityEvidence = acceptBaseline ? StratagemChangeService.Evidence(handle) : old.CapabilityEvidence,
                BaselineSdkVersion = acceptBaseline ? Metadata!.Version : old.BaselineSdkVersion };
            if (!acceptBaseline && old.InstanceKey != handle.InstanceKey) next = next with { InstanceKey = old.InstanceKey };
            p.StratagemChanges.Remove(old);
        }
        // A value back to vanilla keeps the edit while the field has an in-game option (an option-only edit).
        if (!StratagemScalar.Equal(f, next.DesiredValue, f.CurrentDefault) || ModOptionsService.HasOption(p, ModOptionsService.StratagemKey(next.InstanceKey))) p.StratagemChanges.Add(next);
    }
    /// <summary>0.30.0: applies a call-in code and, in the same save, restores the native codes of the named stratagems (a code this project
    /// also gave them, resolved where the code is applied).</summary>
    public Task SetStratagemCodeAsync(string instance, string value, IReadOnlyCollection<string> restore) => EditStratagemAsync(p =>
    {
        p.StratagemChanges.RemoveAll(c => c.FieldType == StratagemField.CalldownCode && restore.Contains(c.Stratagem));
        ApplyStratagem(p, instance, value, false);
    });
    /// <summary>0.30.0: takes every presentation field of a stratagem (name, cased name, description, icon) from one source stratagem in one
    /// save; a field the source cannot give (Runtime's exclusions) keeps the stratagem's own value. The source being the stratagem itself
    /// restores its own presentation. keepImage leaves an icon that shows the mod's own image as it is.</summary>
    public Task SetStratagemPresentationAsync(string stratagem, string source, bool keepImage = false) => EditStratagemAsync(p =>
    {
        var catalog = StratagemChangeService.Catalog(Metadata!);
        foreach (var f in catalog.FieldInstances.Where(f => f.Target.Stratagem == stratagem && f.Type == StratagemField.Presentation && f.Editable))
        {
            if (keepImage && StratagemChangeService.Saved(p, catalog, f) is { } saved && StratagemPresentation.Image(saved.DesiredValue) != null) continue;
            var value = StratagemPresentation.Sources(catalog, f).Contains(source) ? source : f.CurrentDefault.GetString()!;
            ApplyStratagem(p, f.InstanceKey, System.Text.Json.JsonSerializer.Serialize(value), false);
        }
    });
    // ---- 0.30.0 presentation_icon: the mod's own image (hd2.resources.image) ---------------------------------------------------------------

    /// <summary>The project image a stratagem icon edit shows (null: a stratagem's own icon, or no edit).</summary>
    public CustomImage? StratagemImage(string instance) => Metadata?.Stratagems is { } catalog && catalog.Find(instance) is { } f
        && StratagemChangeService.Saved(Project, catalog, f) is { } saved && StratagemPresentation.Image(saved.DesiredValue) is { } id ? CustomImageOf(id) : null;
    /// <summary>Gives a stratagem's presentation_icon the mod's own icon: the PNG is kept in the project's image store (the one custom content
    /// uses) and the edit names it, in one save. A later import replaces that image in place; an image something else also shows is left to it.</summary>
    public Task SetStratagemImageAsync(string instance, byte[] png, string? sourceName) => EditStratagemAsync(p =>
    {
        if (png.Length > CustomContentShape.MaxPngBytes) throw new InvalidDataException(CoreText.Format("CustomContent.Icon.TooLarge", CustomContentShape.MaxPngBytes / 1024));
        if (CustomIconCompiler.IconIssue(png) is { } issue) throw new InvalidDataException(issue);
        var catalog = StratagemChangeService.Catalog(Metadata!); var f = catalog.Field(instance);
        if (!StratagemPresentation.TakesImage(f)) throw new InvalidDataException(CoreText.Format("Messages.Build.Stratagem.Presentation.Value", f.DisplayName));
        var section = p.CustomContent ??= new CustomContentSection();
        var current = StratagemChangeService.Saved(p, catalog, f) is { } saved ? StratagemPresentation.Image(saved.DesiredValue) : null;
        if (current != null && section.Images.FirstOrDefault(i => i.Id == current) is { } own
            && StratagemPresentation.ImageReferences(p).Concat(Runtime030Target.UsedImageReferences(section)).Count(x => x == own.Id) == 1)
        { own.Png = Convert.ToBase64String(png); own.Sha256 = CustomContentValidator.Sha256(png); own.SourceName = sourceName; return; }
        var basis = (CustomIds.Suggest(f.Target.Stratagem, [], "stratagem") ?? "stratagem") + "_icon";
        var image = new CustomImage { Id = CustomIds.Unique(basis, section.Images.Select(i => i.Id)), Png = Convert.ToBase64String(png),
            Sha256 = CustomContentValidator.Sha256(png), SourceName = sourceName };
        section.Images.Add(image);
        ApplyStratagem(p, instance, StratagemPresentation.ImageValue(image.Id).GetRawText(), false);
    });
    public async Task ImportStratagemImageAsync(string instance, string path)
    {
        var info = new FileInfo(Path.GetFullPath(path.Trim()));
        if (!info.Exists) throw new InvalidDataException(CoreText.Format("PackagedFiles.Source.Missing", info.FullName));
        if (info.Length > CustomContentShape.MaxPngBytes) throw new InvalidDataException(CoreText.Format("CustomContent.Icon.TooLarge", CustomContentShape.MaxPngBytes / 1024));
        await SetStratagemImageAsync(instance, await File.ReadAllBytesAsync(info.FullName), info.Name);
    }
    /// <summary>How the build prepares the icon's PNG: automatic (masks kept, red-and-white art converted) or raw.</summary>
    public Task SetStratagemImagePreparationAsync(string instance, string preparation) => EditStratagemAsync(p =>
    {
        if (preparation is not (CustomImage.Auto or CustomImage.Raw)) throw new InvalidDataException(CoreText.Format("CustomContent.Icon.Preparation", preparation));
        var catalog = StratagemChangeService.Catalog(Metadata!);
        if (StratagemChangeService.Saved(p, catalog, catalog.Field(instance)) is { } saved && StratagemPresentation.Image(saved.DesiredValue) is { } id
            && p.CustomContent?.Images.FirstOrDefault(i => i.Id == id) is { } image) image.Preparation = preparation;
    });
    private readonly Dictionary<string, string> stratagemPreviews = [];
    /// <summary>The icon as the game draws it (its masks in the stratagem's category colour and white), or null when it cannot be prepared.</summary>
    public string? StratagemImagePreviewUri(CustomImage image, (byte, byte, byte) colour)
    {
        var key = image.Sha256 + image.Preparation + colour;
        if (stratagemPreviews.TryGetValue(key, out var uri)) return uri;
        try { uri = "data:image/png;base64," + Convert.ToBase64String(CustomIconCompiler.Preview(Convert.FromBase64String(image.Png), image.Preparation, colour)); }
        catch (Exception e) when (e is InvalidDataException or FormatException) { return null; }
        if (stratagemPreviews.Count > 64) stratagemPreviews.Clear();
        return stratagemPreviews[key] = uri;
    }
    // Images neither a custom object nor a stratagem icon edit shows are dropped; a section left with nothing at all is omitted again.
    private static void PruneStratagemImages(ModProject p)
    {
        if (p.CustomContent is not { } section) return;
        var used = Runtime030Target.UsedImageReferences(section).Concat(StratagemPresentation.ImageReferences(p)).ToHashSet(StringComparer.Ordinal);
        if (section.Images.RemoveAll(i => !used.Contains(i.Id)) > 0 && section.IsEmpty && section.Images.Count == 0 && section.Models.Count == 0) p.CustomContent = null;
    }
    public Task ResetStratagemAsync(string? stratagem = null, string? instance = null) => EditStratagemAsync(p =>
    {
        var catalog = StratagemChangeService.Catalog(Metadata!);
        var saved = instance == null ? null : catalog.Find(instance) is { } f ? StratagemChangeService.Saved(p, catalog, f) : null;
        p.StratagemChanges.RemoveAll(c => (stratagem == null || c.Stratagem == stratagem) && (instance == null || c.InstanceKey == instance || c == saved));
    });
    // One acknowledgement per exact published shared scope; it records the reviewed consumer list so scope changes invalidate it.
    public Task SetStratagemApprovalAsync(string instance, bool approved) => EditStratagemAsync(p =>
    {
        var f = StratagemChangeService.Catalog(Metadata!).Field(instance);
        if (approved && f.AllowSharedRequired) p.StratagemApprovals[f.ScopeKey] = StratagemChangeService.ApprovalEvidence(f);
        else p.StratagemApprovals.Remove(f.ScopeKey);
    });
    // 0.26.0: Runtime's allow_unverified_effect opt-in for one saved change (mission uses). Kept when the value changes.
    public Task SetStratagemEffectAcknowledgedAsync(string instance, bool acknowledged) => EditStratagemAsync(p =>
    {
        var catalog = StratagemChangeService.Catalog(Metadata!); var f = catalog.Field(instance);
        var old = StratagemChangeService.Saved(p, catalog, f) ?? throw new InvalidDataException(CoreText.Get("Messages.Workspace.ChangeBeforeAcknowledging"));
        p.StratagemChanges[p.StratagemChanges.IndexOf(old)] = old with { EffectAcknowledgement = acknowledged ? StratagemChangeService.EffectEvidence(f) : null };
    });
    public Task ToggleStratagemAsync(string instance) => EditStratagemAsync(p =>
        p.StratagemChanges = p.StratagemChanges.Select(c => c.InstanceKey == instance ? c with { Enabled = !c.Enabled } : c).ToList());
}

using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace HD2RuntimeGUI.Components.Pages;

public partial class Home
{
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private HD2RuntimeGUI.Core.GameAssets.GameIconStore Icons { get; set; } = default!;
    // Local, read-only import of the game's own icon libraries into the GUI data folder (not shipped with the tool).
    private string IconDataPath = HD2RuntimeGUI.Core.GameAssets.GameIconStore.DefaultGameDataPath() ?? "";
    private Task ImportIcons() => Run(async () => { var m = await Icons.ImportAsync(IconDataPath); Notice = $"Imported {m.Sources.Sum(s => s.Icons)} icons from the installed game."; }, "Reading icon libraries from the game data…");
    private Task RemoveIcons() => Run(() => { Icons.Clear(); Icons.DisableAutoImport(); Notice = "Imported game icons removed. Automatic import is off until you import again."; return Task.CompletedTask; });
    // Reads the game's icon libraries once in the background when an install is detected and nothing is imported yet.
    private async Task AutoImportIconsAsync()
    {
        if (Icons.AutoImportPath() is not { } path) return;
        try { await Icons.ImportAsync(path); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { IconImportError = e.Message; }
        await InvokeAsync(StateHasChanged);
    }
    private string? IconImportError;
    private string Page = "library", Search = "", TargetKey = "", FieldKey = "", NewValue = "", ChangeGroup = "Gameplay", ExportDirectory = "";
    private string ModName = "", Author = "", ResourceId = "", ModVersion = "0.1.0", Description = "";
    // The resource ID follows author and mod name until the user types their own; clearing it resumes the suggestion.
    private bool ResourceIdEdited;
    private void SuggestResourceId() { if (!ResourceIdEdited) ResourceId = HD2RuntimeGUI.Core.Projects.ProjectIdentity.SuggestResourceId(Author, ModName, Workspace.Library.Select(p => p.ResourceId)) ?? ""; }
    private void ResourceIdTyped() { ResourceIdEdited = !string.IsNullOrWhiteSpace(ResourceId); if (!ResourceIdEdited) SuggestResourceId(); }
    private string OverviewName = "", OverviewAuthor = "", OverviewVersion = "", OverviewDescription = "";
    private string? EditingWeapon;
    private string StratagemCategory = "";
    private bool IsSelected(NavItem item) => item.Page.StartsWith("stratagems:", StringComparison.Ordinal)
        ? Page == "stratagems" && StratagemCategory == item.Page["stratagems:".Length..] : Page == item.Page && (item.Page != "stratagems" || StratagemCategory == "");
    private void NavigateTo(string page)
    {
        if (page.StartsWith("stratagems:", StringComparison.Ordinal)) { StratagemCategory = page["stratagems:".Length..]; Navigate("stratagems"); }
        else if (page == "stratagems") { StratagemCategory = ""; Navigate(page); }
        else if (page == "export" && Workspace.Project != null) ShowExport();
        else Navigate(page);
    }
    private void RefreshWeaponUI() => StateHasChanged();
    private void EditWeapon(string weapon) { EditingWeapon = weapon; Navigate("player-weapons"); }
    private bool HadDialog;
    private bool Busy, EnsureEnabled = true, FocusDialog;
    private string BusyMessage = "";
    private string? Error, Notice, Dialog;
    private Guid? EditingChange;
    private Guid ActionProject;
    private CreationTicket? Ticket;
    private SdkMetadata? CreationSdk;
    private ElementReference DialogElement;
    private string PageTitle => Page switch { "stratagems" => "Stratagems", "vehicles" => "Vehicles", "backpacks" => "Backpacks", "boosters" => "Boosters", "support" => "Support equipment", "player-weapons" => "Player Weapons", "lua" => "Lua Preview", "research" => "Snapshot Research", "library" => "Projects", "overview" => "Overview", "changes" => "Changes", "export" => "Export", "settings" => "Settings", _ => Workspace.Metadata?.CategoryName(Page) ?? Page };
    private IEnumerable<SdkResource> VisibleResources => Workspace.Metadata!.Resources.Values.Where(r => r.Kind == Page && r.Label.Contains(Search, StringComparison.OrdinalIgnoreCase));
    private SdkResource? SelectedResource => Workspace.Metadata?.Resources.GetValueOrDefault(TargetKey);
    private SdkField? SelectedField => SelectedResource?.Fields.GetValueOrDefault(FieldKey);
    private int ModificationCount => (Workspace.Project?.EntityChanges.Count ?? 0) + (Workspace.Project?.StratagemChanges.Count ?? 0) + (Workspace.Project?.SupportChanges.Count ?? 0) + (Workspace.Project?.CompositionChanges.Count ?? 0) + (Workspace.Project?.ProjectileChanges.Count ?? 0) + (Workspace.Project?.Changes.Count ?? 0) + Workspace.WeaponGroups.Count(g => g.Conflict != null || FieldPresentation.Modified(g.Field, g.Representative));
    private bool LegacyDraftModified => SelectedField?.Expected != null && FieldPresentation.Parse(NewValue) is { } value && !System.Text.Json.JsonElement.DeepEquals(System.Text.Json.JsonSerializer.SerializeToElement(SelectedField.Expected), value);
    private string SupportedValues => string.Join(", ", Workspace.Metadata!.Transitions.Where(t => t.Resource == TargetKey && t.Field == FieldKey).Select(t => $"{t.Expected} → {t.Value}"));
    protected override async Task OnInitializedAsync() { await Run(Workspace.InitializeAsync, "Loading projects and checking GitHub releases…"); _ = AutoImportIconsAsync(); }
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (FocusDialog && Dialog != null) { FocusDialog = false; await JS.InvokeVoidAsync("builderDialog.focus", DialogElement); }
        if (HadDialog && Dialog == null) await JS.InvokeVoidAsync("builderDialog.restore");
        HadDialog = Dialog != null;
    }
    private async Task Run(Func<Task> action, string message = "Saving…")
    {
        if (Busy) return;
        Busy = true; Error = null; Notice = null; BusyMessage = message;
        try { await action(); } catch (Exception ex) { Error = ex.Message; } finally { Busy = false; }
    }
    private void Navigate(string page)
    {
        Page = page; Notice = null; Error = null;
        if (page == "overview" && Workspace.Project is { } p)
            (OverviewName, OverviewAuthor, OverviewVersion, OverviewDescription) = (p.DisplayName, p.Author, p.Version, p.Description);
    }
    private void ShowLibrary() => Navigate("library");
    private void ShowExport() { ExportDirectory = Workspace.Project!.ExportDirectory; Navigate("export"); }
    private async Task OpenProject(Guid id) => await Run(async () => { await Workspace.OpenAsync(id); Navigate("overview"); }, "Opening project…");
    private async Task ImportProject() => await Run(async () => { await Workspace.ImportAsync(); if (Workspace.Project != null) Navigate("overview"); }, "Opening project…");
    private async Task BeginCreate() => await Run(async () =>
    {
        ModName = Author = ResourceId = Description = ""; ModVersion = "0.1.0"; CreationSdk = null; ResourceIdEdited = false;
        Ticket = await Workspace.BeginCreationAsync();
        if (Ticket.Status.UpdateAvailable) SetDialog("update");
        else { CreationSdk = await Workspace.ResolveCreationAsync(Ticket, UpdateDecision.UseInstalled); SetDialog("create"); }
    }, "Checking latest SDK before creating a project…");
    private async Task ResolveSdk(UpdateDecision decision) => await Run(async () => { CreationSdk = await Workspace.ResolveCreationAsync(Ticket!, decision); SetDialog("create"); }, decision == UpdateDecision.InstallUpdate ? "Downloading and validating SDK…" : "Preparing project…");
    private async Task SubmitProject() => await Run(async () =>
    {
        if (string.IsNullOrWhiteSpace(ResourceId)) { ResourceIdEdited = false; SuggestResourceId(); }
        var request = new CreateProjectRequest(ModName, Author, ResourceId.Trim(), ModVersion, Description);
        if (Dialog == "duplicate") await Workspace.DuplicateAsync(ActionProject, request); else await Workspace.CreateAsync(request, CreationSdk!);
        Dialog = null; Navigate("overview");
    }, "Creating project…");
    private void SetDialog(string dialog) { Dialog = dialog; FocusDialog = true; Error = null; }
    private void CloseDialog() { if (!Busy) { Dialog = null; Error = null; } }
    private void DialogKey(KeyboardEventArgs e) { if (e.Key == "Escape") CloseDialog(); }
    private void BeginRename(ProjectSummary p) { ActionProject = p.Id; ModName = p.DisplayName; SetDialog("rename"); }
    private async Task SubmitRename() => await Run(async () => { await Workspace.RenameAsync(ActionProject, ModName); Dialog = null; });
    private void BeginRemove(ProjectSummary p) { ActionProject = p.Id; ModName = p.DisplayName; SetDialog("remove"); }
    private async Task SubmitRemove() => await Run(async () => { await Workspace.RemoveAsync(ActionProject); Dialog = null; });
    private async Task BeginDuplicate(ProjectSummary p) => await Run(async () =>
    {
        await Workspace.OpenAsync(p.Id); ActionProject = p.Id; CreationSdk = Workspace.Metadata;
        ModName = p.DisplayName + " Copy"; Author = Workspace.Project!.Author; ResourceId = p.ResourceId + "_copy"; ResourceIdEdited = true;
        ModVersion = Workspace.Project.Version; Description = Workspace.Project.Description; SetDialog("duplicate");
    });
    private void SelectCategory(string category)
    {
        Navigate(category); Search = ""; EditingChange = null;
        var resource = Workspace.Metadata!.Resources.Values.FirstOrDefault(r => r.Kind == category);
        if (resource != null) SelectResource(resource.Key); else { TargetKey = FieldKey = ""; }
    }
    private void SelectResource(string key)
    { TargetKey = key; EditingChange = null; var field = SelectedResource!.Fields.Values.OrderByDescending(f => f.Writable).FirstOrDefault(); if (field != null) SelectField(field.Name); else FieldKey = ""; }
    private void OnFieldChanged(ChangeEventArgs e) => SelectField(e.Value!.ToString()!);
    private void SelectField(string key)
    {
        FieldKey = key;
        var change = Workspace.Project!.Changes.FirstOrDefault(c => c.Target == TargetKey && c.Field == key);
        EditingChange = change?.Id; NewValue = (change?.NewValue ?? SelectedField!.Expected)?.ToJsonString() ?? "0";
        EnsureEnabled = change?.EnsureEnabled ?? true; ChangeGroup = change?.Group ?? "Gameplay";
    }
    private async Task AddChange() => await Run(async () =>
    {
        await Workspace.AddOrEditChangeAsync(TargetKey, FieldKey, NewValue, EnsureEnabled, ChangeGroup, EditingChange);
        EditingChange = null; Navigate("changes"); Notice = "Modification saved.";
    });
    private void EditChange(ModChange change)
    {
        SelectCategory(Workspace.Metadata!.Resources[change.Target].Kind); SelectResource(change.Target); SelectField(change.Field);
        EditingChange = change.Id; NewValue = change.NewValue!.ToJsonString(); EnsureEnabled = change.EnsureEnabled; ChangeGroup = change.Group;
    }
    private async Task BuildExport() => await Run(async () => { ShowExport(); await Workspace.ExportAsync(); }, "Building gameplay mod ZIP…");
    private async Task ExportFromLibrary(Guid id) => await Run(async () => { await Workspace.OpenAsync(id); ShowExport(); await Workspace.ExportAsync(); }, "Building gameplay mod ZIP…");
    private async Task SaveExportDirectory() => await Run(async () => { await Workspace.SaveExportDirectoryAsync(ExportDirectory); Notice = "Export directory saved."; });
    private async Task SaveDetails() => await Run(async () => { await Workspace.SaveDetailsAsync(OverviewName, OverviewAuthor, OverviewVersion, OverviewDescription); Notice = "Project details saved."; });
    private async Task CheckUpdates() => await Run(Workspace.CheckUpdatesAsync, "Checking GitHub releases…");
    private async Task InstallUpdate() => await Run(Workspace.InstallUpdateAsync, "Downloading and validating SDK…");
    private static string Initials(string name) => string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(n => char.ToUpperInvariant(n[0])));
}

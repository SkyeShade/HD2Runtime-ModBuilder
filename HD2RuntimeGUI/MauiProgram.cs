using Microsoft.Extensions.Logging;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.GitHub;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Projects;
using HD2RuntimeGUI.Core.Services;
using HD2RuntimeGUI.Core.Storage;
using HD2RuntimeGUI.Services;

namespace HD2RuntimeGUI;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts => { fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular"); });

        builder.Services.AddMauiBlazorWebView();
        var dataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HD2RuntimeGUI");
        // An explicit root is useful for isolated smoke tests; normal users continue to use LocalAppData.
        var configuredRoot = Environment.GetEnvironmentVariable("HD2RUNTIMEGUI_DATA_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRoot)) dataRoot = Path.GetFullPath(configuredRoot);
        builder.Services.AddSingleton(new AppPaths(dataRoot));
        builder.Services.AddSingleton(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(25) });
        builder.Services.AddSingleton<IGitHubReleaseClient, GitHubReleaseClient>();
        builder.Services.AddSingleton<IMetadataReader, MetadataReader>();
        builder.Services.AddSingleton<ISdkCache, SdkCache>();
        builder.Services.AddSingleton<ISdkUpdateService, SdkUpdateService>();
        builder.Services.AddSingleton<IProjectStore, JsonProjectStore>();
        builder.Services.AddSingleton<IProjectService, ProjectService>();
        builder.Services.AddSingleton<IChangeService, ChangeService>();
        builder.Services.AddSingleton<IWeaponChangeService, WeaponChangeService>();
        builder.Services.AddSingleton<IProjectileChangeService, ProjectileChangeService>();
        builder.Services.AddSingleton<ICompositionChangeService, CompositionChangeService>();
        builder.Services.AddSingleton<ISupportAuthoringReader, SupportAuthoringReader>();
        builder.Services.AddSingleton<ISupportChangeService, SupportChangeService>();
        builder.Services.AddSingleton<ISupportLua, SupportLua>();
        builder.Services.AddSingleton<ISemanticOperationPlanner, SemanticOperationPlanner>();
        builder.Services.AddSingleton<IPlayerWeaponCompositionReader, PlayerWeaponCompositionReader>();
        builder.Services.AddSingleton<IAdvancedCapabilitiesReader, AdvancedCapabilitiesReader>();
        builder.Services.AddSingleton<IPlayerWeaponHeatCatalogReader, PlayerWeaponHeatCatalogReader>();
        builder.Services.AddSingleton<ICompositionPlanCapabilitiesReader, CompositionPlanCapabilitiesReader>();
        builder.Services.AddSingleton<IPlayerWeaponCatalogReader, PlayerWeaponCatalogReader>();
        builder.Services.AddSingleton<IPlayerWeaponAmmoCatalogReader, PlayerWeaponAmmoCatalogReader>();
        builder.Services.AddSingleton<ILuaGenerator, LuaGenerator>();
        builder.Services.AddSingleton<IModExporter, ModExporter>();
        builder.Services.AddSingleton<IFolderOpener, WindowsFolderOpener>();
        builder.Services.AddSingleton<IProjectFilePicker, ProjectFilePicker>();
        builder.Services.AddSingleton<BuilderWorkspace>();
        builder.Services.AddSingleton<ISnapshotReader, SnapshotReader>();
        builder.Services.AddSingleton<IResearchFilePicker, ResearchFilePicker>();
        builder.Services.AddSingleton<SnapshotWorkspace>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}

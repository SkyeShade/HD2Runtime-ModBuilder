using Microsoft.Maui.LifecycleEvents;
using Microsoft.Extensions.Logging;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.GitHub;
using System.Globalization;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Projects;
using HD2RuntimeGUI.Core.Services;
using HD2RuntimeGUI.Core.Storage;
using HD2RuntimeGUI.Core.Updates;
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
#if WINDOWS
        // Window title-bar and taskbar icon: the HD2Runtime ModBuilder reticle (appicon.ico generated from Resources/AppIcon/appicon.svg).
        builder.ConfigureLifecycleEvents(events => events.AddWindows(windows => windows.OnWindowCreated(window =>
        {
            var icon = Path.Combine(AppContext.BaseDirectory, "appicon.ico");
            if (File.Exists(icon)) window.AppWindow.SetIcon(icon);
        })));
#endif

        builder.Services.AddMauiBlazorWebView();
        var dataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HD2RuntimeGUI");
        // An explicit root is useful for isolated smoke tests; normal users continue to use LocalAppData.
        var configuredRoot = Environment.GetEnvironmentVariable("HD2RUNTIMEGUI_DATA_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRoot)) dataRoot = Path.GetFullPath(configuredRoot);
        var paths = new AppPaths(dataRoot);
        builder.Services.AddSingleton(paths);
        // UI language (Settings → Language: system / en / zh-Hans): applied before the first render, changed live afterwards.
        var language = new LanguageService(paths, CultureInfo.CurrentUICulture);
        language.Apply();
        builder.Services.AddSingleton(language);
        builder.Services.AddSingleton<IUiText, UiText>();
        builder.Services.AddSingleton(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(25) });
        builder.Services.AddSingleton<IGitHubReleaseClient, GitHubReleaseClient>();
        builder.Services.AddSingleton<IMetadataReader, MetadataReader>();
        // Developer-only: bind an unpublished local Runtime SDK (sdk/ directory or SDK zip) for this run without touching the SDK cache:
        // --sdk-path, then HD2RUNTIME_SDK_PATH, then the local SDK remembered in Settings.
        var localSdk = DeveloperSdk.Resolve(Environment.GetCommandLineArgs(), Environment.GetEnvironmentVariable("HD2RUNTIME_SDK_PATH"), DeveloperSdk.Load(paths));
        builder.Services.AddSingleton(new ActiveLocalSdk(localSdk));
        builder.Services.AddSingleton<ISdkCache>(services => { var cache = ActivatorUtilities.CreateInstance<SdkCache>(services); if (localSdk != null) cache.LocalSdkPath = localSdk.Path; return cache; });
        builder.Services.AddSingleton<ISdkUpdateService, SdkUpdateService>();
        // HD2Runtime ModBuilder application updates (GitHub Releases of SkyeShade/HD2Runtime-ModBuilder), separate from SDK updates.
        builder.Services.AddSingleton<IAppReleaseClient, AppReleaseClient>();
        builder.Services.AddSingleton<IAppUpdateHost, WindowsAppUpdateHost>();
        builder.Services.AddSingleton(services => new AppUpdateService(services.GetRequiredService<IAppReleaseClient>(), services.GetRequiredService<AppPaths>(), services.GetRequiredService<IAppUpdateHost>()));
        builder.Services.AddSingleton<IProjectStore, JsonProjectStore>();
        builder.Services.AddSingleton<IProjectService, ProjectService>();
        builder.Services.AddSingleton<IChangeService, ChangeService>();
        builder.Services.AddSingleton<IWeaponChangeService, WeaponChangeService>();
        builder.Services.AddSingleton<IProjectileChangeService, ProjectileChangeService>();
        builder.Services.AddSingleton<ICompositionChangeService, CompositionChangeService>();
        builder.Services.AddSingleton<ISupportAuthoringReader, SupportAuthoringReader>();
        builder.Services.AddSingleton<ISupportChangeService, SupportChangeService>();
        builder.Services.AddSingleton<ISupportLua, SupportLua>();
        builder.Services.AddSingleton<IStratagemCatalogReader, StratagemCatalogReader>();
        builder.Services.AddSingleton<IStratagemChangeService, StratagemChangeService>();
        builder.Services.AddSingleton<IStratagemLua, StratagemLua>();
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
        builder.Services.AddSingleton<HD2RuntimeGUI.Core.GameAssets.GameIconStore>();
        builder.Services.AddSingleton<BuilderWorkspace>();
        builder.Services.AddSingleton<ISnapshotReader, SnapshotReader>();
        builder.Services.AddSingleton<IResearchFilePicker, ResearchFilePicker>();
        builder.Services.AddSingleton<IPackagedFilePicker, PackagedFilePicker>();
        builder.Services.AddSingleton<SnapshotWorkspace>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}

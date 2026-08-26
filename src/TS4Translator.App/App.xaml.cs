using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using TS4Translator.App.Services;
using TS4Translator.App.ViewModels;
using TS4Translator.Infrastructure;
using TS4Translator.Persistence;
using TS4Translator.Providers;
using TS4Translator.Sims4;
using TS4Translator.Translation;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace TS4Translator.App;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private readonly ServiceProvider _services;
    private Window? _window;

    /// <summary>
    /// The main application window. Use <c>App.Window</c> from any class that needs the window
    /// reference (for dialogs, pickers, interop, etc.).
    /// </summary>
    public static Window Window { get; private set; } = null!;

    /// <summary>The native window handle (HWND) - required to own WinRT file/folder pickers under WinUI 3.</summary>
    public static nint WindowHandle => WinRT.Interop.WindowNative.GetWindowHandle(Window);

    /// <summary>
    /// Service locator for pages, which WinUI's <c>Frame.Navigate(Type)</c> instantiates via a
    /// parameterless constructor - there is no built-in way to inject a page's ViewModel through
    /// navigation, so each page resolves its own ViewModel from here instead.
    /// </summary>
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        InitializeComponent();

        _services = new ServiceCollection()
            .AddSims4Services()
            .AddTranslationServices()
            .AddPersistenceServices(AppDataPaths.DatabaseFilePath)
            .AddInfrastructureServices()
            .AddProviderServices()
            .AddSingleton<IPathPickerService, WindowsAppSdkPathPickerService>()
            .AddTransient<HomePageViewModel>()
            .AddTransient<EditorPageViewModel>()
            .AddTransient<SettingsPageViewModel>()
            .BuildServiceProvider();
        Services = _services;
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        using (var dbContext = _services.GetRequiredService<IDbContextFactory<TranslatorDbContext>>().CreateDbContext())
        {
            dbContext.Database.Migrate();
        }

        _window = new MainWindow();
        Window = _window;
        _window.Activate();
    }
}

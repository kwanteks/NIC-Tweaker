using Kwanteks.NicTweaker.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Kwanteks.NicTweaker;

public partial class App : Application
{
    private Window? _window;
    public static ServiceProvider Services { get; private set; } = null!;
    public static DispatcherQueue? CurrentDispatcherQueue { get; private set; }
    public static Window? MainAppWindow => ((App)Current)._window;

    public App()
    {
        InitializeComponent();
        Services = AppServices.Build();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        CurrentDispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _window = new MainWindow();
        _window.Activate();

        var storage = Services.GetRequiredService<IStorageService>();
        ApplyTheme(storage.LoadSettings().Theme);
    }

    public static void ApplyTheme(string theme)
    {
        if (MainAppWindow?.Content is not FrameworkElement fe) return;
        fe.RequestedTheme = theme switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
    }

    public static void SetWindowOpacity(bool reduced)
    {
        if (MainAppWindow?.Content is UIElement element)
            element.Opacity = reduced ? 0.90 : 1.0;
    }
}

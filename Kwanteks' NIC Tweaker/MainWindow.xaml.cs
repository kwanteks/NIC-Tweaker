using Kwanteks.NicTweaker.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

namespace Kwanteks.NicTweaker;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        ConfigureAppWindow();

        var settings = App.Services.GetRequiredService<IStorageService>().LoadSettings();
        App.ApplyTheme(settings.Theme);
        App.SetWindowOpacity(settings.OpacityReduced);
    }

    private void ConfigureAppWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Title = "Network Adapter - Tweaker";
        appWindow.Resize(new SizeInt32(1550, 1000));

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Kwanteks.ico");
        if (!File.Exists(iconPath))
            iconPath = Path.Combine(AppContext.BaseDirectory, "Kwanteks.ico");
        if (File.Exists(iconPath))
            appWindow.SetIcon(iconPath);
    }
}

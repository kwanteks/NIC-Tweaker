using System.Runtime.InteropServices;
using System.Text;
using Kwanteks.NicTweaker.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Kwanteks.NicTweaker;

public partial class App : Application
{
    private const string AppUserModelId = "Kwanteks.NicTweaker";

    private Window? _window;
    private static string? _pendingImportPath;
    public static ServiceProvider Services { get; private set; } = null!;
    public static DispatcherQueue? CurrentDispatcherQueue { get; private set; }
    public static Window? MainAppWindow => ((App)Current)._window;

    public App()
    {
        try { SetCurrentProcessExplicitAppUserModelID(AppUserModelId); }
        catch { /* ignore */ }

        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        InitializeComponent();
        Services = AppServices.Build();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        CurrentDispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _pendingImportPath = ParseImportArg(Environment.GetCommandLineArgs());
        _window = new MainWindow();
        _window.Activate();

        var storage = Services.GetRequiredService<IStorageService>();
        ApplyTheme(storage.LoadSettings().Theme);
    }

    /// <summary>Consumes --import "&lt;file&gt;" once after startup.</summary>
    public static string? TakePendingImportPath()
    {
        var path = _pendingImportPath;
        _pendingImportPath = null;
        return path;
    }

    private static string? ParseImportArg(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (!string.Equals(args[i], "--import", StringComparison.OrdinalIgnoreCase))
                continue;
            var path = args[i + 1].Trim().Trim('"');
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        return null;
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

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        WriteCrashLog("UI UnhandledException", e.Exception);
        e.Handled = true;
    }

    private static void OnDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        WriteCrashLog("AppDomain.UnhandledException", e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteCrashLog("UnobservedTaskException", e.Exception);
        e.SetObserved();
    }

    private static void WriteCrashLog(string source, Exception? ex)
    {
        try
        {
            AppPaths.EnsureCreated();
            var sb = new StringBuilder();
            sb.AppendLine($"==== {DateTimeOffset.Now:o} — {source} ====");
            sb.AppendLine(ex?.ToString() ?? "(null)");
            sb.AppendLine();
            File.AppendAllText(AppPaths.CrashLogFile, sb.ToString());
            try { File.AppendAllText(AppPaths.AppLogFile, sb.ToString()); } catch { /* ignore */ }
        }
        catch { /* last resort: never throw from crash logger */ }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appID);
}

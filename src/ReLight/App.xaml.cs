using System.Windows;
using System.Windows.Interop;
using ReLight.Core;
using ReLight.Services;

namespace ReLight;

public partial class App : Application
{
    const string InstanceName = "ReLight.SingleInstance";

    Mutex? _instance;
    EventWaitHandle? _showSignal;
    AdalightDevice? _device;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            LogCrash(args.Exception);
            MessageWindow.Show(MainWindow, args.Exception.Message, MessageKind.Warning);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogCrash(args.ExceptionObject as Exception);

        // One copy only: a second start just brings the running window forward.
        _instance = new Mutex(true, InstanceName, out bool first);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceName + ".Show");
        if (!first)
        {
            _showSignal.Set();
            Shutdown();
            return;
        }

        // For screenshots of windows that are not in front (tests, documentation): draw without the video card.
        if (Environment.GetEnvironmentVariable("RELIGHT_SOFTWARE_RENDER") == "1")
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        var settings = AppSettings.Load();
        ThemeManager.Apply(settings.Theme);
        LocResources.Attach(Resources);
        Loc.SetLanguage(settings.Language);

        _device = new AdalightDevice();
        var engine = new LightEngine(settings, _device);
        var window = new MainWindow(settings, _device, engine);
        MainWindow = window;

        if (settings.StartMinimized)
            new WindowInteropHelper(window).EnsureHandle(); // tray and hotkeys come up without showing the window
        else
            window.Show();

        var signal = _showSignal;
        new Thread(() =>
        {
            while (signal.WaitOne())
                Dispatcher.BeginInvoke(window.ShowFromTray);
        }) { IsBackground = true, Name = "ReLight show signal" }.Start();
    }

    /// <summary>Keep the details for a bug report: %APPDATA%\ReLight\crash.log.</summary>
    static void LogCrash(Exception? ex)
    {
        try
        {
            System.IO.Directory.CreateDirectory(AppSettings.Folder);
            System.IO.File.AppendAllText(System.IO.Path.Combine(AppSettings.Folder, "crash.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} ReLight {typeof(App).Assembly.GetName().Version}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _device?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}

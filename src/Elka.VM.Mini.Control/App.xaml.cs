using System.Windows;
using Elka.VM.Mini.Control.Services;

namespace Elka.VM.Mini.Control;

public partial class App : Application
{
    private TrayService? _tray;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        var window = new MainWindow();
        MainWindow = window;
        _tray = new TrayService(window, () => window.CloseToTray);
        SessionEnding += (_, _) => _tray.AllowExit();
        window.Start(window.StartInTray);
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }
}

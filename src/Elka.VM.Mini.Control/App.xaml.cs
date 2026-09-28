using System.Windows;
using Elka.VM.Mini.Control.Services;

namespace Elka.VM.Mini.Control;

public partial class App : Application
{
    private TrayService? _tray;
    private SingleInstance? _instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        _instance = new SingleInstance();
        if (!_instance.IsPrimary)
        {
            _instance.RequestActivation();
            Shutdown();
            return;
        }
        using var legacy = SingleInstance.FindLegacyInstance();
        if (legacy is not null)
        {
            if (!SingleInstance.RestoreLegacyWindow(legacy))
                MessageBox.Show("An older Elka VM Mini Control is already running. Open it from the tray, or choose Exit there before starting this version.",
                    "Elka VM Mini Control", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        var window = new MainWindow();
        MainWindow = window;
        _tray = new TrayService(window, () => window.CloseToTray);
        SessionEnding += (_, _) => _tray.AllowExit();
        window.Start(window.StartInTray);
        _instance.Listen(() =>
        {
            if (!Dispatcher.HasShutdownStarted)
                Dispatcher.BeginInvoke(new Action(() => _tray?.Open()));
        });
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}

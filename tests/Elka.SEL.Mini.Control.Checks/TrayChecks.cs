using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Elka.SEL.Mini.Control;
using Elka.SEL.Mini.Control.Core;
using Elka.SEL.Mini.Control.Services;
using Elka.SEL.Mini.Control.Views;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

internal static partial class Program
{
    private static void StartupChecks(string output)
    {
        string keyPath = @"Software\ElkaSoft\MiniControlChecks\" + Guid.NewGuid().ToString("N");
        string executable = Path.Combine(output, "startup path with spaces.exe");
        File.WriteAllText(executable, "File existence fixture; never executed.");
        var startup = new WindowsStartup(executable, keyPath);
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(keyPath);
            key.SetValue("Unrelated app", "Preserve this");
            Check(!startup.Enabled, "Startup is opt-in and initially unregistered");
            startup.SetEnabled(true);
            Check(startup.Enabled && (string?)key.GetValue("Elka SEL Mini Control") == $"\"{executable}\"", "Windows startup quotes executable paths containing spaces");
            startup.SetEnabled(false);
            Check(!startup.Enabled && (string?)key.GetValue("Unrelated app") == "Preserve this", "Disabling startup removes only this app's registration");
            bool failed = false;
            try { new WindowsStartup(executable + ".missing", keyPath).SetEnabled(true); }
            catch (InvalidOperationException) { failed = true; }
            Check(failed && !startup.Enabled, "Missing startup executable is rejected without registering a broken entry");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false); File.Delete(executable); }
        var store = new SettingsStore(Path.Combine(output, "tray-settings.json"));
        var settings = new ControlSettings { StartWithWindows = true, StartInTray = true, CloseToTray = false };
        store.Save(settings); var loaded = store.Load();
        Check(loaded.StartWithWindows && loaded.StartInTray && !loaded.CloseToTray, "All three startup/tray options persist independently");
        settings.StartWithWindows = false; settings.CloseToTray = true; store.Save(settings); loaded = store.Load();
        Check(!loaded.StartWithWindows && loaded.StartInTray && loaded.CloseToTray, "Start in tray does not enable Windows startup");
        var dialog = new SettingsDialog(null, loaded, startWithWindows: true);
        Check(dialog.StartWithWindows && dialog.StartInTray && dialog.CloseToTray, "Settings reflects the actual Windows registration and saved tray choices");
        dialog.Close();
    }

    private static void TrayChecks(string output)
    {
        var fake = new FakeRemote();
        var mixer = new MixerController(fake);
        var store = new SettingsStore(Path.Combine(output, "hidden-ui-settings.json"));
        var settings = new ControlSettings
        {
            StartInTray = true, CloseToTray = true,
            Vban = new() { Enabled = true, Port = FreePort(), StreamName = "HiddenTray" }
        };
        settings.Hotkeys[0] = new(15, 0x87);
        store.Save(settings);
        var window = new MainWindow(mixer, store);
        bool closed = false; window.Closed += (_, _) => closed = true;
        using var tray = new TrayService(window, () => window.CloseToTray);
        try
        {
            window.Start(window.StartInTray);
            Check(!window.IsVisible && !window.IsLoaded && tray.Visible && mixer.Connected, "Start in tray creates a live notification icon and API session without showing the window");
            Check(fake.Scripts.Count == 0, "Hidden startup does not change mixer settings");
            fake.Select(6);
            PumpUntil(() => mixer.Source == 6);
            Check(!window.IsVisible, "SEL polling continues while the window has never been shown");
            SendMessage(new WindowInteropHelper(window).Handle, 0x0312, (IntPtr)1, IntPtr.Zero);
            Check(fake.Values["Bus[0].Sel"] == 1 && !window.IsVisible, "A global hotkey can select SEL while hidden");
            using (var sender = new UdpClient())
            {
                byte[] packet = Packet("VMC.SEL(B3)=1;", 1, settings.Vban.StreamName);
                sender.Send(packet, packet.Length, new IPEndPoint(IPAddress.Loopback, settings.Vban.Port));
            }
            PumpUntil(() => mixer.Source == 7);
            Check(!window.IsVisible, "VBAN Text remains active from hidden startup");
            Check(tray.Menu.Items.Cast<Forms.ToolStripItem>().Select(i => i.Text).SequenceEqual(["Open", "Exit"]), "Tray menu contains exactly Open and Exit");
            Check(tray.Menu.Renderer is DarkTrayRenderer && tray.Menu.BackColor == Drawing.Color.FromArgb(26, 32, 37), "Tray menu uses the Elka dark theme independently of Windows theme");
            tray.Menu.Size = tray.Menu.GetPreferredSize(Drawing.Size.Empty);
            tray.Menu.CreateControl();
            using (var bitmap = new Drawing.Bitmap(tray.Menu.Width, tray.Menu.Height))
            {
                tray.Menu.DrawToBitmap(bitmap, new Drawing.Rectangle(Drawing.Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(output, "tray-menu.png"), Drawing.Imaging.ImageFormat.Png);
            }
            tray.Menu.Items[0].PerformClick();
            Check(window.IsVisible && tray.Visible, "Tray Open restores the hidden control window");
            window.Close();
            Check(!window.IsVisible && !closed && tray.Visible, "Close to tray intercepts X and keeps the app and icon alive");
            fake.Select(4); PumpUntil(() => mixer.Source == 4);
            Check(!window.IsVisible, "Closing to tray preserves bidirectional SEL polling");
            tray.Open(); window.WindowState = WindowState.Minimized; tray.Open();
            Check(window.WindowState == WindowState.Normal, "Tray Open also restores a minimized window");
            tray.Menu.Items[1].PerformClick();
            Check(closed && !tray.Visible, "Tray Exit bypasses close-to-tray and removes the notification icon");
            using var source = new HwndSource(new HwndSourceParameters("released hotkey check") { WindowStyle = 0 });
            using var binding = new HotkeyService(source.Handle);
            Check(binding.CanRegister(settings.Hotkeys[0]!), "Tray Exit releases registered hotkeys");
            using var port = new UdpClient(settings.Vban.Port);
            Check(port.Client.IsBound, "Tray Exit releases the VBAN port");
        }
        finally { if (!closed) { tray.AllowExit(); window.Close(); } }

        var plain = new Window(); bool plainClosed = false; plain.Closed += (_, _) => plainClosed = true;
        using var plainTray = new TrayService(plain, () => false);
        new WindowInteropHelper(plain).EnsureHandle(); plain.Close();
        Check(plainClosed && !plainTray.Visible, "With Close to tray disabled, X exits and removes the icon");
        var session = new Window(); bool sessionClosed = false; session.Closed += (_, _) => sessionClosed = true;
        using var sessionTray = new TrayService(session, () => true);
        new WindowInteropHelper(session).EnsureHandle(); sessionTray.AllowExit(); session.Close();
        Check(sessionClosed && !sessionTray.Visible, "Windows session ending can bypass close-to-tray");
    }
}

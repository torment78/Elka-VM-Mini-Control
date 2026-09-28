using System.ComponentModel;
using System.Windows;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Elka.SEL.Mini.Control.Services;

public sealed class TrayService : IDisposable
{
    private readonly Window _window;
    private readonly Func<bool> _closeToTray;
    private readonly Forms.NotifyIcon _tray;
    private readonly Drawing.Icon _icon;
    private bool _exit, _disposed;
    public Forms.ContextMenuStrip Menu { get; }
    public bool Visible => !_disposed && _tray.Visible;

    public TrayService(Window window, Func<bool> closeToTray)
    {
        _window = window; _closeToTray = closeToTray;
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Elka.SEL.Mini.Control;component/Assets/ElkaMiniControl.ico"))!.Stream;
        _icon = new Drawing.Icon(stream, new Drawing.Size(32, 32));
        Menu = new Forms.ContextMenuStrip
        {
            BackColor = Drawing.Color.FromArgb(26, 32, 37),
            ForeColor = Drawing.Color.FromArgb(237, 243, 246),
            Renderer = new DarkTrayRenderer(), ShowImageMargin = false,
            Font = new Drawing.Font("Segoe UI", 10),
            Padding = new Forms.Padding(4), AccessibleName = "Elka SEL Mini Control"
        };
        Menu.Items.Add("Open", null, (_, _) => Open());
        Menu.Items.Add("Exit", null, (_, _) => Exit());
        foreach (Forms.ToolStripItem item in Menu.Items)
        {
            item.ForeColor = Menu.ForeColor;
            item.Padding = new Forms.Padding(12, 5, 12, 5);
        }
        _tray = new Forms.NotifyIcon { Icon = _icon, Text = "Elka SEL Mini Control", ContextMenuStrip = Menu, Visible = true };
        _tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) Open(); };
        _window.Closing += OnClosing;
        _window.Closed += OnClosed;
    }
    public void Open()
    {
        if (_disposed) return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
        foreach (Window owned in _window.OwnedWindows)
            if (owned.IsVisible) owned.Activate();
    }
    public void AllowExit() => _exit = true;
    public void Exit()
    {
        if (_disposed) return;
        AllowExit();
        _window.Close();
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_exit && _closeToTray()) { e.Cancel = true; _window.Hide(); }
    }
    private void OnClosed(object? sender, EventArgs e) => Dispose();
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _window.Closing -= OnClosing; _window.Closed -= OnClosed;
        _tray.Visible = false; _tray.Dispose(); Menu.Dispose(); _icon.Dispose();
    }
}

public sealed class DarkTrayRenderer : Forms.ToolStripProfessionalRenderer
{
    public DarkTrayRenderer() : base(new DarkTrayColors()) { RoundedEdges = false; }
    protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = Drawing.Color.FromArgb(237, 243, 246);
        base.OnRenderItemText(e);
    }
    private sealed class DarkTrayColors : Forms.ProfessionalColorTable
    {
        public DarkTrayColors() { UseSystemColors = false; }
        public override Drawing.Color ToolStripDropDownBackground => Drawing.Color.FromArgb(26, 32, 37);
        public override Drawing.Color MenuBorder => Drawing.Color.FromArgb(51, 65, 74);
        public override Drawing.Color MenuItemBorder => Drawing.Color.FromArgb(34, 166, 179);
        public override Drawing.Color MenuItemSelected => Drawing.Color.FromArgb(20, 52, 59);
        public override Drawing.Color MenuItemSelectedGradientBegin => MenuItemSelected;
        public override Drawing.Color MenuItemSelectedGradientEnd => MenuItemSelected;
    }
}

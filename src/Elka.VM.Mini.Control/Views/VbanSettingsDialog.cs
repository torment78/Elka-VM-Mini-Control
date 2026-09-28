using System.Windows;
using System.Windows.Controls;
using Elka.VM.Mini.Control.Core;

namespace Elka.VM.Mini.Control.Views;

public sealed class VbanSettingsDialog : SmallDialog
{
    private readonly CheckBox _enabled = new() { Content = "Enable incoming VBAN Text", Margin = new Thickness(0, 0, 0, 16) };
    private readonly TextBox _port = new(), _stream = new(), _sender = new(), _listen = new();
    public VbanSettings Settings { get; private set; }

    public VbanSettingsDialog(Window? owner, VbanSettings settings) : base(owner, "VBAN Text · Elka VM Mini Control")
    {
        Settings = settings; Width = 490;
        MaxHeight = Math.Max(400, SystemParameters.WorkArea.Height - 60);
        Content = new ScrollViewer { Content = Body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Text("Incoming VBAN Text", true);
        Text("Control SEL and Apply alongside your hotkeys or MIDI.");
        _enabled.IsChecked = settings.Enabled; Body.Children.Add(_enabled);
        Field("Listen IP (0.0.0.0 = all interfaces)", _listen, settings.ListenAddress);
        Field("UDP port", _port, settings.Port.ToString());
        Field("Stream name (must match the sender)", _stream, settings.StreamName);
        Field("Allowed sender IPv4 (blank = any)", _sender, settings.SenderAddress);
        Text("127.0.0.1 receives from this PC only. For LAN control, listen on 0.0.0.0 or this PC’s LAN IP. The sender must use the same port and stream name.");
        Text("Commands", true);
        Body.Children.Add(new TextBox
        {
            Text = VmcCommands.Examples, IsReadOnly = true, FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 12, Padding = new Thickness(10), AcceptsReturn = true, Height = 128,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 12)
        });
        Text("Top row: SEL selects that bus and keeps it selected. Master mode requires Ctrl-click on the active SEL in the app. Bottom row: Apply uses that bus’s saved destinations. Square brackets work too.");
        Actions(Button("Cancel", Close), Button("Use settings", () =>
        {
            if (!int.TryParse(_port.Text, out int port)) { Error.Text = "Enter a valid UDP port."; return; }
            var edited = new VbanSettings
            {
                Enabled = _enabled.IsChecked == true, Port = port, StreamName = _stream.Text.Trim(),
                ListenAddress = _listen.Text.Trim(), SenderAddress = _sender.Text.Trim()
            };
            try { edited.Validate(); }
            catch (System.IO.InvalidDataException ex) { Error.Text = ex.Message; return; }
            Settings = edited; DialogResult = true;
        }));
    }
    private void Field(string label, TextBox box, string value)
    {
        var text = Text(label); text.Margin = new Thickness(0, 0, 0, 5);
        box.Text = value; box.Margin = new Thickness(0, 0, 0, 12); Body.Children.Add(box);
    }
}

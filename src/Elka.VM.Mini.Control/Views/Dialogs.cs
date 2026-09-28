using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Elka.VM.Mini.Control.Core;
using Elka.VM.Mini.Control.Services;
using InputMode = Elka.VM.Mini.Control.Core.InputMode;

namespace Elka.VM.Mini.Control.Views;

public class SmallDialog : Window
{
    protected readonly StackPanel Body = new() { Margin = new Thickness(20) };
    protected readonly TextBlock Error = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    public SmallDialog(Window? owner, string title)
    {
        if (owner is not null) Owner = owner;
        SetResourceReference(StyleProperty, typeof(Window));
        Title = title; Width = 450; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false; Content = Body;
        Error.SetResourceReference(TextBlock.ForegroundProperty, "OrangeBrush");
        SourceInitialized += (_, _) => WindowAppearance.Apply(this);
    }
    protected TextBlock Text(string value, bool heading = false)
    {
        var block = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12), FontSize = heading ? 17 : 12, FontWeight = heading ? FontWeights.SemiBold : FontWeights.Normal };
        if (!heading) block.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        Body.Children.Add(block); return block;
    }
    protected Button Button(string title, Action action)
    {
        var button = new Button { Content = title, Margin = new Thickness(6, 0, 0, 0), MinWidth = 76 };
        button.Click += (_, _) => action(); return button;
    }
    protected void Actions(params Button[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        foreach (var button in buttons) row.Children.Add(button);
        Body.Children.Add(Error); Body.Children.Add(row);
    }
}

public sealed class SettingsDialog : SmallDialog
{
    private readonly RadioButton _hotkeys = new() { Content = "Hotkeys" };
    private readonly RadioButton _midi = new() { Content = "MIDI input" };
    private readonly ListBox _devices = new() { Height = 124, DisplayMemberPath = "Name" };
    private readonly CheckBox _startWithWindows = new() { Content = "Start with Windows", Margin = new Thickness(0, 0, 0, 9) };
    private readonly CheckBox _startInTray = new() { Content = "Start in tray · keep the window hidden", Margin = new Thickness(0, 0, 0, 9) };
    private readonly CheckBox _closeToTray = new() { Content = "Close to tray · X keeps the app running", Margin = new Thickness(0, 0, 0, 9) };
    private readonly ToggleButton _faderMode = new() { Content = "Fader mode", Height = 32, Width = 150, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12) };
    public bool StartWithWindows => _startWithWindows.IsChecked == true;
    public bool StartInTray => _startInTray.IsChecked == true;
    public bool CloseToTray => _closeToTray.IsChecked == true;
    public bool FaderMode => _faderMode.IsChecked == true;
    public InputMode Mode => _midi.IsChecked == true ? InputMode.Midi : InputMode.Hotkeys;
    public string? Device => (_devices.SelectedItem as MidiDevice)?.Name;
    public VbanSettings Vban { get; private set; }
    public SettingsDialog(Window? owner, ControlSettings settings, bool? startWithWindows = null, Action<SettingsDialog>? save = null) : base(owner, "Settings · Elka VM Mini Control")
    {
        Vban = settings.Vban;
        _faderMode.SetResourceReference(StyleProperty, "DirectInputToggle");
        _faderMode.IsChecked = settings.FaderMode;
        _faderMode.ToolTip = "Show eight named input faders for the selected SEL. Save to apply.";
        Body.Children.Add(_faderMode);
        Text("Startup and tray", true);
        _startWithWindows.IsChecked = startWithWindows ?? settings.StartWithWindows;
        _startInTray.IsChecked = settings.StartInTray; _closeToTray.IsChecked = settings.CloseToTray;
        Body.Children.Add(_startWithWindows); Body.Children.Add(_startInTray); Body.Children.Add(_closeToTray);
        Text("The tray menu has Open and Exit. Exit always quits. Look under the taskbar's hidden-icons arrow if needed.");
        Text("Input controls", true);
        Text("Choose the input method. Mouse control is always available.");
        var modes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
        modes.Children.Add(_hotkeys); modes.Children.Add(_midi); Body.Children.Add(modes);
        _hotkeys.IsChecked = settings.Mode == InputMode.Hotkeys; _midi.IsChecked = settings.Mode == InputMode.Midi;
        Text("MIDI input device"); Body.Children.Add(_devices);
        var refresh = Button("Refresh devices", () => Refresh(Device ?? settings.MidiDevice));
        refresh.HorizontalAlignment = HorizontalAlignment.Left; refresh.Margin = new Thickness(0, 8, 0, 14); Body.Children.Add(refresh);
        Text("Ctrl-click Apply to choose destinations. Right-click either row to assign its input. MIDI buttons should send press/release.");
        _hotkeys.Checked += (_, _) => _devices.IsEnabled = false;
        _midi.Checked += (_, _) => _devices.IsEnabled = true;
        _devices.IsEnabled = settings.Mode == InputMode.Midi;
        var vban = Button("VBAN Text…", () =>
        {
            var dialog = new VbanSettingsDialog(this, Vban);
            if (dialog.ShowDialog() == true) Vban = dialog.Settings;
        });
        vban.HorizontalAlignment = HorizontalAlignment.Left; vban.Margin = new Thickness(0, 6, 0, 4);
        Body.Children.Add(vban);
        Actions(Button("Cancel", Close), Button("Save", () =>
        {
            if (Mode == InputMode.Midi && Device is null) { Error.Text = "Choose an available MIDI input device."; return; }
            try { save?.Invoke(this); }
            catch (Exception ex) { Error.Text = "Could not save settings: " + ex.Message; return; }
            DialogResult = true;
        }));
        Refresh(settings.MidiDevice);
    }
    private void Refresh(string? selected)
    {
        var devices = MidiInput.Devices(); _devices.ItemsSource = devices;
        _devices.SelectedItem = devices.FirstOrDefault(d => d.Name == selected);
        Error.Text = devices.Count == 0 ? "No MIDI input devices detected. Hotkeys are available." : "";
    }
}

public sealed class HotkeyDialog : SmallDialog
{
    private readonly Func<HotkeyBinding, string?> _validate;
    public HotkeyBinding? Binding { get; private set; }
    public HotkeyDialog(Window? owner, string bus, HotkeyBinding? current, Func<HotkeyBinding, string?> validate)
        : base(owner, $"Assign hotkey · {bus}")
    {
        _validate = validate; Binding = current;
        Text($"Hotkey for {bus}", true);
        Text("Press your shortcut, then click Save. Any combination of Ctrl, Alt, Shift and Win is supported. Escape cancels.");
        var captured = Text(current?.ToString() ?? "Press a key combination…", true);
        var field = new Border { Padding = new Thickness(16), CornerRadius = new CornerRadius(5), Focusable = true };
        field.SetResourceReference(Border.BackgroundProperty, "RaisedBrush");
        Body.Children.Remove(captured); field.Child = captured; Body.Children.Add(field);
        PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None) { Close(); return; }
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
            e.Handled = true;
            Binding = new((uint)Keyboard.Modifiers, (uint)KeyInterop.VirtualKeyFromKey(key));
            captured.Text = Binding.ToString(); Error.Text = "";
        };
        Actions(Button("Clear", () => { Binding = null; DialogResult = true; }), Button("Cancel", Close), Button("Save", () =>
        {
            if (Binding is null) { Error.Text = "Press a shortcut first, or choose Clear."; return; }
            if (_validate(Binding) is { } error) { Error.Text = error; return; }
            DialogResult = true;
        }));
        Loaded += (_, _) => field.Focus();
    }
}

public sealed class MidiLearnDialog : SmallDialog
{
    private readonly TextBlock _captured;
    private readonly Func<MidiBinding, string?> _validate;
    public MidiBinding? Binding { get; private set; }
    public MidiLearnDialog(Window? owner, string bus, MidiBinding? current, string device, Func<MidiBinding, string?> validate)
        : base(owner, $"Learn MIDI · {bus}")
    {
        _validate = validate;
        Text($"Learn MIDI for {bus}", true);
        Text($"Listening to {device}. Press the MIDI button you want to use, then click Save. Learning does not trigger the button.");
        _captured = Text("Waiting for a MIDI button…", true);
        Text(current is null ? "No MIDI button assigned yet." : $"Current: {current}");
        Actions(Button("Clear", () => { Binding = null; DialogResult = true; }), Button("Cancel", Close), Button("Save", () =>
        {
            if (Binding is null) { Error.Text = "Press a MIDI button first, or choose Clear."; return; }
            if (_validate(Binding) is { } error) { Error.Text = error; return; }
            DialogResult = true;
        }));
    }
    public void Receive(MidiSignal signal)
    {
        if (!signal.Pressed) return;
        Binding = signal.Binding; _captured.Text = Binding.ToString(); Error.Text = "";
    }
}

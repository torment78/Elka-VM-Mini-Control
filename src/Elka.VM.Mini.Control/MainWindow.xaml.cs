using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Elka.VM.Mini.Control.Core;
using Elka.VM.Mini.Control.Services;
using Elka.VM.Mini.Control.Views;
using InputMode = Elka.VM.Mini.Control.Core.InputMode;

namespace Elka.VM.Mini.Control;

public partial class MainWindow : Window
{
    private readonly MixerController _mixer;
    private readonly SettingsStore _store;
    private readonly IWindowsStartup _startup;
    public bool StartInTray => _settings.StartInTray;
    public bool CloseToTray => _settings.CloseToTray;
    private ControlSettings _settings = new();
    private readonly BusViewModel[] _buses = Enumerable.Range(0, 8).Select(i => new BusViewModel(i)).ToArray();
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly MidiInput _midi;
    private readonly MidiPressTracker _midiPresses = new();
    private HotkeyService? _hotkeys;
    private VbanTextReceiver? _vban;
    private int _vbanGeneration;
    private bool _vbanExecuting;
    private string _vbanStatus = "VBAN Text disabled";
    private bool _vbanError;
    private MidiLearnDialog? _learning;
    private bool _dialogOpen, _closed;
    private string? _message, _inputError;
    private bool _messageIsError;
    private DateTime _messageUntil;
    private readonly ContextMenu[] _targetMenus = new ContextMenu[8];
    private readonly MenuItem[,] _targetItems = new MenuItem[8, 8];

    public MainWindow() : this(new MixerController(new VoiceMeeterRemote()), new SettingsStore(SettingsStore.DefaultPath)) { }
    public MainWindow(MixerController mixer, SettingsStore store, IWindowsStartup? startup = null)
    {
        _mixer = mixer; _store = store; _startup = startup ?? new WindowsStartup();
        InitializeComponent();
        try { _settings = _store.Load(); }
        catch (Exception ex) { Notice("Could not load saved settings: " + ex.Message, true); }
        BusButtons.ItemsSource = ApplyButtons.ItemsSource = _buses;
        _midi = new MidiInput(Dispatcher); _midi.Received += ReceiveMidi;
        BuildTargetMenu();
        _poll.Tick += (_, _) => RefreshState();
        SourceInitialized += (_, _) =>
        {
            WindowAppearance.Apply(this);
            _hotkeys = new HotkeyService(new WindowInteropHelper(this).Handle);
            _hotkeys.Pressed += Toggle;
            ConfigureInputs();
            ConfigureVban();
            RefreshState(); _poll.Start();
        };
        Closed += (_, _) =>
        {
            _closed = true; _vbanGeneration++; _vban?.Dispose();
            _poll.Stop(); _midi.Dispose(); _hotkeys?.Dispose(); _mixer.Dispose();
        };
    }

    public void Start(bool hidden)
    {
        // Create the message handle and inputs even when no visible window is loaded.
        new WindowInteropHelper(this).EnsureHandle();
        if (!hidden) Show();
    }

    public void RefreshState()
    {
        _mixer.Poll();
        for (int i = 0; i < 8; i++) _buses[i].Update(_mixer.Selected[i], _settings);
        BusButtons.IsEnabled = ApplyButtons.IsEnabled = !_vbanExecuting && !_mixer.Applying;
        SettingsButton.IsEnabled = !_vbanExecuting && !_mixer.Applying;
        bool showMessage = _message is not null && DateTime.UtcNow < _messageUntil;
        StatusText.Text = showMessage ? _message : _mixer.ActionError ?? _mixer.Status;
        StatusText.Foreground = (Brush)FindResource((showMessage && _messageIsError) || _mixer.ActionError is not null ? "OrangeBrush" : _mixer.Connected ? "GoodBrush" : "MutedBrush");
        InputText.Text = _inputError ?? (_settings.Mode == InputMode.Hotkeys ? "Hotkeys enabled · " + _settings.Hotkeys.Count(h => h is not null) + " assigned" : "MIDI · " + (_settings.MidiDevice ?? "No device selected"));
        InputText.Foreground = (Brush)FindResource(_inputError is null ? "MutedBrush" : "OrangeBrush");
        VbanText.Visibility = _settings.Vban.Enabled ? Visibility.Visible : Visibility.Collapsed;
        VbanText.Text = _vbanStatus; VbanText.ToolTip = _vbanStatus;
        VbanText.Foreground = (Brush)FindResource(_vbanError ? "OrangeBrush" : "MutedBrush");
        for (int source = 0; source < 8; source++)
            for (int target = 0; target < 8; target++)
                _targetItems[source, target].IsChecked = source != target && _settings.ApplyTargets[source][target];
    }
    private void Toggle(int bus)
    {
        if (_dialogOpen || _closed || _vbanExecuting) return;
        if (bus >= 8) { _ = ApplyBusAsync(bus - 8); return; }
        _message = null; _mixer.Toggle(bus); RefreshState();
    }
    private void SelectBus(object sender, RoutedEventArgs e) => Toggle(((BusViewModel)((Button)sender).DataContext).Index);
    private void ConfigureHotkey(object sender, MouseButtonEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || _settings.Mode != InputMode.Hotkeys) return;
        e.Handled = true;
        EditHotkey(((BusViewModel)((Button)sender).DataContext).Index);
    }
    private void ConfigureMidi(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        int bus = ((BusViewModel)((Button)sender).DataContext).Index;
        if (_settings.Mode == InputMode.Midi) LearnMidi(bus);
        else EditHotkey(bus);
    }
    private void ApplyBus(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        int source = ((BusViewModel)button.DataContext).Index;
        if (!_settings.Destinations(source).Any()) { ShowDestinations(button, source); return; }
        Toggle(source + 8);
    }
    private void ConfigureDestinations(object sender, MouseButtonEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        e.Handled = true;
        var button = (Button)sender;
        ShowDestinations(button, ((BusViewModel)button.DataContext).Index);
    }
    private void ConfigureApplyInput(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var button = (Button)sender;
        int source = ((BusViewModel)button.DataContext).Index;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) ShowDestinations(button, source);
        else if (_settings.Mode == InputMode.Midi) LearnMidi(source + 8);
        else EditHotkey(source + 8);
    }
    private void EditHotkey(int bus)
    {
        if (_dialogOpen) return;
        _dialogOpen = true; _hotkeys?.Clear();
        try
        {
            var dialog = new HotkeyDialog(this, ControlSettings.ActionName(bus), _settings.Hotkeys[bus], binding =>
            {
                int other = Array.FindIndex(_settings.Hotkeys, h => h == binding);
                if (other >= 0 && other != bus) return $"Already assigned to {ControlSettings.ActionName(other)}.";
                return _hotkeys?.CanRegister(binding) == true ? null : "Windows or another app is using this shortcut. Choose another.";
            });
            if (dialog.ShowDialog() == true) { _settings.Hotkeys[bus] = dialog.Binding; Save(); }
        }
        finally { _dialogOpen = false; _inputError = _hotkeys?.Configure(_settings); RefreshState(); }
    }
    private void LearnMidi(int bus)
    {
        if (_dialogOpen) return;
        if (!_midi.IsOpen) { Notice("Choose an available MIDI device in Settings first."); return; }
        _dialogOpen = true;
        try
        {
            _learning = new MidiLearnDialog(this, ControlSettings.ActionName(bus), _settings.Midi[bus], _settings.MidiDevice!, binding =>
            {
                int other = Array.FindIndex(_settings.Midi, m => m == binding);
                return other >= 0 && other != bus ? $"Already assigned to {ControlSettings.ActionName(other)}." : null;
            });
            if (_learning.ShowDialog() == true) { _settings.Midi[bus] = _learning.Binding; Save(); }
        }
        finally { _learning = null; _dialogOpen = false; RefreshState(); }
    }
    private void ReceiveMidi(MidiSignal signal)
    {
        // Track releases even while a dialog is open, so its learn gesture cannot trigger SEL later.
        bool pressed = _midiPresses.Process(signal);
        if (_learning is not null) { _learning.Receive(signal); return; }
        if (!pressed || _dialogOpen || _settings.Mode != InputMode.Midi) return;
        int bus = Array.FindIndex(_settings.Midi, b => b == signal.Binding);
        if (bus >= 0) Toggle(bus);
    }
    private void OpenSettings(object sender, RoutedEventArgs e)
    {
        if (_dialogOpen) return;
        _dialogOpen = true;
        try
        {
            var dialog = new SettingsDialog(this, _settings, _startup.Enabled, selected =>
            {
                var updated = new ControlSettings
                {
                    Mode = selected.Mode, MidiDevice = selected.Device ?? _settings.MidiDevice,
                    Hotkeys = _settings.Hotkeys, Midi = _settings.Midi, ApplyTargets = _settings.ApplyTargets,
                    Vban = selected.Vban, StartWithWindows = selected.StartWithWindows,
                    StartInTray = selected.StartInTray, CloseToTray = selected.CloseToTray
                };
                _store.Save(updated);
                try { _startup.SetEnabled(updated.StartWithWindows); }
                catch { _store.Save(_settings); throw; }
                _settings = updated;
            });
            if (dialog.ShowDialog() == true)
            {
                ConfigureInputs(); ConfigureVban();
            }
        }
        catch (Exception ex) { Notice("Could not update settings: " + ex.Message, true); }
        finally { _dialogOpen = false; RefreshState(); }
    }
    private void ConfigureInputs()
    {
        _inputError = _hotkeys?.Configure(_settings);
        _midi.Close(); _midiPresses.Clear();
        if (_settings.Mode != InputMode.Midi) return;
        try
        {
            if (_settings.MidiDevice is null) throw new InvalidOperationException("Select a MIDI input device in Settings.");
            _midi.Open(_settings.MidiDevice);
        }
        catch (Exception ex) { _inputError = ex.Message; }
    }
    private void ConfigureVban()
    {
        int generation = ++_vbanGeneration;
        _vban?.Dispose(); _vban = null; _vbanError = false;
        Height = _settings.Vban.Enabled ? 350 : 330;
        if (!_settings.Vban.Enabled) { _vbanStatus = "VBAN Text disabled"; return; }
        try
        {
            _vban = new VbanTextReceiver(_settings.Vban,
                async (packet, sender, token) =>
                {
                    if (token.IsCancellationRequested || Dispatcher.HasShutdownStarted) return;
                    await Dispatcher.InvokeAsync(() => ExecuteVbanAsync(packet, sender.ToString(), generation, token)).Task.Unwrap();
                },
                diagnostic =>
                {
                    if (Dispatcher.HasShutdownStarted) return;
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (_closed || generation != _vbanGeneration) return;
                        _vbanError = true; _vbanStatus = "VBAN · " + diagnostic; RefreshState();
                    });
                });
            _vbanStatus = $"VBAN · {_settings.Vban.ListenAddress}:{_settings.Vban.Port} · {_settings.Vban.StreamName} · listening";
        }
        catch (Exception ex)
        {
            _vbanError = true;
            _vbanStatus = $"VBAN could not listen on {_settings.Vban.ListenAddress}:{_settings.Vban.Port}: {ex.Message}";
        }
    }
    private async Task ExecuteVbanAsync(VbanTextPacket packet, string sender, int generation, CancellationToken token)
    {
        if (_closed || generation != _vbanGeneration || token.IsCancellationRequested) return;
        if (_dialogOpen) throw new InvalidOperationException("Command ignored while an input/settings dialog is open.");
        _vbanExecuting = true; _vbanError = false; RefreshState();
        try
        {
            _message = null;
            string? result = await VmcCommands.ExecuteAsync(packet.Text, _mixer, source => _settings.Destinations(source), token);
            if (result is not null) Notice(result);
            _vbanStatus = $"VBAN · received from {sender} · {packet.Text}";
        }
        finally { _vbanExecuting = false; if (!_closed) RefreshState(); }
    }
    private void BuildTargetMenu()
    {
        for (int s = 0; s < 8; s++)
        {
            int source = s;
            var menu = new ContextMenu(); _targetMenus[source] = menu;
            menu.Items.Add(new MenuItem { Header = $"Apply {MixerController.BusNames[source]} levels to", IsEnabled = false });
            var all = new MenuItem { Header = "Select all destinations", StaysOpenOnClick = true };
            all.Click += (_, _) => { for (int i = 0; i < 8; i++) _settings.ApplyTargets[source][i] = i != source; Save(); RefreshState(); };
            var clear = new MenuItem { Header = "Clear selection", StaysOpenOnClick = true };
            clear.Click += (_, _) => { Array.Clear(_settings.ApplyTargets[source]); Save(); RefreshState(); };
            menu.Items.Add(all); menu.Items.Add(clear); menu.Items.Add(new Separator());
            for (int t = 0; t < 8; t++)
            {
                int target = t;
                var item = new MenuItem { Header = MixerController.BusNames[target] + (source == target ? " · source" : ""), IsCheckable = true, IsEnabled = target != source, StaysOpenOnClick = true };
                item.Click += (_, _) => { _settings.ApplyTargets[source][target] = item.IsChecked; Save(); RefreshState(); };
                _targetItems[source, target] = item; menu.Items.Add(item);
            }
            menu.Opened += (_, _) => RefreshState();
        }
    }
    private void ShowDestinations(Button button, int source)
    {
        if (_mixer.Applying || _vbanExecuting) return;
        var menu = _targetMenus[source]; button.ContextMenu = menu;
        menu.PlacementTarget = button; menu.Placement = PlacementMode.Bottom; menu.IsOpen = true;
    }
    private async Task ApplyBusAsync(int source)
    {
        try
        {
            var work = _mixer.ApplyFromAsync(source, _settings.Destinations(source)); RefreshState();
            Notice(await work);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Notice(ex.Message, true); }
        finally { if (!_closed) RefreshState(); }
    }
    private void Save()
    {
        try { _store.Save(_settings); }
        catch (Exception ex) { Notice("Settings could not be saved: " + ex.Message, true); }
    }
    private void Notice(string text, bool persistent = false)
    {
        _message = text; _messageIsError = persistent; _messageUntil = persistent ? DateTime.MaxValue : DateTime.UtcNow.AddSeconds(8);
        if (StatusText is not null) StatusText.Text = text;
    }
}

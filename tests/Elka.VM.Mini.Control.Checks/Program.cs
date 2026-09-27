using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Elka.VM.Mini.Control;
using Elka.VM.Mini.Control.Core;
using Elka.VM.Mini.Control.Services;
using Elka.VM.Mini.Control.Views;

internal static partial class Program
{
    private static int _checks;
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Contains("--live-probe")) { LiveProbe(); return 0; }
            CoreChecks().GetAwaiter().GetResult();
            VbanChecks().GetAwaiter().GetResult();
            MidiChecks();
            string output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/checks");
            Directory.CreateDirectory(output);
            SettingsChecks(output);
            StartupChecks(output);
            UiChecks(output);
            Console.WriteLine($"PASS: {_checks} checks. UI renders: {output}");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + description);
        _checks++; Console.WriteLine("PASS: " + description);
    }
    private static async Task Reject(Func<Task> action, string description)
    {
        try { await action(); }
        catch (InvalidOperationException) { Check(true, description); return; }
        throw new InvalidOperationException("FAIL: " + description);
    }
    private static async Task CoreChecks()
    {
        using var fake = new FakeRemote(); using var mixer = new MixerController(fake);
        mixer.Poll(); Check(mixer.Connected && mixer.Source == -1 && fake.Scripts.Count == 0, "Startup reads without writing mixer settings");
        mixer.Toggle(2); mixer.Poll(); Check(mixer.Source == 2, "App selects A3 through the SEL API");
        mixer.Toggle(2); mixer.Poll(); Check(mixer.Source == -1, "Pressing active SEL clears it");
        fake.Select(7); mixer.Poll(); Check(mixer.Source == 7, "External selection updates the app");
        fake.Select(-1); mixer.Poll(); Check(mixer.Source == -1, "External deselection turns the button off");
        fake.Select(2); mixer.Poll();
        for (int strip = 0; strip < 8; strip++) fake.Values[$"Strip[{strip}].GainLayer[2]"] = strip == 0 ? -60 : strip == 7 ? 12 : -strip * 1.25f;
        var before = new Dictionary<string, float>(fake.Values);
        var oldCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            await mixer.ApplyAsync([0, 2, 5, 7, 5]);
        }
        finally { CultureInfo.CurrentCulture = oldCulture; }
        Check(new[] { 0, 5, 7 }.All(b => Enumerable.Range(0, 8).All(s => fake.Values[$"Strip[{s}].GainLayer[{b}]"] == before[$"Strip[{s}].GainLayer[2]"])), "Exact eight-strip copy to checked destinations, with invariant decimals and gain boundaries");
        Check(before.Where(p => !new[] { 0, 5, 7 }.Any(b => p.Key.Contains($"GainLayer[{b}]"))).All(p => fake.Values[p.Key] == p.Value), "Source, unchecked buses, routing, pan and bus gain remain unchanged");
        Check(fake.Scripts.Last().Split(';', StringSplitOptions.RemoveEmptyEntries).Length == 24, "Source and duplicate targets excluded");
        int writes = fake.Scripts.Count;
        await Reject(() => mixer.ApplyAsync([2]), "Source-only target selection rejected");
        fake.Select(-1); await Reject(() => mixer.ApplyAsync([0]), "Apply without a source rejected");
        fake.Select(2); fake.FailRead = "Strip[6].GainLayer[2]";
        await Reject(() => mixer.ApplyAsync([0]), "Failed source read aborts before writes");
        Check(fake.Scripts.Count == writes, "Invalid apply requests perform no writes");
        fake.FailRead = null; fake.Values["Strip[1].GainLayer[2]"] = float.NaN;
        await Reject(() => mixer.ApplyAsync([0]), "Invalid source level rejected");
        fake.Values["Strip[1].GainLayer[2]"] = -11;
        fake.Connected = false; mixer.Poll(); Check(!mixer.Connected && mixer.Source == -1, "Disconnect clears stale SEL indicators");
        fake.Connected = true; fake.Select(4); mixer.Poll(); Check(mixer.Connected && mixer.Source == 4, "Reconnect restores actual SEL state");
        fake.ApplyWrites = false; mixer.Toggle(1);
        await Reject(() => mixer.ApplyAsync([0]), "Apply blocked until SEL is confirmed");
        fake.Select(1); mixer.Poll(); Check(!mixer.SelectionPending, "SEL readback clears pending change");
        await Reject(() => mixer.ApplyAsync([0]), "Unconfirmed destination writes report failure");
        Check(!mixer.Applying, "Failed copy releases busy state");
        fake.ApplyWrites = true; fake.Values["Bus[4].Sel"] = 1; mixer.Poll();
        Check(mixer.Source == -1, "Ambiguous external multi-selection cannot become a copy source");
    }
    private static void MidiChecks()
    {
        uint Pack(int status, int number, int value) => (uint)(status | (number << 8) | (value << 16));
        var tracker = new MidiPressTracker();
        var note = MidiSignal.Decode(Pack(0x92, 60, 100))!;
        Check(note.Binding == new MidiBinding(2, 0x90, 60) && note.Pressed, "MIDI captures channel, note and press");
        Check(tracker.Process(note) && !tracker.Process(note), "Held MIDI note triggers once");
        Check(!tracker.Process(MidiSignal.Decode(Pack(0x82, 60, 127))!) && tracker.Process(note), "Note-off rearms the next press");
        Check(!tracker.Process(MidiSignal.Decode(Pack(0x92, 60, 0))!) && tracker.Process(note), "Zero-velocity note-on is a release");
        var cc = MidiSignal.Decode(Pack(0xBF, 42, 1))!;
        Check(tracker.Process(cc) && !tracker.Process(cc), "CC buttons accept nonzero values without repeat toggles");
        tracker.Process(MidiSignal.Decode(Pack(0xBF, 42, 0))!);
        Check(tracker.Process(cc) && cc.Binding.Channel == 15, "CC release and MIDI channel 16 supported");
        Check(MidiSignal.Decode(0xF8) is null && MidiSignal.Decode(Pack(0xE0, 1, 1)) is null, "Clock and pitch bend do not learn or trigger buttons");
        Check(MidiInput.Devices().All(d => d.Name.Length > 0), "WinMM MIDI device enumeration succeeds");
    }
    private static void SettingsChecks(string output)
    {
        var store = new SettingsStore(Path.Combine(output, "roundtrip-settings.json"));
        var settings = new ControlSettings { Mode = Elka.VM.Mini.Control.Core.InputMode.Midi, MidiDevice = "Test MIDI" };
        settings.Hotkeys[0] = new(7, 65); settings.Hotkeys[1] = new(15, 66); settings.Midi[7] = new(15, 0xB0, 42); settings.ApplyTargets[2][5] = true;
        settings.Hotkeys[15] = new(7, 67); settings.Midi[15] = new(14, 0x90, 49); settings.ApplyTargets[1][7] = true;
        store.Save(settings); var loaded = store.Load();
        Check(loaded.Hotkeys[0] == settings.Hotkeys[0] && loaded.Hotkeys[1] == settings.Hotkeys[1] && loaded.Midi[7] == settings.Midi[7] && loaded.ApplyTargets[2][5] && loaded.MidiDevice == settings.MidiDevice, "Settings and all modifier combinations survive save/load");
        Check(loaded.Hotkeys[15] == settings.Hotkeys[15] && loaded.Midi[15] == settings.Midi[15] && loaded.ApplyTargets[1][7] && !loaded.ApplyTargets[2][7], "Apply-row bindings and independent destination profiles survive save/load");
        var legacyPath = Path.Combine(output, "legacy-settings.json");
        File.WriteAllText(legacyPath, "{\"Targets\":[true,false,false,false,false,true,false,false],\"Hotkeys\":[{\"Modifiers\":7,\"Key\":65},null,null,null,null,null,null,null],\"Midi\":[null,null,null,null,null,null,null,null]}");
        var legacy = new SettingsStore(legacyPath).Load();
        Check(legacy.Hotkeys.Length == 16 && legacy.Hotkeys[0] == new HotkeyBinding(7, 65) && legacy.ApplyTargets[2][0] && legacy.ApplyTargets[2][5] && !legacy.ApplyTargets[0][0], "Original settings migrate to per-bus profiles while preserving SEL bindings");
        Check(settings.Hotkeys[0]!.ToString() == "Ctrl+Alt+Shift+A" && settings.Hotkeys[1]!.ToString() == "Ctrl+Alt+Shift+Win+B", "Triple and quadruple modifier labels");
        File.WriteAllText(Path.Combine(output, "invalid-settings.json"), "{\"Hotkeys\":[]}");
        bool invalid = false;
        try { new SettingsStore(Path.Combine(output, "invalid-settings.json")).Load(); } catch (InvalidDataException) { invalid = true; }
        Check(invalid, "Malformed settings rejected rather than used");
    }
    private static void UiChecks(string output)
    {
        var app = new App(); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());
        var fake = new FakeRemote(); fake.Select(2);
        var store = new SettingsStore(Path.Combine(output, "ui-settings.json"));
        var settings = new ControlSettings(); settings.ApplyTargets[2][0] = settings.ApplyTargets[2][5] = settings.ApplyTargets[2][7] = true;
        settings.ApplyTargets[0][5] = true; settings.ApplyTargets[1][0] = settings.ApplyTargets[1][7] = true;
        settings.Hotkeys[2] = new(7, 51); store.Save(settings);
        var window = new MainWindow(new MixerController(fake), store); window.RefreshState();
        Check(window.Title == "Elka VM Mini Control", "Correct Elka application branding");
        Check(((ItemsControl)window.FindName("BusButtons")).Items.Count == 8, "Exactly eight SEL buttons");
        Check(((ItemsControl)window.FindName("ApplyButtons")).Items.Count == 8, "Exactly eight separate Apply buttons");
        var root = (FrameworkElement)window.FindName("RootPanel");
        Render(root, 684, 300, Path.Combine(output, "main-window.png"));
        var buttons = Descendants<Button>((ItemsControl)window.FindName("BusButtons")).ToList();
        Check(buttons.Count == 8, "All SEL templates instantiate");
        buttons.Single(b => ((BusViewModel)b.DataContext).Index == 2).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(fake.Values["Bus[2].Sel"] == 0, "Mouse button deselects SEL");
        fake.Select(7); window.RefreshState();
        Check(((BusViewModel)buttons[7].DataContext).Selected, "UI follows external source changes");
        var applyRow = (ItemsControl)window.FindName("ApplyButtons");
        var applyButtons = Descendants<Button>(applyRow).ToList();
        Check(applyButtons.Count == 8, "All eight Apply button templates instantiate");
        applyButtons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => applyRow.IsEnabled);
        Check(fake.Values["Strip[4].GainLayer[0]"] == fake.Values["Strip[4].GainLayer[1]"] && fake.Values["Strip[4].GainLayer[7]"] == fake.Values["Strip[4].GainLayer[1]"] && fake.Values["Bus[7].Sel"] == 1, "Mouse Apply uses its own column profile and preserves current SEL");
        var menus = (ContextMenu[])typeof(MainWindow).GetField("_targetMenus", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(window)!;
        Check(menus.Length == 8 && menus.All(m => m.Items.OfType<MenuItem>().Count(item => item.IsCheckable) == 8), "Each Apply button has its own eight-destination checklist");
        var selectAll = (MenuItem)menus[1].Items[1]; selectAll.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check(store.Load().ApplyTargets[1].Count(v => v) == 7 && !store.Load().ApplyTargets[1][1] && store.Load().ApplyTargets[2].Count(v => v) == 3, "Select all updates only its own profile and excludes its source");
        var settingsDialog = new SettingsDialog(null, settings);
        Render((FrameworkElement)settingsDialog.Content, 434, 720, Path.Combine(output, "settings-window.png"));
        var vbanDialog = new VbanSettingsDialog(null, new VbanSettings { Enabled = true });
        Render((FrameworkElement)vbanDialog.Content, 474, 730, Path.Combine(output, "vban-window.png"));
        Check(vbanDialog.Settings.Port == 6982 && vbanDialog.Settings.StreamName == "Command1", "VBAN settings use the separate port and matching stream");
        var hotkeyDialog = new HotkeyDialog(null, "A3 Apply", new(7, 51), _ => null);
        Render((FrameworkElement)hotkeyDialog.Content, 434, 280, Path.Combine(output, "hotkey-window.png"));
        var midiDialog = new MidiLearnDialog(null, "A3", null, "Example MIDI input", _ => null);
        midiDialog.Receive(new(new(1, 0x90, 48), true));
        Check(midiDialog.Binding == new MidiBinding(1, 0x90, 48), "Learn MIDI dialog captures a bus assignment");
        Render((FrameworkElement)midiDialog.Content, 434, 285, Path.Combine(output, "midi-window.png"));
        using var source = new HwndSource(new HwndSourceParameters("Elka control checks") { Width = 1, Height = 1, WindowStyle = 0 });
        using var hotkeys = new HotkeyService(source.Handle);
        int fired = -1; hotkeys.Pressed += bus => fired = bus;
        SendMessage(source.Handle, 0x8001, new IntPtr(0x100000001), IntPtr.Zero);
        Check(fired == -1, "Unrelated 64-bit window messages bypass hotkey parsing");
        var keys = new ControlSettings(); keys.Hotkeys[3] = new(7, 0x86);
        Check(hotkeys.Configure(keys) is null, "Windows registers a global triple-modifier hotkey");
        Check(!hotkeys.CanRegister(keys.Hotkeys[3]!), "Registered hotkey conflict is detected");
        SendMessage(source.Handle, 0x0312, (IntPtr)4, IntPtr.Zero);
        Check(fired == 3, "WM_HOTKEY dispatches the assigned bus");
        hotkeys.Clear(); Check(hotkeys.CanRegister(keys.Hotkeys[3]!), "Clearing bindings releases the Windows hotkey");
        keys.Hotkeys[3] = new(15, 0x86);
        Check(hotkeys.Configure(keys) is null, "Windows registers all four modifiers together");
        keys.Hotkeys[15] = new(7, 0x85); hotkeys.Configure(keys);
        SendMessage(source.Handle, 0x0312, (IntPtr)16, IntPtr.Zero);
        Check(fired == 15, "Global hotkeys can trigger bottom-row Apply buttons");
        fake.Connected = false; window.RefreshState(); Render(root, 684, 300, Path.Combine(output, "disconnected-window.png"));
        Check(buttons.All(b => !((BusViewModel)b.DataContext).Selected), "Disconnected UI clears SEL while destination setup stays available");
        window.Close();
        TrayChecks(output);
    }
    private static void PumpUntil(Func<bool> ready)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var end = DateTime.UtcNow.AddSeconds(3);
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (ready() || DateTime.UtcNow > end) frame.Continue = false; };
        timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame); timer.Stop();
        if (!ready()) throw new InvalidOperationException("UI operation timed out.");
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T result) yield return result;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static void Render(FrameworkElement element, int width, int height, string path)
    {
        element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(17, 20, 23)), null, new Rect(0, 0, width, height));
            dc.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, width, height));
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static void LiveProbe()
    {
        using var remote = new VoiceMeeterRemote();
        remote.Refresh();
        Console.WriteLine("Installed API: " + remote.LibraryPath);
        for (int bus = 0; bus < 8; bus++)
        {
            Console.WriteLine($"{MixerController.BusNames[bus]} SEL={remote.Read($"Bus[{bus}].Sel")}; input levels=" +
                string.Join(", ", Enumerable.Range(0, 8).Select(s => remote.Read($"Strip[{s}].GainLayer[{bus}]").ToString("0.##", CultureInfo.InvariantCulture))));
        }
        Console.WriteLine("Read-only live probe passed; no mixer writes performed.");
        Console.WriteLine("MIDI inputs: " + string.Join(", ", MidiInput.Devices().Select(d => d.Name)));
    }
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}

internal sealed class FakeRemote : IRemoteApi
{
    public readonly Dictionary<string, float> Values = [];
    public readonly List<string> Scripts = [];
    public bool Connected = true, ApplyWrites = true;
    public string? FailRead;
    public FakeRemote()
    {
        for (int i = 0; i < 8; i++)
        {
            Values[$"Bus[{i}].Sel"] = 0; Values[$"Bus[{i}].Gain"] = -3;
            Values[$"Strip[{i}].Pan_x"] = .2f;
            for (int j = 0; j < 8; j++) { Values[$"Strip[{i}].GainLayer[{j}]"] = -i - j; Values[$"Strip[{i}].{MixerController.BusNames[j]}"] = j % 2; }
        }
    }
    public void Select(int bus) { for (int i = 0; i < 8; i++) Values[$"Bus[{i}].Sel"] = i == bus ? 1 : 0; }
    public void Refresh() { if (!Connected) throw new InvalidOperationException("VoiceMeeter disconnected."); }
    public float Read(string parameter) => FailRead == parameter ? throw new InvalidOperationException("Test read failed.") : Values[parameter];
    public void Write(string script)
    {
        Scripts.Add(script); if (!ApplyWrites) return;
        foreach (var statement in script.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = statement.Split('='); Values[parts[0]] = float.Parse(parts[1], CultureInfo.InvariantCulture);
        }
    }
    public void Dispose() { }
}

using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using Elka.VM.Mini.Control;
using Elka.VM.Mini.Control.Core;

internal static partial class Program
{
    private static async Task SelectionGuardChecks()
    {
        long now = 0;
        using var fake = new FakeRemote();
        using var mixer = new MixerController(fake, () => now);
        var gains = fake.Values.Where(p => !p.Key.EndsWith(".Sel")).ToDictionary(p => p.Key, p => p.Value);
        mixer.Poll(); now = 200; mixer.Poll();
        Check(fake.Scripts.Count == 0 && !mixer.MasterMode, "Startup waits for the native SEL snapshot before choosing a default");
        now = 260; mixer.Poll(); mixer.Poll();
        Check(mixer.Source == 0 && !mixer.MasterMode && fake.Scripts.Count == 1, "With no SEL at startup the safeguard selects A1");
        mixer.Toggle(3); mixer.Poll();
        Check(mixer.Source == 3 && fake.Scripts.Last().StartsWith("Bus[3].Sel=1;"), "Switching SEL selects the new bus before clearing the old one");
        int writes = fake.Scripts.Count;
        mixer.Toggle(3); mixer.Poll(); await mixer.SelectAsync(3, SelAction.Toggle);
        Check(mixer.Source == 3 && fake.Scripts.Count == writes, "Repeated mouse and VBAN toggle commands keep the active SEL selected");
        await Reject(() => mixer.SelectAsync(3, SelAction.Off), "Explicit remote Off requires Ctrl-click in the app");
        mixer.EnterMasterMode(1); mixer.Poll();
        Check(mixer.Source == 3 && fake.Scripts.Count == writes, "An inactive SEL cannot be used to clear the active selection");
        fake.Select(-1); mixer.Poll(); now += 300; mixer.Poll(); mixer.Poll();
        Check(mixer.MasterMode && fake.Scripts.Count == writes, "External MIDI/hotkey deselection is respected instead of being auto-restored");
        mixer.Toggle(3); mixer.Poll();
        mixer.EnterMasterMode(3); mixer.Poll(); writes = fake.Scripts.Count;
        now += 5000; mixer.Poll();
        Check(mixer.MasterMode && mixer.Source == -1 && fake.Scripts.Count == writes, "Explicit master mode stays all-off instead of being auto-restored");
        mixer.UpdateFaders(true); mixer.UpdateDirectInput(true, _ => [1]);
        Check(!mixer.Faders.Ready && fake.Scripts.Count == writes, "Master mode pauses submix faders and Direct Input without touching levels");
        mixer.Toggle(6); mixer.Poll();
        Check(mixer.Source == 6 && !mixer.MasterMode, "A normal SEL click leaves master mode");
        mixer.EnterMasterMode(6); mixer.Poll(); fake.Select(4); mixer.Poll();
        fake.Select(-1); mixer.Poll(); now += 300; mixer.Poll(); mixer.Poll();
        Check(mixer.MasterMode, "External selection and deselection continue to work in both directions");
        mixer.Toggle(4); mixer.Poll();
        mixer.EnterMasterMode(4); mixer.Poll(); fake.Connected = false; mixer.Poll();
        fake.Connected = true; mixer.Poll(); now += 300; mixer.Poll(); mixer.Poll();
        Check(mixer.Source == 4 && !mixer.MasterMode, "Reconnect returns to protected selection instead of retaining master permission");
        mixer.ToggleBinding(4); mixer.Poll(); writes = fake.Scripts.Count;
        now += 5000; mixer.Poll();
        Check(mixer.MasterMode && fake.Scripts.Count == writes, "MIDI/hotkey toggles can enter master mode without automatic re-selection");
        mixer.ToggleBinding(4); mixer.Poll();
        Check(mixer.Source == 4 && !mixer.MasterMode, "The next MIDI/hotkey press selects the bus again");
        Check(gains.All(p => fake.Values[p.Key] == p.Value), "Selection protection never changes gain, routing, or other mixer parameters");

        using var rejected = new FakeRemote { FailWrite = true };
        now = 0; using var failedGuard = new MixerController(rejected, () => now);
        failedGuard.Poll(); now = 300; failedGuard.Poll();
        rejected.FailWrite = false; now = 5000; failedGuard.Poll();
        Check(failedGuard.ActionError is not null && rejected.Scripts.Count == 0, "A failed automatic selection reports the error and does not loop writes");
        failedGuard.Toggle(2); failedGuard.Poll();
        Check(failedGuard.Source == 2 && failedGuard.ActionError is null, "An explicit selection can retry after a guard error");

        using var delayed = new FakeRemote { ApplyWrites = false }; delayed.Select(1);
        now = 0; using var delayedMixer = new MixerController(delayed, () => now);
        delayedMixer.Poll(); delayedMixer.EnterMasterMode(1); delayedMixer.Poll();
        Check(!delayedMixer.MasterMode && delayedMixer.SelectionPending, "Master mode is not reported until API readback confirms all SELs off");
        now = 1100; delayedMixer.Poll();
        Check(!delayedMixer.MasterMode && delayedMixer.ActionError is not null, "Unconfirmed master entry leaves protection armed");
        delayedMixer.Toggle(2); delayed.Select(2); delayedMixer.Poll();
        Check(delayedMixer.Source == 2 && !delayedMixer.SelectionPending, "A confirmed selection recovers after delayed API writes");

        using var sync = new FakeRemote(); now = 0;
        using var syncMixer = new MixerController(sync, () => now);
        syncMixer.Poll(); sync.Select(7); now = 100; syncMixer.Poll(); now = 500; syncMixer.Poll();
        Check(syncMixer.Source == 7 && sync.Scripts.Count == 0, "Initial native synchronization preserves an existing bus instead of selecting A1");
    }

    private static void SelectionGuardUiChecks(string output)
    {
        using var fake = new FakeRemote(); fake.Select(2);
        var mixer = new MixerController(fake);
        var store = new SettingsStore(Path.Combine(output, "selection-guard-settings.json"));
        var settings = new ControlSettings { FaderMode = true };
        settings.Hotkeys[2] = new(3, 0x85); store.Save(settings);
        var window = new MainWindow(mixer, store, new FaderTestStartup());
        try
        {
            window.Start(hidden: true);
            var root = (FrameworkElement)window.FindName("RootPanel");
            Render(root, 684, 606, Path.Combine(output, "protected-sel.png"));
            var buttons = Descendants<Button>((ItemsControl)window.FindName("BusButtons")).ToArray();
            buttons[2].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(mixer.Source == 2, "A normal mouse click keeps the active SEL selected");
            SendMessage(new WindowInteropHelper(window).Handle, 0x0312, (IntPtr)3, IntPtr.Zero);
            Check(mixer.MasterMode, "An assigned global hotkey can toggle the active SEL off without the mouse safeguard");
            buttons[2].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var activate = typeof(MainWindow).GetMethod("ActivateSel", BindingFlags.Instance | BindingFlags.NonPublic)!;
            activate.Invoke(window, [2, true]);
            Check(mixer.MasterMode && ((TextBlock)window.FindName("StatusText")).Text.Contains("Master mode"), "Ctrl-click action clears SEL and visibly identifies master mode");
            Check(Descendants<Slider>((FrameworkElement)window.FindName("FaderPanel")).All(s => !s.IsEnabled), "The fader bank is disabled in explicit master mode");
            Render(root, 684, 606, Path.Combine(output, "master-mode.png"));
            activate.Invoke(window, [5, true]);
            Check(mixer.Source == 5 && !mixer.MasterMode, "Ctrl-click on an inactive bus selects it normally");
            // MIDI toggles remain unguarded, with the existing press/release debouncing.
            var liveSettings = (ControlSettings)typeof(MainWindow).GetField("_settings", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            liveSettings.Mode = Elka.VM.Mini.Control.Core.InputMode.Midi;
            liveSettings.Midi[5] = new(0, 0x90, 60);
            typeof(MainWindow).GetMethod("ReceiveMidi", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [new MidiSignal(liveSettings.Midi[5]!, true)]);
            Check(mixer.MasterMode, "An assigned MIDI press can toggle the active SEL off");
            typeof(MainWindow).GetMethod("ReceiveMidi", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [new MidiSignal(liveSettings.Midi[5]!, true)]);
            Check(mixer.MasterMode, "Holding a MIDI button does not immediately toggle SEL back on");
            typeof(MainWindow).GetMethod("ReceiveMidi", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [new MidiSignal(liveSettings.Midi[5]!, false)]);
            typeof(MainWindow).GetMethod("ReceiveMidi", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [new MidiSignal(liveSettings.Midi[5]!, true)]);
            Check(mixer.Source == 5 && !mixer.MasterMode, "Releasing and pressing MIDI again restores the selected bus");
            activate.Invoke(window, [5, true]);
            Check(mixer.MasterMode, "Ctrl-click can deliberately enter master mode while MIDI input is selected");
            buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(mixer.Source == 0 && !mixer.MasterMode && ((BusViewModel)buttons[0].DataContext).Tooltip.Contains("Right-click"), "Normal selection returns from master mode and binding help uses right-click");
        }
        finally { window.Close(); }
    }
}

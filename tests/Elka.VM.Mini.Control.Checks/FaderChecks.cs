using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Elka.VM.Mini.Control;
using Elka.VM.Mini.Control.Core;
using Elka.VM.Mini.Control.Services;
using Elka.VM.Mini.Control.Views;

internal static partial class Program
{
    private static void FaderChecks()
    {
        using var fake = new FakeRemote(); fake.Select(2);
        fake.TextValues["Strip[0].Label"] = " RØDE mic ";
        using var mixer = new MixerController(fake);
        void Read(bool enabled = true) { mixer.Poll(); mixer.UpdateFaders(enabled); }
        void RejectEdit(Action action, string message)
        {
            int before = fake.Scripts.Count;
            bool rejected = false;
            try { action(); } catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected && fake.Scripts.Count == before, message);
        }
        Read();
        Check(mixer.Faders.Ready && mixer.Faders.Source == 2 && mixer.Faders.Levels[7] == -9 && fake.Scripts.Count == 0, "Faders read the selected submix without startup writes");
        Check(mixer.Faders.Names[0] == "RØDE mic" && mixer.Faders.Names[1] == "IN 2" && mixer.Faders.Names[7] == "VAIO 3", "Live Unicode channel names and unnamed input fallbacks");
        var before = new Dictionary<string, float>(fake.Values);
        var culture = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE"); mixer.SetFaderLevel(2, 0, -14.5f); }
        finally { CultureInfo.CurrentCulture = culture; }
        Check(fake.Scripts.Single() == "Strip[0].GainLayer[2]=-14.5;" && before.Where(p => p.Key != "Strip[0].GainLayer[2]").All(p => fake.Values[p.Key] == p.Value), "A fader changes only its input on the selected bus, with invariant decimals");
        fake.Select(4);
        RejectEdit(() => mixer.SetFaderLevel(2, 0, -2), "An external SEL switch rejects stale drag writes before the next UI poll");
        Read();
        Check(mixer.Faders.Levels[0] == -4 && mixer.Faders.Source == 4, "Switching SEL reads the new mix without copying fader positions");
        fake.Values["Strip[3].GainLayer[4]"] = -25.75f;
        fake.TextValues["Strip[0].Label"] = "New mic"; Read();
        Check(mixer.Faders.Levels[3] == -25.75f && mixer.Faders.Names[0] == "New mic", "External MIDI/mouse level changes and renamed inputs refresh live");
        mixer.SetFaderLevel(4, 7, -60); mixer.SetFaderLevel(4, 7, 12); Read();
        Check(mixer.Faders.Levels[7] == 12, "Faders support the full -60 to +12 dB gain range");
        RejectEdit(() => mixer.SetFaderLevel(4, 7, float.NaN), "Invalid fader values never reach the API");
        RejectEdit(() => mixer.SetFaderLevel(4, 7, 12.1f), "Out-of-range fader values are rejected");
        fake.Select(-1); Read();
        Check(!mixer.Faders.Ready && mixer.Faders.Names[0] == "New mic", "No SEL disables faders while preserving channel names");
        RejectEdit(() => mixer.SetFaderLevel(-1, 0, -20), "No SEL cannot turn a fader into an all-bus write");
        fake.Select(1); fake.Values["Bus[3].Sel"] = 1; Read();
        Check(!mixer.Faders.Ready, "Multiple external SEL selections disable ambiguous fader edits");
        fake.Select(1); Read(); fake.Connected = false; Read();
        Check(!mixer.Faders.Ready, "Disconnect disables stale faders");
        fake.Connected = true; Read();
        Check(mixer.Faders.Ready && mixer.Faders.Levels[0] == -1, "Reconnect restores current levels without replaying edits");
        fake.FailRead = "Strip[7].GainLayer[1]"; Read();
        Check(!mixer.Faders.Ready && mixer.Faders.Error is not null, "Partial snapshots disable the entire fader bank");
        fake.FailRead = null; Read(); fake.FailWrite = true;
        RejectEdit(() => mixer.SetFaderLevel(1, 0, -5), "Rejected writes leave the displayed API level intact");
        fake.FailWrite = false; Read(false);
        RejectEdit(() => mixer.SetFaderLevel(1, 0, -5), "Collapsed fader mode cannot accept edits");
        Read();
        var profiles = new ControlSettings { DirectInputEnabled = true }; profiles.ApplyTargets[1][4] = true;
        mixer.UpdateDirectInput(true, profiles.Destinations);
        mixer.SetFaderLevel(1, 3, -17.5f); mixer.Poll(); mixer.UpdateDirectInput(true, profiles.Destinations);
        Check(fake.Values["Strip[3].GainLayer[4]"] == -17.5f && fake.Values["Strip[3].GainLayer[5]"] == -8, "App fader changes follow only checked Direct Input destinations");

        using var delayed = new FakeRemote { ApplyWrites = false }; delayed.Select(0);
        var bank = new SubmixFaders(delayed); bank.Refresh(true, 0, now: 0);
        bank.SetLevel(0, 1, -8, now: 10); bank.Refresh(true, 0, now: 100);
        Check(bank.Levels[1] == -8, "Delayed API readback does not pull a moved fader backward");
        bank.SetLevel(0, 1, -9, now: 200); delayed.Values["Strip[1].GainLayer[0]"] = -8; bank.Refresh(true, 0, now: 300);
        Check(bank.Levels[1] == -9, "An older readback cannot replace the latest drag value");
        delayed.Values["Strip[1].GainLayer[0]"] = -9; bank.Refresh(true, 0, now: 400);
        delayed.Values["Strip[1].GainLayer[0]"] = -10; bank.Refresh(true, 0, now: 500);
        Check(bank.Levels[1] == -10 && delayed.Scripts.Count == 2, "Confirmed faders resume following external edits without echo writes");
        bank.SetLevel(0, 1, -11, now: 600); bank.Refresh(true, 0, now: 2200);
        Check(bank.Levels[1] == -10 && bank.Error is not null && delayed.Scripts.Count == 3, "Unconfirmed writes time out, show actual levels, and never retry themselves");
    }

    private sealed class FaderTestStartup : IWindowsStartup
    {
        public bool Enabled { get; private set; }
        public void SetEnabled(bool enabled) => Enabled = enabled;
    }

    private static void FaderUiChecks(string output)
    {
        var fake = new FakeRemote(); fake.Select(2);
        string[] names = ["RØDE mic", "Game audio", "Music", "Chat", "Spare input", "Desktop", "Discord", "Stream audio"];
        for (int i = 0; i < 8; i++) fake.TextValues[$"Strip[{i}].Label"] = names[i];
        var store = new SettingsStore(Path.Combine(output, "fader-ui-settings.json"));
        var settings = new ControlSettings { FaderMode = true };
        store.Save(settings);
        var window = new MainWindow(new MixerController(fake), store, new FaderTestStartup());
        try
        {
            window.Start(hidden: true);
            var panel = (FrameworkElement)window.FindName("FaderPanel");
            var root = (FrameworkElement)window.FindName("RootPanel");
            Render(root, 684, 606, Path.Combine(output, "fader-mode.png"));
            var sliders = Descendants<Slider>(panel).ToArray();
            Check(sliders.Length == 8 && window.Height == 636 && fake.Scripts.Count == 0, "Fader mode extends downward and creates eight controls without binding writes");
            Check(sliders.All(s => s.Orientation == Orientation.Vertical && s.IsEnabled) && ((FaderViewModel)sliders[0].DataContext).Name == "RØDE mic", "Wide vertical faders show VoiceMeeter channel names");
            var firstTrack = (Track)sliders[0].Template.FindName("PART_Track", sliders[0]);
            double beforeY = firstTrack.Thumb.TranslatePoint(new Point(), sliders[0]).Y;
            sliders[0].SetCurrentValue(Slider.ValueProperty, -30d); window.UpdateLayout();
            double afterY = firstTrack.Thumb.TranslatePoint(new Point(), sliders[0]).Y;
            Check(fake.Values["Strip[0].GainLayer[2]"] == -30 && afterY > beforeY, "Lower gain moves the fader cap downward and writes the selected submix");
            int writes = fake.Scripts.Count;
            fake.Select(6); window.RefreshState();
            Check(sliders[0].Value == -6 && fake.Scripts.Count == writes, "SEL switches reposition all faders without changing mixer values");
            fake.Values["Strip[7].GainLayer[6]"] = -31.2f; window.RefreshState();
            Check(Math.Abs(sliders[7].Value + 31.2) < .001 && fake.Scripts.Count == writes, "External gain changes reach WPF faders without feedback");
            // Keep a gesture tied to its starting source, even after the view shows a new one.
            typeof(MainWindow).GetField("_faderGestureSource", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, 6);
            fake.Select(1); window.RefreshState(); sliders[0].SetCurrentValue(Slider.ValueProperty, -29d);
            Check(fake.Scripts.Count == writes && sliders[0].Value == -1, "A drag across a SEL switch is rejected and returns to the current level");
            typeof(MainWindow).GetField("_faderGestureSource", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, -1);
            fake.FailWrite = true; sliders[0].SetCurrentValue(Slider.ValueProperty, -28d);
            Check(sliders[0].Value == -1 && fake.Scripts.Count == writes, "A failed fader write restores its actual displayed position");
            fake.FailWrite = false;
            fake.Select(-1); window.RefreshState();
            Check(sliders.All(s => !s.IsEnabled) && fake.Scripts.Count == writes, "Deselecting SEL disables the faders without overwriting any submix");
            Descendants<Button>((ItemsControl)window.FindName("BusButtons")).Single(b => ((BusViewModel)b.DataContext).Index == 2).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            writes = fake.Scripts.Count;
            Render(root, 684, 606, Path.Combine(output, "fader-mode.png"));

            window.Show();
            void SettingsClick(bool enabled, bool save)
            {
                window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    var dialog = Application.Current.Windows.OfType<SettingsDialog>().Single(d => d.IsVisible);
                    var toggle = Descendants<ToggleButton>(dialog).Single(b => Equals(b.Content, "Fader mode"));
                    toggle.IsChecked = enabled;
                    Descendants<Button>(dialog).Single(b => Equals(b.Content, save ? "Save" : "Cancel")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }));
                ((Button)window.FindName("SettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            SettingsClick(false, false);
            Check(panel.Visibility == Visibility.Visible && store.Load().FaderMode, "Canceling settings preserves fader mode");
            SettingsClick(false, true);
            Check(panel.Visibility == Visibility.Collapsed && window.Height == 330 && !store.Load().FaderMode, "Saving Fader mode off collapses the window and persists the setting");
            SettingsClick(true, true);
            Check(panel.Visibility == Visibility.Visible && window.Height == 636 && store.Load().FaderMode && fake.Scripts.Count == writes, "Saving Fader mode on expands and reads levels without mixer writes");
            var dialog = new SettingsDialog(null, store.Load());
            Render((FrameworkElement)dialog.Content, 434, 764, Path.Combine(output, "fader-settings.png"));
            dialog.Close();
        }
        finally { window.Close(); }
    }
}

using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Elka.VM.Mini.Control;
using Elka.VM.Mini.Control.Core;

internal static partial class Program
{
    private static async Task DirectInputChecks()
    {
        using var fake = new FakeRemote(); using var mixer = new MixerController(fake);
        var profiles = new ControlSettings(); profiles.ApplyTargets[1][3] = profiles.ApplyTargets[1][4] = profiles.ApplyTargets[1][5] = true;
        profiles.ApplyTargets[2][0] = profiles.ApplyTargets[2][7] = true;
        void Tick(bool enabled = true, bool suspended = false) { mixer.Poll(); mixer.UpdateDirectInput(enabled, profiles.Destinations, suspended); }
        fake.Select(1); Tick(false); Tick();
        Check(fake.Scripts.Count == 0, "Direct Input arms without copying existing levels");
        var before = new Dictionary<string, float>(fake.Values);
        fake.Values["Strip[2].GainLayer[1]"] = -18.75f;
        var culture = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE"); Tick(); }
        finally { CultureInfo.CurrentCulture = culture; }
        Check(new[] { 3, 4, 5 }.All(bus => fake.Values[$"Strip[2].GainLayer[{bus}]"] == -18.75f), "Moving an A2 input relays its exact level to A4, A5 and B1");
        Check(fake.Scripts.Single().Split(';', StringSplitOptions.RemoveEmptyEntries).Length == 3 && !fake.Scripts[0].Contains(','), "Direct Input writes only changed inputs with invariant decimal values");
        Check(before.Where(p => p.Key != "Strip[2].GainLayer[1]" && !new[] { 3, 4, 5 }.Any(b => p.Key == $"Strip[2].GainLayer[{b}]" )).All(p => fake.Values[p.Key] == p.Value), "Unmoved inputs, routing, pan, master levels and unchecked buses stay unchanged");
        Tick(); Tick(); Check(fake.Scripts.Count == 1 && !mixer.DirectInput.Confirming, "Readback confirms changes without resending unchanged values");
        fake.Values["Strip[2].GainLayer[3]"] = -9; Tick();
        Check(fake.Scripts.Count == 1 && fake.Values["Strip[2].GainLayer[1]"] == -18.75f, "Destination changes never feed back into the source or other destinations");
        for (int strip = 0; strip < 8; strip++) fake.Values[$"Strip[{strip}].GainLayer[1]"] = strip == 0 ? -60 : strip == 7 ? 12 : -strip * 2.5f;
        Tick();
        Check(new[] { 3, 4, 5 }.All(bus => Enumerable.Range(0, 8).All(strip => fake.Values[$"Strip[{strip}].GainLayer[{bus}]"] == fake.Values[$"Strip[{strip}].GainLayer[1]"])), "All eight inputs can move together, including mute-floor and maximum gain");
        int writes = fake.Scripts.Count; fake.Select(2); Tick();
        Check(fake.Scripts.Count == writes, "External SEL changes switch source without copying old baselines");
        fake.Values["Strip[5].GainLayer[2]"] = -31; Tick();
        Check(fake.Values["Strip[5].GainLayer[0]"] == -31 && fake.Values["Strip[5].GainLayer[7]"] == -31 && fake.Values["Strip[5].GainLayer[3]"] != -31, "New SEL follows only its own Apply profile");
        writes = fake.Scripts.Count; Tick(false); fake.Values["Strip[5].GainLayer[2]"] = -22; Tick(false); Tick();
        Check(fake.Scripts.Count == writes, "Turning Direct Input off stops copying and rearming does not replay old changes");
        profiles.ApplyTargets[2][0] = false; profiles.ApplyTargets[2][5] = true; Tick();
        Check(fake.Scripts.Count == writes, "Changing saved destinations takes a fresh baseline");
        fake.Values["Strip[5].GainLayer[2]"] = -21; Tick();
        Check(fake.Values["Strip[5].GainLayer[0]"] == -31 && fake.Values["Strip[5].GainLayer[5]"] == -21 && fake.Values["Strip[5].GainLayer[7]"] == -21, "Removed destinations stop following immediately");
        writes = fake.Scripts.Count; Tick(suspended: true); fake.Values["Strip[5].GainLayer[2]"] = -20; Tick(suspended: true); Tick();
        Check(fake.Scripts.Count == writes, "Modal input/settings actions suspend linking without replaying edits afterward");
        fake.Select(-1); Tick(); fake.Values["Strip[5].GainLayer[2]"] = -19; Tick(); fake.Select(2); Tick();
        Check(fake.Scripts.Count == writes, "No SEL pauses linking and reselecting does not replay changes");
        fake.Values["Bus[7].Sel"] = 1; Tick(); fake.Values["Strip[5].GainLayer[2]"] = -18; Tick(); fake.Select(2); Tick();
        Check(fake.Scripts.Count == writes, "Ambiguous multi-SEL state never becomes a Direct Input source");
        fake.Connected = false; Tick(); fake.Values["Strip[5].GainLayer[2]"] = -17; fake.Connected = true; Tick();
        Check(fake.Scripts.Count == writes, "Reconnect starts from current levels without replaying stale values");
        profiles.ApplyTargets[2] = new bool[8]; Tick(); fake.Values["Strip[5].GainLayer[2]"] = -16; Tick();
        Check(fake.Scripts.Count == writes && mixer.DirectInput.Status.Contains("destinations"), "An empty Apply profile waits without writing");
        profiles.ApplyTargets[2][0] = true; Tick(); fake.ApplyWrites = false; mixer.Toggle(1); Tick();
        fake.Values["Strip[5].GainLayer[2]"] = -15; writes = fake.Scripts.Count; Tick();
        Check(fake.Scripts.Count == writes && mixer.SelectionPending, "Unconfirmed SEL changes pause Direct Input");
        fake.Select(1); fake.ApplyWrites = true; Tick();
        var manual = mixer.ApplyFromAsync(2, [1]); Tick(); await manual; Tick();
        writes = fake.Scripts.Count; Tick();
        Check(fake.Scripts.Count == writes, "Manual Apply into the source is not echoed into linked destinations");
        fake.Values["Strip[0].GainLayer[1]"] = -43; fake.FailRead = "Strip[7].GainLayer[1]"; Tick();
        Check(fake.Scripts.Count == writes && mixer.DirectInput.Faulted, "A failed source snapshot pauses Direct Input before any writes");
        fake.FailRead = null; Tick(); Check(fake.Scripts.Count == writes, "A fault remains paused until explicitly rearmed");
        Tick(false); Tick(); fake.Values["Strip[0].GainLayer[1]"] = float.NaN; Tick();
        Check(fake.Scripts.Count == writes && mixer.DirectInput.Faulted, "Invalid source levels cannot be relayed");
        fake.Values["Strip[0].GainLayer[1]"] = -43; Tick(false); Tick();
        fake.FailWrite = true; fake.Values["Strip[0].GainLayer[1]"] = -42; Tick();
        Check(mixer.DirectInput.Faulted && fake.Scripts.Count == writes, "Rejected API writes pause the link instead of repeatedly retrying");
        fake.FailWrite = false;

        using var delayed = new FakeRemote { ApplyWrites = false };
        var link = new DirectInputLink(delayed);
        link.Tick(true, 0, [1, 1, 0], now: 0);
        delayed.Values["Strip[0].GainLayer[0]"] = -6; link.Tick(true, 0, [1], now: 100);
        delayed.Values["Strip[0].GainLayer[0]"] = -7; link.Tick(true, 0, [1], now: 200);
        delayed.Values["Strip[0].GainLayer[0]"] = -8; link.Tick(true, 0, [1], now: 300);
        Check(delayed.Scripts.Count == 3 && delayed.Scripts.Last() == "Strip[0].GainLayer[1]=-8;" && !link.Faulted, "Rapid fader moves send the newest value without waiting for earlier readbacks");
        delayed.Values["Strip[0].GainLayer[1]"] = -8; link.Tick(true, 0, [1], now: 400);
        Check(!link.Confirming && !link.Faulted, "Delayed readback confirms the most recent fader value");
        delayed.Values["Strip[0].GainLayer[0]"] = -9; link.Tick(true, 0, [1], now: 500); link.Tick(true, 0, [1], now: 2100);
        Check(link.Faulted && delayed.Scripts.Count == 4, "Unconfirmed writes time out and pause without a write loop");
        link.Tick(false, 0, [], now: 2200); link.Tick(true, 0, [1], now: 2300);
        delayed.Values["Strip[0].GainLayer[0]"] = -10; link.Tick(true, 0, [1], now: 2400);
        link.Tick(true, 2, [3], now: 5000);
        Check(!link.Faulted && !link.Confirming && delayed.Scripts.Count == 5, "Switching SEL discards pending tracking for the former source");
    }

    private static void DirectInputUiChecks(string output)
    {
        var fake = new FakeRemote(); fake.Select(1);
        var store = new SettingsStore(Path.Combine(output, "direct-ui-settings.json"));
        var settings = new ControlSettings { DirectInputEnabled = true };
        settings.ApplyTargets[1][3] = settings.ApplyTargets[1][4] = settings.ApplyTargets[1][5] = true; store.Save(settings);
        var window = new MainWindow(new MixerController(fake), store);
        try
        {
            window.Start(hidden: true);
            var toggle = (ToggleButton)window.FindName("DirectInputButton");
            Check(toggle.IsChecked == true && fake.Scripts.Count == 0, "Saved Direct Input mode restores without an initial mixer write");
            var root = (FrameworkElement)window.FindName("RootPanel");
            Render(root, 684, 318, Path.Combine(output, "direct-input-on.png"));
            var applies = Descendants<Button>((ItemsControl)window.FindName("ApplyButtons")).ToArray();
            var position = toggle.TransformToAncestor(root).Transform(new Point());
            var left = applies[6].TransformToAncestor(root).Transform(new Point());
            var right = applies[7].TransformToAncestor(root).Transform(new Point(applies[7].ActualWidth, applies[7].ActualHeight));
            Check(Math.Abs(position.X - left.X) <= 1 && Math.Abs(position.X + toggle.ActualWidth - right.X) <= 1 && position.Y >= right.Y, $"Direct Input spans Apply B2 and B3 and sits underneath them on the right (toggle {position.X:0.##}–{position.X + toggle.ActualWidth:0.##}, Apply {left.X:0.##}–{right.X:0.##}, top {position.Y:0.##}, row bottom {right.Y:0.##})");
            Check(Math.Abs(toggle.ActualHeight - applies[7].ActualHeight / 2) <= 1 && ((SolidColorBrush)toggle.Background).Color == Color.FromRgb(22, 136, 245), "Direct Input is half an Apply button high and bright blue while on");
            fake.Values["Strip[3].GainLayer[1]"] = -38;
            PumpUntil(() => fake.Values["Strip[3].GainLayer[3]"] == -38);
            Check(!window.IsVisible && fake.Values["Strip[3].GainLayer[4]"] == -38 && fake.Values["Strip[3].GainLayer[5]"] == -38, "UI polling relays changes to all saved targets even while hidden in tray");
            toggle.IsChecked = false; toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            fake.Values["Strip[3].GainLayer[1]"] = -37; window.RefreshState();
            Check(!store.Load().DirectInputEnabled && fake.Values["Strip[3].GainLayer[3]"] == -38, "Turning the toggle off saves the choice and stops automatic copying");
            Render(root, 684, 300, Path.Combine(output, "direct-input-off.png"));
            Check(((SolidColorBrush)toggle.Background).Color == Color.FromRgb(34, 42, 48), "Direct Input uses the normal gray button color while off");
            toggle.IsChecked = true; toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check(store.Load().DirectInputEnabled && fake.Values["Strip[3].GainLayer[3]"] == -38, "Re-enabling persists the mode without an unsolicited full copy");
        }
        finally { window.Close(); }
    }
}

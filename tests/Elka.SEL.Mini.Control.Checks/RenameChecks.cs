using System.IO;
using Elka.SEL.Mini.Control.Core;
using Elka.SEL.Mini.Control.Services;
using Microsoft.Win32;

internal static partial class Program
{
    private static void RenameSettingsChecks(string output)
    {
        string folder = Path.Combine(output, "rename-" + Guid.NewGuid().ToString("N"));
        string oldPath = Path.Combine(folder, "ElkaVMMiniControl", "settings.json");
        string newPath = Path.Combine(folder, "ElkaSELMiniControl", "settings.json");
        var old = new ControlSettings
        {
            FaderMode = true, DirectInputEnabled = true, StartInTray = true,
            Mode = InputMode.Midi, MidiDevice = "Rename test controller",
            Vban = new() { Port = 43210, StreamName = "SavedStream" }
        };
        old.Hotkeys[2] = new(7, 65); old.Midi[9] = new(1, 0x90, 62); old.ApplyTargets[2][5] = true;
        new SettingsStore(oldPath).Save(old);
        string backup = File.ReadAllText(oldPath);
        var renamedStore = new SettingsStore(newPath, oldPath);
        var imported = renamedStore.Load();
        Check(imported.FaderMode && imported.DirectInputEnabled && imported.StartInTray
            && imported.MidiDevice == old.MidiDevice && imported.Midi[9] == old.Midi[9]
            && imported.Hotkeys[2] == old.Hotkeys[2] && imported.ApplyTargets[2][5]
            && imported.Vban.Port == 43210 && imported.Vban.StreamName == "SavedStream",
            "Rename migration retains input bindings, Apply targets, VBAN and display preferences");
        Check(File.Exists(newPath) && File.ReadAllText(oldPath) == backup, "Migrating validates and saves new preferences while preserving the old file");
        imported.FaderMode = false; renamedStore.Save(imported);
        Check(!renamedStore.Load().FaderMode, "Existing renamed preferences take precedence over the old backup");
        string invalidPath = Path.Combine(folder, "invalid.json"), unusedPath = Path.Combine(folder, "unused.json");
        File.WriteAllText(invalidPath, "{\"Hotkeys\":[]}");
        bool rejected = false;
        try { new SettingsStore(unusedPath, invalidPath).Load(); } catch (InvalidDataException) { rejected = true; }
        Check(rejected && !File.Exists(unusedPath), "Invalid legacy settings are reported without creating a replacement file");

        string keyPath = @"Software\ElkaSoft\RenameChecks\" + Guid.NewGuid().ToString("N");
        string executable = Path.Combine(folder, "Elka.SEL.Mini.Control.exe");
        File.WriteAllText(executable, "Startup path fixture, not executed.");
        var startup = new WindowsStartup(executable, keyPath);
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(keyPath);
            key.SetValue("Unrelated app", "Keep this");
            startup.MigrateLegacyRegistration();
            Check(!startup.Enabled, "Rebranding does not enable startup when no previous entry exists");
            key.SetValue(WindowsStartup.LegacyEntryName, "old-app.exe");
            Check(startup.Enabled, "Settings recognizes startup enabled under the previous app name");
            startup.MigrateLegacyRegistration();
            Check((string?)key.GetValue(WindowsStartup.EntryName) == $"\"{executable}\""
                && key.GetValue(WindowsStartup.LegacyEntryName) is null && (string?)key.GetValue("Unrelated app") == "Keep this",
                "Startup migration points to the renamed executable and removes only the old app entry");
            key.SetValue(WindowsStartup.LegacyEntryName, "old-app.exe");
            startup.SetEnabled(false);
            Check(!startup.Enabled && key.GetValue(WindowsStartup.EntryName) is null && key.GetValue(WindowsStartup.LegacyEntryName) is null,
                "Turning startup off removes both old and renamed app registrations");
            key.SetValue(WindowsStartup.LegacyEntryName, "old-app.exe");
            bool failed = false;
            try { new WindowsStartup(executable + ".missing", keyPath).MigrateLegacyRegistration(); }
            catch (InvalidOperationException) { failed = true; }
            Check(failed && key.GetValue(WindowsStartup.LegacyEntryName) is not null && key.GetValue(WindowsStartup.EntryName) is null,
                "A failed startup migration retains the previous working registration");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(keyPath, false); }
    }
}

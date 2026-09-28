using System.IO;
using Microsoft.Win32;

namespace Elka.SEL.Mini.Control.Services;

public interface IWindowsStartup
{
    bool Enabled { get; }
    void SetEnabled(bool enabled);
}

public sealed class WindowsStartup : IWindowsStartup
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string EntryName = "Elka SEL Mini Control";
    public const string LegacyEntryName = "Elka VM Mini Control";
    private readonly string _executable, _key, _name;
    public WindowsStartup(string? executable = null, string key = RunKey, string name = EntryName)
    {
        _executable = executable ?? Path.Combine(AppContext.BaseDirectory, "Elka.SEL.Mini.Control.exe");
        _key = key; _name = name;
    }
    public bool Enabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(_key);
            return HasCommand(key, _name) || (_name == EntryName && HasCommand(key, LegacyEntryName));
        }
    }
    public void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            using var existing = Registry.CurrentUser.OpenSubKey(_key, writable: true);
            existing?.DeleteValue(_name, throwOnMissingValue: false);
            if (_name == EntryName) existing?.DeleteValue(LegacyEntryName, throwOnMissingValue: false);
            return;
        }
        if (!Path.IsPathFullyQualified(_executable) || !File.Exists(_executable) || _executable.Contains('"'))
            throw new InvalidOperationException("The app executable could not be found. Run it from its permanent folder before enabling Windows startup.");
        string command = $"\"{_executable}\"";
        if (command.Length > 260)
            throw new InvalidOperationException("The app's folder path is too long for Windows startup. Move it to a shorter path first.");
        using var key = Registry.CurrentUser.CreateSubKey(_key, writable: true);
        key.SetValue(_name, command, RegistryValueKind.String);
        if (_name == EntryName) key.DeleteValue(LegacyEntryName, throwOnMissingValue: false);
    }

    public void MigrateLegacyRegistration()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_key);
        if (_name == EntryName && HasCommand(key, LegacyEntryName)) SetEnabled(true);
    }

    private static bool HasCommand(RegistryKey? key, string name)
        => key?.GetValue(name) is string command && !string.IsNullOrWhiteSpace(command);
}

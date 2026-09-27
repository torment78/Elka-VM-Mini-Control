using System.IO;
using Microsoft.Win32;

namespace Elka.VM.Mini.Control.Services;

public interface IWindowsStartup
{
    bool Enabled { get; }
    void SetEnabled(bool enabled);
}

public sealed class WindowsStartup : IWindowsStartup
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly string _executable, _key, _name;
    public WindowsStartup(string? executable = null, string key = RunKey, string name = "Elka VM Mini Control")
    {
        _executable = executable ?? Path.Combine(AppContext.BaseDirectory, "Elka.VM.Mini.Control.exe");
        _key = key; _name = name;
    }
    public bool Enabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(_key);
            return key?.GetValue(_name) is string command && !string.IsNullOrWhiteSpace(command);
        }
    }
    public void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            using var existing = Registry.CurrentUser.OpenSubKey(_key, writable: true);
            existing?.DeleteValue(_name, throwOnMissingValue: false);
            return;
        }
        if (!Path.IsPathFullyQualified(_executable) || !File.Exists(_executable) || _executable.Contains('"'))
            throw new InvalidOperationException("The app executable could not be found. Run it from its permanent folder before enabling Windows startup.");
        string command = $"\"{_executable}\"";
        if (command.Length > 260)
            throw new InvalidOperationException("The app's folder path is too long for Windows startup. Move it to a shorter path first.");
        using var key = Registry.CurrentUser.CreateSubKey(_key, writable: true);
        key.SetValue(_name, command, RegistryValueKind.String);
    }
}

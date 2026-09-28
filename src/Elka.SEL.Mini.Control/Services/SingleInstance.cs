using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Elka.SEL.Mini.Control.Services;

/// <summary>One mixer connection per Windows session, shared across folders and versions.</summary>
public sealed class SingleInstance : IDisposable
{
    // Keep the shared identity so the renamed app and v0.5.0 cannot run together.
    public const string Identity = @"Local\ElkaSoft.ElkaVMMiniControl";
    public static IReadOnlyList<string> ProcessNames { get; } = Array.AsReadOnly(new[] { "Elka.SEL.Mini.Control", "Elka.VM.Mini.Control" });
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private RegisteredWaitHandle? _listener;
    private bool _disposed;
    public bool IsPrimary { get; }

    public SingleInstance(string identity = Identity)
    {
        // Create the event first so an activation during startup is retained.
        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, identity + ".Activate");
        _mutex = new Mutex(false, identity + ".Instance");
        try { IsPrimary = _mutex.WaitOne(0); }
        catch (AbandonedMutexException) { IsPrimary = true; }
    }

    public void Listen(Action activate)
    {
        if (!IsPrimary || _listener is not null) throw new InvalidOperationException("Only the primary instance can listen once.");
        _listener = ThreadPool.RegisterWaitForSingleObject(_activate, (_, _) => activate(), null, Timeout.Infinite, false);
    }

    public void RequestActivation()
    {
        // Pass foreground permission from this user-launched process to the running copy.
        using var current = Process.GetCurrentProcess();
        foreach (var process in ProcessNames.SelectMany(Process.GetProcessesByName))
        {
            using (process)
            {
                try
                {
                    if (process.Id != current.Id && process.SessionId == current.SessionId)
                        AllowSetForegroundWindow(process.Id);
                }
                catch (InvalidOperationException) { }
                catch (Win32Exception) { }
            }
        }
        _activate.Set();
    }

    // Builds before 0.5.0 do not participate in the named lock. Detect them on upgrade.
    public static bool IsLegacyVersion(string? productVersion)
    {
        string text = productVersion?.Split('+')[0] ?? "";
        string number = text.Split('-')[0];
        return !Version.TryParse(number, out var version) || version < new Version(0, 5, 0)
            || text.StartsWith("0.5.0-", StringComparison.Ordinal);
    }

    public static Process? FindLegacyInstance()
    {
        using var current = Process.GetCurrentProcess();
        foreach (var process in ProcessNames.SelectMany(Process.GetProcessesByName))
        {
            try
            {
                if (process.Id != current.Id && process.SessionId == current.SessionId
                    && IsLegacyVersion(process.MainModule?.FileVersionInfo.ProductVersion)) return process;
            }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { }
            process.Dispose();
        }
        return null;
    }

    public static bool RestoreLegacyWindow(Process process)
    {
        try
        {
            process.Refresh();
            nint handle = process.MainWindowHandle;
            if (handle == 0) return false;
            AllowSetForegroundWindow(process.Id);
            ShowWindowAsync(handle, 9); // SW_RESTORE
            SetForegroundWindow(handle);
            return true;
        }
        catch (InvalidOperationException) { return false; }
        catch (Win32Exception) { return false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _listener?.Unregister(null);
        _activate.Dispose();
        // Owned and released on the application startup/shutdown thread.
        if (IsPrimary) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }

    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(nint window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
}

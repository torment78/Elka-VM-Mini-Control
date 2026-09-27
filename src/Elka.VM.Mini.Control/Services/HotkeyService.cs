using System.Runtime.InteropServices;
using System.Windows.Interop;
using Elka.VM.Mini.Control.Core;

namespace Elka.VM.Mini.Control.Services;

public sealed class HotkeyService : IDisposable
{
    private readonly HwndSource _source;
    private readonly HashSet<int> _registered = [];
    public event Action<int>? Pressed;
    public HotkeyService(IntPtr handle)
    {
        _source = HwndSource.FromHwnd(handle)!;
        _source.AddHook(Hook);
    }
    public string? Configure(ControlSettings settings)
    {
        Clear();
        if (settings.Mode != InputMode.Hotkeys) return null;
        var failed = new List<string>();
        for (int i = 0; i < ControlSettings.ButtonCount; i++)
            if (settings.Hotkeys[i] is { } binding)
            {
                if (RegisterHotKey(_source.Handle, i + 1, binding.Modifiers | 0x4000, binding.Key)) _registered.Add(i + 1);
                else failed.Add($"{ControlSettings.ActionName(i)} ({binding})");
            }
        return failed.Count == 0 ? null : "Hotkey unavailable: " + string.Join(", ", failed) + ". Assign another shortcut.";
    }
    public bool CanRegister(HotkeyBinding binding)
    {
        if (!binding.IsValid || !RegisterHotKey(_source.Handle, 99, binding.Modifiers | 0x4000, binding.Key)) return false;
        UnregisterHotKey(_source.Handle, 99);
        return true;
    }
    public void Clear()
    {
        foreach (int id in _registered) UnregisterHotKey(_source.Handle, id);
        _registered.Clear();
    }
    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Most window messages carry pointers in wParam, not 32-bit hotkey IDs.
        if (msg != 0x0312) return IntPtr.Zero;
        long value = wParam.ToInt64();
        if (value is >= 1 and <= ControlSettings.ButtonCount && _registered.Contains((int)value))
        {
            handled = true;
            Pressed?.Invoke((int)value - 1);
        }
        return IntPtr.Zero;
    }
    public void Dispose() { Clear(); _source.RemoveHook(Hook); }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}

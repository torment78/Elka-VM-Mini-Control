using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Elka.VM.Mini.Control.Services;

public static class WindowAppearance
{
    public static void Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        int dark = 1, caption = 0x00171411, text = 0x00F6F3ED;
        DwmSetWindowAttribute(handle, 20, ref dark, 4);
        DwmSetWindowAttribute(handle, 35, ref caption, 4);
        DwmSetWindowAttribute(handle, 36, ref text, 4);
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

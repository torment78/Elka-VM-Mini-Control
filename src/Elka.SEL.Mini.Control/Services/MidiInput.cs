using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using Elka.SEL.Mini.Control.Core;

namespace Elka.SEL.Mini.Control.Services;

public sealed record MidiDevice(uint Id, string Name);
public sealed class MidiInput : IDisposable
{
    private readonly MidiCallback _callback;
    private readonly Dispatcher _dispatcher;
    private IntPtr _handle;
    private int _generation;
    public bool IsOpen => _handle != IntPtr.Zero;
    public event Action<MidiSignal>? Received;
    public MidiInput(Dispatcher dispatcher) { _dispatcher = dispatcher; _callback = Callback; }

    public static List<MidiDevice> Devices()
    {
        var devices = new List<MidiDevice>();
        var counts = new Dictionary<string, int>();
        for (uint i = 0; i < midiInGetNumDevs(); i++)
        {
            if (midiInGetDevCapsW((UIntPtr)i, out var caps, (uint)Marshal.SizeOf<MidiCaps>()) != 0) continue;
            counts.TryGetValue(caps.Name, out int count); counts[caps.Name] = ++count;
            devices.Add(new(i, caps.Name + (count == 1 ? "" : $" ({count})")));
        }
        return devices;
    }
    public void Open(string name)
    {
        Close();
        var device = Devices().FirstOrDefault(d => d.Name == name) ?? throw new InvalidOperationException("MIDI device unavailable. Connect it and reselect it in Settings.");
        Check(midiInOpen(out _handle, device.Id, _callback, UIntPtr.Zero, 0x30000));
        try { Check(midiInStart(_handle)); }
        catch { Close(); throw; }
    }
    private void Callback(IntPtr handle, uint message, UIntPtr instance, UIntPtr data, UIntPtr timestamp)
    {
        if (message != 0x3C3 || _dispatcher.HasShutdownStarted) return;
        var signal = MidiSignal.Decode((uint)data.ToUInt64());
        if (signal is null) return;
        int generation = Volatile.Read(ref _generation);
        // WinMM callbacks must not call multimedia APIs. Dispatch all work to the UI thread.
        _dispatcher.BeginInvoke(() => { if (generation == _generation && IsOpen) Received?.Invoke(signal); });
    }
    public void Close()
    {
        Interlocked.Increment(ref _generation);
        if (_handle == IntPtr.Zero) return;
        midiInStop(_handle); midiInReset(_handle); midiInClose(_handle); _handle = IntPtr.Zero;
    }
    private static void Check(uint code)
    {
        if (code == 0) return;
        var message = new StringBuilder(256);
        midiInGetErrorTextW(code, message, (uint)message.Capacity);
        throw new InvalidOperationException($"MIDI: {message} ({code})");
    }
    public void Dispose() { Close(); GC.KeepAlive(_callback); }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MidiCaps
    {
        public ushort Manufacturer, Product;
        public uint DriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        public uint Support;
    }
    private delegate void MidiCallback(IntPtr handle, uint message, UIntPtr instance, UIntPtr data, UIntPtr timestamp);
    [DllImport("winmm.dll")] private static extern uint midiInGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] private static extern uint midiInGetDevCapsW(UIntPtr id, out MidiCaps caps, uint size);
    [DllImport("winmm.dll")] private static extern uint midiInOpen(out IntPtr handle, uint id, MidiCallback callback, UIntPtr instance, uint flags);
    [DllImport("winmm.dll")] private static extern uint midiInStart(IntPtr handle);
    [DllImport("winmm.dll")] private static extern uint midiInStop(IntPtr handle);
    [DllImport("winmm.dll")] private static extern uint midiInReset(IntPtr handle);
    [DllImport("winmm.dll")] private static extern uint midiInClose(IntPtr handle);
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] private static extern uint midiInGetErrorTextW(uint code, StringBuilder text, uint size);
}

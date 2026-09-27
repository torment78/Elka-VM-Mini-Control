using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;

namespace Elka.VM.Mini.Control.Core;

public enum InputMode { Hotkeys, Midi }
public sealed record HotkeyBinding(uint Modifiers, uint Key)
{
    public bool IsValid => Key is > 0 and < 255 && (Modifiers & ~15u) == 0 && Key is not (16 or 17 or 18 or 91 or 92);
    public override string ToString()
    {
        var names = new List<string>();
        if ((Modifiers & 2) != 0) names.Add("Ctrl");
        if ((Modifiers & 1) != 0) names.Add("Alt");
        if ((Modifiers & 4) != 0) names.Add("Shift");
        if ((Modifiers & 8) != 0) names.Add("Win");
        names.Add(KeyInterop.KeyFromVirtualKey((int)Key).ToString());
        return string.Join("+", names);
    }
}
public sealed record MidiBinding(int Channel, int Command, int Number)
{
    public bool IsValid => Channel is >= 0 and < 16 && Command is 0x90 or 0xB0 && Number is >= 0 and < 128;
    public override string ToString() => $"Ch {Channel + 1} · {(Command == 0x90 ? "Note" : "CC")} {Number}";
}
public sealed record MidiSignal(MidiBinding Binding, bool Pressed)
{
    public static MidiSignal? Decode(uint packed)
    {
        int status = (int)(packed & 255), command = status & 0xF0;
        if (command is not (0x80 or 0x90 or 0xB0)) return null;
        int value = (int)((packed >> 16) & 127);
        return new(new(status & 15, command == 0x80 ? 0x90 : command, (int)((packed >> 8) & 127)),
            command != 0x80 && value > 0);
    }
}
public sealed class MidiPressTracker
{
    private readonly HashSet<MidiBinding> _held = [];
    public bool Process(MidiSignal signal)
    {
        if (signal.Pressed) return _held.Add(signal.Binding);
        _held.Remove(signal.Binding);
        return false;
    }
    public void Clear() => _held.Clear();
}
public sealed class ControlSettings
{
    public const int ButtonCount = 16;
    public InputMode Mode { get; set; } = InputMode.Hotkeys;
    public string? MidiDevice { get; set; }
    public bool StartWithWindows { get; set; }
    public bool StartInTray { get; set; }
    public bool CloseToTray { get; set; } = true;
    public HotkeyBinding?[] Hotkeys { get; set; } = new HotkeyBinding?[ButtonCount];
    public MidiBinding?[] Midi { get; set; } = new MidiBinding?[ButtonCount];
    public bool[][] ApplyTargets { get; set; } = Enumerable.Range(0, 8).Select(_ => new bool[8]).ToArray();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool[]? Targets { get; set; } // Read the original single Apply profile for migration.
    public VbanSettings Vban { get; set; } = new();
    public static string ActionName(int index) => $"{MixerController.BusNames[index % 8]} {(index < 8 ? "SEL" : "Apply")}";
    public IEnumerable<int> Destinations(int source) => Enumerable.Range(0, 8).Where(i => i != source && ApplyTargets[source][i]);
    public void Upgrade()
    {
        if (Hotkeys?.Length == 8) Hotkeys = [.. Hotkeys, .. new HotkeyBinding?[8]];
        if (Midi?.Length == 8) Midi = [.. Midi, .. new MidiBinding?[8]];
        if (Targets is not null)
        {
            if (Targets.Length != 8) throw new InvalidDataException("Invalid saved destination list.");
            ApplyTargets = Enumerable.Range(0, 8).Select(source => Targets.Select((on, bus) => on && bus != source).ToArray()).ToArray();
            Targets = null;
        }
    }
    public void Validate()
    {
        if (Vban is null) throw new InvalidDataException("Invalid VBAN settings.");
        Vban.Validate();
        if (!Enum.IsDefined(Mode) || Hotkeys is null || Midi is null || ApplyTargets is null || Hotkeys.Length != ButtonCount || Midi.Length != ButtonCount || ApplyTargets.Length != 8 || ApplyTargets.Any(row => row is null || row.Length != 8))
            throw new InvalidDataException("Invalid settings structure.");
        if (Hotkeys.Any(h => h is not null && !h.IsValid) || Midi.Any(m => m is not null && !m.IsValid))
            throw new InvalidDataException("Invalid input binding.");
        if (Hotkeys.Where(h => h is not null).Distinct().Count() != Hotkeys.Count(h => h is not null) ||
            Midi.Where(m => m is not null).Distinct().Count() != Midi.Count(m => m is not null))
            throw new InvalidDataException("Duplicate input bindings.");
    }
}
public sealed class SettingsStore(string path)
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ElkaSoft", "ElkaVMMiniControl", "settings.json");
    public ControlSettings Load()
    {
        if (!File.Exists(path)) return new();
        var settings = JsonSerializer.Deserialize<ControlSettings>(File.ReadAllText(path)) ?? throw new InvalidDataException("Empty settings file.");
        settings.Upgrade();
        settings.Validate();
        return settings;
    }
    public void Save(ControlSettings settings)
    {
        settings.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, true);
    }
}

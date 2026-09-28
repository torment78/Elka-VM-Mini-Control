using System.ComponentModel;

namespace Elka.VM.Mini.Control.Core;

public sealed class BusViewModel(int index) : INotifyPropertyChanged
{
    public int Index { get; } = index;
    public string Name => MixerController.BusNames[Index];
    public bool Selected { get; private set; }
    public string BindingLabel { get; private set; } = "Unassigned";
    public string AccessibleLabel => $"{Name} SEL, {(Selected ? "on" : "off")}, {BindingLabel}";
    public string Tooltip { get; private set; } = "";
    public string ApplySummary { get; private set; } = "Set targets";
    public string ApplyTooltip { get; private set; } = "";
    public string ApplyAccessibleLabel => $"{Name} Apply · {ApplySummary}";
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Update(bool selected, ControlSettings settings)
    {
        string label = settings.Mode == InputMode.Hotkeys ? settings.Hotkeys[Index]?.ToString() ?? "Unassigned" : settings.Midi[Index]?.ToString() ?? "Unassigned";
        string tip = $"{Name} SEL · {(selected ? "On" : "Off")}\n{label}\nClick to select. Ctrl-click the active SEL for master mode.\n" + (settings.Mode == InputMode.Hotkeys ? "Right-click to assign a hotkey." : "Right-click to learn a MIDI button.");
        string[] destinations = settings.Destinations(Index).Select(i => MixerController.BusNames[i]).ToArray();
        string summary = destinations.Length == 0 ? "Set targets" : string.Join(", ", destinations);
        string applyBinding = settings.Mode == InputMode.Hotkeys ? settings.Hotkeys[Index + 8]?.ToString() ?? "No hotkey" : settings.Midi[Index + 8]?.ToString() ?? "No MIDI binding";
        string applyTip = $"Copy {Name} input levels to: {summary}\n{applyBinding}\nCtrl-click to choose destinations.\nRight-click to {(settings.Mode == InputMode.Hotkeys ? "assign a hotkey" : "learn MIDI")}.";
        if (Selected == selected && BindingLabel == label && Tooltip == tip && ApplyTooltip == applyTip) return;
        Selected = selected; BindingLabel = label; Tooltip = tip;
        ApplySummary = summary; ApplyTooltip = applyTip;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}

using System.ComponentModel;
using System.Globalization;

namespace Elka.SEL.Mini.Control.Core;

public sealed class FaderViewModel(int index) : INotifyPropertyChanged
{
    public int Index { get; } = index;
    public string Input => SubmixFaders.InputNames[Index];
    public string Name { get; private set; } = SubmixFaders.InputNames[index];
    public double Level { get; private set; } = -60;
    public bool Ready { get; private set; }
    public string LevelText => Ready ? Level.ToString("0.0", CultureInfo.InvariantCulture) + " dB" : "—";
    public string AccessibleLabel => $"{Input} · {Name} · {LevelText}";
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Update(string name, float level, bool ready)
    {
        double shown = ready ? level : -60;
        if (Name == name && Level == shown && Ready == ready) return;
        Name = name; Level = shown; Ready = ready;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}

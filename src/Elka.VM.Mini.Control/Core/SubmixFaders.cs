using System.Globalization;

namespace Elka.VM.Mini.Control.Core;

/// <summary>A read-through submix bank; only explicit user edits issue writes.</summary>
public sealed class SubmixFaders(IRemoteApi remote)
{
    public static readonly string[] InputNames = ["IN 1", "IN 2", "IN 3", "IN 4", "IN 5", "VAIO", "AUX", "VAIO 3"];
    public int Source { get; private set; } = -1;
    public bool Ready { get; private set; }
    public float[] Levels { get; private set; } = new float[8];
    public string[] Names { get; private set; } = [.. InputNames];
    public string? Error { get; private set; }
    private readonly Dictionary<int, (float Level, long Deadline)> _pending = [];

    public void Refresh(bool enabled, int source, bool suspended = false, long? now = null)
    {
        Ready = false;
        if (!enabled)
        {
            Source = -1; _pending.Clear(); Error = null; return;
        }
        if (Source != source) { _pending.Clear(); Source = source; }
        try
        {
            var names = Enumerable.Range(0, 8).Select(strip =>
            {
                string label = remote.ReadText($"Strip[{strip}].Label").Trim();
                return string.IsNullOrWhiteSpace(label) ? InputNames[strip] : label;
            }).ToArray();
            Names = names;
            if (source is < 0 or > 7 || suspended)
            {
                Source = -1; _pending.Clear(); Error = null; return;
            }
            var levels = new float[8];
            for (int strip = 0; strip < 8; strip++)
            {
                levels[strip] = remote.Read($"Strip[{strip}].GainLayer[{source}]");
                if (!float.IsFinite(levels[strip]) || levels[strip] is < -60 or > 12)
                    throw new InvalidOperationException("VoiceMeeter returned an invalid input level.");
            }
            Error = null;
            long time = now ?? Environment.TickCount64;
            foreach (var (strip, pending) in _pending.ToArray())
            {
                if (Math.Abs(levels[strip] - pending.Level) <= .025f) _pending.Remove(strip);
                else if (time >= pending.Deadline)
                {
                    _pending.Remove(strip);
                    Error = "Fader change was not confirmed. Showing VoiceMeeter's current level.";
                }
                else levels[strip] = pending.Level; // Avoid jumping back while a queued write arrives.
            }
            Levels = levels; Names = names; Ready = true;
        }
        catch (Exception ex) { _pending.Clear(); Error = ex.Message; }
    }

    public void SetLevel(int source, int strip, float level, long? now = null)
    {
        if (!Ready || source != Source || source is < 0 or > 7)
            throw new InvalidOperationException("Select one SEL and wait for its input levels first.");
        if (strip is < 0 or > 7 || !float.IsFinite(level) || level is < -60 or > 12)
            throw new ArgumentOutOfRangeException(nameof(level));
        // Check live selection again; a drag may outlive the source shown on screen.
        remote.Refresh();
        int[] selected = Enumerable.Range(0, 8).Where(i => remote.Read($"Bus[{i}].Sel") > .5f).ToArray();
        if (selected.Length != 1 || selected[0] != source)
            throw new InvalidOperationException("SEL changed. Release the fader and try again.");
        remote.Write($"Strip[{strip}].GainLayer[{source}]={level.ToString("R", CultureInfo.InvariantCulture)};");
        Levels[strip] = level; Error = null;
        _pending[strip] = (level, (now ?? Environment.TickCount64) + 1500);
    }
}

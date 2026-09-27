using System.Globalization;

namespace Elka.VM.Mini.Control.Core;

/// <summary>Relays only changes in the active SEL's input levels to its saved destinations.</summary>
public sealed class DirectInputLink(IRemoteApi remote)
{
    private int _source = -1;
    private int[] _targets = [];
    private float[]? _baseline;
    private readonly Dictionary<int, (float Level, long Deadline)> _pending = [];
    public bool Faulted { get; private set; }
    public string Status { get; private set; } = "Direct Input off";
    public bool Confirming => _pending.Count > 0;

    private void ResetTracking()
    {
        _source = -1; _targets = []; _baseline = null; _pending.Clear();
    }

    // The caller refreshes the API before each tick. All access stays on the UI thread.
    public void Tick(bool enabled, int source, IEnumerable<int> destinations, string? pauseReason = null, long? now = null)
    {
        if (!enabled)
        {
            ResetTracking(); Faulted = false; Status = "Direct Input off"; return;
        }
        if (Faulted) return;
        if (pauseReason is not null || source is < 0 or > 7)
        {
            ResetTracking(); Status = "Direct Input · " + (pauseReason ?? "select one SEL source"); return;
        }
        try
        {
            int[] targets = destinations.Distinct().Where(bus => bus != source).Order().ToArray();
            if (targets.Any(bus => bus is < 0 or > 7)) throw new InvalidOperationException("Invalid destination bus.");
            if (targets.Length == 0)
            {
                ResetTracking(); Status = $"Direct Input · choose {MixerController.BusNames[source]} Apply destinations"; return;
            }
            // Finish reading all eight inputs before issuing a write. GainLayer is independent of SEL.
            float[] levels = Enumerable.Range(0, 8).Select(strip => remote.Read($"Strip[{strip}].GainLayer[{source}]")).ToArray();
            if (levels.Any(level => !float.IsFinite(level) || level < -60 || level > 12))
                throw new InvalidOperationException("VoiceMeeter returned an invalid submix level.");
            string link = $"Direct Input · {MixerController.BusNames[source]} → {string.Join(", ", targets.Select(bus => MixerController.BusNames[bus]))}";
            if (_source != source || !_targets.SequenceEqual(targets) || _baseline is null)
            {
                // Arming, changing source/profile and reconnecting capture a fresh baseline.
                // They never overwrite destinations before the user moves a source input.
                ResetTracking(); _source = source; _targets = targets; _baseline = levels;
                Status = link + " · watching"; return;
            }
            long time = now ?? Environment.TickCount64;
            int[] changed = Enumerable.Range(0, 8).Where(strip => Math.Abs(levels[strip] - _baseline[strip]) > .0001f).ToArray();
            // Confirm earlier writes without blocking the next fader move. New moves supersede
            // outstanding values on the same strip, so dragging always sends the latest value.
            foreach (var (strip, pending) in _pending.ToArray())
            {
                if (changed.Contains(strip)) continue;
                if (targets.All(bus => Math.Abs(remote.Read($"Strip[{strip}].GainLayer[{bus}]") - pending.Level) <= .025f))
                    _pending.Remove(strip);
                else if (time >= pending.Deadline)
                    throw new InvalidOperationException("Destination levels were not confirmed. Check VoiceMeeter.");
            }
            if (changed.Length > 0)
            {
                string script = string.Join("", targets.SelectMany(bus => changed.Select(strip =>
                    $"Strip[{strip}].GainLayer[{bus}]={levels[strip].ToString("R", CultureInfo.InvariantCulture)};")));
                remote.Write(script);
                foreach (int strip in changed)
                {
                    _baseline[strip] = levels[strip];
                    _pending[strip] = (levels[strip], time + 1500);
                }
            }
            Status = link + (Confirming ? " · sending" : " · watching");
        }
        catch (Exception ex)
        {
            ResetTracking(); Faulted = true;
            Status = "Direct Input paused · " + ex.Message + " Toggle off/on to retry.";
        }
    }
}

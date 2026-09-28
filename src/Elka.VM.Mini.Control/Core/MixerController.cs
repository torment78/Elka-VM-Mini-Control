using System.Globalization;

namespace Elka.VM.Mini.Control.Core;

public interface IRemoteApi : IDisposable
{
    void Refresh();
    float Read(string parameter);
    string ReadText(string parameter);
    void Write(string script);
}

public sealed class MixerController(IRemoteApi remote, Func<long>? clock = null) : IDisposable
{
    public static readonly string[] BusNames = ["A1", "A2", "A3", "A4", "A5", "B1", "B2", "B3"];
    public bool[] Selected { get; private set; } = new bool[8];
    public bool Connected { get; private set; }
    public bool Applying { get; private set; }
    public bool SelectionPending => _pending is not null;
    public string Status { get; private set; } = "Connecting to VoiceMeeter…";
    public string? ActionError { get; private set; }
    public DirectInputLink DirectInput { get; } = new(remote);
    public SubmixFaders Faders { get; } = new(remote);
    public int Source => Selected.Count(v => v) == 1 ? Array.IndexOf(Selected, true) : -1;
    public bool MasterMode => Connected && _allowMaster && !SelectionPending && !Selected.Any(v => v);
    private bool[]? _pending;
    private long _pendingUntil;
    private int _lastSource;
    private bool _allowMaster, _guardFaulted, _haveSelection;
    private long? _emptySince;
    private long Now => clock?.Invoke() ?? Environment.TickCount64;
    private readonly CancellationTokenSource _lifetime = new();

    public void UpdateDirectInput(bool enabled, Func<int, IEnumerable<int>> destinations, bool suspended = false)
    {
        string? pause = !Connected ? "waiting for VoiceMeeter" :
            SelectionPending ? "waiting for SEL" : Applying || suspended ? "waiting for the current action" : null;
        DirectInput.Tick(enabled, Source, Source >= 0 ? destinations(Source) : [], pause);
    }

    public void Poll()
    {
        try
        {
            remote.Refresh();
            var selected = Enumerable.Range(0, 8).Select(i => remote.Read($"Bus[{i}].Sel") > .5f).ToArray();
            Selected = selected;
            Connected = true;
            if (_pending is not null)
            {
                if (selected.SequenceEqual(_pending)) _pending = null;
                else if (Now > _pendingUntil)
                {
                    _pending = null;
                    _allowMaster = false; _guardFaulted = true;
                    ActionError = "VoiceMeeter did not confirm the SEL change. Try again.";
                    return;
                }
            }
            if (!SelectionPending)
            {
                if (Selected.Any(v => v))
                {
                    if (Source >= 0) _lastSource = Source;
                    _haveSelection = true;
                    _allowMaster = false; _emptySince = null; _guardFaulted = false;
                }
                else if (_haveSelection && !_guardFaulted)
                {
                    // External clears may come from MIDI/hotkeys outside this app.
                    // Their normal toggle behavior is intentionally not protected.
                    _allowMaster = true; _emptySince = null;
                }
                else if (!_allowMaster && !_guardFaulted)
                {
                    // Allow initial API synchronization / an external bus switch to finish.
                    _emptySince ??= Now;
                    if (Now - _emptySince >= 250 && !Applying) QueueSelection(_lastSource);
                }
            }
            Status = SelectionPending ? "Waiting for VoiceMeeter…" : MasterMode
                ? "Master mode · all SEL off" : !Selected.Any(v => v)
                ? "Restoring SEL · select a bus to continue" : "VoiceMeeter Potato connected";
        }
        catch (Exception ex)
        {
            Connected = false;
            Selected = new bool[8];
            _pending = null;
            _allowMaster = false; _guardFaulted = false; _haveSelection = false; _emptySince = null;
            Status = ex.Message;
        }
    }

    public void Toggle(int bus)
        => RequestSelection(bus, SelAction.On);

    // MIDI/hotkey bindings retain ordinary toggle behavior for this release.
    public void ToggleBinding(int bus)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bus);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bus, 7);
        if (Applying || SelectionPending) return;
        ActionError = null;
        Poll();
        if (!Connected || SelectionPending) return;
        QueueSelection(Selected[bus] ? -1 : bus);
    }

    // Mouse deselection requires the explicit Ctrl-click UI path.
    public void EnterMasterMode(int activeBus)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(activeBus);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(activeBus, 7);
        if (Applying || SelectionPending) return;
        Poll();
        if (!Connected || SelectionPending || !Selected[activeBus]) return;
        ActionError = null;
        QueueSelection(-1);
    }

    public void UpdateFaders(bool enabled, bool suspended = false)
        => Faders.Refresh(enabled && Connected, Source, SelectionPending || Applying || suspended);

    public void SetFaderLevel(int source, int strip, float level)
    {
        Poll();
        if (!Connected || Applying || SelectionPending || Source != source)
            throw new InvalidOperationException("SEL changed or VoiceMeeter is busy. Release the fader and try again.");
        Faders.SetLevel(source, strip, level);
    }

    private void RequestSelection(int bus, SelAction action)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bus);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bus, 7);
        if (Applying || SelectionPending) return;
        ActionError = null;
        Poll();
        if (!Connected || SelectionPending) return;
        if (action == SelAction.Off)
        {
            if (Selected[bus]) ActionError = "Use Ctrl-click on the active SEL to enter master mode.";
            return;
        }
        _allowMaster = false;
        if (Source == bus) return;
        QueueSelection(bus);
    }

    private void QueueSelection(int bus)
    {
        var desired = new bool[8];
        if (bus >= 0) desired[bus] = true;
        try
        {
            // Select the new bus before clearing others, avoiding an all-off gap.
            string script = bus >= 0 ? $"Bus[{bus}].Sel=1;" : "";
            script += string.Join("", Enumerable.Range(0, 8).Where(i => i != bus).Select(i => $"Bus[{i}].Sel=0;"));
            remote.Write(script);
            _allowMaster = bus < 0; _guardFaulted = false; _emptySince = null;
            _pending = desired;
            _pendingUntil = Now + 1000;
        }
        catch (Exception ex) { _allowMaster = false; _guardFaulted = true; ActionError = ex.Message; }
    }

    public async Task SelectAsync(int bus, SelAction action, CancellationToken token = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        for (int attempt = 0; Applying || SelectionPending; attempt++)
        {
            if (attempt >= 50) throw new InvalidOperationException("Mixer is busy. Send the command again.");
            await Task.Delay(40, linked.Token); Poll();
        }
        linked.Token.ThrowIfCancellationRequested();
        RequestSelection(bus, action);
        while (SelectionPending) { await Task.Delay(40, linked.Token); Poll(); }
        if (!Connected) throw new InvalidOperationException(Status);
        if (ActionError is not null) throw new InvalidOperationException(ActionError);
    }

    public Task<string> ApplyAsync(IEnumerable<int> requestedTargets)
    {
        Poll();
        if (Source < 0) throw new InvalidOperationException("Select one source bus first.");
        return ApplyFromAsync(Source, requestedTargets);
    }

    public async Task<string> ApplyFromAsync(int source, IEnumerable<int> requestedTargets)
    {
        if (Applying || SelectionPending) throw new InvalidOperationException("Wait for the current change to finish.");
        Poll();
        if (!Connected) throw new InvalidOperationException(Status);
        if (source is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(source));
        int[] targets = requestedTargets.Distinct().Where(i => i != source).ToArray();
        if (targets.Length == 0) throw new InvalidOperationException($"Ctrl-click {BusNames[source]} Apply to choose its destinations.");
        if (targets.Any(i => i is < 0 or > 7)) throw new ArgumentOutOfRangeException(nameof(requestedTargets));
        Applying = true;
        ActionError = null;
        try
        {
            // Read the entire source before the first write. Never use Strip.Gain: it depends on SEL.
            float[] levels = Enumerable.Range(0, 8).Select(i => remote.Read($"Strip[{i}].GainLayer[{source}]")).ToArray();
            if (levels.Any(v => !float.IsFinite(v) || v < -60 || v > 12))
                throw new InvalidOperationException("VoiceMeeter returned an invalid submix level. Nothing was copied.");
            string script = string.Join("", targets.SelectMany(bus => Enumerable.Range(0, 8)
                .Select(strip => $"Strip[{strip}].GainLayer[{bus}]={levels[strip].ToString("R", CultureInfo.InvariantCulture)};")));
            remote.Write(script);
            // The remote API queues writes; success is reported only after readback agrees.
            for (int attempt = 0; attempt < 30; attempt++)
            {
                await Task.Delay(50, _lifetime.Token);
                remote.Refresh();
                if (targets.All(bus => Enumerable.Range(0, 8).All(strip =>
                    Math.Abs(remote.Read($"Strip[{strip}].GainLayer[{bus}]") - levels[strip]) <= .025f)))
                    return $"Copied {BusNames[source]} levels to {string.Join(", ", targets.Select(i => BusNames[i]))}.";
            }
            throw new InvalidOperationException("Copy was not fully confirmed. Some levels may have changed; check VoiceMeeter before retrying.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException("Apply: " + ex.Message, ex);
        }
        finally { Applying = false; }
    }

    public void Dispose() { _lifetime.Cancel(); remote.Dispose(); _lifetime.Dispose(); }
}

using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Elka.VM.Mini.Control.Core;

namespace Elka.VM.Mini.Control.Services;

public sealed record VbanTextPacket(string Text, uint Frame, byte Channel);
public static class VbanTextProtocol
{
    public static VbanTextPacket Decode(ReadOnlySpan<byte> packet, string stream)
    {
        if (packet.Length is <= 28 or > 1464 || !packet[..4].SequenceEqual("VBAN"u8) || (packet[4] & 0xE0) != 0x40)
            throw new FormatException("Not a complete VBAN Text packet.");
        if ((packet[4] & 0x1F) > 24 || packet[5] != 0 || packet[6] != 0)
            throw new FormatException("Unsupported VBAN Text header. Use text channel 0.");
        var nameBytes = packet.Slice(8, 16);
        int terminator = nameBytes.IndexOf((byte)0);
        if (terminator >= 0) nameBytes = nameBytes[..terminator];
        foreach (byte b in nameBytes) if (b is < 32 or > 126) throw new FormatException("Invalid VBAN stream name.");
        if (!Encoding.ASCII.GetString(nameBytes).Equals(stream, StringComparison.Ordinal))
            throw new FormatException("VBAN stream name does not match " + stream + ".");
        Encoding encoding = packet[7] switch
        {
            0x00 => Encoding.GetEncoding("us-ascii", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
            0x10 => new UTF8Encoding(false, true),
            0x20 => new UnicodeEncoding(false, false, true),
            _ => throw new FormatException("Unsupported VBAN Text encoding.")
        };
        string text;
        try { text = encoding.GetString(packet[28..]).TrimEnd('\0').Trim(); }
        catch (DecoderFallbackException) { throw new FormatException("Malformed VBAN Text encoding."); }
        if (text.Length == 0 || text.Contains('\0')) throw new FormatException("Empty or malformed VBAN text.");
        return new(text, BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(24, 4)), packet[6]);
    }
}

public sealed class VbanFrameTracker
{
    private readonly Dictionary<string, (uint Frame, long At)> _senders = [];
    public bool Accept(string sender, uint frame, long now)
    {
        if (_senders.TryGetValue(sender, out var previous) && now - previous.At < 2000 && unchecked((int)(frame - previous.Frame)) <= 0)
            return false;
        if (_senders.Count >= 128 && !_senders.ContainsKey(sender)) _senders.Remove(_senders.MinBy(p => p.Value.At).Key);
        _senders[sender] = (frame, now);
        return true;
    }
}

public sealed class VbanTextReceiver : IDisposable
{
    private readonly UdpClient _udp;
    private readonly CancellationTokenSource _cancel = new();
    private readonly VbanSettings _settings;
    private readonly Func<VbanTextPacket, IPEndPoint, CancellationToken, Task> _received;
    private readonly Action<string> _diagnostic;
    private readonly VbanFrameTracker _frames = new();
    private readonly IPAddress? _allowedSender;
    private long _lastDiagnostic;
    private bool _disposed;
    public Task Completion { get; }
    public int Port => ((IPEndPoint)_udp.Client.LocalEndPoint!).Port;

    public VbanTextReceiver(VbanSettings settings, Func<VbanTextPacket, IPEndPoint, CancellationToken, Task> received, Action<string> diagnostic)
    {
        settings.Validate(); _settings = settings; _received = received; _diagnostic = diagnostic;
        _allowedSender = settings.SenderAddress.Length == 0 ? null : IPAddress.Parse(settings.SenderAddress);
        _udp = new UdpClient(AddressFamily.InterNetwork);
        try
        {
            _udp.ExclusiveAddressUse = true;
            _udp.Client.ReceiveBufferSize = 65536;
            _udp.Client.Bind(new IPEndPoint(IPAddress.Parse(settings.ListenAddress), settings.Port));
        }
        catch { _udp.Dispose(); _cancel.Dispose(); throw; }
        var token = _cancel.Token;
        Completion = Task.Run(() => ReceiveLoop(token));
    }
    private async Task ReceiveLoop(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var result = await _udp.ReceiveAsync(token).ConfigureAwait(false);
                if (_allowedSender is not null && !_allowedSender.Equals(result.RemoteEndPoint.Address))
                { Report("Packet ignored: sender address does not match."); continue; }
                try
                {
                    var packet = VbanTextProtocol.Decode(result.Buffer, _settings.StreamName);
                    if (!_frames.Accept(result.RemoteEndPoint.ToString(), packet.Frame, Environment.TickCount64)) continue;
                    // Await each batch: no unbounded dispatcher queue and no overlapping Apply operations.
                    await _received(packet, result.RemoteEndPoint, token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is FormatException or InvalidOperationException) { Report(ex.Message); }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (token.IsCancellationRequested) { }
        catch (SocketException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { Report("VBAN receiver stopped: " + ex.Message, true); }
        finally { _cancel.Dispose(); }
    }
    private void Report(string text, bool force = false)
    {
        long now = Environment.TickCount64;
        if (!force && now - _lastDiagnostic < 1000) return;
        _lastDiagnostic = now; _diagnostic(text);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _cancel.Cancel(); } catch (ObjectDisposedException) { }
        _udp.Dispose();
    }
}

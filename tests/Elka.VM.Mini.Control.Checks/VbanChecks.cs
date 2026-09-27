using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Elka.VM.Mini.Control.Core;
using Elka.VM.Mini.Control.Services;

internal static partial class Program
{
    private static async Task VbanChecks()
    {
        var basic = VmcCommands.Parse("VMC.SEL(A1); VMC.SEL[A2]=On; vmc.sel.(b3)=0;");
        Check(basic.Count == 3 && basic[0].Bus == 0 && basic[1].Action == SelAction.On && basic[2].Bus == 7 && basic[2].Action == SelAction.Off, "VMC prefix, bus names, brackets, optional dot and on/off parsing");
        var copies = VmcCommands.Parse("VMC.SEL.Apply(A1);VMC.SEL.Apply[B3];VMC.SEL.Apply.(A2);");
        Check(copies.All(c => c.Apply) && copies.Select(c => c.Bus).SequenceEqual([0, 7, 1]), "Each VMC Apply command identifies one saved source button");
        foreach (string invalid in new[] { "VMC.SEL(A6);", "VMC.SEL(A1];", "VMC.SEL: A1;", "VMC.SEL(A1,A2);", "VMC.SEL.Apply(A1,B1);", "VMC.SEL.Apply();", "VMC.SEL.Apply(All);", "VMC.SEL.Apply(A1)=0;", "Command.Shutdown=1;", "VMC.SEL(A1)=3;" })
        {
            bool rejected = false; try { VmcCommands.Parse(invalid); } catch (FormatException) { rejected = true; }
            Check(rejected, "Reject unsupported command: " + invalid);
        }
        byte[] standard = Packet("VMC.SEL(A1);", 123);
        var decoded = VbanTextProtocol.Decode(standard, "Command1");
        Check(decoded.Frame == 123 && decoded.Text == "VMC.SEL(A1);", "Decode official 0x52 / UTF-8 VBAN header and frame counter");
        Check(VbanTextProtocol.Decode(Packet("VMC.SEL(A2);", 4, encoding: 0), "Command1").Text == "VMC.SEL(A2);", "ASCII VBAN Text accepted");
        Check(VbanTextProtocol.Decode(Packet("VMC.SEL.Apply(B1);", 5, encoding: 0x20), "Command1").Text == "VMC.SEL.Apply(B1);", "Wide-character VBAN Text accepted");
        var badPackets = new List<byte[]> { "VMC.SEL(A1);"u8.ToArray(), standard[..27], Packet("VMC.SEL(A1);", 1, "Other") };
        var audio = (byte[])standard.Clone(); audio[4] = 0x12; badPackets.Add(audio);
        var encoding = (byte[])standard.Clone(); encoding[7] = 0x11; badPackets.Add(encoding);
        var channel = (byte[])standard.Clone(); channel[6] = 1; badPackets.Add(channel);
        var malformed = Packet("X", 1); malformed[28] = 0xFF; badPackets.Add(malformed);
        var embeddedNull = Packet("VMC.SEL(A1);\0Command.Shutdown=1;", 2); badPackets.Add(embeddedNull);
        var oversized = new byte[1465]; standard.CopyTo(oversized, 0); badPackets.Add(oversized);
        Check(badPackets.All(RejectPacket), "Reject wrong stream, raw UDP, audio, malformed encoding, nonzero channel and oversized packets");
        var frames = new VbanFrameTracker();
        Check(frames.Accept("sender", uint.MaxValue, 0) && !frames.Accept("sender", uint.MaxValue, 10) && frames.Accept("sender", 0, 20) && !frames.Accept("sender", uint.MaxValue, 30), "Duplicate/reordered frames ignored and frame counter wrap supported");
        Check(frames.Accept("sender", 0, 2200) && frames.Accept("other", 0, 2201), "Sender restart after idle and independent senders supported");
        var old = JsonSerializer.Deserialize<ControlSettings>("{}")!; old.Validate();
        Check(!old.Vban.Enabled && old.Vban.Port == 6982, "Older saved settings get disabled VBAN defaults");
        var stored = new ControlSettings { Vban = new() { Enabled = true, Port = 16982, ListenAddress = "0.0.0.0", StreamName = "MiniControl", SenderAddress = "192.0.2.1" } };
        var restored = JsonSerializer.Deserialize<ControlSettings>(JsonSerializer.Serialize(stored))!; restored.Validate();
        Check(restored.Vban == stored.Vban, "VBAN settings round-trip without disturbing existing bindings");
        foreach (var invalid in new[] { new VbanSettings { Port = 0 }, new VbanSettings { ListenAddress = "not an IP" }, new VbanSettings { StreamName = "12345678901234567" }, new VbanSettings { StreamName = "é" }, new VbanSettings { SenderAddress = "not an IP" } })
        {
            bool rejected = false; try { invalid.Validate(); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Invalid VBAN configuration rejected");
        }
        using var fake = new FakeRemote(); using var mixer = new MixerController(fake);
        var profiles = new ControlSettings(); profiles.ApplyTargets[2][0] = profiles.ApplyTargets[2][7] = true; profiles.ApplyTargets[5][1] = true;
        await VmcCommands.ExecuteAsync("VMC.SEL(A3)=1;VMC.SEL.Apply[A3];", mixer, profiles.Destinations);
        Check(mixer.Source == 2 && Enumerable.Range(0, 8).All(i => fake.Values[$"Strip[{i}].GainLayer[0]"] == fake.Values[$"Strip[{i}].GainLayer[2]"] && fake.Values[$"Strip[{i}].GainLayer[7]"] == fake.Values[$"Strip[{i}].GainLayer[2]"]), "Combined VMC SEL and Apply waits for source and uses its saved profile");
        await VmcCommands.ExecuteAsync("VMC.SEL.Apply(B1);", mixer, profiles.Destinations);
        Check(fake.Values["Strip[0].GainLayer[1]"] == fake.Values["Strip[0].GainLayer[5]"] && mixer.Source == 2, "Each Apply uses its own source and destinations without changing active SEL");
        int writes = fake.Scripts.Count;
        await VmcCommands.ExecuteAsync("VMC.SEL(A3)=1;VMC.SEL(A1)=0;", mixer, _ => []);
        Check(mixer.Source == 2 && fake.Scripts.Count == writes, "Explicit On is idempotent and Off on an inactive bus preserves the source");
        bool invalidBatch = false;
        try { await VmcCommands.ExecuteAsync("VMC.SEL(A2);Command.Shutdown=1;", mixer, _ => []); } catch (FormatException) { invalidBatch = true; }
        Check(invalidBatch && fake.Scripts.Count == writes, "Whole command batch validated before any mixer write");
        await VmcCommands.ExecuteAsync("VMC.SEL[A3];", mixer, _ => []);
        Check(mixer.Source == -1, "VMC SEL without a value toggles off an active bus");
        await VmcCommands.ExecuteAsync("VMC.SEL.Apply(A3);", mixer, profiles.Destinations);
        Check(mixer.Source == -1, "A saved Apply profile can run with no SEL active");
        await Reject(() => VmcCommands.ExecuteAsync("VMC.SEL.Apply(A4);", mixer, profiles.Destinations), "VBAN Apply with no saved destinations is rejected");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        bool cancellation = false; writes = fake.Scripts.Count;
        try { await VmcCommands.ExecuteAsync("VMC.SEL(A1);", mixer, _ => [], cancelled.Token); } catch (OperationCanceledException) { cancellation = true; }
        Check(cancellation && fake.Scripts.Count == writes, "Cancelled receiver work cannot write mixer settings");
        await UdpChecks();
    }
    private static bool RejectPacket(byte[] bytes)
    {
        try { VbanTextProtocol.Decode(bytes, "Command1"); return false; } catch (FormatException) { return true; }
    }
    private static byte[] Packet(string text, uint frame, string stream = "Command1", byte encoding = 0x10)
    {
        var codec = encoding == 0x20 ? Encoding.Unicode : encoding == 0 ? Encoding.ASCII : Encoding.UTF8;
        byte[] payload = codec.GetBytes(text + "\0");
        byte[] packet = new byte[28 + payload.Length];
        "VBAN"u8.CopyTo(packet); packet[4] = 0x52; packet[7] = encoding;
        Encoding.ASCII.GetBytes(stream).CopyTo(packet, 8); BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(24, 4), frame);
        payload.CopyTo(packet, 28); return packet;
    }
    private static int FreePort()
    {
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)udp.Client.LocalEndPoint!).Port;
    }
    private static async Task UdpChecks()
    {
        using var fake = new FakeRemote(); using var mixer = new MixerController(fake);
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int handled = 0;
        var settings = new VbanSettings { Enabled = true, Port = FreePort(), StreamName = "CustomMini", ListenAddress = "127.0.0.2" };
        using var receiver = new VbanTextReceiver(settings, async (packet, sender, token) =>
        {
            await VmcCommands.ExecuteAsync(packet.Text, mixer, _ => [0, 7], token);
            if (++handled == 1) first.SetResult(); else if (handled == 2) second.SetResult();
        }, _ => { });
        using var sender = new UdpClient(AddressFamily.InterNetwork);
        var endpoint = new IPEndPoint(IPAddress.Parse(settings.ListenAddress), receiver.Port);
        byte[] selectAndCopy = Packet("VMC.SEL(A3)=1;VMC.SEL.Apply(A3);", 1, settings.StreamName);
        await sender.SendAsync(selectAndCopy, endpoint); await first.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Check(mixer.Source == 2 && fake.Values["Strip[4].GainLayer[7]"] == fake.Values["Strip[4].GainLayer[2]"], "Custom port, listen IP and stream receive real UDP commands and apply a saved profile");
        await sender.SendAsync(selectAndCopy, endpoint); // duplicate
        await sender.SendAsync(Packet("VMC.SEL(A3);", 0, settings.StreamName), endpoint); // stale
        await sender.SendAsync(Packet("VMC.SEL(A3);", 2, "Other"), endpoint);
        await sender.SendAsync("VMC.SEL(A3);"u8.ToArray(), endpoint);
        await sender.SendAsync(Packet("VMC.SEL(A3);", 2, settings.StreamName), endpoint);
        await second.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Check(handled == 2 && mixer.Source == -1, "UDP duplicates, stale frames, wrong streams and raw text do not trigger SEL");
        bool conflict = false;
        try { using var other = new VbanTextReceiver(settings, (_, _, _) => Task.CompletedTask, _ => { }); } catch (SocketException) { conflict = true; }
        Check(conflict, "Occupied VBAN port reports a bind failure instead of sharing the socket");
        receiver.Dispose(); await receiver.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        using (var reopened = new VbanTextReceiver(settings, (_, _, _) => Task.CompletedTask, _ => { }))
        {
            reopened.Dispose(); await reopened.Completion.WaitAsync(TimeSpan.FromSeconds(3));
            Check(true, "Stopping VBAN releases the port and allows an immediate restart");
        }
        var denied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int unexpected = 0;
        using var filtered = new VbanTextReceiver(settings with { SenderAddress = "192.0.2.5" }, (_, _, _) => { unexpected++; return Task.CompletedTask; }, _ => denied.TrySetResult());
        await sender.SendAsync(Packet("VMC.SEL(A1);", 9), new IPEndPoint(IPAddress.Parse(settings.ListenAddress), filtered.Port));
        await denied.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Check(unexpected == 0, "Allowed-sender IP filter rejects other sources");
    }
}

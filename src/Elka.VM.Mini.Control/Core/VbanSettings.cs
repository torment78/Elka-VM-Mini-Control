using System.IO;
using System.Net;
using System.Net.Sockets;

namespace Elka.VM.Mini.Control.Core;

public sealed record VbanSettings
{
    public bool Enabled { get; init; }
    public int Port { get; init; } = 6982;
    public string StreamName { get; init; } = "Command1";
    public string ListenAddress { get; init; } = "127.0.0.1";
    public string SenderAddress { get; init; } = "";
    public void Validate()
    {
        if (Port is < 1 or > 65535) throw new InvalidDataException("VBAN port must be between 1 and 65535.");
        if (!IPAddress.TryParse(ListenAddress, out var local) || local.AddressFamily != AddressFamily.InterNetwork)
            throw new InvalidDataException("Listen IP must be an IPv4 address. Use 0.0.0.0 for all local interfaces.");
        if (StreamName is null || StreamName.Length is < 1 or > 16 || StreamName.Any(c => c is < ' ' or > '~') || StreamName != StreamName.Trim())
            throw new InvalidDataException("VBAN stream name must contain 1–16 ASCII characters with no leading or trailing spaces.");
        if (SenderAddress is null || (SenderAddress.Length > 0 && (!IPAddress.TryParse(SenderAddress, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)))
            throw new InvalidDataException("Allowed sender must be an IPv4 address, or blank for any sender.");
    }
}

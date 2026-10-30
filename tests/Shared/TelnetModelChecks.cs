using PortHarbor;

namespace PortHarbor.Checks;

internal static class TelnetModelChecks
{
    internal static void Run(Action<bool, string> Check)
    {
        var replies = new List<byte[]>(); var codec = new TelnetCodec(replies.Add);
        Check(codec.Decode(new byte[] { 65, 255, 251 }).SequenceEqual(new byte[] { 65 }), "Telnet IAC fragmentation preserves text");
        codec.Decode(new byte[] { 1 }); Check(replies.Single().SequenceEqual(new byte[] { 255, 253, 1 }), "Telnet negotiates echo across packet boundary");
        codec.Decode(new byte[] { 255, 253, 24, 255, 250, 24, 1, 255 }); codec.Decode(new byte[] { 240 });
        Check(replies.Last().Contains((byte)'v'), "Telnet terminal type subnegotiation");
        Check(codec.Decode(new byte[] { 255, 255, 66 }).SequenceEqual(new byte[] { 255, 66 }), "Telnet escapes literal IAC");
        Check(TelnetCodec.Encode([13, 255]).SequenceEqual(new byte[] { 13, 0, 255, 255 }), "Telnet NVT CR and outgoing IAC encoding");
    }
}

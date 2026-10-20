namespace EasyConnect;

// Incremental IAC parser. State persists across TCP packets.
public sealed class TelnetCodec(Action<byte[]> reply)
{
    int state; byte command;
    readonly List<byte> sub = [];
    readonly HashSet<byte> remoteOptions = [], localOptions = [];
    public byte[] Decode(ReadOnlySpan<byte> bytes)
    {
        var result = new List<byte>();
        foreach (byte b in bytes)
        {
            switch (state)
            {
                case 0: if (b == 255) state = 1; else result.Add(b); break;
                case 1:
                    if (b == 255) { result.Add(b); state = 0; }
                    else if (b is 251 or 252 or 253 or 254) { command = b; state = 2; }
                    else if (b == 250) { sub.Clear(); state = 3; }
                    else state = 0;
                    break;
                case 2:
                    if (command == 251) { if (b is 0 or 1 or 3) { if (remoteOptions.Add(b)) reply([255, 253, b]); } else reply([255, 254, b]); }
                    else if (command == 253) { if (b is 0 or 3 or 24) { if (localOptions.Add(b)) reply([255, 251, b]); } else reply([255, 252, b]); }
                    else if (command == 252) { if (remoteOptions.Remove(b)) reply([255, 254, b]); }
                    else if (command == 254) { if (localOptions.Remove(b)) reply([255, 252, b]); }
                    state = 0; break;
                case 3: if (b == 255) state = 4; else if (sub.Count < 1024) sub.Add(b); break;
                case 4:
                    if (b == 240) { if (sub.Count >= 2 && sub[0] == 24 && sub[1] == 1 && localOptions.Contains(24)) reply([255, 250, 24, 0, .. System.Text.Encoding.ASCII.GetBytes("vt100"), 255, 240]); state = 0; }
                    else { if (b == 255 && sub.Count < 1024) sub.Add(255); state = 3; }
                    break;
            }
        }
        return result.ToArray();
    }
    public static byte[] Encode(byte[] data)
    {
        var result = new List<byte>(data.Length);
        foreach (byte b in data) { result.Add(b); if (b == 255) result.Add(255); if (b == 13) result.Add(0); }
        return result.ToArray();
    }
}

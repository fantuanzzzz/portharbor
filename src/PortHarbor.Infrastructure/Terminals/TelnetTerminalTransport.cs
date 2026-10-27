using System.Net.Sockets;

namespace PortHarbor;

internal sealed class TelnetTerminalTransport(TelnetProtocolConfiguration session) : StreamTerminalTransport
{
    readonly List<byte[]> replies = [];
    TelnetCodec? codec;
    protected override async Task OpenAsync(int columns, int rows, Action<ConnectionStage> reportStage, CancellationToken token)
    {
        reportStage(ConnectionStage.Connecting);
        var tcp = Own(new TcpClient());
        await tcp.ConnectAsync(session.Host, session.Port, token).ConfigureAwait(false);
        reportStage(ConnectionStage.OpeningChannel);
        Input = Output = tcp.GetStream();
        codec = new TelnetCodec(replies.Add);
    }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token)
    {
        var raw = new byte[buffer.Length];
        while (true)
        {
            int count = await Output.ReadAsync(raw, token).ConfigureAwait(false);
            if (count == 0) return 0;
            byte[] payload = codec!.Decode(raw.AsSpan(0, count));
            foreach (var reply in replies) await WriteRawAsync(reply, token).ConfigureAwait(false);
            replies.Clear();
            if (payload.Length == 0) continue;
            payload.CopyTo(buffer);
            return payload.Length;
        }
    }
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token)
        => WriteRawAsync(TelnetCodec.Encode(bytes.ToArray()), token);
    protected override async ValueTask WriteStreamAsync(ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        await Input.WriteAsync(bytes, token).ConfigureAwait(false);
        await Input.FlushAsync(token).ConfigureAwait(false);
    }
}

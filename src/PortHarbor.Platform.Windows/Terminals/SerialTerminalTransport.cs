using System.IO.Ports;

namespace PortHarbor;

internal sealed class SerialTerminalTransport(SerialProtocolConfiguration session) : StreamTerminalTransport
{
    protected override Task OpenAsync(int columns, int rows, Action<ConnectionStage> reportStage, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        reportStage(ConnectionStage.OpeningChannel);
        var serial = Own(new SerialPort(session.PortName, session.BaudRate, Enum.Parse<Parity>(session.Parity), session.DataBits, Enum.Parse<StopBits>(session.StopBits))
            { Handshake = Enum.Parse<Handshake>(session.Handshake), WriteTimeout = 5000 });
        serial.Open();
        Input = Output = serial.BaseStream;
        return Task.CompletedTask;
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token)
        => base.WriteAsync(bytes.ToArray().Select(b => b == 127 ? (byte)8 : b).ToArray(), token);
}

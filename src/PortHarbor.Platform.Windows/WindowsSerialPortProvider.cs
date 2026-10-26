using System.IO.Ports;

namespace PortHarbor;

public sealed class WindowsSerialPortProvider : ISerialPortProvider
{
    public IReadOnlyList<string> GetPortNames() => SerialPort.GetPortNames();
    public IReadOnlyList<string> ParityOptions { get; } = Array.AsReadOnly(Enum.GetNames<Parity>());
    public IReadOnlyList<string> StopBitOptions { get; } = Array.AsReadOnly(new[] { "One", "OnePointFive", "Two" });
    public IReadOnlyList<string> HandshakeOptions { get; } = Array.AsReadOnly(Enum.GetNames<Handshake>());
}

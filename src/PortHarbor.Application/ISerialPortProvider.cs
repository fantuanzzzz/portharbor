namespace PortHarbor;

/// <summary>Device names and persisted option values, without exposing platform serial types.</summary>
public interface ISerialPortProvider
{
    IReadOnlyList<string> GetPortNames();
    IReadOnlyList<string> ParityOptions { get; }
    IReadOnlyList<string> StopBitOptions { get; }
    IReadOnlyList<string> HandshakeOptions { get; }
}

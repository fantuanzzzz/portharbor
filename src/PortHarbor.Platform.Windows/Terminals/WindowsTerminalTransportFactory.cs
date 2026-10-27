namespace PortHarbor;

public static class WindowsTerminalTransportFactory
{
    public static ITerminalTransport Create(Session session)
        => session.Protocol switch
        {
            Protocol.Shell => new LocalShellTerminalTransport((ShellProtocolConfiguration)session.ProtocolConfiguration),
            Protocol.Serial => new SerialTerminalTransport((SerialProtocolConfiguration)session.ProtocolConfiguration),
            _ => throw new ArgumentException("Windows terminals support Shell or Serial.", nameof(session))
        };
}

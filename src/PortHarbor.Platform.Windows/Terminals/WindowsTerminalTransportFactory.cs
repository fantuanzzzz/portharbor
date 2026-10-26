namespace PortHarbor;

public static class WindowsTerminalTransportFactory
{
    public static ITerminalTransport Create(Session session)
        => session.Protocol switch
        {
            Protocol.Shell => new LocalShellTerminalTransport(session with { }),
            Protocol.Serial => new SerialTerminalTransport(session with { }),
            _ => throw new ArgumentException("Windows terminals support Shell or Serial.", nameof(session))
        };
}

namespace PortHarbor;

public static class TerminalTransportFactory
{
    public static ITerminalTransport Create(Session session, IHostKeyTrust trust, string password, Func<string, bool> confirm)
        => session.Protocol switch
        {
            Protocol.SSH => new SshTerminalTransport((SshProtocolConfiguration)session.ProtocolConfiguration, trust, password, confirm),
            Protocol.Telnet => new TelnetTerminalTransport((TelnetProtocolConfiguration)session.ProtocolConfiguration),
            _ => throw new ArgumentException("Network terminals support SSH or Telnet.", nameof(session))
        };
}

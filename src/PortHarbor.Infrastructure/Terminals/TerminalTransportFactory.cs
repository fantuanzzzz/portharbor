namespace PortHarbor;

public static class TerminalTransportFactory
{
    public static ITerminalTransport Create(Session session, IHostKeyTrust trust, string password, Func<string, bool> confirm)
        => session.Protocol switch
        {
            Protocol.SSH => new SshTerminalTransport(session with { }, trust, password, confirm),
            Protocol.Telnet => new TelnetTerminalTransport(session with { }),
            _ => throw new ArgumentException("Network terminals support SSH or Telnet.", nameof(session))
        };
}

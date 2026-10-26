namespace PortHarbor;

public static class TerminalConnectionFactory
{
    public static TerminalConnection Create(Session session, IHostKeyTrust trust, string password, Func<string, bool> confirm)
    {
        ITerminalTransport transport = session.Protocol is Protocol.Shell or Protocol.Serial
            ? WindowsTerminalTransportFactory.Create(session)
            : TerminalTransportFactory.Create(session, trust, password, confirm);
        return new TerminalConnection(session, transport, DiagnosticClassifier.Classify);
    }
}

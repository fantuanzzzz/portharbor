namespace PortHarbor;

public sealed record SessionMetadata(Guid Id, string Name, string Group, bool SavePassword);
public sealed record ConnectionProfile(SessionMetadata Metadata, ProtocolConfiguration Configuration);
public abstract record ProtocolConfiguration;
public sealed record SshProtocolConfiguration(string Host, int Port, string Username, string PrivateKey) : ProtocolConfiguration;
public sealed record SftpProtocolConfiguration(string Host, int Port, string Username, string PrivateKey, string RemotePath) : ProtocolConfiguration;
public sealed record TelnetProtocolConfiguration(string Host, int Port) : ProtocolConfiguration;
public sealed record FtpProtocolConfiguration(string Host, int Port, string Username, bool UseTls, string RemotePath) : ProtocolConfiguration;
public sealed record SerialProtocolConfiguration(string PortName, int BaudRate, int DataBits, string Parity, string StopBits, string Handshake) : ProtocolConfiguration;
public sealed record ShellProtocolConfiguration(string Executable, string Arguments, string WorkingDirectory) : ProtocolConfiguration;
public sealed record RdpProtocolConfiguration(string Host, int Port, string Username, bool FullScreen, bool Clipboard) : ProtocolConfiguration;

public static class SessionProtocolConfiguration
{
    public static ProtocolConfiguration Get(Session session) => session.Protocol switch
    {
        Protocol.Serial => new SerialProtocolConfiguration(session.SerialPort, session.BaudRate, session.DataBits, session.Parity, session.StopBits, session.Handshake),
        Protocol.Shell => new ShellProtocolConfiguration(session.Executable, session.Arguments, session.WorkingDirectory),
        Protocol.RDP => new RdpProtocolConfiguration(session.Host, session.Port, session.Username, session.RdpFullScreen, session.Clipboard),
        Protocol.SSH => new SshProtocolConfiguration(session.Host, session.Port, session.Username, session.PrivateKey),
        Protocol.SFTP => new SftpProtocolConfiguration(session.Host, session.Port, session.Username, session.PrivateKey, session.RemotePath),
        Protocol.Telnet => new TelnetProtocolConfiguration(session.Host, session.Port),
        Protocol.FTP => new FtpProtocolConfiguration(session.Host, session.Port, session.Username, session.UseTls, session.RemotePath),
        _ => throw new ArgumentException("未知协议。", nameof(session))
    };

    public static ConnectionProfile Profile(Session session) => new(new(session.Id, session.Name, session.Group, session.SavePassword), Get(session));
}

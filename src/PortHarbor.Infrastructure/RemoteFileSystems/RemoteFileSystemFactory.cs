namespace PortHarbor;

internal static class RemoteFileSystemFactory
{
    public static IRemoteFileSystem Create(Session session, IHostKeyTrust hostKeyTrust, string password, Func<string, bool> confirm)
        => session.Protocol switch
        {
            Protocol.FTP => new FtpRemoteFileSystem((FtpProtocolConfiguration)session.ProtocolConfiguration, password),
            Protocol.SFTP => new SftpRemoteFileSystem((SftpProtocolConfiguration)session.ProtocolConfiguration, hostKeyTrust, password, confirm),
            _ => throw new ArgumentException("文件传输仅支持 FTP 和 SFTP。", nameof(session))
        };
}

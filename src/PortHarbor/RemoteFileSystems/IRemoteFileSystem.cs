namespace PortHarbor;

// Vendor clients and protocol-specific overwrite semantics stay behind this boundary.
// Streams must be disposed before another operation starts (including upload commit).
internal interface IRemoteFileSystem : IDisposable
{
    void Connect(Action<ConnectionStage> reportStage);
    List<RemoteEntry> List(string path);
    bool Exists(string path);
    Stream OpenRead(string path);
    Stream OpenWrite(string path);
    void CommitUpload(string temporaryPath, string destination);
    void DeleteFile(string path);
    void Mkdir(string path);
    void Rename(string source, string destination);
    void Delete(RemoteEntry entry);
}

internal static class RemoteFileSystemFactory
{
    public static IRemoteFileSystem Create(Session session, Store store, string password, Func<string, bool> confirm)
        => session.Protocol switch
        {
            Protocol.FTP => new FtpRemoteFileSystem(session, password),
            Protocol.SFTP => new SftpRemoteFileSystem(session, store, password, confirm),
            _ => throw new ArgumentException("文件传输仅支持 FTP 和 SFTP。", nameof(session))
        };
}

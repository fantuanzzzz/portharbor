using FluentFTP;

namespace PortHarbor;

internal sealed class FtpRemoteFileSystem(Session session, string password) : IRemoteFileSystem
{
    FtpClient? client;
    FtpClient Client => client ?? throw new InvalidOperationException("FTP 尚未连接。");

    public void Connect(Action<ConnectionStage> reportStage)
    {
        reportStage(ConnectionStage.Authenticating);
        client = new FtpClient(session.Host, session.Username, password, session.Port);
        client.Config.EncryptionMode = session.UseTls ? FtpEncryptionMode.Explicit : FtpEncryptionMode.None;
        client.Config.DataConnectionType = FtpDataConnectionType.AutoPassive;
        client.Config.ConnectTimeout = 15000;
        client.Config.ReadTimeout = 30000;
        client.Config.DataConnectionReadTimeout = 30000;
        client.Connect();
    }

    public List<RemoteEntry> List(string path) => Client.GetListing(path)
        .Where(f => f.Name is not ("." or ".."))
        .Select(f => new RemoteEntry(f.Name, f.FullName, f.Type == FtpObjectType.Directory, f.Size, f.Modified)).ToList();

    public bool Exists(string path) => Client.FileExists(path) || Client.DirectoryExists(path);
    public Stream OpenRead(string path) => Client.OpenRead(path);
    public Stream OpenWrite(string path) => Client.OpenWrite(path);

    public void CommitUpload(string temporaryPath, string destination)
    {
        if (!Client.MoveFile(temporaryPath, destination, FtpRemoteExists.Overwrite))
            throw new IOException("Remote rename failed.");
    }

    public void DeleteFile(string path) => Client.DeleteFile(path);
    public void Mkdir(string path) => Client.CreateDirectory(path);
    public void Rename(string source, string destination) => Client.Rename(source, destination);

    public void Delete(RemoteEntry entry)
    {
        if (entry.Directory)
        {
            if (List(entry.Path).Count != 0) throw new IOException("仅允许删除空目录。");
            Client.DeleteDirectory(entry.Path);
        }
        else Client.DeleteFile(entry.Path);
    }

    public void Dispose()
    {
        var current = client;
        client = null;
        current?.Dispose();
    }
}

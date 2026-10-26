using FluentFTP;

namespace PortHarbor;

internal sealed class FtpRemoteFileSystem(Session session, string password) : IRemoteFileSystem
{
    AsyncFtpClient? client;
    AsyncFtpClient Client => client ?? throw new InvalidOperationException("FTP 尚未连接。");

    public async Task ConnectAsync(Action<ConnectionStage> reportStage, CancellationToken token)
    {
        reportStage(ConnectionStage.Authenticating);
        client = new AsyncFtpClient(session.Host, session.Username, password, session.Port);
        client.Config.EncryptionMode = session.UseTls ? FtpEncryptionMode.Explicit : FtpEncryptionMode.None;
        client.Config.DataConnectionType = FtpDataConnectionType.AutoPassive;
        client.Config.ConnectTimeout = 15000;
        client.Config.ReadTimeout = 35000;
        client.Config.DataConnectionReadTimeout = 35000;
        await client.Connect(token).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RemoteEntry>> ListAsync(string path, CancellationToken token)
        => (await Client.GetListing(path, token).ConfigureAwait(false))
            .Where(f => f.Name is not ("." or ".."))
            .Select(f => new RemoteEntry(f.Name, f.FullName, f.Type == FtpObjectType.Directory, f.Size, f.Modified)).ToArray();

    public async Task<bool> ExistsAsync(string path, CancellationToken token)
        => await Client.FileExists(path, token).ConfigureAwait(false) || await Client.DirectoryExists(path, token).ConfigureAwait(false);

    public async Task<long?> GetLengthAsync(string path, CancellationToken token)
    {
        long length = await Client.GetFileSize(path, -1, token).ConfigureAwait(false);
        return length >= 0 ? length : null;
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken token)
        => Client.OpenRead(path, token: token);

    public Task<Stream> OpenWriteAsync(string path, CancellationToken token)
        => Client.OpenWrite(path, checkIfFileExists: false, token: token);

    public async Task CompleteWriteAsync(CancellationToken token)
    {
        var reply = await Client.GetReply(token).ConfigureAwait(false);
        if (!reply.Success) throw new IOException("FTP server did not confirm upload completion.");
    }

    public async Task CommitUploadAsync(string temporaryPath, string destination, CancellationToken token)
    {
        if (!await Client.MoveFile(temporaryPath, destination, FtpRemoteExists.Overwrite, token).ConfigureAwait(false))
            throw new IOException("Remote rename failed.");
    }

    public Task DeleteFileAsync(string path, CancellationToken token) => Client.DeleteFile(path, token);
    public Task MkdirAsync(string path, CancellationToken token) => Client.CreateDirectory(path, token);
    public Task RenameAsync(string source, string destination, CancellationToken token) => Client.Rename(source, destination, token);

    public async Task DeleteAsync(RemoteEntry entry, CancellationToken token)
    {
        if (entry.Directory)
        {
            if ((await ListAsync(entry.Path, token).ConfigureAwait(false)).Count != 0) throw new IOException("仅允许删除空目录。");
            await Client.DeleteDirectory(entry.Path, token).ConfigureAwait(false);
        }
        else await Client.DeleteFile(entry.Path, token).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        var current = client;
        client = null;
        if (current == null) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await current.Disconnect(timeout.Token).ConfigureAwait(false); }
        finally { current.Dispose(); }
    }
}

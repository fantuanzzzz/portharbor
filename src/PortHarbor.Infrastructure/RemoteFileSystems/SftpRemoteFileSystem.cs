using Renci.SshNet;

namespace PortHarbor;

internal sealed class SftpRemoteFileSystem(Session session, IHostKeyTrust hostKeyTrust, string password, Func<string, bool> confirm) : IRemoteFileSystem
{
    SftpClient? client;
    PrivateKeyFile? key;
    SftpClient Client => client ?? throw new InvalidOperationException("SFTP 尚未连接。");

    public async Task ConnectAsync(Action<ConnectionStage> reportStage, CancellationToken token)
    {
        reportStage(ConnectionStage.Authenticating);
        client = new SftpClient(SshClientSupport.SshInfo(session, password, out key));
        SshClientSupport.HostKey(client, session, hostKeyTrust, confirm, reportStage);
        reportStage(ConnectionStage.Connecting);
        client.OperationTimeout = TimeSpan.FromSeconds(35);
        token.ThrowIfCancellationRequested();
        await client.ConnectAsync(token).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RemoteEntry>> ListAsync(string path, CancellationToken token)
    {
        var entries = new List<RemoteEntry>();
        await foreach (var f in Client.ListDirectoryAsync(path, token).WithCancellation(token).ConfigureAwait(false))
            if (f.Name is not ("." or "..")) entries.Add(new RemoteEntry(f.Name, f.FullName, f.IsDirectory, f.Length, f.LastWriteTime));
        return entries;
    }

    public Task<bool> ExistsAsync(string path, CancellationToken token) => Client.ExistsAsync(path, token);

    public async Task<long?> GetLengthAsync(string path, CancellationToken token)
    {
        try { return (await Client.GetAttributesAsync(path, token).ConfigureAwait(false)).Size; }
        catch (Renci.SshNet.Common.SftpPathNotFoundException) { return null; }
    }

    public async Task<Stream> OpenReadAsync(string path, CancellationToken token)
        => await Client.OpenAsync(path, FileMode.Open, FileAccess.Read, token).ConfigureAwait(false);

    public Task CompleteReadAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.CompletedTask; }

    public async Task<Stream> OpenWriteAsync(string path, CancellationToken token)
        => await Client.OpenAsync(path, FileMode.Create, FileAccess.Write, token).ConfigureAwait(false);

    public Task CompleteWriteAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.CompletedTask; }

    public async Task CommitUploadAsync(string temporaryPath, string destination, CancellationToken token)
    {
        if (!await Client.ExistsAsync(destination, token).ConfigureAwait(false))
        {
            await Client.RenameFileAsync(temporaryPath, destination, token).ConfigureAwait(false);
            return;
        }

        // SSH.NET 2026 exposes cancellation-aware standard rename, while the existing
        // overwrite extension only has a synchronous overload.
        string backup = destination + ".portharbor-" + Guid.NewGuid().ToString("N") + ".bak";
        await Client.RenameFileAsync(destination, backup, token).ConfigureAwait(false);
        try { await Client.RenameFileAsync(temporaryPath, destination, token).ConfigureAwait(false); }
        catch
        {
            try
            {
                if (!await Client.ExistsAsync(destination, CancellationToken.None).ConfigureAwait(false))
                    await Client.RenameFileAsync(backup, destination, CancellationToken.None).ConfigureAwait(false);
            }
            catch { }
            throw;
        }

        // Cleanup must not turn a committed upload into a reported failure.
        try { await Client.DeleteFileAsync(backup, CancellationToken.None).ConfigureAwait(false); } catch { }
    }

    public Task DeleteFileAsync(string path, CancellationToken token) => Client.DeleteFileAsync(path, token);
    public Task MkdirAsync(string path, CancellationToken token) => Client.CreateDirectoryAsync(path, token);
    public Task RenameAsync(string source, string destination, CancellationToken token) => Client.RenameFileAsync(source, destination, token);

    public async Task DeleteAsync(RemoteEntry entry, CancellationToken token)
    {
        // Use the same empty-directory rule for both protocols.
        if (entry.Directory)
        {
            if ((await ListAsync(entry.Path, token).ConfigureAwait(false)).Count != 0) throw new IOException("仅允许删除空目录。");
            await Client.DeleteDirectoryAsync(entry.Path, token).ConfigureAwait(false);
        }
        else await Client.DeleteFileAsync(entry.Path, token).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        var current = client;
        var currentKey = key;
        client = null;
        key = null;
        try { current?.Dispose(); }
        finally { currentKey?.Dispose(); }
        return ValueTask.CompletedTask;
    }
}

using Renci.SshNet;

namespace PortHarbor;

internal sealed class SftpRemoteFileSystem(SftpProtocolConfiguration session, IHostKeyTrust hostKeyTrust, string password, Func<string, bool> confirm, SshAuthenticationPromptHandler? prompt = null) : IRemoteFileSystem
{
    static readonly TimeSpan RecoveryTimeout = TimeSpan.FromSeconds(5);
    SftpClient? client;
    PrivateKeyFile? key;
    SftpClient Client => client ?? throw new InvalidOperationException("SFTP 尚未连接。");

    public async Task ConnectAsync(Action<ConnectionStage> reportStage, CancellationToken token)
    {
        reportStage(ConnectionStage.Authenticating);
        client = new SftpClient(SshClientSupport.SshInfo(session.Host, session.Port, session.Username, session.PrivateKey, password, out key, prompt));
        SshClientSupport.HostKey(client, session.Host, session.Port, hostKeyTrust, confirm, reportStage);
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
        try
        {
            // Either rename may have executed even when its acknowledgement was lost.
            await Client.RenameFileAsync(destination, backup, token).ConfigureAwait(false);
            await Client.RenameFileAsync(temporaryPath, destination, token).ConfigureAwait(false);
        }
        catch
        {
            // Recovery has its own short budget, including when the caller cancelled.
            // Never reconnect/replay here or overwrite a target already present.
            using var recovery = new CancellationTokenSource(RecoveryTimeout);
            try
            {
                if (!await Client.ExistsAsync(destination, recovery.Token).ConfigureAwait(false)
                    && await Client.ExistsAsync(backup, recovery.Token).ConfigureAwait(false))
                    await Client.RenameFileAsync(backup, destination, recovery.Token).ConfigureAwait(false);
            }
            catch { /* Keep the original failure and leave the backup for explicit recovery. */ }
            throw;
        }

        // Cleanup must neither block indefinitely nor turn a confirmed commit into failure.
        using var cleanup = new CancellationTokenSource(RecoveryTimeout);
        try { await Client.DeleteFileAsync(backup, cleanup.Token).ConfigureAwait(false); }
        catch { /* A confirmed replacement remains successful; the old backup may remain. */ }
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

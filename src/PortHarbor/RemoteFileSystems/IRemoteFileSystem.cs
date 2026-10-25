namespace PortHarbor;

// Vendor clients and protocol-specific overwrite semantics stay behind this boundary.
// Streams must be disposed before another operation starts (including upload commit).
internal interface IRemoteFileSystem : IAsyncDisposable
{
    Task ConnectAsync(Action<ConnectionStage> reportStage, CancellationToken token);
    Task<IReadOnlyList<RemoteEntry>> ListAsync(string path, CancellationToken token);
    Task<bool> ExistsAsync(string path, CancellationToken token);
    Task<long?> GetLengthAsync(string path, CancellationToken token);
    Task<Stream> OpenReadAsync(string path, CancellationToken token);
    Task<Stream> OpenWriteAsync(string path, CancellationToken token);
    Task CompleteWriteAsync(CancellationToken token);
    Task CommitUploadAsync(string temporaryPath, string destination, CancellationToken token);
    Task DeleteFileAsync(string path, CancellationToken token);
    Task MkdirAsync(string path, CancellationToken token);
    Task RenameAsync(string source, string destination, CancellationToken token);
    Task DeleteAsync(RemoteEntry entry, CancellationToken token);
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

internal static class TimeoutRunner
{
    public static async Task<T> RunAsync<T>(TimeSpan timeout, CancellationToken cancellationToken, Func<CancellationToken, Task<T>> action)
    {
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        try { return await action(linkedSource.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutSource.IsCancellationRequested)
        {
            throw new TimeoutException("The file operation exceeded its configured timeout.");
        }
    }

    public static async Task RunAsync(TimeSpan timeout, CancellationToken cancellationToken, Func<CancellationToken, Task> action)
    {
        await RunAsync(timeout, cancellationToken, async token => { await action(token).ConfigureAwait(false); return true; }).ConfigureAwait(false);
    }
}

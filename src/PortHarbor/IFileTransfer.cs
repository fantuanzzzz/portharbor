namespace PortHarbor;

public sealed record RemoteEntry(string Name, string Path, bool Directory, long Size, DateTime Modified);

/// <summary>
/// Protocol-independent asynchronous file operations. Operations on one connection are serialized.
/// </summary>
public interface IFileTransfer : IAsyncDisposable
{
    Guid OperationId { get; }
    ConnectionState State { get; }
    event Action<ConnectionDiagnostic>? Diagnostic;
    Task ConnectAsync(CancellationToken token, TimeSpan timeout);
    Task<IReadOnlyList<RemoteEntry>> ListAsync(string path, CancellationToken token, TimeSpan timeout);
    Task<bool> ExistsAsync(string path, CancellationToken token, TimeSpan timeout);
    Task DownloadAsync(string remote, string local, CancellationToken token, TimeSpan timeout, IProgress<TransferProgress>? progress = null);
    Task UploadAsync(string local, string remote, CancellationToken token, TimeSpan timeout, IProgress<TransferProgress>? progress = null);
    Task MkdirAsync(string path, CancellationToken token, TimeSpan timeout);
    Task RenameAsync(string source, string dest, CancellationToken token, TimeSpan timeout);
    Task DeleteAsync(RemoteEntry entry, CancellationToken token, TimeSpan timeout);
}

public enum TransferDirection { Download, Upload }

/// <summary>Progress for a single transfer operation; byte counts are cumulative.</summary>
public sealed record TransferProgress(TransferDirection Direction, long BytesTransferred, long? TotalBytes);

public static class FileTransferFactory
{
    public static IFileTransfer Create(Session session, Store store, string password, Func<string, bool> confirm)
        => new FileConnection(session, store, password, confirm);
}

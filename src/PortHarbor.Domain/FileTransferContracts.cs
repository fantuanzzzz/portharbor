namespace PortHarbor;

public sealed record RemoteEntry(string Name, string Path, bool Directory, long Size, DateTime Modified);

public enum TransferDirection { Download, Upload }

/// <summary>Progress for a single transfer operation; byte counts are cumulative.</summary>
public sealed record TransferProgress(TransferDirection Direction, long BytesTransferred, long? TotalBytes);

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

/// <summary>Infrastructure port implemented by FTP/SFTP adapters.</summary>
public interface IRemoteFileSystem : IAsyncDisposable
{
    Task ConnectAsync(Action<ConnectionStage> reportStage, CancellationToken token);
    Task<IReadOnlyList<RemoteEntry>> ListAsync(string path, CancellationToken token);
    Task<bool> ExistsAsync(string path, CancellationToken token);
    Task<long?> GetLengthAsync(string path, CancellationToken token);
    Task<Stream> OpenReadAsync(string path, CancellationToken token);
    /// <summary>Called after closing the read stream, before committing a local download.</summary>
    Task CompleteReadAsync(CancellationToken token);
    Task<Stream> OpenWriteAsync(string path, CancellationToken token);
    Task CompleteWriteAsync(CancellationToken token);
    Task CommitUploadAsync(string temporaryPath, string destination, CancellationToken token);
    Task DeleteFileAsync(string path, CancellationToken token);
    Task MkdirAsync(string path, CancellationToken token);
    Task RenameAsync(string source, string destination, CancellationToken token);
    Task DeleteAsync(RemoteEntry entry, CancellationToken token);
}

namespace PortHarbor;

public sealed record RemoteEntry(string Name, string Path, bool Directory, long Size, DateTime Modified);

/// <summary>
/// Protocol-independent file operations. Calls are synchronous and must be serialized by the caller.
/// Transfer progress is the cumulative byte count; cancellation is checked between data blocks.
/// </summary>
public interface IFileTransfer : IDisposable
{
    Guid OperationId { get; }
    ConnectionState State { get; }
    event Action<ConnectionDiagnostic>? Diagnostic;
    void Connect();
    List<RemoteEntry> List(string path);
    bool Exists(string path);
    void Download(string remote, string local, CancellationToken token, Action<long> progress);
    void Upload(string local, string remote, CancellationToken token, Action<long> progress);
    void Mkdir(string path);
    void Rename(string source, string dest);
    void Delete(RemoteEntry entry);
}

public static class FileTransferFactory
{
    public static IFileTransfer Create(Session session, Store store, string password, Func<string, bool> confirm)
        => new FileConnection(session, store, password, confirm);
}

namespace PortHarbor;

/// <summary>Shared transfer policy and diagnostics, independent of vendor protocol clients.</summary>
public sealed class FileConnection : IFileTransfer
{
    readonly Protocol protocol;
    readonly IRemoteFileSystem remote;
    bool disposed;

    public Guid OperationId { get; } = Guid.NewGuid();
    public ConnectionState State { get; private set; } = ConnectionState.Created;
    public event Action<ConnectionDiagnostic>? Diagnostic;

    public FileConnection(Session session, Store store, string password, Func<string, bool> confirm)
        : this(session.Protocol, RemoteFileSystemFactory.Create(session, store, password, confirm)) { }

    internal FileConnection(Protocol protocol, IRemoteFileSystem remote)
    {
        this.protocol = protocol;
        this.remote = remote;
    }

    public void Connect()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (State == ConnectionState.Connected) return;
        SetState(ConnectionState.Connecting, ConnectionStage.Connecting, ConnectionErrorCode.None, false, "Connecting.");
        try
        {
            remote.Connect(stage => SetState(ConnectionState.Connecting, stage, ConnectionErrorCode.None, false, "Connecting."));
            SetState(ConnectionState.Connected, ConnectionStage.None, ConnectionErrorCode.None, false, "Connected.");
        }
        catch (Exception error)
        {
            var code = DiagnosticClassifier.Classify(error, ConnectionStage.Connecting);
            SetState(ConnectionState.Failed, ConnectionStage.Connecting, code, DiagnosticClassifier.IsRetryable(code), "File connection failed.");
            try { remote.Dispose(); }
            finally { SetState(ConnectionState.Closed, ConnectionStage.Closing, code, false, "Closed."); }
            throw;
        }
    }

    public List<RemoteEntry> List(string path) => Execute(ConnectionStage.Reading, "Listing failed.", () => remote.List(path));
    public bool Exists(string path) => Execute(ConnectionStage.Reading, "Existence check failed.", () => remote.Exists(path));

    public void Download(string remotePath, string local, CancellationToken token, Action<long> progress)
        => Execute(ConnectionStage.Reading, "Download failed.", () =>
        {
            token.ThrowIfCancellationRequested();
            using var source = remote.OpenRead(remotePath);
            CopyAtomically(source, local, token, progress);
            return true;
        });

    public static void CopyAtomically(Stream source, string destination, CancellationToken token, Action<long> progress)
    {
        token.ThrowIfCancellationRequested();
        string temporaryPath = destination + ".portharbor-" + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            using (var target = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                Copy(source, target, token, progress);
            token.ThrowIfCancellationRequested();
            File.Move(temporaryPath, destination, true);
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }

    public void Upload(string local, string remotePath, CancellationToken token, Action<long> progress)
        => Execute(ConnectionStage.Writing, "Upload failed.", () =>
        {
            token.ThrowIfCancellationRequested();
            string temporaryPath = remotePath + ".portharbor-" + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                using (var source = File.OpenRead(local))
                using (var target = remote.OpenWrite(temporaryPath))
                    Copy(source, target, token, progress);
                token.ThrowIfCancellationRequested();
                remote.CommitUpload(temporaryPath, remotePath);
            }
            finally
            {
                // Cleanup is best effort; preserve the original transfer error if disconnected.
                try { if (remote.Exists(temporaryPath)) remote.DeleteFile(temporaryPath); } catch { }
            }
            return true;
        });

    static void Copy(Stream source, Stream destination, CancellationToken token, Action<long> progress)
    {
        var buffer = new byte[65536];
        long done = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            int count = source.Read(buffer);
            if (count == 0) break;
            destination.Write(buffer, 0, count);
            done += count;
            progress(done);
        }
        destination.Flush();
    }

    public void Mkdir(string path) => Execute(ConnectionStage.Writing, "Create directory failed.", () => { remote.Mkdir(path); return true; });

    public void Rename(string source, string dest) => Execute(ConnectionStage.Writing, "Rename failed.", () =>
    {
        if (remote.Exists(dest)) throw new IOException("目标名称已经存在。");
        remote.Rename(source, dest);
        return true;
    });

    public void Delete(RemoteEntry entry) => Execute(ConnectionStage.Writing, "Delete failed.", () => { remote.Delete(entry); return true; });

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        SetState(ConnectionState.Closing, ConnectionStage.Closing, ConnectionErrorCode.None, false, "Closing.");
        try { remote.Dispose(); }
        finally { SetState(ConnectionState.Closed, ConnectionStage.Closing, ConnectionErrorCode.None, false, "Closed."); }
    }

    T Execute<T>(ConnectionStage stage, string message, Func<T> action)
    {
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (State != ConnectionState.Connected) throw new InvalidOperationException("文件连接尚未建立。");
            return action();
        }
        catch (Exception error)
        {
            var code = DiagnosticClassifier.Classify(error, stage);
            Diagnostic?.Invoke(new ConnectionDiagnostic(OperationId, protocol, State, stage, code, DiagnosticClassifier.IsRetryable(code), message));
            throw;
        }
    }

    void SetState(ConnectionState state, ConnectionStage stage, ConnectionErrorCode code, bool retryable, string message)
    {
        State = state;
        Diagnostic?.Invoke(new ConnectionDiagnostic(OperationId, protocol, state, stage, code, retryable, message));
    }
}
